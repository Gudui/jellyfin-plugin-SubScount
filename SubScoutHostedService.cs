// SubScoutHostedService.cs — Jellyfin 10.10.7
using System;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Library;   // ILibraryManager + ItemChangeEventArgs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SubScout;

public sealed class SubScoutHostedService : IHostedService, IDisposable
{
    private readonly ILibraryManager _library;
    private readonly ISubScoutRunner _runner;
    private readonly ILogger<SubScoutHostedService> _logger;

    private readonly object _gate = new();
    private Timer? _debounceTimer;
    private bool _pending;

    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(8);

    public SubScoutHostedService(
        ILibraryManager library,
        ISubScoutRunner runner,
        ILogger<SubScoutHostedService> logger)
    {
        _library = library;
        _runner  = runner;
        _logger  = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded   += OnLibraryChanged;
        _library.ItemUpdated += OnLibraryChanged;
        _library.ItemRemoved += OnLibraryChanged;

        _logger.LogInformation("SubScoutHostedService: attached ILibraryManager item events.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _library.ItemAdded   -= OnLibraryChanged;
        _library.ItemUpdated -= OnLibraryChanged;
        _library.ItemRemoved -= OnLibraryChanged;

        _logger.LogInformation("SubScoutHostedService: detached ILibraryManager item events.");
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
            _pending = false;
        }

        return Task.CompletedTask;
    }

    // >>> Correct delegate signature for 10.10.x <<<
    private void OnLibraryChanged(object? sender, ItemChangeEventArgs e)
    {
        lock (_gate)
        {
            _pending = true;
            if (_debounceTimer is null)
            {
                _debounceTimer = new Timer(async _ => await FlushAsync().ConfigureAwait(false),
                                           null, Debounce, Timeout.InfiniteTimeSpan);
            }
            else
            {
                _debounceTimer.Change(Debounce, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private async Task FlushAsync()
    {
        bool run;
        lock (_gate)
        {
            run = _pending;
            _pending = false;
        }

        if (!run) return;

        try
        {
            _logger.LogInformation("SubScout: Debounced library change detected → running sweep.");
            await _runner.RunAsync(dryRun: false, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SubScout: sweep failed during debounced run.");
        }
        finally
        {
            lock (_gate)
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;
            }
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _debounceTimer?.Dispose();
            _debounceTimer = null;
        }
    }
}
