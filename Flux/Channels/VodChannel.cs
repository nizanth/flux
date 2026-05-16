using Jellyfin.Plugin.Flux.Api;
using Jellyfin.Plugin.Flux.Services;
using MediaBrowser.Controller.Channels;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Channels;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Channels;

/// <summary>
/// Jellyfin IChannel implementation that exposes Xtream Codes VOD content as a
/// browse-able virtual channel library. All configured providers are merged into
/// a single flat list of movies.
/// </summary>
public sealed class VodChannel : IChannel, IRequiresMediaInfoCallback
{
    private readonly ProviderRegistry _providerRegistry;
    private readonly CatalogCache _catalogCache;
    private readonly XtreamApiClient _apiClient;
    private readonly ILogger<VodChannel> _logger;

    /// <summary>Initializes a new instance of the <see cref="VodChannel"/> class.</summary>
    public VodChannel(
        ProviderRegistry providerRegistry,
        CatalogCache catalogCache,
        XtreamApiClient apiClient,
        ILogger<VodChannel> logger)
    {
        _providerRegistry = providerRegistry;
        _catalogCache = catalogCache;
        _apiClient = apiClient;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Flux — Movies";

    /// <inheritdoc />
    public string Description => "VOD movies from all configured Flux (Xtream Codes) providers.";

    /// <inheritdoc />
    public string DataVersion
    {
        get
        {
            var version = Plugin.Instance?.Version.ToString() ?? "1";
            var timestamps = _providerRegistry.GetAll()
                .Select(p => _catalogCache.GetOrCreate(p.Id).VodRefreshedAt)
                .Where(t => t.HasValue)
                .ToList();
            var ts = timestamps.Count > 0
                ? timestamps.Max()!.Value.ToString("yyyyMMddHHmmss")
                : "empty";
            return $"{version}_{ts}";
        }
    }

    /// <inheritdoc />
    public string HomePageUrl => "https://github.com/nizanth/flux";

    /// <inheritdoc />
    public ChannelParentalRating ParentalRating => ChannelParentalRating.GeneralAudience;

    /// <inheritdoc />
    public InternalChannelFeatures GetChannelFeatures()
        => new InternalChannelFeatures
        {
            ContentTypes = new List<ChannelMediaContentType> { ChannelMediaContentType.Movie },
            MediaTypes = new List<ChannelMediaType> { ChannelMediaType.Video },
        };

    /// <inheritdoc />
    public bool IsEnabledFor(string userId) => true;

    /// <inheritdoc />
    public Task<ChannelItemResult> GetChannelItems(
        InternalChannelItemQuery query,
        CancellationToken cancellationToken)
    {
        var items = BuildVodItems();
        return Task.FromResult(new ChannelItemResult
        {
            Items = items,
            TotalRecordCount = items.Count,
        });
    }

    /// <inheritdoc />
    public Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(
        string id,
        CancellationToken cancellationToken)
    {
        // ID format: "{providerId}_vod_{streamId}"
        var parts = id.Split("_vod_", 2, StringSplitOptions.None);
        if (parts.Length != 2 || !Guid.TryParse(parts[0], out var providerId) || !int.TryParse(parts[1], out var streamId))
        {
            _logger.LogWarning("VodChannel: could not parse composite ID '{Id}'", id);
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var provider = _providerRegistry.GetById(providerId);
        if (provider is null)
        {
            _logger.LogWarning("VodChannel: provider '{ProviderId}' not found for item '{Id}'", providerId, id);
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var container = "mp4";
        var catalog = _catalogCache.GetOrCreate(provider.Id);
        var stream = catalog.VodStreams?.FirstOrDefault(s => s.StreamId == streamId);
        if (stream is not null && !string.IsNullOrEmpty(stream.ContainerExtension))
        {
            container = stream.ContainerExtension;
        }

        var url = _apiClient.BuildVodStreamUrl(provider, streamId, container);

        IEnumerable<MediaSourceInfo> result = new List<MediaSourceInfo>
        {
            new MediaSourceInfo
            {
                Id = id,
                Path = url,
                Protocol = MediaProtocol.Http,
                IsRemote = true,
                Container = container,
                Name = stream?.Name ?? id,
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
            },
        };

        return Task.FromResult(result);
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages()
        => Enumerable.Empty<ImageType>();

    private List<ChannelItemInfo> BuildVodItems()
    {
        var items = new List<ChannelItemInfo>();

        foreach (var provider in _providerRegistry.GetAll())
        {
            var catalog = _catalogCache.GetOrCreate(provider.Id);
            var streams = catalog.VodStreams;

            if (streams is null || streams.Count == 0)
            {
                _logger.LogDebug("No VOD streams cached for provider '{Provider}'; skipping.", provider.DisplayName);
                continue;
            }

            foreach (var stream in streams)
            {
                var item = new ChannelItemInfo
                {
                    Id = $"{provider.Id}_vod_{stream.StreamId}",
                    Name = stream.Name,
                    Type = ChannelItemType.Media,
                    MediaType = ChannelMediaType.Video,
                    ContentType = ChannelMediaContentType.Movie,
                };

                if (!string.IsNullOrEmpty(stream.StreamIcon))
                {
                    item.ImageUrl = stream.StreamIcon;
                }

                items.Add(item);
            }
        }

        return items;
    }
}
