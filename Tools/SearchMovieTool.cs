using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for searching movies via Radarr's TMDB integration.</summary>
[McpServerToolType]
public sealed class SearchMovieTool(RadarrClient radarr)
{
    /// <summary>
    /// Searches for movies by title via Radarr (which queries TMDB).
    /// Returns both movies already in the library and new candidates.
    /// </summary>
    [McpServerTool(Name = "radarr_search_movie")]
    [Description("Search for movies by title (searches TMDB via Radarr). Returns movies not yet in library and those already added.")]
    public async Task<string> SearchMovieAsync(
        [Description("Movie title to search for.")] string query,
        [Description("Maximum number of results to return (1-20, default 5).")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
            return ToolHelpers.ErrorJson("radarr_search_movie", "query must not be empty.");

        if (limit < 1 || limit > 20)
            return ToolHelpers.ErrorJson("radarr_search_movie", "limit must be between 1 and 20.");

        var result = await radarr.LookupMoviesAsync(query, cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_search_movie", result.Error!);

        var items = (result.Value ?? [])
            .Take(limit)
            .Select(r => new MovieSearchResult(
                TmdbId: r.TmdbId,
                ImdbId: r.ImdbId,
                Title: r.Title,
                OriginalTitle: r.OriginalTitle,
                Year: r.Year,
                Overview: r.Overview,
                Genres: r.Genres,
                Runtime: r.Runtime,
                Studio: r.Studio,
                Status: r.Status,
                InLibrary: r.Id > 0,
                HasFile: r.HasFile,
                Monitored: r.Monitored,
                RadarrId: r.Id > 0 ? r.Id : null))
            .ToList();

        return ToolHelpers.ToJson(items);
    }
}
