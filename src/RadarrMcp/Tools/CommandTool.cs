using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for sending commands to Radarr.</summary>
[McpServerToolType]
public sealed class CommandTool(IRadarrClient radarr)
{
    /// <summary>Sends a named command to Radarr and returns the queued command response.</summary>
    [McpServerTool(Name = "radarr_command")]
    [Description("Send a command to Radarr via POST /api/v3/command. Use this to trigger immediate actions like MoviesSearch, RescanMovie, RefreshMovie, etc.")]
    public async Task<string> SendCommandAsync(
        [Description("The Radarr command name, e.g. \"MoviesSearch\"")] string commandName,
        [Description("Additional arguments as a JSON object string merged into the command body, e.g. \"{\\\"movieIds\\\": [123, 456]}\"")] string? commandArgs = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return ToolHelpers.ErrorJson("radarr_command", "commandName is required.");

        JsonElement? args = null;
        if (!string.IsNullOrWhiteSpace(commandArgs))
        {
            try
            {
                using var doc = JsonDocument.Parse(commandArgs);
                args = doc.RootElement.Clone();
            }
            catch (JsonException)
            {
                return ToolHelpers.ErrorJson("radarr_command", "commandArgs must be a valid JSON object string.");
            }
        }

        var result = await radarr.SendCommandAsync(commandName, args, cancellationToken);
        if (!result.IsSuccess)
            return ToolHelpers.ErrorJson("radarr_command", result.Error!);

        return ToolHelpers.ToJson(result.Value!);
    }
}
