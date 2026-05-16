using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.Flux.Api.Dto;
using Jellyfin.Plugin.Flux.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Flux.Api;

/// <summary>
/// HTTP client for communicating with an Xtream Codes IPTV panel API.
/// </summary>
public sealed class XtreamApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<XtreamApiClient> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="XtreamApiClient"/> class.
    /// </summary>
    /// <param name="httpClient">The <see cref="HttpClient"/> used for requests.</param>
    /// <param name="logger">Logger instance.</param>
    public XtreamApiClient(HttpClient httpClient, ILogger<XtreamApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Authenticates against the Xtream Codes API and retrieves server/user info.
    /// </summary>
    /// <param name="provider">The provider configuration to authenticate with.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The <see cref="AuthResponse"/> or <c>null</c> on failure.</returns>
    public async Task<AuthResponse?> AuthenticateAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        var url = BuildPlayerApiUrl(provider, string.Empty);
        _logger.LogDebug("Authenticating against {Url}", url);

        try
        {
            return await _httpClient.GetFromJsonAsync<AuthResponse>(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Authentication failed for provider {Provider}", provider.DisplayName);
            return null;
        }
    }

    /// <summary>
    /// Retrieves all live stream categories for the given provider.
    /// </summary>
    public async Task<List<Category>?> GetLiveCategoriesAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        var url = BuildPlayerApiUrl(provider, "&action=get_live_categories");
        return await GetListAsync<Category>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves all live streams for the given provider.
    /// </summary>
    public async Task<List<LiveStream>?> GetLiveStreamsAsync(ProviderConfig provider, string? categoryId = null, CancellationToken cancellationToken = default)
    {
        var action = categoryId is null
            ? "&action=get_live_streams"
            : $"&action=get_live_streams&category_id={Uri.EscapeDataString(categoryId)}";
        var url = BuildPlayerApiUrl(provider, action);
        return await GetListAsync<LiveStream>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves all VOD categories for the given provider.
    /// </summary>
    public async Task<List<Category>?> GetVodCategoriesAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        var url = BuildPlayerApiUrl(provider, "&action=get_vod_categories");
        return await GetListAsync<Category>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves all VOD streams for the given provider.
    /// </summary>
    public async Task<List<VodStream>?> GetVodStreamsAsync(ProviderConfig provider, string? categoryId = null, CancellationToken cancellationToken = default)
    {
        var action = categoryId is null
            ? "&action=get_vod_streams"
            : $"&action=get_vod_streams&category_id={Uri.EscapeDataString(categoryId)}";
        var url = BuildPlayerApiUrl(provider, action);
        return await GetListAsync<VodStream>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves detailed info for a specific VOD stream.
    /// </summary>
    public async Task<VodInfo?> GetVodInfoAsync(ProviderConfig provider, int vodId, CancellationToken cancellationToken = default)
    {
        var url = BuildPlayerApiUrl(provider, $"&action=get_vod_info&vod_id={vodId}");
        try
        {
            return await _httpClient.GetFromJsonAsync<VodInfo>(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch VOD info for stream {VodId}", vodId);
            return null;
        }
    }

    /// <summary>
    /// Retrieves all series categories for the given provider.
    /// </summary>
    public async Task<List<Category>?> GetSeriesCategoriesAsync(ProviderConfig provider, CancellationToken cancellationToken = default)
    {
        var url = BuildPlayerApiUrl(provider, "&action=get_series_categories");
        return await GetListAsync<Category>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves all series entries for the given provider.
    /// </summary>
    public async Task<List<SeriesStream>?> GetSeriesAsync(ProviderConfig provider, string? categoryId = null, CancellationToken cancellationToken = default)
    {
        var action = categoryId is null
            ? "&action=get_series"
            : $"&action=get_series&category_id={Uri.EscapeDataString(categoryId)}";
        var url = BuildPlayerApiUrl(provider, action);
        return await GetListAsync<SeriesStream>(url, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Retrieves detailed info (seasons and episodes) for a specific series.
    /// </summary>
    public async Task<SeriesInfo?> GetSeriesInfoAsync(ProviderConfig provider, int seriesId, CancellationToken cancellationToken = default)
    {
        var url = BuildPlayerApiUrl(provider, $"&action=get_series_info&series_id={seriesId}");
        try
        {
            return await _httpClient.GetFromJsonAsync<SeriesInfo>(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch series info for series {SeriesId}", seriesId);
            return null;
        }
    }

    /// <summary>
    /// Retrieves the short EPG listing for a specific live stream.
    /// </summary>
    public async Task<ShortEpgResponse?> GetShortEpgAsync(ProviderConfig provider, int streamId, int limit = 4, CancellationToken cancellationToken = default)
    {
        var url = BuildPlayerApiUrl(provider, $"&action=get_short_epg&stream_id={streamId}&limit={limit}");
        try
        {
            return await _httpClient.GetFromJsonAsync<ShortEpgResponse>(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch short EPG for stream {StreamId}", streamId);
            return null;
        }
    }

    /// <summary>
    /// Builds the stream URL for a live channel.
    /// </summary>
    public string BuildLiveStreamUrl(ProviderConfig provider, int streamId, string extension = "ts")
    {
        var password = DecryptPassword(provider.EncryptedPassword);
        return $"{provider.BaseUrl}/live/{Uri.EscapeDataString(provider.Username)}/{Uri.EscapeDataString(password)}/{streamId}.{extension}";
    }

    /// <summary>
    /// Builds the stream URL for a VOD movie.
    /// </summary>
    public string BuildVodStreamUrl(ProviderConfig provider, int streamId, string extension = "mp4")
    {
        var password = DecryptPassword(provider.EncryptedPassword);
        return $"{provider.BaseUrl}/movie/{Uri.EscapeDataString(provider.Username)}/{Uri.EscapeDataString(password)}/{streamId}.{extension}";
    }

    /// <summary>
    /// Builds the stream URL for a series episode.
    /// </summary>
    public string BuildSeriesStreamUrl(ProviderConfig provider, int episodeId, string extension = "mp4")
    {
        var password = DecryptPassword(provider.EncryptedPassword);
        return $"{provider.BaseUrl}/series/{Uri.EscapeDataString(provider.Username)}/{Uri.EscapeDataString(password)}/{episodeId}.{extension}";
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private string BuildPlayerApiUrl(ProviderConfig provider, string extraParams)
    {
        var password = DecryptPassword(provider.EncryptedPassword);
        return $"{provider.BaseUrl}/player_api.php?username={Uri.EscapeDataString(provider.Username)}&password={Uri.EscapeDataString(password)}{extraParams}";
    }

    private async Task<List<T>?> GetListAsync<T>(string url, CancellationToken cancellationToken)
    {
        try
        {
            return await _httpClient.GetFromJsonAsync<List<T>>(url, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to fetch list from {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Decrypts a stored password. Currently a no-op placeholder — replace with
    /// a real implementation (e.g. DPAPI or AES) before production use.
    /// </summary>
    private static string DecryptPassword(string encryptedPassword)
    {
        if (string.IsNullOrEmpty(encryptedPassword))
        {
            return string.Empty;
        }

        try
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encryptedPassword));
        }
        catch
        {
            return encryptedPassword;
        }
    }
}
