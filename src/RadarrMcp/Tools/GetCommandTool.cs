using System.ComponentModel;
using ModelContextProtocol.Server;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for checking the status of a previously started Radarr command.</summary>
[McpServerToolType]
public sealed class GetCommandTool(IRadarrClient radarr)
{
    private const string ToolName = "radarr_get_command";

    /// <summary>Returns the compact status of a Radarr command via GET /api/v3/command/{id}.</summary>
    [McpServerTool(Name = ToolName)]
    [Description("""
        Check the status of a previously started Radarr command (e.g. the BulkMoveMovie returned in queuedCommands by radarr_move_movies, or a command sent with radarr_command). Use it to confirm that a move between different physical volumes has really finished.

        Returns id, name, status (queued|started|completed|failed|aborted), result, queued/started/ended timestamps, duration, the progress message and, on failure, errorMessage/exception. Note: for moves Radarr may report "completed" even when a single movie could not be transferred (it reverts that movie's path) — radarr_move_movies with waitForCompletion=true checks this for you.
        """)]
    public async Task<string> GetCommandAsync(
        [Description("Radarr command ID, e.g. queuedCommands[].id from radarr_move_movies or id from radarr_command.")] int commandId,
        CancellationToken cancellationToken = default)
    {
        if (commandId <= 0)
            return ToolHelpers.ErrorJson(ToolName, "commandId must be a positive integer.");

        var result = await radarr.GetCommandAsync(commandId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson(ToolName, result.Error!);

        if (result.Value is null)
            return ToolHelpers.ErrorJson(ToolName,
                $"Command {commandId} not found in Radarr. Radarr only keeps commands for a limited time, so a finished command may already have been purged; " +
                "for a move, check the movie paths with radarr_get_movie_details instead.");

        return ToolHelpers.ToJson(ToolHelpers.ToCommandStatusResult(result.Value));
    }
}
