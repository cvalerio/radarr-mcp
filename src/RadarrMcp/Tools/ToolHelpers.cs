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
}
