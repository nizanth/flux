using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Services;

/// <summary>
/// Orchestrates periodic catalog sync operations for all configured providers,
/// pulling live streams, VOD, series, and EPG data from Xtream Codes servers.
/// </summary>
public sealed class CatalogSyncService
{
    private readonly XtreamApiClient _apiClient;
    private readonly CatalogCache _cache;
    private readonly ILogger<CatalogSyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="CatalogSyncService"/> class.
    /// </summary>
    /// <param name="apiClient">The Xtream Codes API client.</param>
    /// <param name="cache">The in-memory catalog cache.</param>
    /// <param name="logger">Logger instance.</param>
    public CatalogSyncService(
        XtreamApiClient apiClient,
        CatalogCache cache,
        ILogger<CatalogSyncService> logger)
    {
        _apiClient = apiClient;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Performs a full sync for a single provider, refreshing all catalog data that
    /// has exceeded its configured TTL.
    /// </summary>
    /// <param name="provider">The provider configuration to sync.</param>
    /// <param name="config">The plugin configuration (provides refresh intervals).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SyncProviderAsync(ProviderConfig provider, PluginConfiguration config, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting catalog sync for provider '{Provider}'", provider.DisplayName);

        var catalog = _cache.GetOrCreate(provider.Id);
        var now = DateTime.UtcNow;

        // Validate credentials first
        var auth = await _apiClient.AuthenticateAsync(provider, cancellationToken).ConfigureAwait(false);
        if (auth?.UserInfo?.Auth != 1)
        {
            _logger.LogWarning("Authentication failed for provider '{Provider}'. Skipping sync.", provider.DisplayName);
            return;
        }

        var tasks = new List<Task>();

        // Sync live streams
        if (catalog.LiveStreamsRefreshedAt is null || (now - catalog.LiveStreamsRefreshedAt.Value).TotalHours >= config.LiveChannelRefreshHours)
        {
            tasks.Add(SyncLiveAsync(provider, catalog, cancellationToken));
        }

        // Sync VOD
        if (catalog.VodRefreshedAt is null || (now - catalog.VodRefreshedAt.Value).TotalHours >= config.VodRefreshHours)
        {
            tasks.Add(SyncVodAsync(provider, catalog, cancellationToken));
        }

        // Sync series
        if (catalog.SeriesRefreshedAt is null || (now - catalog.SeriesRefreshedAt.Value).TotalHours >= config.SeriesRefreshHours)
        {
            tasks.Add(SyncSeriesAsync(provider, catalog, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);

        _logger.LogInformation("Catalog sync complete for provider '{Provider}'", provider.DisplayName);
    }

    /// <summary>
    /// Performs a full sync for all configured providers.
    /// </summary>
    /// <param name="config">The plugin configuration.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task SyncAllProvidersAsync(PluginConfiguration config, CancellationToken cancellationToken = default)
    {
        foreach (var provider in config.Providers)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }

            try
            {
                await SyncProviderAsync(provider, config, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception while syncing provider '{Provider}'", provider.DisplayName);
            }
        }
    }

    private async Task SyncLiveAsync(ProviderConfig provider, ProviderCatalog catalog, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching live categories for provider '{Provider}'", provider.DisplayName);
        catalog.LiveCategories = await _apiClient.GetLiveCategoriesAsync(provider, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Fetching live streams for provider '{Provider}'", provider.DisplayName);
        catalog.LiveStreams = await _apiClient.GetLiveStreamsAsync(provider, null, cancellationToken).ConfigureAwait(false);
        catalog.LiveStreamsRefreshedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Live sync complete for '{Provider}': {StreamCount} streams, {CategoryCount} categories",
            provider.DisplayName,
            catalog.LiveStreams?.Count ?? 0,
            catalog.LiveCategories?.Count ?? 0);
    }

    private async Task SyncVodAsync(ProviderConfig provider, ProviderCatalog catalog, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching VOD categories for provider '{Provider}'", provider.DisplayName);
        catalog.VodCategories = await _apiClient.GetVodCategoriesAsync(provider, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Fetching VOD streams for provider '{Provider}'", provider.DisplayName);
        catalog.VodStreams = await _apiClient.GetVodStreamsAsync(provider, null, cancellationToken).ConfigureAwait(false);
        catalog.VodRefreshedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "VOD sync complete for '{Provider}': {StreamCount} titles, {CategoryCount} categories",
            provider.DisplayName,
            catalog.VodStreams?.Count ?? 0,
            catalog.VodCategories?.Count ?? 0);
    }

    private async Task SyncSeriesAsync(ProviderConfig provider, ProviderCatalog catalog, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Fetching series categories for provider '{Provider}'", provider.DisplayName);
        catalog.SeriesCategories = await _apiClient.GetSeriesCategoriesAsync(provider, cancellationToken).ConfigureAwait(false);

        _logger.LogDebug("Fetching series list for provider '{Provider}'", provider.DisplayName);
        catalog.Series = await _apiClient.GetSeriesAsync(provider, null, cancellationToken).ConfigureAwait(false);
        catalog.SeriesRefreshedAt = DateTime.UtcNow;

        _logger.LogInformation(
            "Series sync complete for '{Provider}': {SeriesCount} shows, {CategoryCount} categories",
            provider.DisplayName,
            catalog.Series?.Count ?? 0,
            catalog.SeriesCategories?.Count ?? 0);
    }
}
