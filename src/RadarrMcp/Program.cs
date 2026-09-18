using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Polly;
using RadarrMcp.Options;
using RadarrMcp.Services;

var builder = Host.CreateApplicationBuilder(args);

// ── Logging ────────────────────────────────────────────────────────────────
// stdout is reserved for the MCP stdio protocol: send every console log to stderr.
// Set in code, not only in appsettings.json, because that file is resolved from the
// current working directory and MCP clients may launch the server from anywhere.
builder.Logging.AddConsole(options => options.LogToStandardErrorThreshold = LogLevel.Trace);

// ── Configuration ──────────────────────────────────────────────────────────
// Host.CreateApplicationBuilder already adds environment variables.
// Bind from env vars using double-underscore hierarchy separator:
//   RADARR__URL, RADARR__APIKEY, RADARR__TIMEOUTMS, RADARR__MOVETIMEOUTMS

builder.Services
    .AddOptions<RadarrOptions>()
    .Bind(builder.Configuration.GetSection("Radarr"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// ── HTTP client ────────────────────────────────────────────────────────────
// Read timeout from config at setup time — IOptions<T> is not yet resolvable here.
var timeoutMs = int.TryParse(builder.Configuration["Radarr:TimeoutMs"], out var ms) ? ms : 15_000;
var perAttempt = TimeSpan.FromMilliseconds(timeoutMs);

builder.Services
    .AddHttpClient<IRadarrClient, RadarrClient>((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<RadarrOptions>>().Value;
        client.BaseAddress = new Uri(opts.Url.TrimEnd('/'));
        client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
        client.Timeout = Timeout.InfiniteTimeSpan; // Polly manages per-request timeout
    })
    .AddStandardResilienceHandler(options =>
    {
        options.AttemptTimeout.Timeout = perAttempt;
        options.TotalRequestTimeout.Timeout = perAttempt * 3;
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.BackoffType = DelayBackoffType.Exponential;
    });

// Slow client for endpoints that can take minutes (e.g. root folder unmapped scan).
builder.Services.AddHttpClient("RadarrSlow", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<RadarrOptions>>().Value;
    client.BaseAddress = new Uri(opts.Url.TrimEnd('/'));
    client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Move client for the bulk movie editor. No retry pipeline: re-sending a move is not safe.
builder.Services.AddHttpClient("RadarrMove", (sp, client) =>
{
    var opts = sp.GetRequiredService<IOptions<RadarrOptions>>().Value;
    client.BaseAddress = new Uri(opts.Url.TrimEnd('/'));
    client.DefaultRequestHeaders.Add("X-Api-Key", opts.ApiKey);
    client.Timeout = TimeSpan.FromMilliseconds(opts.MoveTimeoutMs);
});

// Injected into tools that wait (radarr_move_movies waitForCompletion) so tests can fake the clock.
builder.Services.AddSingleton(TimeProvider.System);

// ── Background services ───────────────────────────────────────────────────
builder.Services.AddHostedService<RadarrHealthCheckService>();

// ── MCP server ────────────────────────────────────────────────────────────
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();
