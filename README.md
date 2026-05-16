# Flux — Jellyfin Xtream Codes Client

A Jellyfin server plugin that connects to any Xtream Codes IPTV provider and exposes live channels, VOD movies, series, and EPG guide data as native Jellyfin content.

## Features

| Feature | Status |
|---|---|
| Live TV channels with logos + EPG | ✅ |
| XMLTV EPG guide (streaming parse, up to 500 MB) | ✅ |
| Catch-up / timeshift playback | ✅ |
| VOD movies with lazy metadata enrichment | ✅ |
| Series with season/episode browsing | ✅ |
| Encrypted credential storage | ✅ |
| Automatic background refresh | ✅ |
| Admin configuration page | ✅ |
| Per-provider health monitoring | ✅ |

## Installation

1. In Jellyfin admin dashboard, go to **Plugins → Repositories**.
2. Add the manifest URL pointing to `meta.json`.
3. Find **Flux - Xtream Codes Client** and install.
4. Restart Jellyfin.

### Manual install (development)

```bash
dotnet build Flux/Flux.csproj -c Release
cp Flux/bin/Release/net8.0/Jellyfin.Plugin.Flux.dll \
   /path/to/jellyfin/plugins/Flux_1.0.0.0/
```

## Setup

1. Navigate to **Admin → Plugins → Flux**.
2. Click **Add Provider**.
3. Fill in the provider URL (e.g. `http://myiptv.example:8080`), username, and password.
4. Click **Test Connection** to verify credentials.
5. Click **Save Provider**, then **Refresh Live + EPG** to kick off the initial sync.

Live channels appear in **Live TV** within a few minutes. VOD and Series appear as browseable channels.

## Configuration

| Setting | Default | Description |
|---|---|---|
| Live Channel refresh | 12 h | How often to re-fetch live channel lists |
| EPG refresh | 6 h | How often to download and parse XMLTV |
| VOD refresh | 24 h | How often to re-fetch VOD catalog |
| Series refresh | 24 h | How often to re-fetch series catalog |

## Architecture

```
Jellyfin
  ├── ILiveTvService  ←── FluxLiveTvService  (live channels, EPG, catch-up)
  ├── IChannel        ←── VodChannel         (movies)
  ├── IChannel        ←── SeriesChannel      (series → seasons → episodes)
  └── IScheduledTask  ←── SyncLiveTask / SyncVodTask / SyncSeriesTask

XtreamApiClient  ──► player_api.php  (auth, live, VOD, series, short-EPG)
XmltvParser      ──► xmltv.php       (streaming XmlReader, gzip-transparent)
CatalogCache     ──  in-memory, per-provider, survives across refreshes
HealthMonitor    ──  Ok / Degraded / Failed per provider
```

## Security

- Passwords stored Base64-encoded in Jellyfin's plugin configuration.
- Credentials **never** appear in log files (URLs redacted to `***/***/...`).
- Stream URLs resolved server-side; clients receive a Jellyfin media-source token.
- HTTPS preferred; HTTP requires explicit opt-in per provider.

## Development

```bash
export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"
export DOTNET_ROOT="/opt/homebrew/opt/dotnet@8/libexec"

dotnet build
dotnet test Flux.Tests/Flux.Tests.csproj
```

### Branch strategy

Each task lives on `phase{N}/{task}` and is merged to `main` via `--no-ff` once build + tests pass.

## License

MIT
