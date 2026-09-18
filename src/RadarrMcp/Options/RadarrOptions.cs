using System.ComponentModel.DataAnnotations;

namespace RadarrMcp.Options;

/// <summary>Configuration options for the Radarr API connection.</summary>
public sealed class RadarrOptions
{
    /// <summary>Base URL of the Radarr instance, e.g. http://radarr:7878</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "RADARR__URL is required.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>Radarr API key from Settings → General → Security.</summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "RADARR__APIKEY is required.")]
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>Per-request timeout in milliseconds. Defaults to 15000.</summary>
    [Range(1000, 300_000)]
    public int TimeoutMs { get; set; } = 15_000;

    /// <summary>Timeout in milliseconds for the bulk move (movie editor) request. Defaults to 120000.</summary>
    [Range(1000, 1_800_000)]
    public int MoveTimeoutMs { get; set; } = 120_000;
}
