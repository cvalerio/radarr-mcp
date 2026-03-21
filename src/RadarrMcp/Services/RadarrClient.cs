using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RadarrMcp.Models;
using RadarrMcp.Options;

namespace RadarrMcp.Services;

/// <summary>
/// Typed HTTP client for the Radarr v3 API.
/// All methods return <see cref="Result{T}"/> — exceptions are never propagated.
/// </summary>
public sealed class RadarrClient : IRadarrClient
{
    private readonly HttpClient _http;
    private readonly RadarrOptions _options;
    private readonly ILogger<RadarrClient> _logger;

    private static readonly JsonSerializerOptions DeserializeOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        TypeInfoResolver = RadarrJsonContext.Default
    };

    /// <summary>Initializes a new instance of <see cref="RadarrClient"/>.</summary>
    public RadarrClient(HttpClient http, IOptions<RadarrOptions> options, ILogger<RadarrClient> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    // ── Health ────────────────────────────────────────────────────────────────

    /// <summary>Returns Radarr system status. Used as a startup health check.</summary>
    public Task<Result<RadarrSystemStatus>> GetSystemStatusAsync(CancellationToken ct = default)
        => GetAsync<RadarrSystemStatus>("/api/v3/system/status", ct);

    /// <summary>Returns all active health check warnings/errors.</summary>
    public Task<Result<List<RadarrHealthCheck>>> GetHealthAsync(CancellationToken ct = default)
        => GetAsync<List<RadarrHealthCheck>>("/api/v3/health", ct);

    // ── Movie lookup / library ────────────────────────────────────────────────

    /// <summary>Searches TMDB via Radarr for movies matching the given term.</summary>
    public Task<Result<List<RadarrLookupResult>>> LookupMoviesAsync(string term, CancellationToken ct = default)
        => GetAsync<List<RadarrLookupResult>>($"/api/v3/movie/lookup?term={Uri.EscapeDataString(term)}", ct);

    /// <summary>Searches for a single movie by TMDB ID via the lookup endpoint.</summary>
    public Task<Result<List<RadarrLookupResult>>> LookupMovieByTmdbIdAsync(int tmdbId, CancellationToken ct = default)
        => GetAsync<List<RadarrLookupResult>>($"/api/v3/movie/lookup?term=tmdb:{tmdbId}", ct);

    /// <summary>Returns all movies in the library.</summary>
    public Task<Result<List<RadarrMovie>>> GetLibraryAsync(CancellationToken ct = default)
        => GetAsync<List<RadarrMovie>>("/api/v3/movie", ct);

    /// <summary>Returns full details of a single movie by its Radarr ID.</summary>
    public Task<Result<RadarrMovie>> GetMovieAsync(int radarrId, CancellationToken ct = default)
        => GetAsync<RadarrMovie>($"/api/v3/movie/{radarrId}", ct);

    // ── Add / update / delete ─────────────────────────────────────────────────

    /// <summary>Adds a movie to the Radarr library.</summary>
    public Task<Result<RadarrMovie>> AddMovieAsync(object body, CancellationToken ct = default)
        => PostAsync<RadarrMovie>("/api/v3/movie", body, ct);

    /// <summary>Updates an existing movie (full resource PUT).</summary>
    public Task<Result<RadarrMovie>> UpdateMovieAsync(int radarrId, RadarrMovie body, CancellationToken ct = default)
        => PutAsync<RadarrMovie>($"/api/v3/movie/{radarrId}", body, ct);

    /// <summary>Deletes a movie from the library, optionally removing its files.</summary>
    public Task<Result<bool>> DeleteMovieAsync(int radarrId, bool deleteFiles, bool addImportExclusion, CancellationToken ct = default)
        => DeleteAsync($"/api/v3/movie/{radarrId}?deleteFiles={deleteFiles}&addImportExclusion={addImportExclusion}", ct);

    // ── Queue ─────────────────────────────────────────────────────────────────

    /// <summary>Returns the current download queue.</summary>
    public Task<Result<RadarrQueueResponse>> GetQueueAsync(bool includeMovie, CancellationToken ct = default)
        => GetAsync<RadarrQueueResponse>($"/api/v3/queue?includeMovie={includeMovie}&pageSize=100", ct);

    // ── Profiles / root folders ───────────────────────────────────────────────

    /// <summary>Returns all configured quality profiles.</summary>
    public Task<Result<List<RadarrQualityProfile>>> GetQualityProfilesAsync(CancellationToken ct = default)
        => GetAsync<List<RadarrQualityProfile>>("/api/v3/qualityprofile", ct);

    /// <summary>Returns all configured root folders.</summary>
    public Task<Result<List<RadarrRootFolder>>> GetRootFoldersAsync(CancellationToken ct = default)
        => GetAsync<List<RadarrRootFolder>>("/api/v3/rootfolder", ct);

    // ── Private HTTP helpers ──────────────────────────────────────────────────

    private async Task<Result<T>> GetAsync<T>(string path, CancellationToken ct)
    {
        try
        {
            var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
            return await ParseResponseAsync<T>(response, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HandleException<T>(ex, path);
        }
    }

    private async Task<Result<T>> PostAsync<T>(string path, object body, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(body);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(path, content, ct).ConfigureAwait(false);
            return await ParseResponseAsync<T>(response, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HandleException<T>(ex, path);
        }
    }

    private async Task<Result<T>> PutAsync<T>(string path, T body, CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(body, DeserializeOptions);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var request = new HttpRequestMessage(HttpMethod.Put, path) { Content = content };
            var response = await _http.SendAsync(request, ct).ConfigureAwait(false);
            return await ParseResponseAsync<T>(response, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return HandleException<T>(ex, path);
        }
    }

    private async Task<Result<bool>> DeleteAsync(string path, CancellationToken ct)
    {
        try
        {
            var response = await _http.DeleteAsync(path, ct).ConfigureAwait(false);
            if (response.IsSuccessStatusCode) return Result<bool>.Ok(true);
            var errorBody = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return Result<bool>.Fail(FormatHttpError(response.StatusCode, errorBody, path));
        }
        catch (Exception ex)
        {
            return HandleException<bool>(ex, path);
        }
    }

    private async Task<Result<T>> ParseResponseAsync<T>(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode)
        {
            var value = await response.Content.ReadFromJsonAsync<T>(DeserializeOptions, ct).ConfigureAwait(false);
            return value is not null
                ? Result<T>.Ok(value)
                : Result<T>.Fail("Radarr returned an empty response.");
        }

        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var message = FormatHttpError(response.StatusCode, body, response.RequestMessage?.RequestUri?.PathAndQuery ?? string.Empty);
        _logger.LogWarning("Radarr API error {StatusCode} on {Path}: {Body}", (int)response.StatusCode, response.RequestMessage?.RequestUri?.PathAndQuery, body);
        return Result<T>.Fail(message);
    }

    private string FormatHttpError(HttpStatusCode statusCode, string body, string path) =>
        statusCode switch
        {
            HttpStatusCode.Unauthorized => "Authentication failed. Check RADARR_API_KEY.",
            HttpStatusCode.NotFound => "Movie/resource not found in Radarr.",
            HttpStatusCode.Conflict => "Movie already exists in library.",
            HttpStatusCode.UnprocessableEntity => $"Invalid request: {body}",
            _ => $"Radarr API error {(int)statusCode}: {body}"
        };

    private Result<T> HandleException<T>(Exception ex, string path)
    {
        var message = ex switch
        {
            TaskCanceledException or TimeoutException =>
                $"Request timed out after {_options.TimeoutMs}ms. Is Radarr reachable?",
            HttpRequestException =>
                $"Cannot connect to Radarr at {_options.Url}. Check RADARR_URL.",
            _ => $"Unexpected error calling Radarr: {ex.Message}"
        };
        _logger.LogWarning(ex, "RadarrClient error on {Path}", path);
        return Result<T>.Fail(message);
    }
}

/// <summary>Discriminated union result type wrapping a success value or an error message.</summary>
/// <typeparam name="T">The success value type.</typeparam>
public sealed record Result<T>
{
    /// <summary>The success value, present when <see cref="IsSuccess"/> is true.</summary>
    public T? Value { get; private init; }

    /// <summary>The error message, present when <see cref="IsSuccess"/> is false.</summary>
    public string? Error { get; private init; }

    /// <summary>True when the operation succeeded and <see cref="Value"/> is populated.</summary>
    public bool IsSuccess { get; private init; }

    private Result() { }

    /// <summary>Creates a successful result with the given value.</summary>
    public static Result<T> Ok(T value) => new() { Value = value, IsSuccess = true };

    /// <summary>Creates a failed result with the given error message.</summary>
    public static Result<T> Fail(string error) => new() { Error = error, IsSuccess = false };
}
