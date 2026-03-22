using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for scanning a root folder and returning all unmapped subdirectories.</summary>
[McpServerToolType]
public sealed class GetUnmappedFoldersTool(IRadarrClient radarr)
{
    /// <summary>Scans a root folder and returns all unmapped subdirectories not yet associated to any movie.</summary>
    [McpServerTool(Name = "radarr_get_unmapped_folders")]
    [Description("Scan a root folder and return all subdirectories on disk not yet associated to any movie in Radarr. First step of the Library Import flow.")]
    public async Task<string> GetUnmappedFoldersAsync(
        [Description("The numeric ID of the root folder to scan (obtain from radarr_get_root_folders).")] int rootFolderId,
        CancellationToken cancellationToken = default)
    {
        if (rootFolderId <= 0)
            return ToolHelpers.ErrorJson("radarr_get_unmapped_folders", "rootFolderId must be a positive integer.");

        var result = await radarr.GetRootFolderAsync(rootFolderId, cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_get_unmapped_folders", result.Error!);

        var folder = result.Value!;
        var unmapped = (folder.UnmappedFolders ?? [])
            .Select(u => new UnmappedFolderInfo(Name: u.Name, Path: u.Path, RelativePath: u.RelativePath))
            .ToList();

        return ToolHelpers.ToJson(new UnmappedFoldersResult(
            Id: folder.Id,
            Path: folder.Path,
            UnmappedFolders: unmapped));
    }
}
