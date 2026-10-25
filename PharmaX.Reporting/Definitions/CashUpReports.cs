using System.Globalization;
using PharmaX.Application.Reporting;
using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Reporting.Engine;

namespace PharmaX.Reporting.Definitions;

/// <summary>
/// The cash-ups as reports, so they export to PDF and Excel through the
/// same exporters, letterhead and graphs as every other report.
/// </summary>
public static class CashUpReports
{
    private static string Money(decimal value) =>
        value.ToString("N2", CultureInfo.CurrentCulture);

    /// <summary>One till shift: totals, tenders, and optionally every transaction.</summary>
    public static ReportResult Shift(ShiftCashUpDto shift, ReportProvenance provenance,
                                     bool everyTransaction)
    {
        ArgumentNullException.ThrowIfNull(shift);

        var figures = new List<ReportFigure>
        {
            new($"Sales ({shift.SalesCount})", Money(shift.SalesTotal)),
            new($"Refunds ({shift.RefundCount})", Money(shift.RefundsTotal)),
            new("Net takings", Money(shift.NetTakings)),
            new("Float", Money(shift.OpeningFloat)),
            new("Change given", Money(shift.ChangeGiven)),
            new("Cash in drawer", Money(shift.ExpectedCash))
        };

        if (shift.AccountPayments != 0m)
            figures.Insert(2, new ReportFigure("Account payments", Money(shift.AccountPayments)));

        if (shift.PaidOut != 0m)
            figures.Insert(figures.Count - 1, new ReportFigure("Paid out", Money(-shift.PaidOut)));

        if (shift.Declared is decimal declared)
        {
            figures.Add(new ReportFigure("Counted at close", Money(declared)));
            figures.Add(new ReportFigure("Variance", Money(shift.Variance ?? 0m)));
        }

        var sections = new List<ReportSection>();

        if (shift.Payouts is { Count: > 0 } payouts)
            sections.Add(PaidOut(payouts.Select(p => (shift.TillNumber, p)).ToList(), withTill: false));

        if (everyTransaction)
            sections.Add(Transactions(shift.Transactions, withTill: false, shift.TillNumber));

        return ReportBuilder
            .For($"Cash-Up - Till {shift.TillNumber} - {shift.CashierName}")
            .Column("Tender", "Tender")
            .Column("Payments", "Payments", rightAlign: true)
            .Column("Amount", "Amount", "N2", rightAlign: true)
            .Rows(TenderRows(shift.Tenders))
            .Build(provenance) with
        {
            Figures = figures,
            Sections = sections
        };
    }

    /// <summary>
    /// The whole day: every till and every cashier, the tenders across
    /// them, and what each drawer should have held against what was counted.
    /// </summary>
    public static ReportResult EndOfDay(EndOfDayDto day, ReportProvenance provenance)
    {
        ArgumentNullException.ThrowIfNull(day);

        var closed = day.Shifts.Where(s => s.Declared is not null).ToList();
        var open = day.Shifts.Count(s => s.IsOpen);

        var figures = new List<ReportFigure>
        {
            new($"Sales ({day.SalesCount})", Money(day.SalesTotal)),
            new("Refunds", Money(day.RefundsTotal)),
            new("Account payments", Money(day.AccountPayments)),
            new("Net takings", Money(day.NetTakings)),
            new("Tills cashed up", $"{closed.Count} of {day.Shifts.Count}")
        };

        if (day.PaidOut != 0m)
            figures.Add(new ReportFigure("Paid out", Money(-day.PaidOut)));

        if (closed.Count > 0)
            figures.Add(new ReportFigure("Total variance",
                Money(closed.Sum(s => s.Variance ?? 0m))));

        if (open > 0)
            figures.Add(new ReportFigure("Still open", $"{open} till(s)"));

        var shiftRows = day.Shifts.Select(s => Row(
            ("Till", s.TillNumber),
            ("Cashier", s.CashierName),
            ("From", s.OpenedAtLocal),
            ("Status", s.IsOpen ? "Open" : $"Closed {s.ClosedAtLocal:HH:mm}"),
            ("Sales", s.SalesTotal),
            ("Refunds", s.RefundsTotal),
            ("Account", s.AccountPayments),
            ("Net", s.NetTakings),
            ("PaidOut", s.PaidOut == 0m ? null : -s.PaidOut),
            ("Expected", s.ExpectedTotal),
            ("Counted", s.Declared),
            ("Variance", s.Variance)));

        var cashierRows = day.Cashiers.Select(c => Row(
            ("Cashier", c.Name),
            ("Sales", c.Sales),
            ("SalesTotal", c.SalesTotal),
            ("Refunds", c.Refunds),
            ("Account", c.AccountPayments),
            ("Net", c.Net)));

        return ReportBuilder
            .For("End-of-Day Cash-Up")
            .Column("Till", "Till")
            .Column("Cashier", "Opened by")
            .Column("From", "From", "dd/MM HH:mm")
            .Column("Status", "Status")
            .Column("Sales", "Sales", "N2", rightAlign: true)
            .Column("Refunds", "Refunds", "N2", rightAlign: true)
            .Column("Account", "Account pmts", "N2", rightAlign: true)
            .Column("Net", "Net", "N2", rightAlign: true)
            .Column("PaidOut", "Paid out", "N2", rightAlign: true)
            .Column("Expected", "Expected", "N2", rightAlign: true)
            .Column("Counted", "Counted", "N2", rightAlign: true)
            .Column("Variance", "Variance", "N2", rightAlign: true)
            .Rows(shiftRows)
            .Build(provenance) with
        {
            Figures = figures,
            Sections = new[]
            {
                new ReportSection("By tender", new[]
                {
                    new ReportColumn("Tender", "Tender"),
                    new ReportColumn("Payments", "Payments", RightAlign: true),
                    new ReportColumn("Amount", "Amount", "N2", RightAlign: true)
                }, TenderRows(day.Tenders)),

                new ReportSection("By cashier", new[]
                {
                    new ReportColumn("Cashier", "Cashier"),
                    new ReportColumn("Sales", "Sales", RightAlign: true),
                    new ReportColumn("SalesTotal", "Sales total", "N2", RightAlign: true),
                    new ReportColumn("Refunds", "Refunds", "N2", RightAlign: true),
                    new ReportColumn("Account", "Account pmts", "N2", RightAlign: true),
                    new ReportColumn("Net", "Net", "N2", RightAlign: true)
                }, cashierRows.ToList()),

                // Only where money came out of a drawer that day.
                day.Payouts.Count > 0 ? PaidOut(day.Payouts, withTill: true) : null,

                Transactions(day.Shifts.SelectMany(s => s.Transactions
                        .Select(t => (s.TillNumber, t)))
                    .OrderBy(x => x.t.AtLocal)
                    .ToList())
            }.OfType<ReportSection>().ToList()
        };
    }

    /// <summary>
    /// Money taken out of the drawers - drawings, lunch, other expenses -
    /// each with what it was for and who took it, voided ones marked.
    /// </summary>
    private static ReportSection PaidOut(IReadOnlyList<(string Till, PayoutDto P)> payouts,
                                         bool withTill)
    {
        var columns = new List<ReportColumn>
        {
            new("Time", "Time", "HH:mm"),
            new("Kind", "For"),
            new("Note", "Note"),
            new("By", "By"),
            new("Amount", "Amount", "N2", RightAlign: true),
            new("Status", "Status")
        };

        if (withTill) columns.Insert(1, new ReportColumn("Till", "Till"));

        var rows = payouts.Select(x => Row(
            ("Time", x.P.PaidAtLocal),
            ("Till", x.Till),
            ("Kind", x.P.KindName),
            ("Note", x.P.Note),
            ("By", x.P.PaidByName),
            ("Amount", -x.P.Amount),
            ("Status", x.P.IsVoided ? $"Voided: {x.P.VoidReason}" : ""))).ToList();

        return new ReportSection("Paid out of the drawer", columns, rows);
    }

    private static ReportSection Transactions(IReadOnlyList<ShiftTransactionDto> transactions,
                                              bool withTill, string till) =>
        Transactions(transactions.Select(t => (till, t)).ToList(), withTill);

    private static ReportSection Transactions(
        IReadOnlyList<(string Till, ShiftTransactionDto T)> transactions, bool withTill = true)
    {
        var columns = new List<ReportColumn>
        {
            new("Time", "Time", "HH:mm"),
            new("Type", "Type"),
            new("Number", "Number"),
            new("By", "By"),
            new("Items", "Items (scripts by number only)"),
            new("Tenders", "Paid by"),
            new("Total", "Total", "N2", RightAlign: true),
            new("Change", "Change", "N2", RightAlign: true)
        };

        if (withTill) columns.Insert(1, new ReportColumn("Till", "Till"));

        var rows = transactions.Select(x => Row(
            ("Time", x.T.AtLocal),
            ("Till", x.Till),
            ("Type", x.T.Kind),
            ("Number", x.T.Number),
            ("By", x.T.OperatorName),
            ("Items", string.Join("; ", x.T.Items.Select(i => i.IsScript
                ? i.Text
                : $"{i.Quantity.ToString("0.###", CultureInfo.CurrentCulture)} x {i.Text}"))),
            ("Tenders", string.Join(", ", x.T.Tenders.Select(t =>
                $"{ShiftTally.Label(t.Tender)} {Money(t.Amount)}"))),
            ("Total", x.T.Total),
            ("Change", x.T.Change == 0m ? null : x.T.Change))).ToList();

        return new ReportSection("Every transaction", columns, rows);
    }

    private static List<IReadOnlyDictionary<string, object?>> TenderRows(
        IEnumerable<TenderTotalDto> tenders) =>
        tenders.Select(t => Row(
            ("Tender", ShiftTally.Label(t.Tender)),
            ("Payments", t.Count),
            ("Amount", t.Amount))).ToList();


    private static IReadOnlyDictionary<string, object?> Row(
        params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);
}

/// <summary>
/// The end-of-day cash-up in the Reports list, for the day the period
/// starts on.
/// </summary>
public sealed class EndOfDayCashUpReport : IReportDefinition
{
    public const string PresetKey = "fin.cashup.end-of-day";

    public string Key => "EndOfDay";

    private readonly IShiftCashUpService _shifts;

    public EndOfDayCashUpReport(IShiftCashUpService shifts) => _shifts = shifts;

    public async Task<ReportResult> RunAsync(ResolvedRequest request,
                                             CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var day = await _shifts.EndOfDayAsync(request.From, ct).ConfigureAwait(false);
        return CashUpReports.EndOfDay(day, request.Provenance);
    }
}
