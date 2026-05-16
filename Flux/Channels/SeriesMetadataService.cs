using System.Collections.Concurrent;
using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Api.Dto;
using Jellyfin.Plugin.Flux.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Channels;

/// <summary>
/// Caches <see cref="SeriesInfo"/> responses from the Xtream Codes API with a 12-hour TTL.
/// </summary>
public sealed class SeriesMetadataService
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(12);

    private readonly XtreamApiClient _apiClient;
    private readonly ILogger<SeriesMetadataService> _logger;

    // Keyed by "{providerId}_{seriesId}"
    private readonly ConcurrentDictionary<string, SeriesInfo> _cache = new();
    private readonly ConcurrentDictionary<string, DateTime> _timestamps = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesMetadataService"/> class.
    /// </summary>
    /// <param name="apiClient">Xtream Codes HTTP API client.</param>
    /// <param name="logger">Logger instance.</param>
    public SeriesMetadataService(XtreamApiClient apiClient, ILogger<SeriesMetadataService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns a <see cref="SeriesInfo"/> from cache when available and fresh, otherwise
    /// fetches it from the provider and stores it.
    /// </summary>
    /// <param name="provider">The provider to query.</param>
    /// <param name="seriesId">The series identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="SeriesInfo"/>, or <c>null</c> if the fetch failed.</returns>
    public async Task<SeriesInfo?> GetOrFetchInfoAsync(
        ProviderConfig provider,
        int seriesId,
        CancellationToken ct)
    {
        var key = $"{provider.Id}_{seriesId}";

        if (_cache.TryGetValue(key, out var cached) &&
            _timestamps.TryGetValue(key, out var fetchedAt) &&
            DateTime.UtcNow - fetchedAt < CacheTtl)
        {
            _logger.LogDebug(
                "SeriesMetadataService: cache hit for series {SeriesId} (provider {Provider})",
                seriesId, provider.DisplayName);
            return cached;
        }

        _logger.LogDebug(
            "SeriesMetadataService: fetching series info for {SeriesId} from provider {Provider}",
            seriesId, provider.DisplayName);

        var info = await _apiClient.GetSeriesInfoAsync(provider, seriesId, ct).ConfigureAwait(false);
        if (info is null)
        {
            _logger.LogWarning(
                "SeriesMetadataService: received null response for series {SeriesId} (provider {Provider})",
                seriesId, provider.DisplayName);
            return null;
        }

        _cache[key] = info;
        _timestamps[key] = DateTime.UtcNow;
        return info;
    }
}
