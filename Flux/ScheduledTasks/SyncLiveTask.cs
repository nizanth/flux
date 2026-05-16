using Jellyfin.Plugin.Flux.Services;
using MediaBrowser.Model.Tasks;

namespace Jellyfin.Plugin.Flux.ScheduledTasks;

/// <summary>Scheduled task that refreshes live channel and EPG data.</summary>
public sealed class SyncLiveTask : IScheduledTask
{
    private readonly CatalogSyncService _sync;

    /// <summary>Initializes a new instance of <see cref="SyncLiveTask"/>.</summary>
    public SyncLiveTask(CatalogSyncService sync)
    {
        _sync = sync;
    }

    /// <inheritdoc/>
    public string Name => "Flux: Refresh Live Channels";

    /// <inheritdoc/>
    public string Key => "FluxSyncLive";

    /// <inheritdoc/>
    public string Description => "Fetches live channel lists and EPG guide data from all configured Xtream Codes providers.";

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
            IntervalTicks = TimeSpan.FromHours(Plugin.Instance?.Configuration.LiveChannelRefreshHours ?? 12).Ticks,
        };
    }
}
