using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for searching movies via Radarr's TMDB integration.</summary>
[McpServerToolType]
public sealed class SearchMovieTool(RadarrClient radarr)
{
    private static readonly SemaphoreSlim _searchSemaphore = new(10, 10);

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

        var (items, error) = await ExecuteSearchAsync(query, limit, cancellationToken);
        return error is not null
            ? ToolHelpers.ErrorJson("radarr_search_movie", error)
            : ToolHelpers.ToJson(items);
    }

    /// <summary>Search for multiple movies in parallel.</summary>
    [McpServerTool(Name = "radarr_multi_search_movie")]
    [Description("Search for multiple movies in parallel. Use instead of calling radarr_search_movie repeatedly. Accepts a JSON array of search requests.")]
    public async Task<string> MultiSearchMovieAsync(
        [Description("JSON array of search requests. Each element has: query (string, required) and limit (int, optional, default 5, max 20).")] string searchesJson,
        CancellationToken cancellationToken = default)
    {
        List<MultiSearchRequest>? requests;
        try
        {
            requests = JsonSerializer.Deserialize(searchesJson, RadarrJsonContext.Default.ListMultiSearchRequest);
        }
        catch (JsonException ex)
        {
            return ToolHelpers.ErrorJson("radarr_multi_search_movie", $"Invalid JSON: {ex.Message}");
        }

        if (requests is null || requests.Count == 0)
            return ToolHelpers.ToJson(new List<MultiSearchResult>());

        if (requests.Count > 50)
            requests = requests.Take(50).ToList();

        var tasks = requests.Select(async req =>
        {
            if (string.IsNullOrWhiteSpace(req.Query))
                return new MultiSearchResult(req.Query ?? "", [], "query is empty, skipped");

            var limit = Math.Clamp(req.Limit, 1, 20);
            await _searchSemaphore.WaitAsync(cancellationToken);
            try
            {
                var (results, error) = await ExecuteSearchAsync(req.Query, limit, cancellationToken);
                return new MultiSearchResult(req.Query, results, error);
            }
            catch (Exception ex)
            {
                return new MultiSearchResult(req.Query, [], ex.Message);
            }
            finally
            {
                _searchSemaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        return ToolHelpers.ToJson(results.ToList());
    }

    private async Task<(List<MovieSearchResult> Results, string? Error)> ExecuteSearchAsync(
        string query, int limit, CancellationToken cancellationToken)
    {
        var result = await radarr.LookupMoviesAsync(query, cancellationToken);
        if (!result.IsSuccess)
            return ([], result.Error!);

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

        return (items, null);
    }
}
