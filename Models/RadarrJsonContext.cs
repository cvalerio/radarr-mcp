using System.Text.Json.Serialization;
using RadarrMcp.Models;

namespace RadarrMcp.Models;

/// <summary>Source-generated JSON serializer context for all Radarr models.</summary>
[JsonSerializable(typeof(RadarrMovie))]
[JsonSerializable(typeof(List<RadarrMovie>))]
[JsonSerializable(typeof(RadarrLookupResult))]
[JsonSerializable(typeof(List<RadarrLookupResult>))]
[JsonSerializable(typeof(RadarrQualityProfile))]
[JsonSerializable(typeof(List<RadarrQualityProfile>))]
[JsonSerializable(typeof(RadarrRootFolder))]
[JsonSerializable(typeof(List<RadarrRootFolder>))]
[JsonSerializable(typeof(RadarrQueueResponse))]
[JsonSerializable(typeof(RadarrQueueRecord))]
[JsonSerializable(typeof(List<RadarrQueueRecord>))]
[JsonSerializable(typeof(RadarrSystemStatus))]
[JsonSerializable(typeof(RadarrHealthCheck))]
[JsonSerializable(typeof(List<RadarrHealthCheck>))]
[JsonSerializable(typeof(MovieSearchResult))]
[JsonSerializable(typeof(List<MovieSearchResult>))]
[JsonSerializable(typeof(LibraryMovie))]
[JsonSerializable(typeof(List<LibraryMovie>))]
[JsonSerializable(typeof(AddMovieResult))]
[JsonSerializable(typeof(List<AddMovieResult>))]
[JsonSerializable(typeof(QueueItem))]
[JsonSerializable(typeof(List<QueueItem>))]
[JsonSerializable(typeof(SystemStatusResponse))]
[JsonSerializable(typeof(ErrorResponse))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
public sealed partial class RadarrJsonContext : JsonSerializerContext { }
