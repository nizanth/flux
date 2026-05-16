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
/// Jellyfin IChannel implementation that exposes Xtream Codes series content browseable
/// by category. Root shows category folders; drilling in shows Series → Season → Episode.
/// Episodes are surfaced as Movie-typed items so Jellyfin uses the stored MediaSources
/// path, which works reliably in Jellyfin 10.9.
/// </summary>
public sealed class SeriesChannel : IChannel, IRequiresMediaInfoCallback
{
    private readonly ProviderRegistry _providerRegistry;
    private readonly CatalogCache _catalogCache;
    private readonly XtreamApiClient _apiClient;
    private readonly SeriesMetadataService _metadataService;
    private readonly ILogger<SeriesChannel> _logger;

    /// <summary>Initializes a new instance of the <see cref="SeriesChannel"/> class.</summary>
    public SeriesChannel(
        ProviderRegistry providerRegistry,
        CatalogCache catalogCache,
        XtreamApiClient apiClient,
        SeriesMetadataService metadataService,
        ILogger<SeriesChannel> logger)
    {
        _providerRegistry = providerRegistry;
        _catalogCache = catalogCache;
        _apiClient = apiClient;
        _metadataService = metadataService;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Flux — Series";

    /// <inheritdoc />
    public string Description => "TV series from all configured Flux (Xtream Codes) providers.";

    /// <inheritdoc />
    public string DataVersion
    {
        get
        {
            var version = Plugin.Instance?.Version.ToString() ?? "1";
            var timestamps = _providerRegistry.GetAll()
                .Select(p => _catalogCache.GetOrCreate(p.Id).SeriesRefreshedAt)
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
    public async Task<ChannelItemResult> GetChannelItems(
        InternalChannelItemQuery query,
        CancellationToken cancellationToken)
    {
        var folderId = query.FolderId;

        if (string.IsNullOrEmpty(folderId))
        {
            var items = BuildCategoryFolders();
            return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
        }

        if (folderId.StartsWith("series-cat:", StringComparison.Ordinal))
        {
            // Format: series-cat:{providerId}:{categoryId}
            var parts = folderId.Split(':', 3);
            if (parts.Length == 3 && Guid.TryParse(parts[1], out var providerId))
            {
                var categoryId = parts[2];
                var items = BuildSeriesItemsForCategory(providerId, categoryId);
                return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
            }

            _logger.LogWarning("SeriesChannel: could not parse category folder '{FolderId}'", folderId);
            return new ChannelItemResult { Items = new List<ChannelItemInfo>() };
        }

        if (folderId.StartsWith("series:", StringComparison.Ordinal))
        {
            // Format: series:{providerId}:{seriesId}
            var parts = folderId.Split(':', 3);
            if (parts.Length == 3 &&
                Guid.TryParse(parts[1], out var providerId) &&
                int.TryParse(parts[2], out var seriesId))
            {
                var provider = _providerRegistry.GetById(providerId);
                if (provider is not null)
                {
                    var seriesInfo = await _metadataService
                        .GetOrFetchInfoAsync(provider, seriesId, cancellationToken)
                        .ConfigureAwait(false);

                    if (seriesInfo?.Episodes is not null && seriesInfo.Episodes.Count > 0)
                    {
                        var seasonItems = seriesInfo.Episodes.Keys
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                            .Select(seasonKey => new ChannelItemInfo
                            {
                                Id = $"season:{providerId}:{seriesId}:{seasonKey}",
                                Name = $"Season {seasonKey}",
                                Type = ChannelItemType.Folder,
                            })
                            .ToList();

                        return new ChannelItemResult
                        {
                            Items = seasonItems,
                            TotalRecordCount = seasonItems.Count,
                        };
                    }
                }
            }

            _logger.LogWarning("SeriesChannel: could not expand series folder '{FolderId}'", folderId);
            return new ChannelItemResult { Items = new List<ChannelItemInfo>() };
        }

        if (folderId.StartsWith("season:", StringComparison.Ordinal))
        {
            // Format: season:{providerId}:{seriesId}:{seasonKey}
            var parts = folderId.Split(':', 4);
            if (parts.Length == 4 &&
                Guid.TryParse(parts[1], out var providerId) &&
                int.TryParse(parts[2], out var seriesId))
            {
                var seasonKey = parts[3];
                var provider = _providerRegistry.GetById(providerId);
                if (provider is not null)
                {
                    var seriesInfo = await _metadataService
                        .GetOrFetchInfoAsync(provider, seriesId, cancellationToken)
                        .ConfigureAwait(false);

                    if (seriesInfo?.Episodes is not null &&
                        seriesInfo.Episodes.TryGetValue(seasonKey, out var episodes) &&
                        episodes is not null)
                    {
                        var episodeItems = episodes
                            .OrderBy(e => e.EpisodeNum)
                            .Select(ep =>
                            {
                                var container = string.IsNullOrEmpty(ep.ContainerExtension)
                                    ? "mp4"
                                    : ep.ContainerExtension;
                                var itemId = $"{providerId}_ep_{seriesId}_{ep.Id}_{container}";
                                var url = _apiClient.BuildSeriesStreamUrl(provider, ep.Id, container);
                                return new ChannelItemInfo
                                {
                                    Id = itemId,
                                    Name = ep.Title,
                                    Type = ChannelItemType.Media,
                                    MediaType = ChannelMediaType.Video,
                                    ContentType = ChannelMediaContentType.Movie,
                                    MediaSources = new List<MediaSourceInfo>
                                    {
                                        new MediaSourceInfo
                                        {
                                            Id = itemId,
                                            Path = url,
                                            Protocol = MediaProtocol.Http,
                                            IsRemote = true,
                                            Container = container,
                                            Name = ep.Title,
                                            SupportsDirectPlay = true,
                                            SupportsDirectStream = true,
                                            SupportsTranscoding = true,
                                        },
                                    },
                                };
                            })
                            .ToList();

                        return new ChannelItemResult
                        {
                            Items = episodeItems,
                            TotalRecordCount = episodeItems.Count,
                        };
                    }
                }
            }

            _logger.LogWarning("SeriesChannel: could not expand season folder '{FolderId}'", folderId);
            return new ChannelItemResult { Items = new List<ChannelItemInfo>() };
        }

        _logger.LogWarning("SeriesChannel: unknown folder format '{FolderId}'", folderId);
        return new ChannelItemResult { Items = new List<ChannelItemInfo>() };
    }

    /// <inheritdoc />
    public Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(
        string id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("SeriesChannel.GetChannelItemMediaInfo called with id='{Id}'", id);

        // ID format: "{providerId}_ep_{seriesId}_{episodeId}_{container}"
        var outerParts = id.Split("_ep_", 2, StringSplitOptions.None);
        if (outerParts.Length != 2 || !Guid.TryParse(outerParts[0], out var providerId))
        {
            _logger.LogWarning("SeriesChannel: could not parse ID '{Id}' (outerParts.Length={Len})", id, outerParts.Length);
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var provider = _providerRegistry.GetById(providerId);
        if (provider is null)
        {
            _logger.LogWarning("SeriesChannel: provider '{ProviderId}' not found", providerId);
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        // tail = "{seriesId}_{episodeId}_{container}"
        var tail = outerParts[1].Split('_');
        _logger.LogInformation("SeriesChannel: tail parts={Parts}", string.Join("|", tail));
        if (tail.Length < 3 || string.IsNullOrEmpty(tail[1]))
        {
            _logger.LogWarning("SeriesChannel: could not parse episode tail '{Tail}' in '{Id}' (tail.Length={Len})", outerParts[1], id, tail.Length);
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var episodeId = tail[1];
        var container = tail[2];
        var url = _apiClient.BuildSeriesStreamUrl(provider, episodeId, container);
        _logger.LogInformation("SeriesChannel: resolved url='{Url}' for id='{Id}'", url, id);

        IEnumerable<MediaSourceInfo> result = new List<MediaSourceInfo>
        {
            new MediaSourceInfo
            {
                Id = id,
                Path = url,
                Protocol = MediaProtocol.Http,
                IsRemote = true,
                Container = container,
                Name = id,
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
            var categories = catalog.SeriesCategories;

            if (categories is null || categories.Count == 0)
            {
                _logger.LogDebug("No series categories cached for provider '{Provider}'; falling back to flat list.", provider.DisplayName);
                var flat = BuildSeriesItemsForProvider(provider.Id);
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
                    Id = $"series-cat:{provider.Id}:{category.CategoryId}",
                    Name = name,
                    Type = ChannelItemType.Folder,
                });
            }
        }

        return items;
    }

    private List<ChannelItemInfo> BuildSeriesItemsForCategory(Guid providerId, string categoryId)
    {
        var provider = _providerRegistry.GetById(providerId);
        if (provider is null)
        {
            _logger.LogWarning("SeriesChannel: provider '{ProviderId}' not found for category '{CategoryId}'", providerId, categoryId);
            return new List<ChannelItemInfo>();
        }

        var catalog = _catalogCache.GetOrCreate(providerId);
        var seriesList = catalog.Series?
            .Where(s => s.CategoryId == categoryId)
            .ToList();

        if (seriesList is null || seriesList.Count == 0)
        {
            _logger.LogDebug("No series for provider '{Provider}' category '{CategoryId}'.", provider.DisplayName, categoryId);
            return new List<ChannelItemInfo>();
        }

        var items = new List<ChannelItemInfo>(seriesList.Count);
        foreach (var series in seriesList)
        {
            var item = new ChannelItemInfo
            {
                Id = $"series:{provider.Id}:{series.SeriesId}",
                Name = series.Name,
                Type = ChannelItemType.Folder,
            };

            items.Add(item);
        }

        return items;
    }

    private List<ChannelItemInfo> BuildSeriesItemsForProvider(Guid providerId)
    {
        var provider = _providerRegistry.GetById(providerId);
        if (provider is null) return new List<ChannelItemInfo>();

        var catalog = _catalogCache.GetOrCreate(providerId);
        var seriesList = catalog.Series;
        if (seriesList is null || seriesList.Count == 0) return new List<ChannelItemInfo>();

        var items = new List<ChannelItemInfo>(seriesList.Count);
        foreach (var series in seriesList)
        {
            var item = new ChannelItemInfo
            {
                Id = $"series:{provider.Id}:{series.SeriesId}",
                Name = series.Name,
                Type = ChannelItemType.Folder,
            };

            items.Add(item);
        }

        return items;
    }
}
