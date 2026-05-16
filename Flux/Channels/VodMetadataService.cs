using System.Collections.Concurrent;
using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Api.Dto;
using Jellyfin.Plugin.Flux.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Channels;

/// <summary>
/// Lazily enriches VOD metadata by fetching detailed information from the Xtream Codes
/// <c>get_vod_info</c> endpoint. Results are cached in memory for the lifetime of the
/// plugin host to avoid repeated round-trips.
/// </summary>
public sealed class VodMetadataService
{
    // Key = "{providerId}_{streamId}"
    private readonly ConcurrentDictionary<string, VodInfo> _cache = new();

    private readonly XtreamApiClient _apiClient;
    private readonly ILogger<VodMetadataService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="VodMetadataService"/> class.
    /// </summary>
    /// <param name="apiClient">Xtream Codes HTTP API client.</param>
    /// <param name="logger">Logger instance.</param>
    public VodMetadataService(XtreamApiClient apiClient, ILogger<VodMetadataService> logger)
    {
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <summary>
    /// Returns cached <see cref="VodInfo"/> for the given stream, or fetches it from the
    /// provider if not yet cached.
    /// </summary>
    /// <param name="provider">The provider configuration to fetch from.</param>
    /// <param name="streamId">The VOD stream identifier.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The <see cref="VodInfo"/>, or <c>null</c> if the fetch failed.</returns>
    public async Task<VodInfo?> GetOrFetchAsync(ProviderConfig provider, int streamId, CancellationToken ct)
    {
        var cacheKey = $"{provider.Id}_{streamId}";

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        _logger.LogDebug(
            "VodMetadataService: fetching VOD info for stream {StreamId} from provider '{Provider}'",
            streamId,
            provider.DisplayName);

        var info = await _apiClient.GetVodInfoAsync(provider, streamId, ct).ConfigureAwait(false);

        if (info is null)
        {
            _logger.LogWarning(
                "VodMetadataService: failed to fetch VOD info for stream {StreamId} from provider '{Provider}'",
                streamId,
                provider.DisplayName);
            return null;
        }

        _cache.TryAdd(cacheKey, info);
        return info;
    }
}
