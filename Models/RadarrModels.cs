using System.Text.Json.Serialization;

namespace RadarrMcp.Models;

// ── Shared primitives ────────────────────────────────────────────────────────

/// <summary>Represents a Radarr image (poster, fanart, etc.).</summary>
public sealed record RadarrImage(
    [property: JsonPropertyName("coverType")] string CoverType,
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("remoteUrl")] string? RemoteUrl);

/// <summary>Rating info from TMDB or IMDB.</summary>
public sealed record RadarrRatings(
    [property: JsonPropertyName("imdb")] RadarrRatingValue? Imdb,
    [property: JsonPropertyName("tmdb")] RadarrRatingValue? Tmdb,
    [property: JsonPropertyName("rottenTomatoes")] RadarrRatingValue? RottenTomatoes,
    [property: JsonPropertyName("metacritic")] RadarrRatingValue? Metacritic);

/// <summary>Single rating value.</summary>
public sealed record RadarrRatingValue(
    [property: JsonPropertyName("votes")] int Votes,
    [property: JsonPropertyName("value")] double Value,
    [property: JsonPropertyName("type")] string? Type);

/// <summary>Media file info attached to a movie.</summary>
public sealed record RadarrMediaFile(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("quality")] RadarrQualityWrapper? Quality);

/// <summary>Quality wrapper (contains nested quality object).</summary>
public sealed record RadarrQualityWrapper(
    [property: JsonPropertyName("quality")] RadarrQualityInfo? Quality);

/// <summary>Quality profile info.</summary>
public sealed record RadarrQualityInfo(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

// ── Full movie resource (returned by GET /api/v3/movie and /api/v3/movie/{id}) ─

/// <summary>Full Radarr movie resource.</summary>
public sealed record RadarrMovie(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tmdbId")] int TmdbId,
    [property: JsonPropertyName("imdbId")] string? ImdbId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("originalTitle")] string? OriginalTitle,
    [property: JsonPropertyName("titleSlug")] string? TitleSlug,
    [property: JsonPropertyName("sortTitle")] string? SortTitle,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("genres")] List<string>? Genres,
    [property: JsonPropertyName("runtime")] int Runtime,
    [property: JsonPropertyName("studio")] string? Studio,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("hasFile")] bool HasFile,
    [property: JsonPropertyName("sizeOnDisk")] long SizeOnDisk,
    [property: JsonPropertyName("qualityProfileId")] int QualityProfileId,
    [property: JsonPropertyName("rootFolderPath")] string? RootFolderPath,
    [property: JsonPropertyName("path")] string? Path,
    [property: JsonPropertyName("added")] DateTime? Added,
    [property: JsonPropertyName("inCinemas")] DateTime? InCinemas,
    [property: JsonPropertyName("physicalRelease")] DateTime? PhysicalRelease,
    [property: JsonPropertyName("digitalRelease")] DateTime? DigitalRelease,
    [property: JsonPropertyName("images")] List<RadarrImage>? Images,
    [property: JsonPropertyName("movieFile")] RadarrMediaFile? MovieFile,
    [property: JsonPropertyName("ratings")] RadarrRatings? Ratings);

// ── Search result (GET /api/v3/movie/lookup) ────────────────────────────────

/// <summary>Raw lookup result from Radarr — identical shape to RadarrMovie for in-library items.</summary>
public sealed record RadarrLookupResult(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("tmdbId")] int TmdbId,
    [property: JsonPropertyName("imdbId")] string? ImdbId,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("originalTitle")] string? OriginalTitle,
    [property: JsonPropertyName("titleSlug")] string? TitleSlug,
    [property: JsonPropertyName("year")] int Year,
    [property: JsonPropertyName("overview")] string? Overview,
    [property: JsonPropertyName("genres")] List<string>? Genres,
    [property: JsonPropertyName("runtime")] int Runtime,
    [property: JsonPropertyName("studio")] string? Studio,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("monitored")] bool Monitored,
    [property: JsonPropertyName("hasFile")] bool HasFile,
    [property: JsonPropertyName("images")] List<RadarrImage>? Images,
    [property: JsonPropertyName("ratings")] RadarrRatings? Ratings);

// ── Quality profile ──────────────────────────────────────────────────────────

/// <summary>A Radarr quality profile.</summary>
public sealed record RadarrQualityProfile(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("name")] string Name);

// ── Root folder ──────────────────────────────────────────────────────────────

/// <summary>A configured root folder in Radarr.</summary>
public sealed record RadarrRootFolder(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("freeSpace")] long FreeSpace);

// ── Queue ────────────────────────────────────────────────────────────────────

/// <summary>Wrapper returned by GET /api/v3/queue.</summary>
public sealed record RadarrQueueResponse(
    [property: JsonPropertyName("totalRecords")] int TotalRecords,
    [property: JsonPropertyName("records")] List<RadarrQueueRecord>? Records);

/// <summary>A single queue entry.</summary>
public sealed record RadarrQueueRecord(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("movieId")] int? MovieId,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("status")] string? Status,
    [property: JsonPropertyName("timeleft")] string? Timeleft,
    [property: JsonPropertyName("size")] double Size,
    [property: JsonPropertyName("sizeleft")] double Sizeleft,
    [property: JsonPropertyName("protocol")] string? Protocol,
    [property: JsonPropertyName("downloadClient")] string? DownloadClient,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage,
    [property: JsonPropertyName("movie")] RadarrMovie? Movie);

// ── System status ────────────────────────────────────────────────────────────

/// <summary>Radarr system status response.</summary>
public sealed record RadarrSystemStatus(
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("buildTime")] DateTime? BuildTime,
    [property: JsonPropertyName("isDocker")] bool IsDocker,
    [property: JsonPropertyName("appData")] string? AppData,
    [property: JsonPropertyName("osName")] string? OsName,
    [property: JsonPropertyName("runtimeVersion")] string? RuntimeVersion);

/// <summary>A single health check item from /api/v3/health.</summary>
public sealed record RadarrHealthCheck(
    [property: JsonPropertyName("source")] string? Source,
    [property: JsonPropertyName("type")] string? Type,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("wikiUrl")] string? WikiUrl);

// ── Tool output DTOs ─────────────────────────────────────────────────────────

/// <summary>Flattened movie search result returned to MCP clients.</summary>
public sealed record MovieSearchResult(
    int TmdbId, string? ImdbId, string Title, string? OriginalTitle,
    int Year, string? Overview, List<string>? Genres, int Runtime,
    string? Studio, string? Status, bool InLibrary, bool HasFile,
    bool Monitored, int? RadarrId);

/// <summary>Library movie summary returned to MCP clients.</summary>
public sealed record LibraryMovie(
    int RadarrId, int TmdbId, string? ImdbId, string Title, string? OriginalTitle,
    int Year, List<string>? Genres, string? Status, bool Monitored,
    bool HasFile, long SizeOnDisk, string? FilePath, string? Quality,
    int Runtime, string? Overview, DateTime? Added);

/// <summary>Result of a single movie add operation.</summary>
public sealed record AddMovieResult(
    int TmdbId, string? Title, int? Year, bool Success, int? RadarrId, string? Error);

/// <summary>Queue item summary returned to MCP clients.</summary>
public sealed record QueueItem(
    int? RadarrId, string? Title, int? Year, string? Status, string? Timeleft,
    double Size, double Sizeleft, string? Protocol, string? DownloadClient,
    string? ErrorMessage);

/// <summary>Combined system status + health response.</summary>
public sealed record SystemStatusResponse(
    string? Version, DateTime? BuildTime, bool IsDocker, string? AppData,
    string? OsName, string? RuntimeVersion, List<RadarrHealthCheck>? Health);

/// <summary>Generic error envelope returned when a tool call fails.</summary>
public sealed record ErrorResponse(string Error, string Tool);

// ── Multi-search DTOs ─────────────────────────────────────────────────────────

/// <summary>Single entry in a radarr_multi_search_movie request array.</summary>
public sealed record MultiSearchRequest(
    [property: JsonPropertyName("query")] string? Query,
    [property: JsonPropertyName("limit")] int Limit = 5);

/// <summary>Result for one entry in a radarr_multi_search_movie response array.</summary>
public sealed record MultiSearchResult(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("results")] List<MovieSearchResult> Results,
    [property: JsonPropertyName("error")] string? Error);
