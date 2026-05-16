using System;

namespace Jellyfin.Plugin.Flux.LiveTv;

/// <summary>
/// Represents a single EPG programme entry for a Flux live TV channel.
/// This is the plugin's internal model; it is mapped to Jellyfin's
/// <see cref="MediaBrowser.Controller.LiveTv.ProgramInfo"/> when returned to the Live TV subsystem.
/// </summary>
public sealed class EpgProgram
{
    /// <summary>Gets or sets the unique identifier for this programme entry.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the composite channel ID in the form <c>{providerId}_{epgChannelId}</c>,
    /// matching the channel IDs used by <see cref="FluxLiveTvService"/>.
    /// </summary>
    public string ChannelId { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC start time of the programme.</summary>
    public DateTime StartUtc { get; set; }

    /// <summary>Gets or sets the UTC end time of the programme.</summary>
    public DateTime EndUtc { get; set; }

    /// <summary>Gets or sets the programme title.</summary>
    public string Title { get; set; } = string.Empty;

    /// <summary>Gets or sets an optional episode/sub-title.</summary>
    public string? SubTitle { get; set; }

    /// <summary>Gets or sets an optional programme description / synopsis.</summary>
    public string? Description { get; set; }

    /// <summary>Gets or sets an optional genre/category label.</summary>
    public string? Category { get; set; }

    /// <summary>Gets or sets an optional URL to a programme artwork/icon image.</summary>
    public string? IconUrl { get; set; }

    /// <summary>Gets or sets an optional episode number string (e.g. "S01E03").</summary>
    public string? EpisodeNum { get; set; }

    /// <summary>Gets or sets an optional parental-guidance rating string (e.g. "PG", "TV-14").</summary>
    public string? Rating { get; set; }
}
