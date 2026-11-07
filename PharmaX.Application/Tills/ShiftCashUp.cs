using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Tills;

namespace PharmaX.Application.Tills;

/// <summary>A shift somebody may cash up, as the picker lists it.</summary>
public sealed record ShiftChoiceDto(Guid SessionId, string TillNumber,
    string CashierName, DateTime OpenedAtLocal, bool IsOpen, bool IsOwn)
{
    public override string ToString() => string.Format(CultureInfo.CurrentCulture,
        "Till {0} - {1} - from {2:dd/MM HH:mm}{3}", TillNumber, CashierName,
        OpenedAtLocal, IsOpen ? "" : " (closed)");
}

/// <summary>One tender's share of a shift: how many payments, how much.</summary>
public sealed record TenderTotalDto(string Tender, int Count, decimal Amount);

/// <summary>
/// One thing on a transaction. A dispensed or collected script is its
/// number alone, never what was in it - the receipt's rule.
/// </summary>
public sealed record ShiftItemDto(string Text, decimal Quantity, decimal Amount,
                                  bool IsScript);

/// <param name="Kind">
/// "Sale", "Script collection", "Refund", "Account payment" or "Paid out".
/// </param>
/// <param name="Total">Signed: a refund and money paid out are negative.</param>
public sealed record ShiftTransactionDto(DateTime AtLocal, string Kind,
    string Number, string OperatorName, decimal Total, decimal Change,
    IReadOnlyList<TenderTotalDto> Tenders, IReadOnlyList<ShiftItemDto> Items);

/// <summary>
/// A till shift's cash-up: what was taken, by tender, and what the drawer
/// should hold for it.
/// </summary>
/// <param name="Tenders">Net of refunds, account payments included.</param>
/// <param name="ExpectedCash">
/// The float plus the net cash taken, less the money paid out of the drawer.
/// </param>
/// <param name="Declared">What was counted at close; null while open.</param>
/// <param name="Payouts">
/// Money taken out of the drawer - drawings, lunch, other expenses -
/// voided entries included.
/// </param>
public sealed record ShiftCashUpDto(Guid SessionId, string TillNumber,
    string CashierName, DateTime OpenedAtLocal, DateTime? ClosedAtLocal,
    bool IsOpen, decimal OpeningFloat, int SalesCount, decimal SalesTotal,
    int RefundCount, decimal RefundsTotal, decimal AccountPayments,
    decimal ChangeGiven, int ScriptsCollected,
    IReadOnlyList<TenderTotalDto> Tenders, decimal ExpectedCash,
    decimal? Declared, IReadOnlyList<ShiftTransactionDto> Transactions,
    IReadOnlyList<PayoutDto>? Payouts = null)
{
    /// Money in for the shift: sales less refunds, plus account payments.
    public decimal NetTakings => SalesTotal - RefundsTotal + AccountPayments;

    /// Out of the drawer during the shift, voided entries left out.
    public decimal PaidOut => PayoutDto.Total(Payouts);

    /// What the till should account for in all, float included, the
    /// money paid out of the drawer taken off.
    public decimal ExpectedTotal => OpeningFloat + Tenders.Sum(t => t.Amount) - PaidOut;

    /// Counted less expected, once the till is closed.
    public decimal? Variance => Declared - ExpectedTotal;

    /// <summary>
    /// What each person who signed in at this till took on it.
    /// </summary>
    /// <remarks>
    /// One till, opened once, and the cashiers take turns at it: the shift
    /// carries the name of whoever opened it, so without this nobody's own
    /// takings could be checked.
    /// </remarks>
    public IReadOnlyList<CashierTotalDto> Cashiers => ShiftTally.ByCashier(Transactions);
}

/// <summary>Every till and cashier for one day.</summary>
public sealed record EndOfDayDto(DateOnly Day, IReadOnlyList<ShiftCashUpDto> Shifts)
{
    public IReadOnlyList<TenderTotalDto> Tenders => ShiftTally.Combine(
        Shifts.SelectMany(s => s.Tenders));

    public decimal SalesTotal => Shifts.Sum(s => s.SalesTotal);
    public decimal RefundsTotal => Shifts.Sum(s => s.RefundsTotal);
    public decimal AccountPayments => Shifts.Sum(s => s.AccountPayments);
    public decimal NetTakings => Shifts.Sum(s => s.NetTakings);
    public int SalesCount => Shifts.Sum(s => s.SalesCount);

    /// Out of every drawer that day, voided entries left out.
    public decimal PaidOut => Shifts.Sum(s => s.PaidOut);

    /// Every payout that day, with the till it came out of, earliest first.
    public IReadOnlyList<(string Till, PayoutDto Payout)> Payouts => Shifts
        .SelectMany(s => (s.Payouts ?? Array.Empty<PayoutDto>()).Select(p => (s.TillNumber, p)))
        .OrderBy(x => x.p.PaidAtLocal)
        .ToList();

    /// By whoever rang each transaction up, whatever till it was on.
    public IReadOnlyList<CashierTotalDto> Cashiers =>
        ShiftTally.ByCashier(Shifts.SelectMany(s => s.Transactions));
}

/// <param name="Cash">The cash they took and kept: what their cash in the drawer should be.</param>
public sealed record CashierTotalDto(string Name, int Sales, decimal SalesTotal,
                                     decimal Refunds, decimal AccountPayments, decimal Cash = 0m)
{
    public decimal Net => SalesTotal - Refunds + AccountPayments;
}

/// <summary>
/// Turns a till session's sales, refunds and account payments into its
/// cash-up. Pure, so the arithmetic is tested without a database.
/// </summary>
public static class ShiftTally
{
    /// Totals by whoever rang each transaction up, by name.
    public static IReadOnlyList<CashierTotalDto> ByCashier(
        IEnumerable<ShiftTransactionDto> transactions) => transactions
        .GroupBy(t => t.OperatorName)
        .Select(g => new CashierTotalDto(g.Key,
            g.Count(t => t.Kind is "Sale" or "Script collection"),
            g.Where(t => t.Kind is "Sale" or "Script collection").Sum(t => t.Total),
            -g.Where(t => t.Kind == "Refund").Sum(t => t.Total),
            g.Where(t => t.Kind == "Account payment").Sum(t => t.Total),
            // Tenders are already what was kept, change handed back off.
            g.Where(t => t.Kind != "Paid out")
                .SelectMany(t => t.Tenders).Where(t => t.Tender == nameof(TenderType.Cash))
                .Sum(t => t.Amount)))
        .OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
        .ToList();

    /// <param name="names">Display names by user id.</param>
    /// <param name="scripts">
    /// The script number for each sale that collected a dispensing,
    /// refunded originals included.
    /// </param>
    /// <param name="payouts">Money taken out of the drawer, voided ones too.</param>
    public static ShiftCashUpDto Build(TillSession session,
        IReadOnlyList<Sale> sales, IReadOnlyList<SaleReturn> returns,
        IReadOnlyList<AccountTransaction> accountPayments,
        IReadOnlyDictionary<Guid, string> names,
        IReadOnlyDictionary<Guid, string> scripts,
        IReadOnlyList<TillPayout>? payouts = null)
    {
        ArgumentNullException.ThrowIfNull(session);

        string Name(Guid id) => names.TryGetValue(id, out var n) ? n : "Unknown";

        var transactions = new List<ShiftTransactionDto>();

        var finalised = sales.Where(s => s.Status == SaleStatus.Finalised).ToList();

        foreach (var sale in finalised)
        {
            scripts.TryGetValue(sale.Id, out var script);

            transactions.Add(new ShiftTransactionDto(
                sale.OccurredAtUtc.ToLocalTime(),
                sale.IsCollection ? "Script collection" : "Sale",
                sale.DocumentNumber, Name(sale.OperatorId),
                sale.Total.Amount, sale.ChangeGiven.Amount,
                Combine(sale.Payments.Select(p =>
                    new TenderTotalDto(p.Tender.ToString(), 1, p.Amount.Amount))),
                SaleItems(sale, script)));
        }

        var refunds = returns.Where(r => r.Status == ReturnStatus.Finalised).ToList();

        foreach (var refund in refunds)
        {
            var script = scripts.TryGetValue(refund.OriginalSaleId, out var n) ? n : null;

            // A refund of a collected script names the script, never the
            // medicines that came back.
            var items = script is not null
                ? new[] { ScriptItem(script, -refund.Lines.Sum(l => l.LineTotal.Amount)) }
                : refund.Lines.Select(l => new ShiftItemDto(l.Description,
                    -l.Quantity, -l.LineTotal.Amount, false)).ToArray();

            transactions.Add(new ShiftTransactionDto(
                refund.OccurredAtUtc.ToLocalTime(), "Refund",
                refund.DocumentNumber, Name(refund.OperatorId),
                -refund.Refunds.Sum(p => p.Amount.Amount), 0m,
                Combine(refund.Refunds.Select(p =>
                    new TenderTotalDto(p.Tender.ToString(), 1, -p.Amount.Amount))),
                items));
        }

        foreach (var payment in accountPayments.Where(t => t.Tender is not null))
            transactions.Add(new ShiftTransactionDto(
                payment.CreatedAtUtc.ToLocalTime(), "Account payment",
                payment.Reference ?? "", Name(payment.CreatedBy),
                payment.Amount.Amount, 0m,
                new[] { new TenderTotalDto(payment.Tender!.Value.ToString(), 1,
                                           payment.Amount.Amount) },
                Array.Empty<ShiftItemDto>()));

        // What was taken, by tender, before the money that went out again:
        // the tenders are the takings, and the drawer is worked out below.
        var tenders = Combine(transactions.SelectMany(t => t.Tenders));
        var cash = tenders.FirstOrDefault(t => t.Tender == nameof(TenderType.Cash))?.Amount ?? 0m;

        var paidOut = (payouts ?? Array.Empty<TillPayout>())
            .OrderBy(p => p.PaidAtUtc)
            .Select(p => PayoutDto.From(p, Name(p.PaidBy)))
            .ToList();

        foreach (var payout in paidOut.Where(p => !p.IsVoided))
            transactions.Add(new ShiftTransactionDto(payout.PaidAtLocal, "Paid out",
                $"{payout.KindName}: {payout.Note}", payout.PaidByName, -payout.Amount, 0m,
                new[] { new TenderTotalDto(nameof(TenderType.Cash), 1, -payout.Amount) },
                Array.Empty<ShiftItemDto>()));

        transactions.Sort((a, b) => a.AtLocal.CompareTo(b.AtLocal));

        return new ShiftCashUpDto(session.Id, session.TillNumber,
            Name(session.OpenedBy), session.OpenedAtUtc.ToLocalTime(),
            session.ClosedAtUtc?.ToLocalTime(),
            session.Status != TillSessionStatus.Closed,
            session.OpeningFloat.Amount,
            finalised.Count, finalised.Sum(s => s.Total.Amount),
            refunds.Count, refunds.Sum(r => r.Refunds.Sum(p => p.Amount.Amount)),
            accountPayments.Where(t => t.Tender is not null).Sum(t => t.Amount.Amount),
            finalised.Sum(s => s.ChangeGiven.Amount),
            finalised.Count(s => s.IsCollection),
            tenders, session.OpeningFloat.Amount + cash - PayoutDto.Total(paidOut),
            session.ClosingDeclared?.Amount, transactions, paidOut);
    }

    /// Payments added up by tender, in the till's tender order.
    public static IReadOnlyList<TenderTotalDto> Combine(IEnumerable<TenderTotalDto> lines) =>
        lines.GroupBy(t => t.Tender, StringComparer.Ordinal)
             .Select(g => new TenderTotalDto(g.Key, g.Sum(t => t.Count), g.Sum(t => t.Amount)))
             .OrderBy(t => Enum.TryParse<TenderType>(t.Tender, out var e) ? (int)e : 99)
             .ToList();

    /// The tender as the till offers it: "Medical aid", not "MedicalAid".
    public static string Label(string tender) =>
        Enum.TryParse<TenderType>(tender, out var t) ? t.DisplayName() : tender;

    /// <summary>
    /// Over-the-counter lines as rung up; everything dispensed as one
    /// "Script 1234" line for the lot, as the receipt prints it.
    /// </summary>
    private static List<ShiftItemDto> SaleItems(Sale sale, string? script)
    {
        var lines = sale.Lines.Where(l => !l.IsVoided).ToList();
        var items = lines.Where(l => !l.IsScript && !sale.IsCollection)
            .Select(l => new ShiftItemDto(l.Description, l.Quantity,
                                          l.LineTotal.Amount, false))
            .ToList();

        var dispensed = lines.Where(l => l.IsScript || sale.IsCollection).ToList();

        if (dispensed.Count > 0)
            items.Insert(0, ScriptItem(script, dispensed.Sum(l => l.LineTotal.Amount)));

        return items;
    }

    private static ShiftItemDto ScriptItem(string? number, decimal amount) =>
        new(string.IsNullOrWhiteSpace(number) ? "Script" : $"Script {number.Trim()}",
            1m, amount, true);
}

/// <summary>
/// The cash-up as it prints on the till roll: thirty-two across, like the
/// receipt, so it prints in the receipt's own large type.
/// </summary>
public static class CashUpSlip
{
    public const int Width = 32;

    public static PrintableDocument Document(ShiftCashUpDto shift, bool everyTransaction,
                                             DateTime printedAtLocal)
    {
        ArgumentNullException.ThrowIfNull(shift);

        var c = CultureInfo.CurrentCulture;
        var lines = new List<string>();
        var rule = new string('-', Width);

        void Amount(string label, decimal value)
        {
            var amount = value.ToString("N2", c);
            var room = Width - amount.Length - 1;
            var pieces = SlipLayout.Wrap(label, room, room);
            lines.AddRange(pieces.SkipLast(1));
            lines.Add(pieces[^1].PadRight(room + 1) + amount);
        }

        void Text(string text) => lines.AddRange(SlipLayout.Wrap(text, Width, Width));

        Text($"Till {shift.TillNumber}  {shift.CashierName}");
        lines.Add(string.Format(c, "From  {0:dd/MM/yyyy HH:mm}", shift.OpenedAtLocal));
        lines.Add(shift.ClosedAtLocal is DateTime closed
            ? string.Format(c, "To    {0:dd/MM/yyyy HH:mm}", closed)
            : string.Format(c, "To    {0:dd/MM/yyyy HH:mm} (open)", printedAtLocal));
        lines.Add(rule);

        Amount($"Sales ({shift.SalesCount})", shift.SalesTotal);
        if (shift.ScriptsCollected > 0)
            lines.Add(string.Format(c, "  incl. {0} script(s)", shift.ScriptsCollected));
        Amount($"Refunds ({shift.RefundCount})", -shift.RefundsTotal);
        if (shift.AccountPayments != 0m) Amount("Account payments", shift.AccountPayments);
        Amount("NET TAKINGS", shift.NetTakings);
        lines.Add(rule);

        lines.Add("By tender");
        foreach (var tender in shift.Tenders)
            Amount($"{ShiftTally.Label(tender.Tender)} ({tender.Count})", tender.Amount);
        lines.Add(rule);

        // Only where more than one person worked the till: whose takings
        // are whose, cash apart, for each to answer for their own.
        if (shift.Cashiers.Count > 1)
        {
            lines.Add("By cashier");
            foreach (var cashier in shift.Cashiers)
            {
                Text($"{cashier.Name} ({cashier.Sales})");
                Amount("  Net takings", cashier.Net);
                Amount("  of which cash", cashier.Cash);
            }
            lines.Add(rule);
        }

        // Each with what it was for, so the drawer's shortfall explains
        // itself: drawings, lunch, the other expenses.
        var paid = (shift.Payouts ?? Array.Empty<PayoutDto>()).Where(p => !p.IsVoided).ToList();

        if (paid.Count > 0)
        {
            lines.Add("Paid out of the drawer");

            foreach (var p in paid)
                Amount(string.Format(c, " {0:HH:mm} {1}: {2}", p.PaidAtLocal, p.KindName, p.Note),
                       -p.Amount);

            Amount("PAID OUT", -shift.PaidOut);
            lines.Add(rule);
        }

        Amount("Float", shift.OpeningFloat);
        Amount("Change given", shift.ChangeGiven);
        if (shift.PaidOut != 0m) Amount("Paid out", -shift.PaidOut);
        Amount("CASH IN DRAWER", shift.ExpectedCash);

        if (shift.Declared is decimal declared)
        {
            Amount("Counted at close", declared);
            Amount("Variance", shift.Variance ?? 0m);
        }

        lines.Add(rule);

        if (everyTransaction && shift.Transactions.Count > 0)
        {
            lines.Add("Transactions");
            lines.Add(rule);

            foreach (var t in shift.Transactions)
            {
                Text(string.Format(c, "{0:HH:mm} {1} {2}", t.AtLocal, t.Kind, t.Number));

                foreach (var item in t.Items)
                {
                    var qty = item.IsScript ? "" : item.Quantity.ToString("0.###", c) + " x ";
                    Amount(" " + qty + item.Text, item.Amount);
                }

                foreach (var tender in t.Tenders)
                    Amount("  " + ShiftTally.Label(tender.Tender), tender.Amount);

                if (t.Change > 0m) Amount("  Change", t.Change);
            }

            lines.Add(rule);
        }

        lines.Add("");
        lines.Add("Cashier ".PadRight(Width, '_'));
        lines.Add("");
        lines.Add("Checked ".PadRight(Width, '_'));
        lines.Add("");
        lines.Add(string.Format(c, "Printed {0:dd/MM/yyyy HH:mm}", printedAtLocal));

        return new PrintableDocument("Cash-up", lines, Width);
    }
}

public interface IShiftCashUpService
{
    /// Whether this user may cash up somebody else's till.
    bool MayActForOthers { get; }

    /// Everyone's shifts running at any time on the day, today where none is
    /// given - still-open ones from before it included - this user's own first.
    /// Only their own, or a supervisor, may pay out of or close one.
    Task<IReadOnlyList<ShiftChoiceDto>> ShiftsAsync(DateOnly? day = null,
        CancellationToken ct = default);

    Task<ShiftCashUpDto> ShiftAsync(Guid sessionId, CancellationToken ct = default);

    /// Every till and cashier with a shift running at any time that day.
    Task<EndOfDayDto> EndOfDayAsync(DateOnly day, CancellationToken ct = default);
}

public sealed class ShiftCashUpService : IShiftCashUpService
{
    private readonly ITillSessionRepository _sessions;
    private readonly ISaleRepository _sales;
    private readonly IReturnRepository _returns;
    private readonly IAccountRepository _accounts;
    private readonly IUserRepository _users;
    private readonly IDispensingRepository _dispensings;
    private readonly IScriptRepository _scripts;
    private readonly ICurrentUser _user;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;

    public ShiftCashUpService(ITillSessionRepository sessions, ISaleRepository sales,
        IReturnRepository returns, IAccountRepository accounts, IUserRepository users,
        IDispensingRepository dispensings, IScriptRepository scripts,
        ICurrentUser user, IPermissionService permissions, IClock clock)
    {
        _sessions = sessions;
        _sales = sales;
        _returns = returns;
        _accounts = accounts;
        _users = users;
        _dispensings = dispensings;
        _scripts = scripts;
        _user = user;
        _permissions = permissions;
        _clock = clock;
    }

    public bool MayActForOthers => MayActForOthersOf(_permissions);

    /// <summary>
    /// The pharmacist, the assistant, the manager and the administrator
    /// cash up for a cashier: the same people who authorise another
    /// person's till.
    /// </summary>
    public static bool MayActForOthersOf(IPermissionService permissions) =>
        permissions.Has(PermissionCode.SessionCloseOtherUser);

    public async Task<IReadOnlyList<ShiftChoiceDto>> ShiftsAsync(DateOnly? day = null,
        CancellationToken ct = default)
    {
        var start = (day ?? DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime()))
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Local);
        var sessions = await _sessions.ActiveBetweenAsync(
            start.ToUniversalTime(), start.AddDays(1).ToUniversalTime(), ct);
        var choices = new List<ShiftChoiceDto>();

        // Everyone who worked a till that day, so a cashier whose till was
        // opened under another login still finds it; their own comes first.
        foreach (var s in sessions
                     .OrderBy(s => s.OpenedBy != _user.Id)
                     .ThenBy(s => s.Status == TillSessionStatus.Closed)
                     .ThenByDescending(s => s.OpenedAtUtc))
            choices.Add(new ShiftChoiceDto(s.Id, s.TillNumber,
                (await _users.GetAsync(s.OpenedBy, ct))?.DisplayName ?? "Unknown",
                s.OpenedAtUtc.ToLocalTime(), s.Status != TillSessionStatus.Closed,
                s.OpenedBy == _user.Id));

        return choices;
    }

    public async Task<ShiftCashUpDto> ShiftAsync(Guid sessionId, CancellationToken ct = default)
    {
        // Anyone may read a shift's totals; paying out of it and closing it
        // are TillPayoutService's and CashUpService's to allow.
        var session = await _sessions.GetAsync(sessionId, ct)
                      ?? throw new InvalidOperationException("That till shift was not found.");

        return await TallyAsync(session, ct);
    }

    public async Task<EndOfDayDto> EndOfDayAsync(DateOnly day, CancellationToken ct = default)
    {
        if (!MayActForOthers && !_permissions.Has(PermissionCode.ReportFinancial))
            throw new PermissionDeniedException(
                "The end-of-day cash-up is for a pharmacist, an assistant, the manager "
                + "or the administrator.");

        var start = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        var sessions = await _sessions.ActiveBetweenAsync(start, start.AddDays(1), ct);
        var shifts = new List<ShiftCashUpDto>();

        foreach (var session in sessions.OrderBy(s => s.TillNumber, StringComparer.Ordinal)
                                         .ThenBy(s => s.OpenedAtUtc))
            shifts.Add(await TallyAsync(session, ct, (start, start.AddDays(1))));

        return new EndOfDayDto(day, shifts);
    }

    /// <param name="day">
    /// For the end of day: only what happened between these UTC times. A
    /// dispensary till left open for a week otherwise brought the whole
    /// week's takings into every day's total.
    /// </param>
    private async Task<ShiftCashUpDto> TallyAsync(TillSession session, CancellationToken ct,
        (DateTime From, DateTime To)? day = null)
    {
        bool On(DateTime atUtc) => day is not { } d || (atUtc >= d.From && atUtc < d.To);

        var sales = (await _sales.ForSessionAsync(session.Id, ct))
            .Where(s => On(s.OccurredAtUtc)).ToList();
        var returns = (await _returns.ForSessionAsync(session.Id, ct))
            .Where(r => On(r.OccurredAtUtc)).ToList();
        var payments = (await _accounts.PaymentsForSessionAsync(session.Id, ct))
            .Where(p => On(p.CreatedAtUtc)).ToList();
        var payouts = (await _sessions.PayoutsAsync(session.Id, ct))
            .Where(p => On(p.PaidAtUtc)).ToList();

        var names = new Dictionary<Guid, string>();
        foreach (var id in sales.Select(s => s.OperatorId)
                     .Concat(returns.Select(r => r.OperatorId))
                     .Concat(payments.Select(p => p.CreatedBy))
                     .Concat(payouts.Select(p => p.PaidBy))
                     .Append(session.OpenedBy).Distinct())
            names[id] = (await _users.GetAsync(id, ct))?.DisplayName ?? "Unknown";

        // The script number for every collection, and for every refunded
        // sale that was one.
        var scripts = new Dictionary<Guid, string>();
        var collections = sales.Where(s => s.IsCollection).ToList();

        foreach (var original in returns.Select(r => r.OriginalSaleId).Distinct()
                     .Where(id => sales.All(s => s.Id != id)))
            if (await _sales.GetAsync(original, ct) is { IsCollection: true } sale)
                collections.Add(sale);

        foreach (var sale in collections)
        {
            var dispensing = await _dispensings.GetAsync(sale.DispensingId!.Value, ct);
            var script = dispensing is null ? null : await _scripts.GetAsync(dispensing.ScriptId, ct);
            scripts[sale.Id] = script?.ScriptNumber ?? "";
        }

        return ShiftTally.Build(session, sales, returns, payments, names, scripts, payouts);
    }
}
