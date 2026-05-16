using System.Collections.Generic;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Flux.Configuration;

/// <summary>
/// Plugin configuration for Flux.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Providers = new List<ProviderConfig>();
        LiveChannelRefreshHours = 12;
        EpgRefreshHours = 6;
        VodRefreshHours = 24;
        SeriesRefreshHours = 24;
    }

    /// <summary>
    /// Gets or sets the list of configured IPTV providers.
    /// </summary>
    public List<ProviderConfig> Providers { get; set; }

    /// <summary>
    /// Gets or sets how often (in hours) live channel data is refreshed.
    /// </summary>
    public int LiveChannelRefreshHours { get; set; }

    /// <summary>
    /// Gets or sets how often (in hours) EPG data is refreshed.
    /// </summary>
    public int EpgRefreshHours { get; set; }

    /// <summary>
    /// Gets or sets how often (in hours) VOD data is refreshed.
    /// </summary>
    public int VodRefreshHours { get; set; }

    /// <summary>
    /// Gets or sets how often (in hours) series data is refreshed.
    /// </summary>
    public int SeriesRefreshHours { get; set; }
}
