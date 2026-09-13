using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Adapters.Devices;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Sales;

namespace PharmaX.Adapters;

public static class DependencyInjection
{
    public static IServiceCollection AddPharmaXAdapters(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // Scoped, not singleton: both read the workstation's chosen
        // printer through a scoped store, and a singleton holding that
        // would capture one screen's database context for the life of
        // the process.
        services.AddScoped<IReceiptPrinter, ReceiptPrinter>();
        services.AddScoped<ILabelPrinter, LabelPrinter>();
        services.AddSingleton<ICashDrawer, EscPosCashDrawer>();

        // The manual terminal is the default and the fallback. An
        // integrated terminal replaces this one line and nothing else
        // (specification Section 7.6).
        services.AddSingleton<ICardTerminal, ManualCardTerminal>();

        // Scanned documents live on this workstation's disk.

        services.AddSingleton<IAttachmentStore, Files.AttachmentStore>();


        return services;
    }
}
