using System.ComponentModel;
using System.Text.Json;
using System.Text.Json.Nodes;
using ModelContextProtocol.Server;
using RadarrMcp.Models;
using RadarrMcp.Services;

namespace RadarrMcp.Tools;

/// <summary>MCP tool for importing unmapped folders into the Radarr library.</summary>
[McpServerToolType]
public sealed class ImportMoviesTool(IRadarrClient radarr)
{
    private static readonly SemaphoreSlim _importSemaphore = new(5, 5);

    /// <summary>Imports movies from unmapped folders into Radarr using TMDB lookup and the import endpoint.</summary>
    [McpServerTool(Name = "radarr_import_movies")]
    [Description("""
        Import movies from unmapped folders into Radarr. For each folder, looks up the best TMDB match and attempts the import.

        IMPORTANT: If a movie already exists in Radarr (lookup returns id > 0), the import will FAIL — Radarr rejects it with a conflict error. In that case the tool returns a warning so the caller (user) can manually delete the existing entry first, then retry.

        This matches the exact UI behavior: the UI warns the user when a conflict exists and requires manual deletion before proceeding.
        """)]
    public async Task<string> ImportMoviesAsync(
        [Description("JSON array of import requests. Each element has: folderPath (string, required), folderName (string, required), qualityProfileId (int, optional), monitored (bool, optional, default true).")] string imports,
        CancellationToken cancellationToken = default)
    {
        List<ImportMovieRequest>? requests;
        try
        {
            requests = JsonSerializer.Deserialize(imports, RadarrJsonContext.Default.ListImportMovieRequest);
        }
        catch (JsonException ex)
        {
            return ToolHelpers.ErrorJson("radarr_import_movies", $"Invalid JSON: {ex.Message}");
        }

        if (requests is null || requests.Count == 0)
            return ToolHelpers.ToJson(new List<ImportMovieResult>());

        var tasks = requests.Select(async req =>
        {
            if (string.IsNullOrWhiteSpace(req.FolderPath))
                return new ImportMovieResult(req.FolderName ?? "", false, null, null, null, null, null, null, "folderPath is required.");
            if (string.IsNullOrWhiteSpace(req.FolderName))
                return new ImportMovieResult(req.FolderPath, false, null, null, null, null, null, null, "folderName is required.");

            await _importSemaphore.WaitAsync(cancellationToken);
            try
            {
                return await ExecuteImportAsync(req, cancellationToken);
            }
            catch (Exception ex)
            {
                return new ImportMovieResult(req.FolderName, false, null, null, null, null, null, null, ex.Message);
            }
            finally
            {
                _importSemaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(tasks);
        return ToolHelpers.ToJson(results.ToList());
    }

    private async Task<ImportMovieResult> ExecuteImportAsync(ImportMovieRequest req, CancellationToken ct)
    {
        // STEP 1: Lookup by folder name
        var lookupResult = await radarr.LookupMoviesRawAsync(req.FolderName, ct);
        if (!lookupResult.IsSuccess)
            return new ImportMovieResult(req.FolderName, false, null, null, null, null, null, null, lookupResult.Error);

        var elements = lookupResult.Value!;
        if (elements.Count == 0)
            return new ImportMovieResult(req.FolderName, false, null, null, null, null, null, null, "No TMDB match found.");

        var first = elements[0];

        // STEP 2: Check for conflict — id > 0 means already in Radarr
        var id = first.TryGetProperty("id", out var idProp) ? idProp.GetInt32() : 0;
        if (id > 0)
        {
            var existingTitle = first.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null;
            return new ImportMovieResult(
                FolderName: req.FolderName,
                Success: false,
                RadarrId: null,
                Title: null,
                Path: null,
                Conflict: true,
                ExistingRadarrId: id,
                ExistingTitle: existingTitle,
                Error: $"Movie already exists in Radarr (id={id}). Delete it first, then retry.");
        }

        // STEP 3: Mutate the lookup object and import
        var movieNode = JsonNode.Parse(first.GetRawText())!.AsObject();
        movieNode["path"] = req.FolderPath;
        movieNode["folderName"] = req.FolderPath;
        movieNode["monitored"] = req.Monitored ?? true;
        if (req.QualityProfileId.HasValue)
            movieNode["qualityProfileId"] = req.QualityProfileId.Value;
        movieNode["addOptions"] = new JsonObject
        {
            ["searchForMovie"] = false,
            ["addMethod"] = "manual",
            ["monitor"] = "movieOnly"
        };

        var mutated = JsonDocument.Parse(movieNode.ToJsonString()).RootElement.Clone();

        var importResult = await radarr.ImportMoviesAsync([mutated], ct);
        if (!importResult.IsSuccess)
            return new ImportMovieResult(req.FolderName, false, null, null, null, null, null, null, importResult.Error);

        var imported = importResult.Value!;
        if (imported.Count == 0)
            return new ImportMovieResult(req.FolderName, false, null, null, null, null, null, null, "Import returned empty response.");

        var movie = imported[0];
        return new ImportMovieResult(
            FolderName: req.FolderName,
            Success: true,
            RadarrId: movie.Id,
            Title: movie.Title,
            Path: movie.Path,
            Conflict: null,
            ExistingRadarrId: null,
            ExistingTitle: null,
            Error: null);
    }
}
