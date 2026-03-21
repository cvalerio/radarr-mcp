using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for retrieving full details of a single library movie.</summary>
[McpServerToolType]
public sealed class GetMovieDetailsTool(IRadarrClient radarr)
{
    /// <summary>
    /// Returns full details of a specific movie in the Radarr library by its Radarr ID.
    /// </summary>
    [McpServerTool(Name = "radarr_get_movie_details")]
    [Description("Get full details of a specific movie in the Radarr library by its Radarr ID.")]
    public async Task<string> GetMovieDetailsAsync(
        [Description("The Radarr-assigned numeric ID of the movie.")] int radarrId,
        CancellationToken cancellationToken = default)
    {
        if (radarrId <= 0)
            return ToolHelpers.ErrorJson("radarr_get_movie_details", "radarrId must be a positive integer.");

        var result = await radarr.GetMovieAsync(radarrId, cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_get_movie_details", result.Error!);

        return ToolHelpers.ToJson(result.Value!);
    }
}
