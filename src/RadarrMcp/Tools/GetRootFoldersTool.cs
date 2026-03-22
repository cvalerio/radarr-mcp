using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for listing all configured root folders in Radarr.</summary>
[McpServerToolType]
public sealed class GetRootFoldersTool(IRadarrClient radarr)
{
    /// <summary>Lists all configured root folders with their IDs, paths, free space and unmapped folder counts.</summary>
    [McpServerTool(Name = "radarr_get_root_folders")]
    [Description("List all configured root folders with their IDs, paths, free space and count of unmapped folders.")]
    public async Task<string> GetRootFoldersAsync(CancellationToken cancellationToken = default)
    {
        var result = await radarr.GetRootFoldersAsync(cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_get_root_folders", result.Error!);

        var folders = (result.Value ?? []).Select(f => new RootFolderInfo(
            Id: f.Id,
            Path: f.Path,
            FreeSpace: f.FreeSpace,
            UnmappedFolderCount: f.UnmappedFolders?.Count ?? 0,
            Accessible: f.Accessible)).ToList();

        return ToolHelpers.ToJson(folders);
    }
}
