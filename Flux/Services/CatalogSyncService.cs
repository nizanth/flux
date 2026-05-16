using System.Collections.Concurrent;
using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Services;

/// <summary>
/// Orchestrates periodic catalog sync for all configured providers.
/// Each provider sync is reentrancy-guarded: a running sync cannot be re-triggered
/// from the UI or a scheduled task while it is still executing (FR-UI-003).
/// </summary>
public sealed class CatalogSyncService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();
    private readonly XtreamApiClient _apiClient;
    private readonly CatalogCache _cache;
    private readonly XmltvParser _epgParser;
    private readonly HealthMonitor _health;
    private readonly ILogger<CatalogSyncService> _logger;

    /// <summary>Initializes a new instance of <see cref="CatalogSyncService"/>.</summary>
    public CatalogSyncService(
        XtreamApiClient apiClient,
        CatalogCache cache,
        XmltvParser epgParser,
        HealthMonitor health,
        ILogger<CatalogSyncService> logger)
    {
        _apiClient = apiClient;
        _cache = cache;
        _epgParser = epgParser;
        _health = health;
        _logger = logger;
    }

    /// <summary>Syncs all configured providers, skipping any that are already syncing.</summary>
    public async Task SyncAllAsync(CancellationToken ct = default)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null)
        {
            return;
        }

        var tasks = config.Providers.Select(p => SyncProviderAsync(p, config, ct));
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    /// <summary>Syncs a single provider. Skips if already in-progress (reentrancy guard).</summary>
    public async Task SyncProviderAsync(ProviderConfig provider, PluginConfiguration config, CancellationToken ct = default)
    {
        var gate = _locks.GetOrAdd(provider.Id, _ => new SemaphoreSlim(1, 1));
        if (!await gate.WaitAsync(0, ct).ConfigureAwait(false))
        {
            _logger.LogInformation("Sync for '{Provider}' already running; skipping duplicate trigger", provider.DisplayName);
            return;
        }

        try
        {
            await RunSyncAsync(provider, config, ct).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    // ── Sync coordination ──────────────────────────────────────────────────

    private async Task RunSyncAsync(ProviderConfig provider, PluginConfiguration config, CancellationToken ct)
    {
        _logger.LogInformation("Starting sync for provider '{Provider}'", provider.DisplayName);

        // Validate credentials first
        var auth = await _apiClient.AuthenticateAsync(provider, ct).ConfigureAwait(false);
        if (auth?.UserInfo?.Auth != 1)
        {
            _health.RecordFailure(provider.Id, "Authentication",
                null, "Credentials rejected (auth != 1) or account expired");
            return;
        }

        var catalog = _cache.GetOrCreate(provider.Id);
        var now = DateTime.UtcNow;

        var syncTasks = new List<Task>();

        if (IsStale(catalog.LiveStreamsRefreshedAt, config.LiveChannelRefreshHours, now))
        {
            syncTasks.Add(SyncLiveAsync(provider, catalog, ct));
        }

        if (IsStale(catalog.EpgRefreshedAt, config.EpgRefreshHours, now))
        {
            syncTasks.Add(SyncEpgAsync(provider, catalog, ct));
        }

        if (IsStale(catalog.VodRefreshedAt, config.VodRefreshHours, now))
        {
            syncTasks.Add(SyncVodAsync(provider, catalog, ct));
        }

        if (IsStale(catalog.SeriesRefreshedAt, config.SeriesRefreshHours, now))
        {
            syncTasks.Add(SyncSeriesAsync(provider, catalog, ct));
        }

        if (syncTasks.Count == 0)
        {
            _logger.LogDebug("All catalogs for '{Provider}' are fresh; nothing to sync", provider.DisplayName);
            return;
        }

        try
        {
            await Task.WhenAll(syncTasks).ConfigureAwait(false);
            _health.RecordSuccess(provider.Id, "CatalogSync");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _health.RecordFailure(provider.Id, "CatalogSync", ex);
        }

        _logger.LogInformation("Sync complete for provider '{Provider}'", provider.DisplayName);
    }

    // ── Content-type syncs ────────────────────────────────────────────────

    private async Task SyncLiveAsync(ProviderConfig provider, ProviderCatalog catalog, CancellationToken ct)
    {
        try
        {
            catalog.LiveCategories = await _apiClient.GetLiveCategoriesAsync(provider, ct).ConfigureAwait(false);
            catalog.LiveStreams = await _apiClient.GetLiveStreamsAsync(provider, null, ct).ConfigureAwait(false);
            catalog.LiveStreamsRefreshedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Live sync: '{Provider}' → {Streams} streams in {Cats} categories",
                provider.DisplayName, catalog.LiveStreams?.Count ?? 0, catalog.LiveCategories?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Live sync failed for '{Provider}'", provider.DisplayName);
            _health.RecordDegraded(provider.Id, $"Live sync error: {ex.Message}");
        }
    }

    private async Task SyncEpgAsync(ProviderConfig provider, ProviderCatalog catalog, CancellationToken ct)
    {
        try
        {
            await using var stream = await _apiClient.GetXmltvStreamAsync(provider, ct).ConfigureAwait(false);
            if (stream is null)
            {
                _logger.LogWarning("XMLTV stream unavailable for '{Provider}'; skipping EPG sync", provider.DisplayName);
                _health.RecordDegraded(provider.Id, "XMLTV stream unavailable");
                return;
            }

            var byChannel = new Dictionary<string, List<XmltvProgramme>>(StringComparer.OrdinalIgnoreCase);
            var cutoff = DateTime.UtcNow.AddHours(-24);

            await foreach (var prog in _epgParser.ParseAsync(stream, ct).ConfigureAwait(false))
            {
                // Retain only programmes not older than 24 h in the past (FR-EPG-005)
                if (prog.StopUtc.HasValue && prog.StopUtc.Value < cutoff)
                {
                    continue;
                }

                if (!byChannel.TryGetValue(prog.ChannelId, out var list))
                {
                    list = new List<XmltvProgramme>();
                    byChannel[prog.ChannelId] = list;
                }

                list.Add(prog);
            }

            catalog.EpgByChannel = byChannel;
            catalog.EpgRefreshedAt = DateTime.UtcNow;

            var totalProgs = byChannel.Values.Sum(l => l.Count);
            _logger.LogInformation(
                "EPG sync: '{Provider}' → {Programs} programs across {Channels} channels",
                provider.DisplayName, totalProgs, byChannel.Count);

            // Warn about unmatched channels (FR-EPG-003)
            if (catalog.LiveStreams is { Count: > 0 })
            {
                var liveEpgIds = catalog.LiveStreams
                    .Where(s => !string.IsNullOrEmpty(s.EpgChannelId))
                    .Select(s => s.EpgChannelId!)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var unmatched = liveEpgIds.Except(byChannel.Keys, StringComparer.OrdinalIgnoreCase).ToList();
                if (unmatched.Count > 0)
                {
                    _logger.LogInformation(
                        "{Count} live channel(s) have no matching EPG entry for '{Provider}'",
                        unmatched.Count, provider.DisplayName);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "EPG sync failed for '{Provider}'", provider.DisplayName);
            _health.RecordDegraded(provider.Id, $"EPG sync error: {ex.Message}");
        }
    }

    private async Task SyncVodAsync(ProviderConfig provider, ProviderCatalog catalog, CancellationToken ct)
    {
        try
        {
            catalog.VodCategories = await _apiClient.GetVodCategoriesAsync(provider, ct).ConfigureAwait(false);
            catalog.VodStreams = await _apiClient.GetVodStreamsAsync(provider, null, ct).ConfigureAwait(false);
            catalog.VodRefreshedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "VOD sync: '{Provider}' → {Titles} titles in {Cats} categories",
                provider.DisplayName, catalog.VodStreams?.Count ?? 0, catalog.VodCategories?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "VOD sync failed for '{Provider}'", provider.DisplayName);
            _health.RecordDegraded(provider.Id, $"VOD sync error: {ex.Message}");
        }
    }

    private async Task SyncSeriesAsync(ProviderConfig provider, ProviderCatalog catalog, CancellationToken ct)
    {
        try
        {
            catalog.SeriesCategories = await _apiClient.GetSeriesCategoriesAsync(provider, ct).ConfigureAwait(false);
            catalog.Series = await _apiClient.GetSeriesAsync(provider, null, ct).ConfigureAwait(false);
            catalog.SeriesRefreshedAt = DateTime.UtcNow;

            _logger.LogInformation(
                "Series sync: '{Provider}' → {Shows} shows in {Cats} categories",
                provider.DisplayName, catalog.Series?.Count ?? 0, catalog.SeriesCategories?.Count ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Series sync failed for '{Provider}'", provider.DisplayName);
            _health.RecordDegraded(provider.Id, $"Series sync error: {ex.Message}");
        }
    }

    private static bool IsStale(DateTime? lastRefreshed, int thresholdHours, DateTime now)
        => lastRefreshed is null || (now - lastRefreshed.Value).TotalHours >= thresholdHours;
}
