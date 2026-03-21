using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for browsing the Radarr movie library.</summary>
[McpServerToolType]
public sealed class GetLibraryTool(RadarrClient radarr)
{
    /// <summary>
    /// Returns movies in the Radarr library with optional status filtering and title search.
    /// </summary>
    [McpServerTool(Name = "radarr_get_library")]
    [Description("Get movies in the Radarr library with optional filtering.")]
    public async Task<string> GetLibraryAsync(
        [Description("Filter: all | missing | downloaded | monitored | unmonitored (default: all).")] string filter = "all",
        [Description("Optional case-insensitive substring filter applied to movie titles.")] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var allowedFilters = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "all", "missing", "downloaded", "monitored", "unmonitored" };

        if (!allowedFilters.Contains(filter))
            return ToolHelpers.ErrorJson("radarr_get_library",
                $"filter must be one of: {string.Join(", ", allowedFilters)}.");

        var result = await radarr.GetLibraryAsync(cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_get_library", result.Error!);

        var movies = result.Value ?? [];

        IEnumerable<RadarrMovie> filtered = filter.ToLowerInvariant() switch
        {
            "missing"     => movies.Where(m => m.Monitored && !m.HasFile),
            "downloaded"  => movies.Where(m => m.HasFile),
            "monitored"   => movies.Where(m => m.Monitored),
            "unmonitored" => movies.Where(m => !m.Monitored),
            _             => movies
        };

        if (!string.IsNullOrWhiteSpace(search))
            filtered = filtered.Where(m => m.Title.Contains(search, StringComparison.OrdinalIgnoreCase));

        var mapped = filtered.Select(m => new LibraryMovie(
            RadarrId: m.Id,
            TmdbId: m.TmdbId,
            ImdbId: m.ImdbId,
            Title: m.Title,
            OriginalTitle: m.OriginalTitle,
            Year: m.Year,
            Genres: m.Genres,
            Status: m.Status,
            Monitored: m.Monitored,
            HasFile: m.HasFile,
            SizeOnDisk: m.SizeOnDisk,
            FilePath: m.MovieFile?.Path ?? m.Path,
            Quality: m.MovieFile?.Quality?.Quality?.Name,
            Runtime: m.Runtime,
            Overview: m.Overview,
            Added: m.Added)).ToList();

        return ToolHelpers.ToJson(mapped);
    }
}
