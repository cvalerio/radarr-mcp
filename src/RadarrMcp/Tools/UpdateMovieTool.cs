using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for updating movie settings in the Radarr library.</summary>
[McpServerToolType]
public sealed class UpdateMovieTool(IRadarrClient radarr)
{
    /// <summary>
    /// Updates the monitored status or quality profile of a movie already in the library.
    /// At least one of <paramref name="monitored"/> or <paramref name="qualityProfileId"/> must be provided.
    /// </summary>
    [McpServerTool(Name = "radarr_update_movie")]
    [Description("Update monitored status or quality profile of a movie already in the library.")]
    public async Task<string> UpdateMovieAsync(
        [Description("The Radarr-assigned numeric ID of the movie to update.")] int radarrId,
        [Description("Set to true to monitor, false to unmonitor. Omit to leave unchanged.")] bool? monitored = null,
        [Description("Quality profile ID to assign. Omit to leave unchanged.")] int? qualityProfileId = null,
        CancellationToken cancellationToken = default)
    {
        if (radarrId <= 0)
            return ToolHelpers.ErrorJson("radarr_update_movie", "radarrId must be a positive integer.");

        if (monitored is null && qualityProfileId is null)
            return ToolHelpers.ErrorJson("radarr_update_movie",
                "At least one of monitored or qualityProfileId must be provided.");

        // Fetch current state
        var getResult = await radarr.GetMovieAsync(radarrId, cancellationToken);
        if (!getResult.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_update_movie", getResult.Error!);

        var current = getResult.Value!;

        // Apply mutations — records are immutable so we use `with`
        var updated = current with
        {
            Monitored = monitored ?? current.Monitored,
            QualityProfileId = qualityProfileId ?? current.QualityProfileId
        };

        var putResult = await radarr.UpdateMovieAsync(radarrId, updated, cancellationToken);
        if (!putResult.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_update_movie", putResult.Error!);

        return ToolHelpers.ToJson(putResult.Value!);
    }
}
