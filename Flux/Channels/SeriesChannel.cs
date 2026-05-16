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

    /// <summary>
    /// Initializes a new instance of the <see cref="SeriesChannel"/> class.
    /// </summary>
    /// <param name="providerRegistry">Registry of configured Xtream Codes providers.</param>
    /// <param name="catalogCache">In-memory catalog cache holding series stream data.</param>
    /// <param name="apiClient">Xtream Codes HTTP API client.</param>
    /// <param name="metadataService">Series metadata / episode info cache service.</param>
    /// <param name="logger">Logger instance.</param>
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
            // Top level — list all series as folders
            var items = BuildSeriesItems();
            return new ChannelItemResult
            {
                Items = items,
                TotalRecordCount = items.Count,
            };
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
            // Format: season:{providerId}:{seriesId}:{seasonNumber}
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
                            .Select(ep => new ChannelItemInfo
                            {
                                // Encode seriesId|episodeId|container so GetChannelItemMediaInfo
                                // can build the URL without iterating all series.
                                Id = $"{providerId}_ep_{seriesId}|{ep.Id}|{ep.ContainerExtension}",
                                Name = ep.Title,
                                Type = ChannelItemType.Media,
                                MediaType = ChannelMediaType.Video,
                                ContentType = ChannelMediaContentType.Episode,
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
    public async Task<IEnumerable<MediaSourceInfo>> GetChannelItemMediaInfo(
        string id,
        CancellationToken cancellationToken)
    {
        // Composite ID: "{providerId}_ep_{seriesId}|{episodeId}|{container}"  (v1.1.7+)
        //           or: "{providerId}_ep_{episodeId}"                          (pre-v1.1.7)
        var outerParts = id.Split("_ep_", 2, StringSplitOptions.None);
        if (outerParts.Length != 2 || !Guid.TryParse(outerParts[0], out var providerId))
        {
            _logger.LogWarning("SeriesChannel: could not parse composite ID '{Id}'", id);
            return Enumerable.Empty<MediaSourceInfo>();
        }

        var provider = _providerRegistry.GetById(providerId);
        if (provider is null)
        {
            _logger.LogWarning("SeriesChannel: provider '{ProviderId}' not found for item '{Id}'", providerId, id);
            return Enumerable.Empty<MediaSourceInfo>();
        }

        var segment = outerParts[1];
        int numericEpisodeId;
        string container;
        int seriesId = -1;
        string episodeIdStr;

        var innerParts = segment.Split('|');
        if (innerParts.Length >= 2
            && int.TryParse(innerParts[0], out seriesId)
            && int.TryParse(innerParts[1], out numericEpisodeId))
        {
            // New format: {seriesId}|{episodeId}|{container}
            episodeIdStr = innerParts[1];
            container = innerParts.Length > 2 && !string.IsNullOrEmpty(innerParts[2])
                ? innerParts[2]
                : "mp4";
        }
        else if (int.TryParse(segment, out numericEpisodeId))
        {
            // Old format: just {episodeId}
            episodeIdStr = segment;
            container = "mp4";
        }
        else
        {
            _logger.LogWarning("SeriesChannel: cannot parse episode segment '{Segment}' in '{Id}'", segment, id);
            return Enumerable.Empty<MediaSourceInfo>();
        }

        // Fetch episode title — best-effort only, never blocks playback on failure.
        var episodeName = numericEpisodeId.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (seriesId > 0)
        {
            try
            {
                var seriesInfo = await _metadataService
                    .GetOrFetchInfoAsync(provider, seriesId, cancellationToken)
                    .ConfigureAwait(false);

                var ep = seriesInfo?.Episodes?.Values
                    .SelectMany(eps => eps)
                    .FirstOrDefault(e => e.Id == episodeIdStr);
                if (ep is not null)
                {
                    episodeName = ep.Title;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "SeriesChannel: metadata lookup failed for series {SeriesId}", seriesId);
            }
        }

        var url = _apiClient.BuildSeriesStreamUrl(provider, numericEpisodeId, container);
        _logger.LogDebug("SeriesChannel: serving episode {EpisodeId} → {Url}", numericEpisodeId, url);

        return new List<MediaSourceInfo>
        {
            new MediaSourceInfo
            {
                Path = url,
                Protocol = MediaProtocol.Http,
                IsRemote = true,
                Container = container,
                Id = id,
                Name = episodeName,
                SupportsDirectPlay = true,
                SupportsDirectStream = true,
                SupportsTranscoding = true,
            },
        };
    }

    /// <inheritdoc />
    public Task<DynamicImageResponse> GetChannelImage(ImageType type, CancellationToken cancellationToken)
        => Task.FromResult(new DynamicImageResponse { HasImage = false });

    /// <inheritdoc />
    public IEnumerable<ImageType> GetSupportedChannelImages()
        => Enumerable.Empty<ImageType>();

    // ── Private helpers ───────────────────────────────────────────────────────

    private List<ChannelItemInfo> BuildSeriesItems()
    {
        var items = new List<ChannelItemInfo>();

        foreach (var provider in _providerRegistry.GetAll())
        {
            var catalog = _catalogCache.GetOrCreate(provider.Id);
            var seriesList = catalog.Series;

            if (seriesList is null || seriesList.Count == 0)
            {
                _logger.LogDebug(
                    "No series cached for provider '{Provider}'; skipping.",
                    provider.DisplayName);
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
