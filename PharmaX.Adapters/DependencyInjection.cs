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

        // Scoped, not singleton: all of them read the workstation's chosen
        // printer through a scoped store, and a singleton holding that
        // would capture one screen's database context for the life of
        // the process.
        services.AddScoped<IReceiptPrinter, ReceiptPrinter>();
        services.AddScoped<ILabelPrinter, LabelPrinter>();
        services.AddScoped<IDocumentPrinter, DocumentPrinter>();
        services.AddScoped<Application.Claims.IClaimFormPrinter,
                           ClaimFormPrinter>();
        services.AddScoped<ICashDrawer, EscPosCashDrawer>();

        // Scanned documents live on this workstation's disk.

        services.AddSingleton<IAttachmentStore, Files.AttachmentStore>();

        services.AddSingleton<Application.Assistant.IAttachmentReader, Files.AttachmentReader>();


        return services;
    }
}
