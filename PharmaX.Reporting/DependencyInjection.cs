using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Reporting;
using PharmaX.Reporting.Definitions;
using PharmaX.Reporting.Engine;
using PharmaX.Reporting.Exporters;

namespace PharmaX.Reporting;

public static class DependencyInjection
{
    public static IServiceCollection AddPharmaXReporting(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IPresetCatalogue, PresetCatalogue>();
        services.AddScoped<IReportRunner, ReportRunner>();

        // One registration per definition. Adding a report the client
        // asks for is usually a preset rather than a class, so this list
        // grows far more slowly than the report menu does.
        services.AddScoped<IReportDefinition, TransactionsReport>();
        services.AddScoped<IReportDefinition, SalesAnalysisReport>();
        services.AddScoped<IReportDefinition, VatReport>();
        services.AddScoped<IReportDefinition, StockOnHandReport>();
        services.AddScoped<IReportDefinition, CashUpReport>();
        services.AddScoped<IReportDefinition, ReversedScriptsReport>();

        services.AddSingleton<IReportExporter, PdfExporter>();
        services.AddSingleton<IReportExporter, ExcelExporter>();
        services.AddSingleton<IReportExporter, CsvExporter>();

        return services;
    }
}
