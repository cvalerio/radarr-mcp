using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for moving movies to a different root folder via Radarr's bulk movie editor.</summary>
[McpServerToolType]
public sealed class MoveMoviesTool(IRadarrClient radarr, TimeProvider timeProvider)
{
    private const string ToolName = "radarr_move_movies";
    private const int MaxMovies = 100;
    private const int MaxWaitTimeoutSeconds = 1800;

    private static readonly HashSet<string> MoveCommandNames = new(StringComparer.OrdinalIgnoreCase)
        { "BulkMoveMovie", "MoveMovie", "RefreshMovie" };

    /// <summary>Commands that actually transfer files; waitForCompletion waits for these only.</summary>
    private static readonly HashSet<string> TransferCommandNames = new(StringComparer.OrdinalIgnoreCase)
        { "BulkMoveMovie", "MoveMovie" };

    /// <summary>
    /// Moves one or more movies to another root folder through PUT /api/v3/movie/editor.
    /// Validates the root folder and all IDs before sending anything; movies already in the target are skipped.
    /// </summary>
    [McpServerTool(Name = ToolName)]
    [Description("""
        Move one or more movies to a different Radarr root folder. When moveFiles is true (default), Radarr PHYSICALLY MOVES the movie folders and files on disk to the new root folder in a background job; when false, only the path stored in Radarr's database changes and the files are left where they are.

        DESTRUCTIVE / IRREVERSIBLE: files are relocated on disk and Radarr may rename the movie folder according to its folder naming format. Always call first with dryRun=true to review the plan, then repeat with dryRun=false.

        Get movie IDs (radarrId) from radarr_get_library and valid root folder paths from radarr_get_root_folders. The call is rejected without changing anything if the root folder does not exist or any ID is not in the library. Movies already in the target root folder are skipped.

        The paths in Radarr change immediately, but the file transfer runs in the background. Between root folders on DIFFERENT physical volumes it is a byte-by-byte copy followed by a delete and can take minutes per movie, so the call returns long before the files are in place. To know when it is done, either pass waitForCompletion=true (the call then waits, up to waitTimeoutSeconds, and returns a "completion" object) or check the returned queuedCommands later with radarr_get_command. Keep waitForCompletion=false for large batches. A wait timeout is not an error: the move keeps going in the background.
        """)]
    public async Task<string> MoveMoviesAsync(
        [Description("Radarr IDs of the movies to move (1-100), as returned by radarr_get_library.")] int[] movieIds,
        [Description("Destination root folder path, exactly as listed by radarr_get_root_folders, e.g. \"/movies/T\". A trailing slash is ignored.")] string rootFolderPath,
        [Description("true (default) = move files on disk; false = only update the path in Radarr's database without touching files.")] bool moveFiles = true,
        [Description("true = do not change anything, only return the plan (id, title, current path, destination). Default false.")] bool dryRun = false,
        [Description("true = after starting the move, wait until Radarr's move command finishes (or waitTimeoutSeconds elapses) and report the outcome in \"completion\". Ignored with dryRun=true or moveFiles=false. Default false.")] bool waitForCompletion = false,
        [Description("Maximum seconds to wait when waitForCompletion=true (1-1800, default 300).")] int waitTimeoutSeconds = 300,
        IProgress<ProgressNotificationValue>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (movieIds is null || movieIds.Length == 0)
            return ToolHelpers.ErrorJson(ToolName, "movieIds must contain at least one ID.");

        var ids = movieIds.Distinct().ToList();
        if (ids.Count > MaxMovies)
            return ToolHelpers.ErrorJson(ToolName, $"At most {MaxMovies} movies can be moved per call (got {ids.Count}).");

        var invalidIds = ids.Where(id => id <= 0).ToList();
        if (invalidIds.Count > 0)
            return ToolHelpers.ErrorJson(ToolName, $"movieIds must be positive integers (invalid: {string.Join(", ", invalidIds)}).");

        if (string.IsNullOrWhiteSpace(rootFolderPath))
            return ToolHelpers.ErrorJson(ToolName, "rootFolderPath is required.");

        if (waitForCompletion && (waitTimeoutSeconds < 1 || waitTimeoutSeconds > MaxWaitTimeoutSeconds))
            return ToolHelpers.ErrorJson(ToolName, $"waitTimeoutSeconds must be between 1 and {MaxWaitTimeoutSeconds} (got {waitTimeoutSeconds}).");

        // Fetch root folders and library in parallel
        var rootFoldersTask = radarr.GetRootFoldersAsync(cancellationToken);
        var libraryTask = radarr.GetLibraryAsync(cancellationToken);
        await Task.WhenAll(rootFoldersTask, libraryTask).ConfigureAwait(false);

        var rootFolders = rootFoldersTask.Result;
        var library = libraryTask.Result;

        if (!rootFolders.IsSuccess)
            return ToolHelpers.ErrorJson(ToolName, $"Could not fetch root folders: {rootFolders.Error}");
        if (!library.IsSuccess)
            return ToolHelpers.ErrorJson(ToolName, $"Could not fetch library: {library.Error}");

        // STEP 1: Resolve the root folder (use Radarr's own spelling of the path)
        var availableFolders = rootFolders.Value ?? [];
        var target = availableFolders.FirstOrDefault(f => PathsEqual(f.Path, rootFolderPath))?.Path;
        if (target is null)
        {
            var available = availableFolders.Count == 0
                ? "none configured"
                : string.Join(", ", availableFolders.Select(f => f.Path));
            return ToolHelpers.ErrorJson(ToolName,
                $"Root folder '{rootFolderPath}' does not exist in Radarr. Available root folders: {available}.");
        }
        var targetNormalized = NormalizePath(target);

        // STEP 2: All IDs must exist — otherwise send nothing
        var moviesById = (library.Value ?? []).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        var missing = ids.Where(id => !moviesById.ContainsKey(id)).ToList();
        if (missing.Count > 0)
            return ToolHelpers.ErrorJson(ToolName,
                $"Movie IDs not found in library: {string.Join(", ", missing)}. Nothing was changed.");

        // STEP 3: Skip movies already in the target root folder
        var toMove = new List<RadarrMovie>();
        var skipped = new List<MoveMovieSkipped>();
        foreach (var id in ids)
        {
            var movie = moviesById[id];
            var currentRoot = GetParent(movie.Path) ?? movie.RootFolderPath;
            if (currentRoot is not null && PathsEqual(currentRoot, targetNormalized))
                skipped.Add(new MoveMovieSkipped(movie.Id, movie.Title, "Already in the target root folder."));
            else
                toMove.Add(movie);
        }

        if (dryRun)
        {
            var planned = toMove.Select(m => new MoveMovieEntry(
                m.Id, m.Title, m.Path, target, EstimateNewPath(targetNormalized, m.Path))).ToList();
            var note = moveFiles && planned.Count > 0
                ? "Dry run: nothing was changed. With moveFiles=true Radarr rebuilds the folder name from its movie folder naming format, so the final folder name may differ from newPath."
                : "Dry run: nothing was changed.";
            return ToolHelpers.ToJson(new MoveMoviesResult(true, target, moveFiles, planned, null, skipped, null, note));
        }

        // STEP 4: Nothing to do → no call
        if (toMove.Count == 0)
            return ToolHelpers.ToJson(new MoveMoviesResult(false, target, moveFiles, null, [], skipped, null,
                "No movies needed moving; Radarr was not called."));

        // Baseline so we only report commands queued by this call (command IDs are monotonic)
        int? commandBaseline = null;
        if (moveFiles)
        {
            var before = await radarr.GetCommandsAsync(cancellationToken).ConfigureAwait(false);
            if (before.IsSuccess)
                commandBaseline = (before.Value ?? []).Select(c => c.Id).DefaultIfEmpty(0).Max();
        }

        var moveResult = await radarr.MoveMoviesAsync(
            new RadarrMovieEditorMoveRequest(toMove.Select(m => m.Id).ToList(), target, moveFiles),
            cancellationToken).ConfigureAwait(false);
        if (!moveResult.IsSuccess)
            return ToolHelpers.ErrorJson(ToolName, moveResult.Error!);

        var updatedById = (moveResult.Value ?? []).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First());
        var moved = toMove.Select(m => new MoveMovieEntry(
            m.Id, m.Title, m.Path, target,
            updatedById.TryGetValue(m.Id, out var updated) ? updated.Path : null)).ToList();

        if (!moveFiles)
            return ToolHelpers.ToJson(new MoveMoviesResult(false, target, false, null, moved, skipped, null,
                "Only the paths in Radarr's database were updated; files on disk were not touched."));

        // STEP 5: Report commands Radarr queued for the move, without waiting for them
        var movedIds = moved.Select(m => m.Id).ToHashSet();
        var after = await radarr.GetCommandsAsync(cancellationToken).ConfigureAwait(false);
        if (!after.IsSuccess)
            return ToolHelpers.ToJson(new MoveMoviesResult(false, target, true, null, moved, skipped, null,
                $"Move accepted by Radarr, but queued commands could not be read: {after.Error}"));

        var queued = (after.Value ?? [])
            .Where(c => c.Name is not null && MoveCommandNames.Contains(c.Name))
            .Where(c => commandBaseline is null || c.Id > commandBaseline)
            .Where(c => ReferencesAny(c.Body, movedIds))
            .Select(c => new MoveQueuedCommand(c.Id, c.Name, c.Status))
            .ToList();

        if (!waitForCompletion)
            return ToolHelpers.ToJson(new MoveMoviesResult(false, target, true, null, moved, skipped, queued,
                "Paths updated in Radarr; files are being moved in the background (not awaited). Check progress with radarr_get_command."));

        var completion = await WaitForMoveAsync(queued, moved, targetNormalized,
            TimeSpan.FromSeconds(waitTimeoutSeconds), progress, cancellationToken).ConfigureAwait(false);
        return ToolHelpers.ToJson(new MoveMoviesResult(false, target, true, null, moved, skipped, queued,
            "Paths updated in Radarr; see \"completion\" for the file transfer outcome.", completion));
    }

    /// <summary>Polling interval: 2s for the first 30s, 5s up to 2 minutes, then 10s.</summary>
    internal static TimeSpan PollInterval(TimeSpan elapsed) =>
        elapsed < TimeSpan.FromSeconds(30) ? TimeSpan.FromSeconds(2)
        : elapsed < TimeSpan.FromMinutes(2) ? TimeSpan.FromSeconds(5)
        : TimeSpan.FromSeconds(10);

    /// <summary>
    /// Polls GET /api/v3/command/{id} for the transfer commands of this move until they end or the timeout expires.
    /// Never throws: timeout and cancellation are reported as a completion status.
    /// </summary>
    private async Task<MoveCompletion> WaitForMoveAsync(
        List<MoveQueuedCommand> queued, List<MoveMovieEntry> moved, string targetNormalized,
        TimeSpan timeout, IProgress<ProgressNotificationValue>? progress, CancellationToken ct)
    {
        var pending = queued.Where(c => c.Name is not null && TransferCommandNames.Contains(c.Name))
            .Select(c => c.Id).Distinct().ToList();
        if (pending.Count == 0)
            return new MoveCompletion("unknown",
                Note: "No move command was found for this call, so there was nothing to wait for. Check the movie paths with radarr_get_movie_details.");

        var last = new Dictionary<int, RadarrCommandStatus>();
        string? lastError = null;
        var start = timeProvider.GetTimestamp();

        try
        {
            while (true)
            {
                foreach (var id in pending.ToList())
                {
                    var poll = await radarr.GetCommandAsync(id, ct).ConfigureAwait(false);
                    if (!poll.IsSuccess)
                    {
                        lastError = poll.Error; // transient: keep polling until the timeout
                        continue;
                    }
                    if (poll.Value is null)
                        return new MoveCompletion("unknown", CommandId: id,
                            Note: $"Command {id} is no longer known to Radarr (it keeps commands only for a limited time). Check the movie paths with radarr_get_movie_details.");

                    last[id] = poll.Value;
                    if (ToolHelpers.IsTerminalCommandStatus(poll.Value.Status))
                        pending.Remove(id);
                }

                if (pending.Count == 0)
                    break;

                var elapsed = timeProvider.GetElapsedTime(start);
                if (elapsed >= timeout)
                {
                    var runningId = pending[0];
                    last.TryGetValue(runningId, out var running);
                    return new MoveCompletion("timeout", CommandId: runningId,
                        LastStatus: running?.Status, LastMessage: running?.Message ?? lastError,
                        Note: "The move continues in the background; check again later with radarr_get_command.");
                }

                if (last.TryGetValue(pending[0], out var current) && current.Message is not null)
                    progress?.Report(new ProgressNotificationValue { Progress = (float)elapsed.TotalSeconds, Message = current.Message });

                var delay = PollInterval(elapsed);
                if (delay > timeout - elapsed) delay = timeout - elapsed;
                await Task.Delay(delay, timeProvider, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new MoveCompletion("cancelled", CommandId: pending.FirstOrDefault(),
                Note: "Waiting was cancelled; the move continues in the background. Check again with radarr_get_command.");
        }

        var failed = last.Values.FirstOrDefault(c => !string.Equals(c.Status, "completed", StringComparison.OrdinalIgnoreCase));
        if (failed is not null)
            return new MoveCompletion(failed.Status?.ToLowerInvariant() ?? "failed", CommandId: failed.Id,
                DurationSeconds: ToolHelpers.CommandDurationSeconds(failed),
                Message: failed.Message, ErrorMessage: ToolHelpers.CommandErrorMessage(failed));

        var duration = last.Values.Select(ToolHelpers.CommandDurationSeconds).Max();

        // Radarr catches per-movie transfer errors, reverts that movie's path and still reports "completed":
        // re-read the library to detect movies that did not end up in the target root folder.
        var library = await radarr.GetLibraryAsync(ct).ConfigureAwait(false);
        if (!library.IsSuccess)
            return new MoveCompletion("completed", DurationSeconds: duration,
                Note: $"Move command completed, but the final paths could not be verified: {library.Error}");

        var pathsById = (library.Value ?? []).GroupBy(m => m.Id).ToDictionary(g => g.Key, g => g.First().Path);
        var reverted = moved
            .Where(m => pathsById.TryGetValue(m.Id, out var path) && !PathsEqual(GetParent(path), targetNormalized))
            .Select(m => new MoveMovieReverted(m.Id, m.Title, pathsById[m.Id]))
            .ToList();

        return reverted.Count == 0
            ? new MoveCompletion("completed", DurationSeconds: duration)
            : new MoveCompletion("completedWithErrors", DurationSeconds: duration, Reverted: reverted,
                Note: "Radarr could not transfer these movies and reverted their paths to the old location (see Radarr's logs for the reason, e.g. disk full or permissions). Their files were not moved.");
    }

    private static string NormalizePath(string path)
    {
        var trimmed = path.Trim().TrimEnd('/', '\\');
        return trimmed.Length == 0 ? path.Trim()[..1] : trimmed;
    }

    private static bool IsWindowsPath(string path) =>
        path.Contains('\\') || (path.Length >= 2 && path[1] == ':');

    private static bool PathsEqual(string? a, string? b)
    {
        if (a is null || b is null) return false;
        var na = NormalizePath(a);
        var nb = NormalizePath(b);
        var comparison = IsWindowsPath(na) || IsWindowsPath(nb)
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(na, nb, comparison);
    }

    private static string? GetParent(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        var trimmed = NormalizePath(path);
        var idx = trimmed.LastIndexOfAny(['/', '\\']);
        return idx <= 0 ? null : trimmed[..idx];
    }

    private static string? EstimateNewPath(string targetRoot, string? currentPath)
    {
        if (string.IsNullOrWhiteSpace(currentPath)) return null;
        var trimmed = NormalizePath(currentPath);
        var folderName = trimmed[(trimmed.LastIndexOfAny(['/', '\\']) + 1)..];
        var separator = IsWindowsPath(targetRoot) ? '\\' : '/';
        return $"{targetRoot}{separator}{folderName}";
    }

    /// <summary>True if a command body refers to any of the given movie IDs (movies[].movieId, movieIds[] or movieId).</summary>
    private static bool ReferencesAny(JsonElement? body, HashSet<int> movieIds)
    {
        if (body is not { ValueKind: JsonValueKind.Object } b) return false;

        foreach (var prop in b.EnumerateObject())
        {
            if (prop.NameEquals("movieId") && prop.Value.ValueKind == JsonValueKind.Number &&
                prop.Value.TryGetInt32(out var single) && movieIds.Contains(single))
                return true;

            if (prop.NameEquals("movieIds") && prop.Value.ValueKind == JsonValueKind.Array &&
                prop.Value.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out var id) && movieIds.Contains(id)))
                return true;

            if (prop.NameEquals("movies") && prop.Value.ValueKind == JsonValueKind.Array &&
                prop.Value.EnumerateArray().Any(e => e.ValueKind == JsonValueKind.Object &&
                    e.TryGetProperty("movieId", out var mid) && mid.ValueKind == JsonValueKind.Number &&
                    mid.TryGetInt32(out var id) && movieIds.Contains(id)))
                return true;
        }

        return false;
    }
}
