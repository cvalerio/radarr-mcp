# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A production-grade Model Context Protocol (MCP) server for Radarr v3, built with .NET 10. It exposes Radarr's HTTP API as MCP tools (stdio transport) so an MCP client (Claude Desktop, Claude Code, etc.) can search, add, monitor, and manage movies via natural language.

## Commands

```bash
# Build
dotnet build RadarrMCP.sln

# Run tests
dotnet test RadarrMCP.sln

# Run a single test
dotnet test RadarrMCP.sln --filter "FullyQualifiedName~ClassName.MethodName"

# Self-contained single-binary publish (linux-x64 / win-x64 / osx-arm64)
dotnet publish -c Release -r linux-x64 --self-contained
```

CI (`.github/workflows/ci.yml`) runs `dotnet restore`, `dotnet build -c Release`, `dotnet test -c Release` on push/PR to `main`.

Running the server locally requires `RADARR__URL` and `RADARR__APIKEY` env vars (see below) pointed at a real or test Radarr instance — there's no mock mode.

## Architecture

**Request flow:** MCP client → `[McpServerTool]` method in `Tools/*.cs` → `IRadarrClient` (injected) → `RadarrClient` (typed `HttpClient`) → Radarr v3 HTTP API.

- **`Program.cs`** — composition root. Uses `Host.CreateApplicationBuilder`. Registers `RadarrOptions` (bound from config + validated on start), two named `HttpClient`s (default with Polly resilience, `"RadarrSlow"` with a 5-minute timeout for the unmapped-folder scan endpoint), the `RadarrHealthCheckService` hosted service, and the MCP server (`AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()` — every `[McpServerToolType]` class in the assembly is auto-discovered, no manual tool registration).
- **`Tools/`** — one class per MCP tool surface, `[McpServerToolType]` + `[McpServerTool(Name = "radarr_...")]`. Tools validate their own inputs, call `IRadarrClient`, and return a **JSON string** (never throw to the client) via `ToolHelpers.ToJson` / `ToolHelpers.ErrorJson`. Tools that fan out to multiple Radarr calls (search, import, multi-get, multi-update) bound concurrency with a `static readonly SemaphoreSlim` and cap batch size (e.g. max 50 in `MultiSearchMovieAsync`).
- **`Services/IRadarrClient.cs` / `RadarrClient.cs`** — the only class that talks HTTP to Radarr. Every method returns `Result<T>` (`Services/RadarrClient.cs`, discriminated success/error record) — **exceptions never propagate out of `RadarrClient`**; they're caught and converted to a `Result.Fail` with a user-facing message via `HandleException`/`FormatHttpError`. When adding a new Radarr endpoint, follow this pattern rather than throwing.
- **`Models/RadarrModels.cs`** — all DTOs: both Radarr API wire models (`RadarrMovie`, `RadarrLookupResult`, etc.) and MCP tool response/request shapes (`MovieSearchResult`, `MultiUpdateRequest`, `ErrorResponse`, etc.).
- **`Models/RadarrJsonContext.cs`** — a single `JsonSerializerContext` (source-generated, `System.Text.Json`) listing every serializable type. **Any new model used in a tool request/response or in `RadarrClient` must be added here** (as both the bare type and `List<T>` if applicable) or serialization will fail at runtime with no compile-time warning.
- **`Options/RadarrOptions.cs`** — `Url`, `ApiKey`, `TimeoutMs`, validated via data annotations at startup (`ValidateOnStart`).

**Config:** bound from env vars using the .NET double-underscore hierarchy separator — `RADARR__URL`, `RADARR__APIKEY`, `RADARR__TIMEOUTMS` (default 15000ms). No underscore-to-underscore substitution: `RADARR__APIKEY` → `ApiKey`, not `RADARR__API_KEY`.

**Logging:** all logs go to **stderr** — stdout is reserved exclusively for the MCP stdio protocol. Never write to stdout directly.

**Resilience:** the default `HttpClient` uses `AddStandardResilienceHandler` (Polly) — 2 retries, exponential backoff, per-attempt timeout from `RadarrOptions.TimeoutMs`, total timeout = 3× per-attempt. The `"RadarrSlow"` named client (used only for `GetRootFolderAsync`'s unmapped-folder scan) bypasses this with a flat 5-minute timeout since that Radarr endpoint can take minutes to enumerate a large folder.

**Startup health check:** `RadarrHealthCheckService` pings `/api/v3/system/status` on start and logs a warning on failure — it does **not** abort startup. Radarr being unreachable at boot is not fatal; individual tool calls will surface the connection error to the client when invoked.

## Adding a new tool

1. Add the Radarr HTTP call to `IRadarrClient` + `RadarrClient` (returning `Result<T>`, following the existing `GetAsync`/`PostAsync`/`PutAsync`/`DeleteAsync` private helpers).
2. Add any new request/response DTOs to `Models/RadarrModels.cs`.
3. Register those DTOs in `Models/RadarrJsonContext.cs` (`[JsonSerializable]`, both bare and `List<T>` forms as needed).
4. Add a new `Tools/XyzTool.cs` (or a method on an existing tool class) with `[McpServerToolType]` / `[McpServerTool(Name = "radarr_...")]`, validate inputs, call `IRadarrClient`, return JSON via `ToolHelpers`.
5. Update the tool table in `README.md`.

## Notes

- `TreatWarningsAsErrors` is enabled — a build with warnings will fail.
- `tests/RadarrMcp.Tests` uses xUnit + NSubstitute, substituting `IRadarrClient` and asserting on the tool's JSON output (see `MoveMoviesToolTests.cs`). There is no global `using Xunit;` — add it per file.
- `radarr_move_movies` uses a third named client, `"RadarrMove"` (no Polly retries — re-sending a move isn't safe; timeout `RADARR__MOVETIMEOUTMS`, default 120s).
- Target framework is `net10.0`; the main project publishes as `PublishSingleFile` + `SelfContained` with `DebugType=none` (no `.pdb` in release output).
