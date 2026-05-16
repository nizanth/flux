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
/// Jellyfin IChannel implementation that exposes Xtream Codes series content as a
/// three-level browse-able hierarchy: Series → Season → Episode.
/// </summary>
public sealed class SeriesChannel : IChannel
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
            var timestamps = _providerRegistry.GetAll()
                .Select(p => _catalogCache.GetOrCreate(p.Id).SeriesRefreshedAt)
                .Where(t => t.HasValue)
                .ToList();
            return timestamps.Count > 0
                ? timestamps.Max()!.Value.ToString("yyyyMMddHHmmss")
                : "empty";
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
            ContentTypes = new List<ChannelMediaContentType> { ChannelMediaContentType.Episode },
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
            var items = BuildSeriesItems();
            return new ChannelItemResult { Items = items, TotalRecordCount = items.Count };
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

                    if (seriesInfo?.Seasons is not null)
                    {
                        var seasonItems = seriesInfo.Seasons.Keys
                            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
                            .Select(seasonKey => new ChannelItemInfo
                            {
                                Id = $"season:{providerId}:{seriesId}:{seasonKey}",
                                Name = $"Season {seasonKey}",
                                Type = ChannelItemType.Folder,
                                FolderType = ChannelFolderType.Season,
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

                                var item = new ChannelItemInfo
                                {
                                    Id = $"{providerId}_ep_{ep.Id}",
                                    Name = ep.Title,
                                    Type = ChannelItemType.Media,
                                    MediaType = ChannelMediaType.Video,
                                    ContentType = ChannelMediaContentType.Episode,
                                };

                                // Embed the stream URL directly so Jellyfin never needs to call
                                // back into the plugin for a media source.
                                if (int.TryParse(ep.Id, out var numericId))
                                {
                                    var url = _apiClient.BuildSeriesStreamUrl(provider, numericId, container);
                                    item.MediaSources = new List<MediaSourceInfo>
                                    {
                                        new MediaSourceInfo
                                        {
                                            Id = "0",
                                            Path = url,
                                            Protocol = MediaProtocol.Http,
                                            IsRemote = true,
                                            Container = container,
                                            Name = ep.Title,
                                            SupportsDirectPlay = true,
                                            SupportsDirectStream = true,
                                            SupportsTranscoding = true,
                                        },
                                    };
                                }
                                else
                                {
                                    _logger.LogWarning(
                                        "SeriesChannel: episode '{Title}' has non-numeric Id '{EpId}'; skipping media source",
                                        ep.Title, ep.Id);
                                }

                                return item;
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
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages()
        => Enumerable.Empty<ImageType>();

    private List<ChannelItemInfo> BuildSeriesItems()
    {
        var items = new List<ChannelItemInfo>();

        foreach (var provider in _providerRegistry.GetAll())
        {
            var catalog = _catalogCache.GetOrCreate(provider.Id);
            var seriesList = catalog.Series;

            if (seriesList is null || seriesList.Count == 0)
            {
                _logger.LogDebug("No series cached for provider '{Provider}'; skipping.", provider.DisplayName);
                continue;
            }

            foreach (var series in seriesList)
            {
                var item = new ChannelItemInfo
                {
                    Id = $"series:{provider.Id}:{series.SeriesId}",
                    Name = series.Name,
                    Type = ChannelItemType.Folder,
                    FolderType = ChannelFolderType.Series,
                };

                if (!string.IsNullOrEmpty(series.Cover))
                {
                    item.ImageUrl = series.Cover;
                }

                items.Add(item);
            }
        }

        return items;
    }
}
