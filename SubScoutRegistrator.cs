// SubScoutRegistrator.cs — Jellyfin 10.10.7
using MediaBrowser.Controller;
using MediaBrowser.Controller.Library;     // ILibraryPostScanTask
using MediaBrowser.Controller.Plugins;     // IPluginServiceRegistrator
using MediaBrowser.Model.Tasks;            // IScheduledTask
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;        // IHostedService

namespace SubScout;

public sealed class SubScoutRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<ISubScoutRunner, SubScoutRunner>();

        // Fires after the Scheduler’s full “Scan media library” job:
        services.AddSingleton<ILibraryPostScanTask, SubScoutLibraryPostScanTask>();

        // Resident listener for granular library changes (manual scans, monitor updates):
        services.AddSingleton<IHostedService, SubScoutHostedService>();

        // Optional: first-class Scheduled Task visible in Dashboard → Scheduled Tasks:
        services.AddSingleton<IScheduledTask, SubScoutScheduledTask>();
    }
}
