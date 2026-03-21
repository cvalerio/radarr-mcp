using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for updating multiple movies in the Radarr library in one call.</summary>
[McpServerToolType]
public sealed class UpdateMoviesTool(RadarrClient radarr)
{
    private static readonly SemaphoreSlim _updateSemaphore = new(10, 10);

    /// <summary>
    /// Updates monitored status or quality profile for multiple movies in parallel.
    /// </summary>
    [McpServerTool(Name = "radarr_update_movies")]
    [Description("Update monitored status or quality profile for multiple movies in parallel. Use instead of calling radarr_update_movie repeatedly. Accepts a JSON array of update requests.")]
    public async Task<string> UpdateMoviesAsync(
        [Description("JSON array of update requests. Each element has: radarrId (int, required), monitored (bool, optional), qualityProfileId (int, optional). At least one of monitored or qualityProfileId must be set per entry.")] string updatesJson,
        CancellationToken cancellationToken = default)
    {
        List<MultiUpdateRequest>? requests;
        try
        {
            requests = JsonSerializer.Deserialize(updatesJson, RadarrJsonContext.Default.ListMultiUpdateRequest);
        }
        catch (JsonException ex)
        {
            return ToolHelpers.ErrorJson("radarr_update_movies", $"Invalid JSON: {ex.Message}");
        }

        if (requests is null || requests.Count == 0)
            return ToolHelpers.ToJson(new List<MultiUpdateResult>());

        if (requests.Count > 50)
            requests = requests.Take(50).ToList();

        var tasks = requests.Select(async req =>
        {
            if (req.RadarrId <= 0)
                return new MultiUpdateResult(req.RadarrId, false, "radarrId must be a positive integer.", null);

            if (req.Monitored is null && req.QualityProfileId is null)
                return new MultiUpdateResult(req.RadarrId, false, "At least one of monitored or qualityProfileId must be provided.", null);

            await _updateSemaphore.WaitAsync(cancellationToken);
            try
            {
                return await ExecuteUpdateAsync(req, cancellationToken);
            }
            catch (Exception ex)
            {
                return new MultiUpdateResult(req.RadarrId, false, ex.Message, null);
            }
            finally
            {
                _updateSemaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        return ToolHelpers.ToJson(results.ToList());
    }

    private async Task<MultiUpdateResult> ExecuteUpdateAsync(MultiUpdateRequest req, CancellationToken cancellationToken)
    {
        var getResult = await radarr.GetMovieAsync(req.RadarrId, cancellationToken);
        if (!getResult.IsSuccess)
            return new MultiUpdateResult(req.RadarrId, false, getResult.Error!, null);

        var current = getResult.Value!;
        var updated = current with
        {
            Monitored = req.Monitored ?? current.Monitored,
            QualityProfileId = req.QualityProfileId ?? current.QualityProfileId
        };

        var putResult = await radarr.UpdateMovieAsync(req.RadarrId, updated, cancellationToken);
        return putResult.IsSuccess
            ? new MultiUpdateResult(req.RadarrId, true, null, putResult.Value!)
            : new MultiUpdateResult(req.RadarrId, false, putResult.Error!, null);
    }
}
