// SubScoutScheduledTask.cs — Jellyfin 10.10.7
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Model.Tasks;

namespace SubScout;

public sealed class SubScoutScheduledTask : IScheduledTask
{
    private readonly ISubScoutRunner _runner;

    public SubScoutScheduledTask(ISubScoutRunner runner) => _runner = runner;

    public string Name => "SubScout: Local subtitle sweep";
    public string Key => "SubScout.Sweep";
    public string Description => "Scans for local subtitles and places them next to media.";
    public string Category => "Subtitles";

    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        progress?.Report(0);
        await _runner.RunAsync(false, cancellationToken).ConfigureAwait(false);
        progress?.Report(100);
    }

    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        yield return new TaskTriggerInfo
        {
            Type = TaskTriggerInfo.TriggerDaily,
            TimeOfDayTicks = TimeSpan.FromHours(3).Ticks
        };
    }
}
