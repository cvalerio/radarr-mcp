using System.Text.Json;
using RadarrMcp.Models;

namespace RadarrMcp.Tools;

/// <summary>Shared helpers for all MCP tool classes.</summary>
internal static class ToolHelpers
{
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        TypeInfoResolver = RadarrJsonContext.Default,
        WriteIndented = false,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>Serializes <paramref name="value"/> to a JSON string.</summary>
    internal static string ToJson<T>(T value) =>
        JsonSerializer.Serialize(value, typeof(T), RadarrJsonContext.Default);

    /// <summary>Returns a JSON error envelope for the given tool name and message.</summary>
    internal static string ErrorJson(string toolName, string message) =>
        ToJson(new ErrorResponse(message, toolName));

    private const int MaxExceptionLength = 2000;

    /// <summary>True for Radarr command statuses that will not change any more.</summary>
    internal static bool IsTerminalCommandStatus(string? status) =>
        status is not null && (status.Equals("completed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || status.Equals("aborted", StringComparison.OrdinalIgnoreCase)
            || status.Equals("cancelled", StringComparison.OrdinalIgnoreCase)
            || status.Equals("orphaned", StringComparison.OrdinalIgnoreCase));

    /// <summary>Command duration in seconds, from Radarr's "duration" (hh:mm:ss.fffffff) or ended − started.</summary>
    internal static double? CommandDurationSeconds(RadarrCommandStatus command)
    {
        if (command.Duration is not null &&
            TimeSpan.TryParse(command.Duration, System.Globalization.CultureInfo.InvariantCulture, out var duration))
            return Math.Round(duration.TotalSeconds, 1);
        if (command.Started is { } started && command.Ended is { } ended)
            return Math.Round((ended - started).TotalSeconds, 1);
        return null;
    }

    /// <summary>First line of the command's exception (Radarr stores the full stack trace), or null.</summary>
    internal static string? CommandErrorMessage(RadarrCommandStatus command)
    {
        if (string.IsNullOrWhiteSpace(command.Exception)) return null;
        var firstLine = command.Exception.Split('\n', 2)[0].Trim();
        return firstLine.Length == 0 ? null : firstLine;
    }

    /// <summary>Maps a Radarr command to the compact shape returned to MCP clients (exception capped in length).</summary>
    internal static CommandStatusResult ToCommandStatusResult(RadarrCommandStatus command)
    {
        var exception = string.IsNullOrWhiteSpace(command.Exception) ? null
            : command.Exception.Length <= MaxExceptionLength ? command.Exception
            : command.Exception[..MaxExceptionLength] + "…";
        return new CommandStatusResult(
            command.Id, command.Name, command.Status, command.Result,
            command.Queued, command.Started, command.Ended, command.Duration, CommandDurationSeconds(command),
            command.Message, CommandErrorMessage(command), exception);
    }
}
