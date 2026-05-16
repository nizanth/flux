using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Services;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.LiveTv;

/// <summary>
/// Jellyfin Live TV service implementation that surfaces Xtream Codes IPTV
/// channels to the Jellyfin Live TV subsystem.
/// </summary>
public sealed class FluxLiveTvService : ILiveTvService
{
    private readonly ProviderRegistry _providerRegistry;
    private readonly CatalogCache _catalogCache;
    private readonly XtreamApiClient _apiClient;
    private readonly ILogger<FluxLiveTvService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="FluxLiveTvService"/> class.
    /// </summary>
    /// <param name="providerRegistry">Registry of configured Xtream Codes providers.</param>
    /// <param name="catalogCache">In-memory catalog cache holding stream data.</param>
    /// <param name="apiClient">Xtream Codes HTTP API client.</param>
    /// <param name="logger">Logger instance.</param>
    public FluxLiveTvService(
        ProviderRegistry providerRegistry,
        CatalogCache catalogCache,
        XtreamApiClient apiClient,
        ILogger<FluxLiveTvService> logger)
    {
        _providerRegistry = providerRegistry;
        _catalogCache = catalogCache;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Flux";

    /// <inheritdoc />
    public string HomePageUrl => "https://github.com/nizanth/flux";

    // ── Channels ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IEnumerable<ChannelInfo>> GetChannelsAsync(CancellationToken cancellationToken)
    {
        var channels = new List<ChannelInfo>();

        foreach (var provider in _providerRegistry.GetAll())
        {
            var catalog = _catalogCache.GetOrCreate(provider.Id);
            var streams = catalog.LiveStreams;

            if (streams is null || streams.Count == 0)
            {
                _logger.LogDebug(
                    "No live streams cached for provider '{Provider}'; skipping channel enumeration.",
                    provider.DisplayName);
                continue;
            }

            foreach (var stream in streams)
            {
                var channelId = $"{provider.Id}_{stream.StreamId}";

                channels.Add(new ChannelInfo
                {
                    Id = channelId,
                    Name = stream.Name,
                    Number = stream.Num.ToString(),
                    ImageUrl = string.IsNullOrEmpty(stream.StreamIcon) ? null : stream.StreamIcon,
                    HasImage = !string.IsNullOrEmpty(stream.StreamIcon),
                    ChannelType = ChannelType.TV,
                    // Tags is not a standard ChannelInfo property in 10.9; we track category via ChannelGroup.
                    ChannelGroup = stream.CategoryId ?? string.Empty,
                });
            }
        }

        _logger.LogInformation("Returning {Count} live channels across all providers.", channels.Count);
        return Task.FromResult<IEnumerable<ChannelInfo>>(channels);
    }

    // ── Programs ──────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IEnumerable<ProgramInfo>> GetProgramsAsync(
        string channelId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        // Phase 1: EPG data is populated by the XMLTV parser in a future phase.
        // Return an empty list so Jellyfin does not error; the Live TV guide
        // will display "No data" placeholders until EPG sync is implemented.
        _logger.LogDebug(
            "GetProgramsAsync called for channel {ChannelId} [{Start} – {End}] — EPG not yet available (Phase 1).",
            channelId, startDateUtc, endDateUtc);

        return Task.FromResult<IEnumerable<ProgramInfo>>(Array.Empty<ProgramInfo>());
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<MediaSourceInfo> GetChannelStream(
        string channelId,
        string streamId,
        CancellationToken cancellationToken)
    {
        // channelId is formatted as "{providerId}_{streamNumericId}"
        var separatorIndex = channelId.IndexOf('_');
        if (separatorIndex < 0)
        {
            throw new ArgumentException($"Invalid channel ID format: '{channelId}'.", nameof(channelId));
        }

        var providerIdStr = channelId[..separatorIndex];
        var streamPartStr = channelId[(separatorIndex + 1)..];

        if (!Guid.TryParse(providerIdStr, out var providerId))
        {
            throw new ArgumentException($"Cannot parse provider ID from channel ID '{channelId}'.", nameof(channelId));
        }

        if (!int.TryParse(streamPartStr, out var numericStreamId))
        {
            throw new ArgumentException($"Cannot parse stream ID from channel ID '{channelId}'.", nameof(channelId));
        }

        var provider = _providerRegistry.GetById(providerId)
            ?? throw new InvalidOperationException($"Provider '{providerId}' not found.");

        // Prefer HLS (.m3u8) if the provider auth response indicates it is allowed.
        // For Phase 1 we default to "ts" since we do not cache the auth response here.
        // A future phase can look up the cached AllowedOutputFormats.
        var extension = "ts";
        var url = _apiClient.BuildLiveStreamUrl(provider, numericStreamId, extension);

        _logger.LogInformation(
            "Building live stream URL for channel {ChannelId} → {StreamId}.{Ext}",
            channelId, numericStreamId, extension);

        var source = new MediaSourceInfo
        {
            Id = channelId,
            Path = url,
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
        };

        return Task.FromResult(source);
    }

    /// <inheritdoc />
    public Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(
        string channelId,
        CancellationToken cancellationToken)
    {
        // Phase 4 will add timeshift/catch-up sources here.
        return Task.FromResult(new List<MediaSourceInfo>());
    }

    /// <inheritdoc />
    public Task CloseLiveStream(string id, CancellationToken cancellationToken)
    {
        // HTTP streams are stateless; nothing to close.
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task ResetTuner(string id, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    // ── Timers / Recordings (not supported in Phase 1) ────────────────────────

    /// <inheritdoc />
    public Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<TimerInfo>>(Array.Empty<TimerInfo>());

    /// <inheritdoc />
    public Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<SeriesTimerInfo>>(Array.Empty<SeriesTimerInfo>());

    /// <inheritdoc />
    public Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(
        CancellationToken cancellationToken,
        ProgramInfo? program = null)
        => throw new NotSupportedException("Flux does not support recording timers.");

    /// <inheritdoc />
    public Task CreateTimerAsync(TimerInfo info, CancellationToken cancellationToken)
        => throw new NotSupportedException("Flux does not support recording timers.");

    /// <inheritdoc />
    public Task CreateSeriesTimerAsync(SeriesTimerInfo info, CancellationToken cancellationToken)
        => throw new NotSupportedException("Flux does not support recording timers.");

    /// <inheritdoc />
    public Task UpdateTimerAsync(TimerInfo updatedTimer, CancellationToken cancellationToken)
        => throw new NotSupportedException("Flux does not support recording timers.");

    /// <inheritdoc />
    public Task UpdateSeriesTimerAsync(SeriesTimerInfo updatedTimer, CancellationToken cancellationToken)
        => throw new NotSupportedException("Flux does not support recording timers.");

    /// <inheritdoc />
    public Task CancelTimerAsync(string timerId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Flux does not support recording timers.");

    /// <inheritdoc />
    public Task CancelSeriesTimerAsync(string timerId, CancellationToken cancellationToken)
        => throw new NotSupportedException("Flux does not support recording timers.");
}
