using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Jellyfin.Plugin.Flux.Api.Dto;

/// <summary>
/// Converts any JSON scalar (number, boolean, string, null) to a nullable string.
/// Xtream Codes providers are inconsistent — the same field may arrive as a quoted
/// string on one server and a bare number on another.
/// </summary>
internal sealed class AnyToStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null: return null;
            case JsonTokenType.String: return reader.GetString();
            case JsonTokenType.True: return "true";
            case JsonTokenType.False: return "false";
            case JsonTokenType.Number:
                // Try integer first to avoid unnecessary decimal points
                return reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString();
            default:
                reader.Skip();
                return null;
        }
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
        => writer.WriteStringValue(value);
}

/// <summary>
/// Root authentication response from the Xtream Codes player_api.php endpoint.
/// </summary>
public sealed class AuthResponse
{
    /// <summary>Gets or sets the user information returned by the server.</summary>
    [JsonPropertyName("user_info")]
    public UserInfo? UserInfo { get; set; }

    /// <summary>Gets or sets the server information returned by the server.</summary>
    [JsonPropertyName("server_info")]
    public ServerInfo? ServerInfo { get; set; }
}

/// <summary>
/// Information about the authenticated Xtream Codes user account.
/// </summary>
public sealed class UserInfo
{
    /// <summary>Gets or sets a value indicating whether authentication succeeded (1) or failed (0).</summary>
    [JsonPropertyName("auth")]
    public int Auth { get; set; }

    /// <summary>Gets or sets the account status (e.g. "Active", "Expired").</summary>
    [JsonPropertyName("status")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? Status { get; set; }

    /// <summary>Gets or sets the Unix timestamp when the account expires (string or number depending on provider).</summary>
    [JsonPropertyName("exp_date")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? ExpDate { get; set; }

    /// <summary>Gets or sets the maximum number of simultaneous connections allowed.</summary>
    [JsonPropertyName("max_connections")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? MaxConnections { get; set; }

    /// <summary>Gets or sets the current number of active connections.</summary>
    [JsonPropertyName("active_cons")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? ActiveCons { get; set; }

    /// <summary>Gets or sets the list of output formats allowed for this account.</summary>
    [JsonPropertyName("allowed_output_formats")]
    public List<string>? AllowedOutputFormats { get; set; }
}

/// <summary>
/// Information about the Xtream Codes server.
/// </summary>
public sealed class ServerInfo
{
    /// <summary>Gets or sets the server hostname or IP address.</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>Gets or sets the HTTP port the server listens on (number or string depending on provider).</summary>
    [JsonPropertyName("port")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? Port { get; set; }

    /// <summary>Gets or sets the HTTPS port the server listens on (number or string depending on provider).</summary>
    [JsonPropertyName("https_port")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? HttpsPort { get; set; }

    /// <summary>Gets or sets the preferred protocol ("http" or "https").</summary>
    [JsonPropertyName("server_protocol")]
    public string? Protocol { get; set; }
}

/// <summary>
/// Represents a stream category (used for live, VOD, and series).
/// </summary>
public sealed class Category
{
    /// <summary>Gets or sets the unique category identifier.</summary>
    [JsonPropertyName("category_id")]
    public string CategoryId { get; set; } = string.Empty;

    /// <summary>Gets or sets the human-readable category name.</summary>
    [JsonPropertyName("category_name")]
    public string CategoryName { get; set; } = string.Empty;

    /// <summary>Gets or sets the parent category ID (0 if top-level).</summary>
    [JsonPropertyName("parent_id")]
    public int ParentId { get; set; }
}

/// <summary>
/// Represents a live TV stream entry.
/// </summary>
public sealed class LiveStream
{
    /// <summary>Gets or sets the unique stream identifier.</summary>
    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    /// <summary>Gets or sets the display name of the channel.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the ordering number for this stream.</summary>
    [JsonPropertyName("num")]
    public int Num { get; set; }

    /// <summary>Gets or sets the URL to the channel logo/icon.</summary>
    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    /// <summary>Gets or sets the EPG channel identifier used to match EPG data.</summary>
    [JsonPropertyName("epg_channel_id")]
    public string? EpgChannelId { get; set; }

    /// <summary>Gets or sets the category this stream belongs to.</summary>
    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? CategoryId { get; set; }

    /// <summary>Gets or sets a value indicating whether TV archive/catch-up is available (1) or not (0).</summary>
    [JsonPropertyName("tv_archive")]
    public int TvArchive { get; set; }

    /// <summary>Gets or sets the number of days of TV archive available.</summary>
    [JsonPropertyName("tv_archive_duration")]
    public int TvArchiveDuration { get; set; }

    /// <summary>Gets or sets the custom stream identifier, if any.</summary>
    [JsonPropertyName("custom_sid")]
    public string? CustomSid { get; set; }
}

/// <summary>
/// Represents a Video-on-Demand stream entry.
/// </summary>
public sealed class VodStream
{
    /// <summary>Gets or sets the unique stream identifier.</summary>
    [JsonPropertyName("stream_id")]
    public int StreamId { get; set; }

    /// <summary>Gets or sets the display name of the movie.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the URL to the movie poster/icon.</summary>
    [JsonPropertyName("stream_icon")]
    public string? StreamIcon { get; set; }

    /// <summary>Gets or sets the category this stream belongs to.</summary>
    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? CategoryId { get; set; }

    /// <summary>Gets or sets the file container extension (e.g. "mp4", "mkv").</summary>
    [JsonPropertyName("container_extension")]
    public string ContainerExtension { get; set; } = "mp4";

    /// <summary>Gets or sets the rating string for this movie.</summary>
    [JsonPropertyName("rating")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? Rating { get; set; }

    /// <summary>Gets or sets the Unix timestamp when the stream was added.</summary>
    [JsonPropertyName("added")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? Added { get; set; }
}

/// <summary>
/// Full VOD info response including metadata and stream data.
/// </summary>
public sealed class VodInfo
{
    /// <summary>Gets or sets the detailed metadata for this movie.</summary>
    [JsonPropertyName("info")]
    public VodInfoDetail? Info { get; set; }

    /// <summary>Gets or sets the stream data for this movie.</summary>
    [JsonPropertyName("movie_data")]
    public VodStream? MovieData { get; set; }
}

/// <summary>
/// Detailed metadata for a VOD movie.
/// </summary>
public sealed class VodInfoDetail
{
    /// <summary>Gets or sets the TMDB identifier for this movie.</summary>
    [JsonPropertyName("tmdb_id")]
    public int? TmdbId { get; set; }

    /// <summary>Gets or sets the IMDb identifier for this movie.</summary>
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the plot synopsis.</summary>
    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    /// <summary>Gets or sets a comma-separated list of cast members.</summary>
    [JsonPropertyName("cast")]
    public string? Cast { get; set; }

    /// <summary>Gets or sets the director(s) of the movie.</summary>
    [JsonPropertyName("director")]
    public string? Director { get; set; }

    /// <summary>Gets or sets the genre(s) of the movie.</summary>
    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    /// <summary>Gets or sets the release date string.</summary>
    [JsonPropertyName("releasedate")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the rating string.</summary>
    [JsonPropertyName("rating")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? Rating { get; set; }

    /// <summary>Gets or sets the duration in seconds.</summary>
    [JsonPropertyName("duration_secs")]
    public int? DurationSecs { get; set; }

    /// <summary>Gets or sets the URL to the movie poster image.</summary>
    [JsonPropertyName("movie_image")]
    public string? MovieImage { get; set; }

    /// <summary>Gets or sets a list of backdrop image URLs.</summary>
    [JsonPropertyName("backdrop_path")]
    public List<string>? BackdropPath { get; set; }

    /// <summary>Gets or sets the YouTube trailer video ID or URL.</summary>
    [JsonPropertyName("youtube_trailer")]
    public string? YoutubeTrailer { get; set; }

    /// <summary>Gets or sets the episode runtime string.</summary>
    [JsonPropertyName("episode_run_time")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? EpisodeRunTime { get; set; }
}

/// <summary>
/// Represents a series (TV show) entry in the Xtream Codes catalog.
/// </summary>
public sealed class SeriesStream
{
    /// <summary>Gets or sets the unique series identifier.</summary>
    [JsonPropertyName("series_id")]
    public int SeriesId { get; set; }

    /// <summary>Gets or sets the display name of the series.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the URL to the series cover art.</summary>
    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    /// <summary>Gets or sets the category this series belongs to.</summary>
    [JsonPropertyName("category_id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? CategoryId { get; set; }

    /// <summary>Gets or sets the genre(s) of the series.</summary>
    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    /// <summary>Gets or sets the release date string.</summary>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the plot synopsis.</summary>
    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    /// <summary>Gets or sets the rating on a 5-point scale.</summary>
    [JsonPropertyName("rating_5based")]
    public double RatingBased { get; set; }
}

/// <summary>
/// Full series info response including metadata, seasons, and episodes.
/// </summary>
public sealed class SeriesInfo
{
    /// <summary>Gets or sets the detailed metadata for this series.</summary>
    [JsonPropertyName("info")]
    public SeriesInfoDetail? Info { get; set; }

    /// <summary>Gets or sets the seasons dictionary keyed by season number string.</summary>
    [JsonPropertyName("seasons")]
    public Dictionary<string, SeasonInfo>? Seasons { get; set; }

    /// <summary>Gets or sets the episodes dictionary keyed by season number string.</summary>
    [JsonPropertyName("episodes")]
    public Dictionary<string, List<EpisodeInfo>>? Episodes { get; set; }
}

/// <summary>
/// Detailed metadata for a series.
/// </summary>
public sealed class SeriesInfoDetail
{
    /// <summary>Gets or sets the TMDB identifier.</summary>
    [JsonPropertyName("tmdb_id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? TmdbId { get; set; }

    /// <summary>Gets or sets the IMDb identifier.</summary>
    [JsonPropertyName("imdb_id")]
    public string? ImdbId { get; set; }

    /// <summary>Gets or sets the TVDB identifier.</summary>
    [JsonPropertyName("tvdb_id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? TvdbId { get; set; }

    /// <summary>Gets or sets the series name.</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the URL to the series cover art.</summary>
    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    /// <summary>Gets or sets the plot synopsis.</summary>
    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    /// <summary>Gets or sets the genre(s) of the series.</summary>
    [JsonPropertyName("genre")]
    public string? Genre { get; set; }

    /// <summary>Gets or sets the release date string.</summary>
    [JsonPropertyName("release_date")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the episode runtime string.</summary>
    [JsonPropertyName("episode_run_time")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string? EpisodeRunTime { get; set; }

    /// <summary>Gets or sets a list of backdrop image URLs.</summary>
    [JsonPropertyName("backdrop_path")]
    public List<string>? BackdropPath { get; set; }
}

/// <summary>
/// Metadata for a single season of a series.
/// </summary>
public sealed class SeasonInfo
{
    /// <summary>Gets or sets the season number.</summary>
    [JsonPropertyName("season_number")]
    public int SeasonNumber { get; set; }

    /// <summary>Gets or sets the season name.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>Gets or sets the season overview/synopsis.</summary>
    [JsonPropertyName("overview")]
    public string? Overview { get; set; }

    /// <summary>Gets or sets the URL to the season cover image.</summary>
    [JsonPropertyName("cover")]
    public string? Cover { get; set; }

    /// <summary>Gets or sets the URL to the large season cover image.</summary>
    [JsonPropertyName("cover_big")]
    public string? CoverBig { get; set; }
}

/// <summary>
/// Represents a single episode within a series season.
/// </summary>
public sealed class EpisodeInfo
{
    /// <summary>Gets or sets the unique episode identifier (stream ID as a string).</summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the episode number within the season.</summary>
    [JsonPropertyName("episode_num")]
    public int EpisodeNum { get; set; }

    /// <summary>Gets or sets the episode title.</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the file container extension (e.g. "mp4", "mkv").</summary>
    [JsonPropertyName("container_extension")]
    public string ContainerExtension { get; set; } = "mp4";

    /// <summary>Gets or sets the detailed metadata for this episode.</summary>
    [JsonPropertyName("info")]
    public EpisodeInfoDetail? Info { get; set; }
}

/// <summary>
/// Detailed metadata for an episode.
/// </summary>
public sealed class EpisodeInfoDetail
{
    /// <summary>Gets or sets the episode plot description.</summary>
    [JsonPropertyName("plot")]
    public string? Plot { get; set; }

    /// <summary>Gets or sets the episode air date string.</summary>
    [JsonPropertyName("releasedate")]
    public string? ReleaseDate { get; set; }

    /// <summary>Gets or sets the episode duration in seconds.</summary>
    [JsonPropertyName("duration_secs")]
    public int? DurationSecs { get; set; }

    /// <summary>Gets or sets the URL to the episode still/thumbnail image.</summary>
    [JsonPropertyName("movie_image")]
    public string? StillImage { get; set; }
}

/// <summary>
/// Response from the short EPG endpoint containing current and upcoming programme listings.
/// </summary>
public sealed class ShortEpgResponse
{
    /// <summary>Gets or sets the list of EPG entries.</summary>
    [JsonPropertyName("epg_listings")]
    public List<ShortEpgEntry>? EpgListings { get; set; }
}

/// <summary>
/// A single EPG programme entry.
/// </summary>
public sealed class ShortEpgEntry
{
    /// <summary>Gets or sets the entry identifier.</summary>
    [JsonPropertyName("id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string Id { get; set; } = string.Empty;

    /// <summary>Gets or sets the EPG channel identifier.</summary>
    [JsonPropertyName("epg_id")]
    [JsonConverter(typeof(AnyToStringConverter))]
    public string EpgId { get; set; } = string.Empty;

    /// <summary>Gets or sets the programme title (may be Base64-encoded depending on provider).</summary>
    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets the programme start time string.</summary>
    [JsonPropertyName("start")]
    public string Start { get; set; } = string.Empty;

    /// <summary>Gets or sets the programme end time string.</summary>
    [JsonPropertyName("end")]
    public string End { get; set; } = string.Empty;

    /// <summary>Gets or sets the programme description (may be Base64-encoded depending on provider).</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }
}
