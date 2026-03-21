using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for inspecting the Radarr download queue.</summary>
[McpServerToolType]
public sealed class GetQueueTool(IRadarrClient radarr)
{
    /// <summary>
    /// Returns the current Radarr download queue including active downloads and pending items.
    /// </summary>
    [McpServerTool(Name = "radarr_get_queue")]
    [Description("Get current download queue (active downloads and pending items).")]
    public async Task<string> GetQueueAsync(
        [Description("Whether to include movie details in each queue record (default: true).")] bool includeMovie = true,
        CancellationToken cancellationToken = default)
    {
        var result = await radarr.GetQueueAsync(includeMovie, cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_get_queue", result.Error!);

        var records = result.Value?.Records ?? [];

        var items = records.Select(r => new QueueItem(
            RadarrId: r.MovieId,
            Title: r.Movie?.Title ?? r.Title,
            Year: r.Movie?.Year,
            Status: r.Status,
            Timeleft: r.Timeleft,
            Size: r.Size,
            Sizeleft: r.Sizeleft,
            Protocol: r.Protocol,
            DownloadClient: r.DownloadClient,
            ErrorMessage: r.ErrorMessage)).ToList();

        return ToolHelpers.ToJson(items);
    }
}
