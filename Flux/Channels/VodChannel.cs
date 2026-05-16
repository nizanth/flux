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
/// Jellyfin IChannel implementation that exposes Xtream Codes VOD content browseable
/// by category. Each provider's categories are shown as folders at the root level.
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
        var folderId = query.FolderId;

        if (string.IsNullOrEmpty(folderId))
        {
            var items = BuildCategoryFolders();
            return Task.FromResult(new ChannelItemResult { Items = items, TotalRecordCount = items.Count });
        }

        if (folderId.StartsWith("vod-cat:", StringComparison.Ordinal))
        {
            // Format: vod-cat:{providerId}:{categoryId}
            var parts = folderId.Split(':', 3);
            if (parts.Length == 3 && Guid.TryParse(parts[1], out var providerId))
            {
                var categoryId = parts[2];
                var items = BuildVodItemsForCategory(providerId, categoryId);
                return Task.FromResult(new ChannelItemResult { Items = items, TotalRecordCount = items.Count });
            }
        }

        _logger.LogWarning("VodChannel: unknown folder format '{FolderId}'", folderId);
        return Task.FromResult(new ChannelItemResult { Items = new List<ChannelItemInfo>() });
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

    private List<ChannelItemInfo> BuildCategoryFolders()
    {
        var items = new List<ChannelItemInfo>();
        var allProviders = _providerRegistry.GetAll();
        var multiProvider = allProviders.Count > 1;

        foreach (var provider in allProviders)
        {
            var catalog = _catalogCache.GetOrCreate(provider.Id);
            var categories = catalog.VodCategories;

            if (categories is null || categories.Count == 0)
            {
                _logger.LogDebug("No VOD categories cached for provider '{Provider}'; falling back to flat list.", provider.DisplayName);
                var flat = BuildVodItemsForProvider(provider.Id);
                items.AddRange(flat);
                continue;
            }

            foreach (var category in categories)
            {
                var name = multiProvider
                    ? $"{provider.DisplayName} — {category.CategoryName}"
                    : category.CategoryName;

                items.Add(new ChannelItemInfo
                {
                    Id = $"vod-cat:{provider.Id}:{category.CategoryId}",
                    Name = name,
                    Type = ChannelItemType.Folder,
                });
            }
        }

        return items;
    }

    private List<ChannelItemInfo> BuildVodItemsForCategory(Guid providerId, string categoryId)
    {
        var provider = _providerRegistry.GetById(providerId);
        if (provider is null)
        {
            _logger.LogWarning("VodChannel: provider '{ProviderId}' not found for category '{CategoryId}'", providerId, categoryId);
            return new List<ChannelItemInfo>();
        }

        var catalog = _catalogCache.GetOrCreate(providerId);
        var streams = catalog.VodStreams?
            .Where(s => s.CategoryId == categoryId)
            .ToList();

        if (streams is null || streams.Count == 0)
        {
            _logger.LogDebug("No VOD streams for provider '{Provider}' category '{CategoryId}'.", provider.DisplayName, categoryId);
            return new List<ChannelItemInfo>();
        }

        var items = new List<ChannelItemInfo>(streams.Count);
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

        return items;
    }

    private List<ChannelItemInfo> BuildVodItemsForProvider(Guid providerId)
    {
        var provider = _providerRegistry.GetById(providerId);
        if (provider is null) return new List<ChannelItemInfo>();

        var catalog = _catalogCache.GetOrCreate(providerId);
        var streams = catalog.VodStreams;
        if (streams is null || streams.Count == 0) return new List<ChannelItemInfo>();

        var items = new List<ChannelItemInfo>(streams.Count);
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

        return items;
    }
}
