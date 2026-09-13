using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Offline;
using PharmaX.Sync.Contracts;

namespace PharmaX.Sync;

public static class DependencyInjection
{
    public static IServiceCollection AddPharmaXSync(
        this IServiceCollection services, PharmaXOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IConnectivityMonitor, ConnectivityMonitor>();

        // Swapped for a real endpoint in Release 2.0. Nothing else changes.
        services.AddSingleton<ISyncEndpoint, NullSyncEndpoint>();

        // A shared database has nothing to drain: the till is writing
        // straight into the pharmacy's one database, so there is no
        // second copy for an outbox entry to travel to. Registering the
        // drainer anyway would have it wake every thirty seconds to find
        // work it must never send.
        if (!options.Database.IsShared)
            services.AddHostedService<OutboxDrainer>();

        return services;
    }
}
