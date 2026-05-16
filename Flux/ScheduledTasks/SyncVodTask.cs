using Jellyfin.Plugin.Flux.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Flux.ScheduledTasks;

/// <summary>Scheduled task that refreshes the VOD catalog.</summary>
public sealed class SyncVodTask : IScheduledTask
{
    private readonly CatalogSyncService _sync;

    /// <summary>Initializes a new instance of <see cref="SyncVodTask"/>.</summary>
    public SyncVodTask(CatalogSyncService sync)
    {
        _sync = sync;
    }

    /// <inheritdoc/>
    public string Name => "Flux: Refresh VOD Catalog";

    /// <inheritdoc/>
    public string Key => "FluxSyncVod";

    /// <inheritdoc/>
    public string Description => "Refreshes the Video on Demand catalog from all configured Xtream Codes providers.";

    /// <inheritdoc/>
    public string Category => "Flux";

    /// <inheritdoc/>
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress.Report(0);
        await _sync.SyncAllAsync(cancellationToken).ConfigureAwait(false);
        progress.Report(100);
    }

    /// <inheritdoc/>
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerInterval,
            IntervalTicks = TimeSpan.FromHours(Plugin.Instance?.Configuration.VodRefreshHours ?? 24).Ticks,
        };
    }
}
