using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for retrieving full details of multiple library movies in parallel.</summary>
[McpServerToolType]
public sealed class MultiGetMovieDetailsTool(RadarrClient radarr)
{
    private static readonly SemaphoreSlim _semaphore = new(10, 10);

    /// <summary>Get full details of multiple movies in parallel by their Radarr IDs.</summary>
    [McpServerTool(Name = "radarr_multi_get_details")]
    [Description("Get full details of multiple movies in parallel by their Radarr IDs. Use instead of calling radarr_get_movie_details repeatedly.")]
    public async Task<string> MultiGetDetailsAsync(
        [Description("JSON array of Radarr movie IDs (integers), e.g. [407, 1513, 464]. Max 100 IDs per call.")] string radarrIdsJson,
        CancellationToken cancellationToken = default)
    {
        List<int>? ids;
        try
        {
            ids = JsonSerializer.Deserialize(radarrIdsJson, RadarrJsonContext.Default.ListInt32);
        }
        catch (JsonException ex)
        {
            return ToolHelpers.ErrorJson("radarr_multi_get_details", $"Invalid JSON: {ex.Message}");
        }

        if (ids is null || ids.Count == 0)
            return ToolHelpers.ToJson(new List<MultiGetDetailsResult>());

        if (ids.Count > 100)
            return ToolHelpers.ErrorJson("radarr_multi_get_details", "Too many IDs: max 100 per call");

        var tasks = ids.Select(async id =>
        {
            await _semaphore.WaitAsync(cancellationToken);
            try
            {
                var result = await radarr.GetMovieAsync(id, cancellationToken);
                return result.IsSuccess
                    ? new MultiGetDetailsResult(id, result.Value!, null)
                    : new MultiGetDetailsResult(id, null, result.Error!);
            }
            catch (Exception ex)
            {
                return new MultiGetDetailsResult(id, null, ex.Message);
            }
            finally
            {
                _semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        return ToolHelpers.ToJson(results.ToList());
    }
}
