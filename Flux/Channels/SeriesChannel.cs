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
                        // Episode ID encodes all info needed to build the URL at playback time:
                        // "{providerId}_ep_{seriesId}_{episodeId}_{container}"
                        // Only alphanumeric/hyphen chars — no encoding issues in Jellyfin's ExternalId.
                        var episodeItems = episodes
                            .OrderBy(e => e.EpisodeNum)
                            .Select(ep =>
                            {
                                var container = string.IsNullOrEmpty(ep.ContainerExtension)
                                    ? "mp4"
                                    : ep.ContainerExtension;
                                return new ChannelItemInfo
                                {
                                    Id = $"{providerId}_ep_{seriesId}_{ep.Id}_{container}",
                                    Name = ep.Title,
                                    Type = ChannelItemType.Media,
                                    MediaType = ChannelMediaType.Video,
                                    ContentType = ChannelMediaContentType.Episode,
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
        // ID format: "{providerId}_ep_{seriesId}_{episodeId}_{container}"
        // e.g.  "a1b2c3d4-e5f6-7890-abcd-ef1234567890_ep_100_12345_mkv"
        // Split on "_ep_" first (2 parts), then split the tail on "_" to get last token as container
        // and second-to-last as episodeId.
        var outerParts = id.Split("_ep_", 2, StringSplitOptions.None);
        if (outerParts.Length != 2 || !Guid.TryParse(outerParts[0], out var providerId))
        {
            _logger.LogWarning("SeriesChannel: could not parse ID '{Id}'", id);
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
        // tail[0] = seriesId, tail[1] = episodeId, tail[2] = container
        if (tail.Length < 3 || !int.TryParse(tail[1], out var numericEpisodeId))
        {
            _logger.LogWarning("SeriesChannel: could not parse episode tail '{Tail}' in '{Id}'", outerParts[1], id);
            return Task.FromResult(Enumerable.Empty<MediaSourceInfo>());
        }

        var container = tail[2];
        var url = _apiClient.BuildSeriesStreamUrl(provider, numericEpisodeId, container);

        IEnumerable<MediaSourceInfo> result = new List<MediaSourceInfo>
        {
            new MediaSourceInfo
            {
                Id = "0",
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
