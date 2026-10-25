using System.Globalization;
using PharmaX.Application.Reporting;
using PharmaX.Application.Suppliers.Dtos;

namespace PharmaX.Application.Suppliers;

/// <summary>
/// A purchase order as a report, so the PDF and Excel writers the reports
/// screen uses can print it for the supplier.
/// </summary>
public static class PurchaseOrderReport
{
    public static ReportResult Build(PurchaseOrderDto order, string username,
        DateTime runAtLocal)
    {
        ArgumentNullException.ThrowIfNull(order);

        var culture = CultureInfo.CurrentCulture;

        var columns = new[]
        {
            new ReportColumn("Line", "#"),
            new ReportColumn("Code", "Code"),
            new ReportColumn("Product", "Product"),
            new ReportColumn("Qty", "Qty", "0.###", RightAlign: true),
            new ReportColumn("Cost", "Unit cost", "N2", RightAlign: true),
            new ReportColumn("Total", "Total", "N2", RightAlign: true)
        };

        var rows = order.Lines
            .Select((l, i) => (IReadOnlyDictionary<string, object?>)
                new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["Line"] = i + 1,
                    ["Code"] = l.ProductCode,
                    ["Product"] = l.Description,
                    ["Qty"] = l.QuantityOrdered,
                    ["Cost"] = l.UnitCost,
                    ["Total"] = l.LineTotal
                })
            .ToList();

        var total = order.Lines.Sum(l => l.LineTotal);

        return new ReportResult(
            Title: $"Purchase order {order.OrderNumber}",
            Columns: columns,
            Rows: rows,
            Totals: new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Product"] = "TOTAL",
                ["Total"] = total
            },
            Provenance: new ReportProvenance(
                RunByUsername: username,
                RunAtLocal: runAtLocal,
                ParameterSummary: $"Order {order.OrderNumber} to {order.SupplierName}",
                DataCurrency: order.Status == "Draft" ? "Draft - not yet sent" : order.Status))
        {
            Figures = new[]
            {
                new ReportFigure("Supplier", order.SupplierName),
                new ReportFigure("Order date", order.OrderedOn.ToString("dd/MM/yyyy", culture)),
                new ReportFigure("Expected", order.ExpectedOn?.ToString("dd/MM/yyyy", culture) ?? "-"),
                new ReportFigure("Lines", order.Lines.Count.ToString(culture)),
                new ReportFigure("Order total", total.ToString("N2", culture))
            }
        };
    }
}
