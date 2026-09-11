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

        services.AddHostedService<OutboxDrainer>();
        return services;
    }
}
