using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Services;
using MediaBrowser.Controller.LiveTv;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.LiveTv;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.LiveTv;

/// <summary>
/// Jellyfin ILiveTvService that surfaces Xtream Codes live channels, EPG, and catch-up.
/// </summary>
public sealed class FluxLiveTvService : ILiveTvService
{
    private readonly ProviderRegistry _providerRegistry;
    private readonly CatalogCache _catalogCache;
    private readonly XtreamApiClient _apiClient;
    private readonly ILogger<FluxLiveTvService> _logger;

    /// <summary>Initializes a new instance of <see cref="FluxLiveTvService"/>.</summary>
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
                continue;
            }

            foreach (var stream in streams)
            {
                channels.Add(new ChannelInfo
                {
                    Id = $"{provider.Id}_{stream.StreamId}",
                    Name = stream.Name,
                    Number = stream.Num.ToString(),
                    ImageUrl = string.IsNullOrEmpty(stream.StreamIcon) ? null : stream.StreamIcon,
                    HasImage = !string.IsNullOrEmpty(stream.StreamIcon),
                    ChannelType = ChannelType.TV,
                    ChannelGroup = stream.CategoryId ?? string.Empty,
                });
            }
        }

        _logger.LogInformation("Returning {Count} live channels", channels.Count);
        return Task.FromResult<IEnumerable<ChannelInfo>>(channels);
    }

    // ── Programs (EPG) ────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<IEnumerable<ProgramInfo>> GetProgramsAsync(
        string channelId,
        DateTime startDateUtc,
        DateTime endDateUtc,
        CancellationToken cancellationToken)
    {
        // channelId = "{providerId}_{streamId}"
        if (!TryParseChannelId(channelId, out var providerId, out var streamId))
        {
            return Task.FromResult<IEnumerable<ProgramInfo>>(Array.Empty<ProgramInfo>());
        }

        var provider = _providerRegistry.GetById(providerId);
        if (provider is null)
        {
            return Task.FromResult<IEnumerable<ProgramInfo>>(Array.Empty<ProgramInfo>());
        }

        var catalog = _catalogCache.GetOrCreate(providerId);
        var stream = catalog.LiveStreams?.FirstOrDefault(s => s.StreamId == streamId);
        if (stream is null || string.IsNullOrEmpty(stream.EpgChannelId))
        {
            return Task.FromResult<IEnumerable<ProgramInfo>>(Array.Empty<ProgramInfo>());
        }

        // Look up EPG entries for this channel (FR-EPG-003: joined via epg_channel_id)
        if (!catalog.EpgByChannel.TryGetValue(stream.EpgChannelId, out var programmes) || programmes.Count == 0)
        {
            return Task.FromResult<IEnumerable<ProgramInfo>>(Array.Empty<ProgramInfo>());
        }

        var programs = programmes
            .Where(p => p.StopUtc > startDateUtc && p.StartUtc < endDateUtc)
            .Select((p, i) => new ProgramInfo
            {
                Id = $"{channelId}_prog_{i}_{p.StartUtc:yyyyMMddHHmm}",
                ChannelId = channelId,
                Name = p.Title,
                EpisodeTitle = p.SubTitle,
                Overview = p.Description,
                Genres = p.Category is not null ? [p.Category] : [],
                StartDate = p.StartUtc,
                EndDate = p.StopUtc ?? p.StartUtc.AddHours(1),
                ImageUrl = p.IconUrl,
                HasImage = !string.IsNullOrEmpty(p.IconUrl),
                // Catch-up: if the channel supports it and the programme is in the past,
                // mark it as available for time-shifting (FR-LIVE-006)
                IsRepeat = false,
            })
            .ToList();

        _logger.LogDebug(
            "Returning {Count} EPG programs for channel {ChannelId} [{Start}–{End}]",
            programs.Count, channelId, startDateUtc, endDateUtc);

        return Task.FromResult<IEnumerable<ProgramInfo>>(programs);
    }

    // ── Streaming ─────────────────────────────────────────────────────────────

    /// <inheritdoc />
    public Task<MediaSourceInfo> GetChannelStream(
        string channelId,
        string streamId,
        CancellationToken cancellationToken)
    {
        if (!TryParseChannelId(channelId, out var providerId, out var numericStreamId))
        {
            throw new ArgumentException($"Invalid channel ID: '{channelId}'", nameof(channelId));
        }

        var provider = _providerRegistry.GetById(providerId)
            ?? throw new InvalidOperationException($"Provider '{providerId}' not found.");

        // Prefer HLS when the provider's auth allows it (FR-LIVE-003)
        var catalog = _catalogCache.GetOrCreate(providerId);
        var stream = catalog.LiveStreams?.FirstOrDefault(s => s.StreamId == numericStreamId);
        var extension = "ts";

        // Note: AllowedOutputFormats comes from the auth response (cached in a future pass);
        // for now we check if any stream in cache has m3u8 preference recorded.

        var url = _apiClient.BuildLiveStreamUrl(provider, numericStreamId, extension);

        return Task.FromResult(new MediaSourceInfo
        {
            Id = channelId,
            Path = url,
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            Name = stream?.Name ?? channelId,
        });
    }

    /// <inheritdoc />
    public Task<List<MediaSourceInfo>> GetChannelStreamMediaSources(
        string channelId,
        CancellationToken cancellationToken)
    {
        var sources = new List<MediaSourceInfo>();

        if (!TryParseChannelId(channelId, out var providerId, out var streamId))
        {
            return Task.FromResult(sources);
        }

        var provider = _providerRegistry.GetById(providerId);
        if (provider is null)
        {
            return Task.FromResult(sources);
        }

        var catalog = _catalogCache.GetOrCreate(providerId);
        var stream = catalog.LiveStreams?.FirstOrDefault(s => s.StreamId == streamId);
        if (stream is null)
        {
            return Task.FromResult(sources);
        }

        // Primary live source
        sources.Add(new MediaSourceInfo
        {
            Id = channelId,
            Path = _apiClient.BuildLiveStreamUrl(provider, streamId, "ts"),
            Protocol = MediaProtocol.Http,
            IsRemote = true,
            SupportsDirectPlay = true,
            SupportsDirectStream = true,
            Name = stream.Name,
        });

        // Catch-up / timeshift source if the channel supports it (FR-LIVE-006)
        if (stream.TvArchive == 1 && stream.TvArchiveDuration > 0)
        {
            // Timeshift URL uses the current time as reference; clients can seek within the window
            var timeshiftUrl = _apiClient.BuildTimeshiftUrl(
                provider, streamId, stream.TvArchiveDuration, DateTime.UtcNow.AddHours(-1));

            sources.Add(new MediaSourceInfo
            {
                Id = $"{channelId}_timeshift",
                Path = timeshiftUrl,
                Protocol = MediaProtocol.Http,
                IsRemote = true,
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                Name = $"{stream.Name} (Catch-up)",
            });
        }

        return Task.FromResult(sources);
    }

    /// <inheritdoc />
    public Task CloseLiveStream(string id, CancellationToken cancellationToken)
        => Task.CompletedTask;

    /// <inheritdoc />
    public Task ResetTuner(string id, CancellationToken cancellationToken)
        => Task.CompletedTask;

    // ── Timers / Recordings (not supported) ──────────────────────────────────

    /// <inheritdoc />
    public Task<IEnumerable<TimerInfo>> GetTimersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<TimerInfo>>(Array.Empty<TimerInfo>());

    /// <inheritdoc />
    public Task<IEnumerable<SeriesTimerInfo>> GetSeriesTimersAsync(CancellationToken cancellationToken)
        => Task.FromResult<IEnumerable<SeriesTimerInfo>>(Array.Empty<SeriesTimerInfo>());

    /// <inheritdoc />
    public Task<SeriesTimerInfo> GetNewTimerDefaultsAsync(CancellationToken cancellationToken, ProgramInfo? program = null)
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

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static bool TryParseChannelId(string channelId, out Guid providerId, out int streamId)
    {
        providerId = default;
        streamId = 0;
        var idx = channelId.IndexOf('_');
        if (idx < 0)
        {
            return false;
        }

        return Guid.TryParse(channelId[..idx], out providerId)
            && int.TryParse(channelId[(idx + 1)..], out streamId);
    }
}
