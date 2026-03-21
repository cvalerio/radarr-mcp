using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace RadarrMcp.Services;

/// <summary>
/// Hosted service that performs a startup connectivity check against the Radarr API.
/// Failure is non-fatal — it logs a warning and allows the MCP server to start.
/// Individual tools will surface errors when Radarr is unreachable.
/// </summary>
public sealed class RadarrHealthCheckService : IHostedService
{
    private readonly IRadarrClient _client;
    private readonly ILogger<RadarrHealthCheckService> _logger;

    /// <summary>Initializes a new instance of <see cref="RadarrHealthCheckService"/>.</summary>
    public RadarrHealthCheckService(IRadarrClient client, ILogger<RadarrHealthCheckService> logger)
    {
        _client = client;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing Radarr startup health check...");
        var result = await _client.GetSystemStatusAsync(cancellationToken).ConfigureAwait(false);

        if (result.IsSuccess)
        {
            _logger.LogInformation(
                "Radarr reachable — version {Version}, docker={IsDocker}",
                result.Value?.Version, result.Value?.IsDocker);
        }
        else
        {
            _logger.LogWarning(
                "Radarr startup health check failed: {Error}. " +
                "The MCP server will start anyway; tools will return errors until Radarr is reachable.",
                result.Error);
        }
    }

    /// <inheritdoc/>
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
