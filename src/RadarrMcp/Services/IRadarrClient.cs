using RadarrMcp.Models;

namespace RadarrMcp.Services;

/// <summary>
/// Abstraction over the Radarr v3 HTTP API, enabling testability via substitution.
/// </summary>
public interface IRadarrClient
{
    // ── Health ────────────────────────────────────────────────────────────────

    /// <summary>Returns Radarr system status. Used as a startup health check.</summary>
    Task<Result<RadarrSystemStatus>> GetSystemStatusAsync(CancellationToken ct = default);

    /// <summary>Returns all active health check warnings/errors.</summary>
    Task<Result<List<RadarrHealthCheck>>> GetHealthAsync(CancellationToken ct = default);

    // ── Movie lookup / library ────────────────────────────────────────────────

    /// <summary>Searches TMDB via Radarr for movies matching the given term.</summary>
    Task<Result<List<RadarrLookupResult>>> LookupMoviesAsync(string term, CancellationToken ct = default);

    /// <summary>Searches for a single movie by TMDB ID via the lookup endpoint.</summary>
    Task<Result<List<RadarrLookupResult>>> LookupMovieByTmdbIdAsync(int tmdbId, CancellationToken ct = default);

    /// <summary>Returns all movies in the library.</summary>
    Task<Result<List<RadarrMovie>>> GetLibraryAsync(CancellationToken ct = default);

    /// <summary>Returns full details of a single movie by its Radarr ID.</summary>
    Task<Result<RadarrMovie>> GetMovieAsync(int radarrId, CancellationToken ct = default);

    // ── Add / update / delete ─────────────────────────────────────────────────

    /// <summary>Adds a movie to the Radarr library.</summary>
    Task<Result<RadarrMovie>> AddMovieAsync(object body, CancellationToken ct = default);

    /// <summary>Updates an existing movie (full resource PUT).</summary>
    Task<Result<RadarrMovie>> UpdateMovieAsync(int radarrId, RadarrMovie body, CancellationToken ct = default);

    /// <summary>Deletes a movie from the library, optionally removing its files.</summary>
    Task<Result<bool>> DeleteMovieAsync(int radarrId, bool deleteFiles, bool addImportExclusion, CancellationToken ct = default);

    // ── Queue ─────────────────────────────────────────────────────────────────

    /// <summary>Returns the current download queue.</summary>
    Task<Result<RadarrQueueResponse>> GetQueueAsync(bool includeMovie, CancellationToken ct = default);

    // ── Profiles / root folders ───────────────────────────────────────────────

    /// <summary>Returns all configured quality profiles.</summary>
    Task<Result<List<RadarrQualityProfile>>> GetQualityProfilesAsync(CancellationToken ct = default);

    /// <summary>Returns all configured root folders.</summary>
    Task<Result<List<RadarrRootFolder>>> GetRootFoldersAsync(CancellationToken ct = default);
}
