using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for retrieving Radarr system status and health checks.</summary>
[McpServerToolType]
public sealed class GetSystemStatusTool(RadarrClient radarr)
{
    /// <summary>
    /// Returns Radarr system information (version, OS, Docker flag) and any active health check warnings.
    /// </summary>
    [McpServerTool(Name = "radarr_get_system_status")]
    [Description("Get Radarr system status, version, and health checks.")]
    public async Task<string> GetSystemStatusAsync(CancellationToken cancellationToken = default)
    {
        var statusTask = radarr.GetSystemStatusAsync(cancellationToken);
        var healthTask = radarr.GetHealthAsync(cancellationToken);

        await Task.WhenAll(statusTask, healthTask).ConfigureAwait(false);

        if (!statusTask.Result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_get_system_status", statusTask.Result.Error!);

        var status = statusTask.Result.Value!;
        var health = healthTask.Result.IsSuccess ? healthTask.Result.Value : null;

        var response = new SystemStatusResponse(
            Version: status.Version,
            BuildTime: status.BuildTime,
            IsDocker: status.IsDocker,
            AppData: status.AppData,
            OsName: status.OsName,
            RuntimeVersion: status.RuntimeVersion,
            Health: health);

        return ToolHelpers.ToJson(response);
    }
}
