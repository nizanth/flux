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
3. Fill in the provider details (see [Configuring Credentials](#configuring-credentials) below).
4. Click **Test Connection** to verify the credentials before saving.
5. Click **Save Provider**, then **Refresh Live + EPG** to kick off the initial sync.

Live channels appear in **Live TV** within a few minutes. VOD and Series appear as browseable channels.

## Configuring Credentials

### Required fields

| Field | Description | Example |
|---|---|---|
| **Display Name** | Friendly label shown in the plugin UI | `My IPTV` |
| **Provider URL** | Base URL of the Xtream Codes server — no trailing slash, include the port | `http://myiptv.example:8080` |
| **Username** | Xtream Codes account username (provided by your IPTV service) | `john_doe` |
| **Password** | Xtream Codes account password | `s3cr3t` |

> **Where to find these:** Your IPTV provider should supply a URL, username, and password — sometimes called an "M3U link" or "Xtream Codes login". The base URL is everything before `/get.php` or `/player_api.php`.

### Optional fields

| Field | Default | Description |
|---|---|---|
| **User-Agent** | `Flux/1.0` | HTTP User-Agent sent with every API request. Change only if your provider rejects the default. |
| **Require HTTPS** | `true` | Uncheck to allow plain HTTP connections. Only disable if your provider does not support HTTPS. |

### Testing the connection

Click **Test Connection** after filling in the URL, username, and password. The plugin calls `player_api.php?action=get_live_categories` and reports:

- **Success** — credentials are valid; provider status turns green.
- **Unauthorized** — wrong username or password; double-check with your IPTV service.
- **Unreachable** — the server URL is incorrect or the server is down.

### How passwords are stored

Passwords are Base64-encoded and written to Jellyfin's plugin configuration file on disk (typically `~/.config/jellyfin/plugins/configurations/Jellyfin.Plugin.Flux.xml`). They are **never** logged — all API URLs are redacted to `***/username/***/...` in log output.

> Base64 is encoding, not encryption. Treat the configuration file as sensitive and restrict OS-level access to it accordingly.

### Updating or removing a provider

- **Edit** — click the pencil icon next to the provider, update the fields, and click **Save Provider**. The password field is left blank; enter a new password only if you want to change it.
- **Remove** — click the trash icon. The provider and all cached catalog data are removed immediately; a Jellyfin restart may be needed for Live TV to reflect the removal.

### Multiple providers

Click **Add Provider** for each IPTV service. Each provider syncs independently on its own schedule and appears as a separate source in Live TV and the VOD/Series channels.

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
