using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace SubScout;

public sealed class SubScoutLibraryPostScanTask : ILibraryPostScanTask
{
    private readonly ISubScoutRunner _runner;
    private readonly ILogger<SubScoutLibraryPostScanTask> _logger;

    public SubScoutLibraryPostScanTask(ISubScoutRunner runner, ILogger<SubScoutLibraryPostScanTask> logger)
    {
        _runner = runner;
        _logger = logger;
        _logger.LogInformation("SubScout[PostScan]: task constructed");
    }
    // Display name in the Scheduled Tasks / Scan logs
    public string Name => "SubScout: Local subtitle sweep (post-scan)";

    public string Description => "After a library scan completes, SubScout searches for local subtitle files and (optionally) copies/moves them next to media.";

    public string Category => "Subtitles";

    /// <summary>
    /// Jellyfin 10.10 expects this exact signature.
    /// </summary>
    public async Task Run(IProgress<double> progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("SubScout[PostScan]: starting…");
        progress?.Report(0);

        // Updated call: remove the nonexistent 'progress' argument
        await _runner.RunAsync(dryRun: false, cancellationToken).ConfigureAwait(false);

        progress?.Report(100);
        _logger.LogInformation("SubScout[PostScan]: complete.");
    }
}
