using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;
using RadarrMcp.Options;
using RadarrMcp.Services;

var builder = Host.CreateApplicationBuilder(args);

// ── Configuration ──────────────────────────────────────────────────────────
// Host.CreateApplicationBuilder already adds environment variables.
// Bind from env vars using double-underscore hierarchy separator:
//   RADARR__URL, RADARR__API_KEY, RADARR__TIMEOUT_MS

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
    .AddHttpClient<RadarrClient>((sp, client) =>
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

// ── Background services ───────────────────────────────────────────────────
builder.Services.AddHostedService<RadarrHealthCheckService>();

// ── MCP server ────────────────────────────────────────────────────────────
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

var app = builder.Build();
await app.RunAsync();
