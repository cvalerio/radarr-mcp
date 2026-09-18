using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for moving movies to a different root folder via Radarr's bulk movie editor.</summary>
[McpServerToolType]
public sealed class MoveMoviesTool(IRadarrClient radarr)
{
    private const string ToolName = "radarr_move_movies";
    private const int MaxMovies = 100;

    private static readonly HashSet<string> MoveCommandNames = new(StringComparer.OrdinalIgnoreCase)
        { "BulkMoveMovie", "MoveMovie", "RefreshMovie" };

    /// <summary>
    /// Moves one or more movies to another root folder through PUT /api/v3/movie/editor.
    /// Validates the root folder and all IDs before sending anything; movies already in the target are skipped.
    /// </summary>
    [McpServerTool(Name = ToolName)]
    [Description("""
        Move one or more movies to a different Radarr root folder. When moveFiles is true (default), Radarr PHYSICALLY MOVES the movie folders and files on disk to the new root folder in a background job; when false, only the path stored in Radarr's database changes and the files are left where they are.

        DESTRUCTIVE / IRREVERSIBLE: files are relocated on disk and Radarr may rename the movie folder according to its folder naming format. Always call first with dryRun=true to review the plan, then repeat with dryRun=false.

        Get movie IDs (radarrId) from radarr_get_library and valid root folder paths from radarr_get_root_folders. The call is rejected without changing anything if the root folder does not exist or any ID is not in the library. Movies already in the target root folder are skipped.
        """)]
    public async Task<string> MoveMoviesAsync(
        [Description("Radarr IDs of the movies to move (1-100), as returned by radarr_get_library.")] int[] movieIds,
        [Description("Destination root folder path, exactly as listed by radarr_get_root_folders, e.g. \"/movies/T\". A trailing slash is ignored.")] string rootFolderPath,
        [Description("true (default) = move files on disk; false = only update the path in Radarr's database without touching files.")] bool moveFiles = true,
        [Description("true = do not change anything, only return the plan (id, title, current path, destination). Default false.")] bool dryRun = false,
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

        return ToolHelpers.ToJson(new MoveMoviesResult(false, target, true, null, moved, skipped, queued,
            "Paths updated in Radarr; files are being moved in the background (not awaited)."));
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
