using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for removing movies from the Radarr library.</summary>
[McpServerToolType]
public sealed class DeleteMovieTool(IRadarrClient radarr)
{
    /// <summary>
    /// Removes a movie from the Radarr library. Optionally deletes files from disk
    /// and/or adds the movie to the import exclusion list.
    /// </summary>
    [McpServerTool(Name = "radarr_delete_movie")]
    [Description("Remove a movie from the Radarr library. Optionally delete files from disk.")]
    public async Task<string> DeleteMovieAsync(
        [Description("The Radarr-assigned numeric ID of the movie to delete.")] int radarrId,
        [Description("If true, also deletes the movie files from disk (default: false).")] bool deleteFiles = false,
        [Description("If true, adds the movie to the import exclusion list so it won't be re-imported (default: false).")] bool addImportExclusion = false,
        CancellationToken cancellationToken = default)
    {
        if (radarrId <= 0)
            return ToolHelpers.ErrorJson("radarr_delete_movie", "radarrId must be a positive integer.");

        var result = await radarr.DeleteMovieAsync(radarrId, deleteFiles, addImportExclusion, cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_delete_movie", result.Error!);

        return ToolHelpers.ToJson(new { success = true });
    }
}
