using System.ComponentModel;
using System.Text.Json.Serialization;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>Request payload for a single movie add operation.</summary>
public sealed record AddMovieRequest(
    [property: Description("TMDB ID of the movie to add.")]
    int TmdbId,
    [property: Description("Quality profile ID. Auto-detected from first profile if null.")]
    int? QualityProfileId = null,
    [property: Description("Root folder path. Auto-detected from first root folder if null.")]
    string? RootFolderPath = null,
    [property: Description("Whether to monitor the movie for new releases.")]
    bool Monitored = true,
    [property: Description("Trigger an immediate search after adding.")]
    bool SearchForMovie = true);

/// <summary>MCP tool for adding movies to the Radarr library.</summary>
[McpServerToolType]
public sealed class AddMovieTool(RadarrClient radarr)
{
    /// <summary>
    /// Adds one or more movies to Radarr for monitoring and download.
    /// Accepts TMDB IDs and auto-detects quality profile and root folder if not specified.
    /// All adds are executed in parallel.
    /// </summary>
    [McpServerTool(Name = "radarr_add_movie")]
    [Description("Add one or more movies to Radarr for monitoring and download. Accepts TMDB IDs.")]
    public async Task<string> AddMoviesAsync(
        [Description("Array of movies to add. Each requires a tmdbId; other fields are optional.")] AddMovieRequest[] movies,
        CancellationToken cancellationToken = default)
    {
        if (movies is null || movies.Length == 0)
            return ToolHelpers.ErrorJson("radarr_add_movie", "movies array must not be empty.");

        // Fetch defaults once, in parallel
        var profilesTask = radarr.GetQualityProfilesAsync(cancellationToken);
        var rootFoldersTask = radarr.GetRootFoldersAsync(cancellationToken);
        await Task.WhenAll(profilesTask, rootFoldersTask).ConfigureAwait(false);

        var profiles = profilesTask.Result;
        var rootFolders = rootFoldersTask.Result;

        if (!profiles.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_add_movie", $"Could not fetch quality profiles: {profiles.Error}");
        if (!rootFolders.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_add_movie", $"Could not fetch root folders: {rootFolders.Error}");

        var defaultProfileId = profiles.Value?.FirstOrDefault()?.Id ?? 1;
        var defaultRootFolder = rootFolders.Value?.FirstOrDefault()?.Path ?? "/movies";

        // Execute all adds in parallel
        var tasks = movies.Select(req => AddSingleMovieAsync(req, defaultProfileId, defaultRootFolder, cancellationToken));
        var results = await Task.WhenAll(tasks).ConfigureAwait(false);

        return ToolHelpers.ToJson(results.ToList());
    }

    private async Task<AddMovieResult> AddSingleMovieAsync(
        AddMovieRequest req, int defaultProfileId, string defaultRootFolder,
        CancellationToken ct)
    {
        // Radarr requires the full movie object from lookup before POST
        var lookupResult = await radarr.LookupMovieByTmdbIdAsync(req.TmdbId, ct).ConfigureAwait(false);
        if (!lookupResult.IsSuccess)
            return new AddMovieResult(req.TmdbId, null, null, false, null, lookupResult.Error);

        var movie = lookupResult.Value?.FirstOrDefault();
        if (movie is null)
            return new AddMovieResult(req.TmdbId, null, null, false, null, $"No movie found for TMDB ID {req.TmdbId}.");

        // Compute letter-based root folder when not explicitly provided
        var autoRootFolder = defaultRootFolder;
        if (req.RootFolderPath is null)
        {
            var sortTitle = GetSortTitle(movie.Title ?? string.Empty);
            if (sortTitle.Length > 0)
            {
                var first = sortTitle[0];
                var letter = char.IsLetter(first)
                    ? char.ToUpperInvariant(first).ToString()
                    : char.IsDigit(first) ? "#" : null;
                if (letter is not null)
                    autoRootFolder = $"{defaultRootFolder.TrimEnd('/')}/{letter}";
            }
        }

        // Compose the POST body Radarr expects
        var body = new
        {
            tmdbId = movie.TmdbId,
            title = movie.Title,
            titleSlug = movie.TitleSlug,
            images = movie.Images ?? [],
            year = movie.Year,
            qualityProfileId = req.QualityProfileId ?? defaultProfileId,
            rootFolderPath = req.RootFolderPath ?? autoRootFolder,
            monitored = req.Monitored,
            addOptions = new { searchForMovie = req.SearchForMovie }
        };

        var addResult = await radarr.AddMovieAsync(body, ct).ConfigureAwait(false);
        if (!addResult.IsSuccess)
            return new AddMovieResult(req.TmdbId, movie.Title, movie.Year, false, null, addResult.Error);

        return new AddMovieResult(req.TmdbId, addResult.Value!.Title, addResult.Value.Year, true, addResult.Value.Id, null);
    }

    private static string GetSortTitle(string originalTitle)
    {
        var title = originalTitle.Trim();

        // Apostrophe-terminated articles: strip prefix up to and including the apostrophe
        string[] apostropheArticles = ["l'", "un'"];
        foreach (var article in apostropheArticles)
        {
            if (title.StartsWith(article, StringComparison.OrdinalIgnoreCase))
                return title[article.Length..].Trim();
        }

        // Space-separated articles: strip article + leading whitespace
        string[] spaceArticles = ["the", "an", "a", "il", "lo", "la", "gli", "le", "i", "uno", "una", "un"];
        foreach (var article in spaceArticles)
        {
            if (title.Length > article.Length &&
                title.StartsWith(article, StringComparison.OrdinalIgnoreCase) &&
                char.IsWhiteSpace(title[article.Length]))
            {
                return title[article.Length..].TrimStart();
            }
        }

        return title;
    }
}
