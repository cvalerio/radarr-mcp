# RadarrMCP

A production-grade [Model Context Protocol (MCP)](https://modelcontextprotocol.io) server for [Radarr v3](https://radarr.video), built with .NET 10.

Expose your Radarr instance as MCP tools so Claude (or any MCP client) can search, add, monitor, and manage movies via natural language.

---

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- A running Radarr v3 instance
- A Radarr API key (Settings → General → Security)

---

## Build

```bash
# Debug build
dotnet build

# Self-contained single binary for Linux x64
dotnet publish -c Release -r linux-x64 --self-contained

# Self-contained single binary for Windows x64
dotnet publish -c Release -r win-x64 --self-contained

# Self-contained single binary for macOS arm64 (Apple Silicon)
dotnet publish -c Release -r osx-arm64 --self-contained
```

Output lands in `bin/Release/net10.0/<rid>/publish/`.

---

## Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `RADARR__URL` | yes | — | Base URL of your Radarr instance, e.g. `http://radarr:7878` |
| `RADARR__API_KEY` | yes | — | API key from Radarr → Settings → General → Security |
| `RADARR__TIMEOUT_MS` | no | `15000` | Per-request timeout in milliseconds |

> **Note:** the separator is double underscore `__` (standard .NET environment variable hierarchy separator).

---

## Claude Desktop Configuration

Add to `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "radarr": {
      "command": "/path/to/RadarrMcp",
      "env": {
        "RADARR__URL": "http://radarr:7878",
        "RADARR__API_KEY": "your-api-key-here"
      }
    }
  }
}
```

On Windows use the `.exe` binary and forward slashes or escaped backslashes.

---

## Claude Code Configuration

```bash
claude mcp add radarr /path/to/RadarrMcp \
  -e RADARR__URL=http://radarr:7878 \
  -e RADARR__API_KEY=your-api-key-here
```

---

## Docker Compose

Deploy alongside an existing Radarr stack:

```yaml
services:
  radarr:
    image: lscr.io/linuxserver/radarr:latest
    container_name: radarr
    environment:
      - PUID=1000
      - PGID=1000
      - TZ=Europe/London
    volumes:
      - /config/radarr:/config
      - /data/movies:/movies
      - /data/downloads:/downloads
    ports:
      - 7878:7878
    restart: unless-stopped

  radarr-mcp:
    image: mcr.microsoft.com/dotnet/runtime-deps:10.0
    container_name: radarr-mcp
    command: /app/RadarrMcp
    volumes:
      - ./publish/linux-x64:/app:ro
    environment:
      - RADARR__URL=http://radarr:7878
      - RADARR__API_KEY=your-api-key-here
      - RADARR__TIMEOUT_MS=15000
    depends_on:
      - radarr
    stdin_open: true
    restart: unless-stopped
```

Build the publish artifact first:
```bash
dotnet publish -c Release -r linux-x64 --self-contained -o publish/linux-x64
```

---

## Available Tools

### `radarr_search_movie`
Search for movies by title via Radarr (which queries TMDB). Returns both library movies and new candidates.

**Example:** *"Search for Interstellar"*

| Parameter | Type | Default | Description |
|---|---|---|---|
| `query` | string | required | Movie title to search |
| `limit` | int | `5` | Max results (1–20) |

---

### `radarr_get_library`
List movies in the Radarr library with optional status filtering and title search.

**Example:** *"Show me all unmonitored movies"*, *"Find movies with 'dark' in the title"*

| Parameter | Type | Default | Description |
|---|---|---|---|
| `filter` | string | `all` | `all` \| `missing` \| `downloaded` \| `monitored` \| `unmonitored` |
| `search` | string | `null` | Case-insensitive title substring filter |

---

### `radarr_add_movie`
Add one or more movies to Radarr by TMDB ID. Fetches full movie metadata automatically and uses sensible defaults for quality profile and root folder.

**Example:** *"Add Dune Part Two and Oppenheimer to Radarr"*

| Parameter | Type | Default | Description |
|---|---|---|---|
| `movies` | array | required | Array of `{ tmdbId, qualityProfileId?, rootFolderPath?, monitored?, searchForMovie? }` |

---

### `radarr_get_movie_details`
Get full details of a specific movie by its Radarr ID.

**Example:** *"Show full details for movie ID 42"*

| Parameter | Type | Description |
|---|---|---|
| `radarrId` | int | Radarr-assigned movie ID |

---

### `radarr_delete_movie`
Remove a movie from the library. Optionally delete files from disk.

**Example:** *"Delete movie 42 and remove its files"*

| Parameter | Type | Default | Description |
|---|---|---|---|
| `radarrId` | int | required | Radarr movie ID |
| `deleteFiles` | bool | `false` | Also delete files from disk |
| `addImportExclusion` | bool | `false` | Prevent future re-import |

---

### `radarr_update_movie`
Update monitored status or quality profile of a library movie.

**Example:** *"Unmonitor movie 42"*, *"Change quality profile of movie 7 to profile ID 3"*

| Parameter | Type | Default | Description |
|---|---|---|---|
| `radarrId` | int | required | Radarr movie ID |
| `monitored` | bool? | `null` | Set monitored state |
| `qualityProfileId` | int? | `null` | Assign a different quality profile |

---

### `radarr_get_queue`
Get the current download queue including active and pending items.

**Example:** *"What's currently downloading in Radarr?"*

| Parameter | Type | Default | Description |
|---|---|---|---|
| `includeMovie` | bool | `true` | Include movie metadata in each queue record |

---

### `radarr_get_system_status`
Get Radarr version, system info, and any active health check warnings.

**Example:** *"Is Radarr healthy?"*

No parameters.

---

## Architecture Notes

- **Transport:** stdio — compatible with Claude Desktop, Claude Code, and any MCP-capable client
- **Resilience:** Polly standard pipeline — 2 retries with exponential backoff, per-attempt timeout
- **Logging:** all logs go to **stderr** (stdout is reserved for the MCP protocol)
- **Startup check:** on start, the server calls Radarr's `/api/v3/system/status`; failure is logged as a warning but does not abort startup
