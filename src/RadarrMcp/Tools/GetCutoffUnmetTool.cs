using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for retrieving movies where the quality cutoff has not been met.</summary>
[McpServerToolType]
public sealed class GetCutoffUnmetTool(IRadarrClient radarr)
{
    /// <summary>Returns all monitored movies in the library where the quality cutoff has not been met.</summary>
    [McpServerTool(Name = "radarr_get_cutoff_unmet")]
    [Description("Returns all movies in the Radarr library where the quality cutoff has not been met (i.e. a file exists but at lower quality than the profile cutoff). Returns radarrId, title, year, qualityProfileId, and current file quality for each.")]
    public async Task<string> GetCutoffUnmetAsync(CancellationToken cancellationToken = default)
    {
        var result = await radarr.GetCutoffUnmetAsync(cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_get_cutoff_unmet", result.Error!);

        var movies = result.Value ?? [];
        var mapped = movies.Select(m => new CutoffUnmetMovie(
            RadarrId: m.Id,
            Title: m.Title,
            Year: m.Year,
            QualityProfileId: m.QualityProfileId,
            Quality: m.MovieFile?.Quality?.Quality?.Name,
            Resolution: m.MovieFile?.Quality?.Quality?.Resolution)).ToList();

        return ToolHelpers.ToJson(mapped);
    }
}
