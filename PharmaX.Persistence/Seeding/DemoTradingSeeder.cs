using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Claims;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Inventory;
using PharmaX.Application.Patients;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Suppliers;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;

namespace PharmaX.Persistence.Seeding;

/// <summary>
/// A day's trading: a till opened and cashed up, sales paid every way the
/// till accepts, a return, a quotation, scripts dispensed and collected,
/// an order raised and received, a stocktake posted, and a scheme
/// remittance banked.
/// </summary>
/// <remarks>
/// Driven entirely through the application services, which is what makes
/// the result worth testing against. A sale written straight to the table
/// has no stock movement, no journal posting, no document number and no
/// outbox row, so the trial balance, the stock report and the day end
/// would all disagree with the till. Going in the front door means the
/// demonstration data is arithmetically the same as data a cashier made.
///
/// One-shot: if a single sale exists, the whole section is skipped. The
/// sections are ordered rather than independent - the return needs a
/// sale, the collection needs a dispensing, the remittance needs a claim -
/// so re-running half of it would build a day that never happened.
/// </remarks>
public sealed class DemoTradingSeeder
{
    private readonly PharmaXDbContext _db;
    private readonly ITillSessionService _sessions;
    private readonly ICashUpService _cashUp;
    private readonly ISaleService _sales;
    private readonly IReturnService _returns;
    private readonly IQuotationService _quotations;
    private readonly ICollectionService _collections;
    private readonly IAccountService _accounts;
    private readonly IScriptService _scripts;
    private readonly IDispensingService _dispensing;
    private readonly IPurchasingService _purchasing;
    private readonly IReceivingService _receiving;
    private readonly IStocktakeService _stocktakes;
    private readonly IClaimService _claims;
    private readonly IClock _clock;
    private readonly ILogger<DemoTradingSeeder> _log;

    public DemoTradingSeeder(PharmaXDbContext db,
        ITillSessionService sessions, ICashUpService cashUp,
        ISaleService sales, IReturnService returns,
        IQuotationService quotations, ICollectionService collections,
        IAccountService accounts, IScriptService scripts,
        IDispensingService dispensing,
        IPurchasingService purchasing, IReceivingService receiving,
        IStocktakeService stocktakes, IClaimService claims, IClock clock,
        ILogger<DemoTradingSeeder> log)
    {
        _db = db;
        _sessions = sessions;
        _cashUp = cashUp;
        _sales = sales;
        _returns = returns;
        _quotations = quotations;
        _collections = collections;
        _accounts = accounts;
        _scripts = scripts;
        _dispensing = dispensing;
        _purchasing = purchasing;
        _receiving = receiving;
        _stocktakes = stocktakes;
        _claims = claims;
        _clock = clock;
        _log = log;
    }

    public async Task<int> SeedAsync(CancellationToken ct = default)
    {
        if (await _db.Sales.AnyAsync(ct).ConfigureAwait(false)) return 0;

        var added = 0;

        // The till has to be open before anything can be sold into it.
        var session = await _sessions.OpenAsync(1500m, ct)
            .ConfigureAwait(false);

        if (session.IsFailure)
        {
            _log.LogWarning(
                "Demonstration trading was skipped: the till would not "
                + "open ({Error}).", session.Error.Message);

            return 0;
        }

        added++;

        added += await Step("sales", SellAsync, ct).ConfigureAwait(false);
        added += await Step("dispensing", DispenseAsync, ct)
            .ConfigureAwait(false);
        added += await Step("quotations", QuoteAsync, ct)
            .ConfigureAwait(false);
        added += await Step("purchasing", BuyAsync, ct)
            .ConfigureAwait(false);
        added += await Step("stocktake", CountAsync, ct)
            .ConfigureAwait(false);
        added += await Step("claims", SettleClaimsAsync, ct)
            .ConfigureAwait(false);

        // Cashed up and closed, so the day end, the cash-up report and
        // the trial balance all have a finished day to read. A second
        // session is opened empty afterwards so that whoever picks the
        // machine up can sell immediately.
        added += await Step("cash-up", CashUpAsync, ct)
            .ConfigureAwait(false);

        var reopened = await _sessions.OpenAsync(1500m, ct)
            .ConfigureAwait(false);

        if (reopened.IsSuccess) added++;

        return added;
    }

    /// <summary>
    /// Runs one step of the day, and turns a failure into a log line.
    /// </summary>
    /// <remarks>
    /// The steps run in the order a day does, and the later ones lean on
    /// the earlier: a return needs a sale, a remittance needs a claim.
    /// Letting one throw all the way out would cost every step after it,
    /// so a stocktake would be lost to a script that could not be
    /// dispensed - which is how the first run of this seeder produced a
    /// database with no purchase orders in it.
    /// </remarks>
    private async Task<int> Step(string name,
        Func<CancellationToken, Task<int>> step, CancellationToken ct)
    {
        try
        {
            return await step(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Deliberately broad: see the remarks.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(ex,
                "Demonstration {Step} could not be seeded; the rest of the "
                + "day was still written.", name);

            return 0;
        }
    }

    /// <summary>
    /// Three finalised sales, one per tender the till has to account for
    /// differently: cash moves the drawer, card does not, and account
    /// moves a patient's balance instead of either.
    /// </summary>
    private async Task<int> SellAsync(CancellationToken ct)
    {
        var added = 0;

        // Cash, which is the only tender that moves the drawer and so
        // the only one the cash-up has to reconcile.
        added += await SaleAsync(ct,
            lines: new[] { (DemoCatalogue.Counter, 2m), ("VITC1G", 1m) },
            tender: "Cash");

        added += await SaleAsync(ct,
            lines: new[] { ("PLAST1", 1m), ("ALLERG", 2m) },
            tender: "Card", reference: "AUTH 004417");

        // On account, so the patient statement and the debtors figure
        // both have something in them.
        var account = await _db.Accounts
            .Where(a => a.PatientId != null)
            .OrderBy(a => a.AccountNumber)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        added += await SaleAsync(ct,
            lines: new[] { (DemoCatalogue.Counter, 3m) },
            tender: "Account", accountId: account?.Id);

        // A part payment off the account, so a statement has a charge
        // and a receipt on it rather than one lonely debit.
        if (account is not null)
        {
            var paid = await _accounts.TakePaymentAsync(account.Id, 50m,
                "Cash", _clock.Today, "Counter payment", ct)
                .ConfigureAwait(false);

            if (paid.IsSuccess) added++;
        }

        // A credit back against the first sale of the day.
        added += await ReturnAsync(ct).ConfigureAwait(false);

        return added;
    }

    private async Task<int> SaleAsync(CancellationToken ct,
        (string Code, decimal Quantity)[] lines, string tender,
        string? reference = null, Guid? accountId = null)
    {
        var opened = await _sales.OpenAsync(ct).ConfigureAwait(false);
        if (opened.IsFailure) return 0;

        var saleId = opened.Value.Id;

        foreach (var (code, quantity) in lines)
        {
            await _sales.AddLineAsync(saleId, code, quantity, ct)
                .ConfigureAwait(false);
        }

        if (accountId is Guid id)
        {
            var assigned = await _sales.AssignAccountAsync(saleId, id, ct)
                .ConfigureAwait(false);

            // An account on hold is refused, which is the point of
            // having one on hold - but this sale still has to finalise,
            // so it falls back to cash the way a cashier would.
            if (assigned.IsFailure) tender = "Cash";
        }

        var sale = await _sales.GetAsync(saleId, ct).ConfigureAwait(false);
        if (sale.IsFailure) return 0;

        var total = sale.Value.Totals.Total;

        // Exactly the total: BR-POS-01 refuses a sale whose tender and
        // total differ, so the change a cashier hands back is worked out
        // in the payment dialog and never reaches the payment itself.
        var finalised = await _sales.FinaliseAsync(saleId, new[]
        {
            new PaymentDto(tender, total, reference)
        }, ct).ConfigureAwait(false);

        if (finalised.IsSuccess) return 1;

        _log.LogWarning("Demonstration sale did not finalise: {Error}",
            finalised.Error.Message);

        return 0;
    }

    private async Task<int> ReturnAsync(CancellationToken ct)
    {
        var sale = await _db.Sales
            .OrderBy(s => s.DocumentNumber)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (sale is null) return 0;

        var found = await _returns.FindSaleAsync(sale.DocumentNumber, ct)
            .ConfigureAwait(false);

        if (found.IsFailure || found.Value.Lines.Count == 0) return 0;

        var line = found.Value.Lines[0];

        var operatorId = await _db.Users
            .Where(u => u.Username == "test.manager")
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        var created = await _returns.CreateAsync(new ReturnEditDto(
            SaleId: sale.Id,
            Kind: "GoodsReturn",
            Reason: "Customer changed their mind.",
            AuthorisedBy: operatorId,
            Lines: new[]
            {
                new ReturnLineEditDto(line.LineNo, 1m, RestockToShelf: true,
                    LineReason: "Unopened.")
            },
            Refunds: new[] { new PaymentDto("Cash", line.UnitPrice, null) }),
            ct).ConfigureAwait(false);

        if (created.IsSuccess) return 1;

        _log.LogWarning("Demonstration return was not created: {Error}",
            created.Error.Message);

        return 0;
    }

    /// <summary>
    /// Two scripts. The first is dispensed and collected, so the till has
    /// a completed co-payment behind it; the second is issued and left
    /// standing, so frmCollect opens with somebody waiting rather than an
    /// empty grid.
    /// </summary>
    private async Task<int> DispenseAsync(CancellationToken ct)
    {
        var pharmacistId = await _db.Users
            .Where(u => u.Username == "test.pharmacist")
            .Select(u => u.Id)
            .FirstOrDefaultAsync(ct).ConfigureAwait(false);

        if (pharmacistId == Guid.Empty) return 0;

        var added = 0;

        added += await ScriptAsync(ct, "Mokoena", pharmacistId,
            collect: true);

        added += await ScriptAsync(ct, "Pillay", pharmacistId,
            collect: false);

        return added;
    }

    private async Task<int> ScriptAsync(CancellationToken ct, string surname,
        Guid pharmacistId, bool collect)
    {
        var patient = await _db.Patients
            .FirstOrDefaultAsync(p => p.Surname == surname, ct)
            .ConfigureAwait(false);

        if (patient is null) return 0;

        var captured = await _scripts.CaptureAsync(new ScriptEditDto(
            PatientId: patient.Id,
            PrescriberName: "Dr M Chikwanda",
            PrescriberNumber: "MP0447213",
            PrescribedOn: _clock.Today,
            ValidUntil: _clock.Today.AddMonths(6),

            // Repeats left, so the script list has something to dispense
            // against tomorrow as well as today.
            RepeatsAuthorised: 5,
            // Small quantities on purpose: two scripts come off one seeded
            // shelf, and the demonstration trading should leave stock for
            // the walkthrough to dispense and sell afterwards.
            Items: new[]
            {
                new ScriptItemEditDto(DemoCatalogue.Prescription, 4m,
                    "One capsule three times a day for seven days"),
                new ScriptItemEditDto(DemoCatalogue.Chronic, 4m,
                    "One tablet twice a day with food")
            }), ct).ConfigureAwait(false);

        if (captured.IsFailure)
        {
            _log.LogWarning("Demonstration script was not captured: {Error}",
                captured.Error.Message);

            return 0;
        }

        var added = 1;

        var begun = await _dispensing.BeginAsync(captured.Value, ct)
            .ConfigureAwait(false);

        if (begun.IsFailure) return added;

        var dispensingId = begun.Value.Id;

        await _dispensing.AddItemAsync(dispensingId,
            DemoCatalogue.Prescription,
            new DirectionsDto(4m,
                "One capsule three times a day for seven days"), ct)
            .ConfigureAwait(false);

        await _dispensing.AddItemAsync(dispensingId, DemoCatalogue.Chronic,
            new DirectionsDto(4m, "One tablet twice a day with food"), ct)
            .ConfigureAwait(false);

        // A clinical warning left unacknowledged blocks the issue, and an
        // allergy to one of the seeded products is exactly what raises
        // one. Acknowledging it here leaves the audit trail a real
        // acknowledgement would.
        foreach (var warning in await _dispensing
                     .PendingWarningsAsync(dispensingId, ct)
                     .ConfigureAwait(false))
        {
            await _dispensing.AcknowledgeWarningAsync(warning.Id,
                "Prescriber consulted; patient has tolerated this before.",
                ct: ct).ConfigureAwait(false);
        }

        var issued = await _dispensing.IssueAsync(dispensingId, pharmacistId,
            ct).ConfigureAwait(false);

        if (issued.IsFailure)
        {
            _log.LogWarning("Demonstration dispensing was not issued: {Error}",
                issued.Error.Message);

            return added;
        }

        added++;

        if (!collect) return added;

        // Taken at the till, which is what turns the co-payment into
        // money and closes the dispensing off.
        var started = await _collections.StartAsync(dispensingId, ct)
            .ConfigureAwait(false);

        if (started.IsFailure) return added;

        var sale = await _sales.GetAsync(started.Value.Id, ct)
            .ConfigureAwait(false);

        if (sale.IsFailure) return added;

        var finalised = await _sales.FinaliseAsync(started.Value.Id, new[]
        {
            new PaymentDto("Cash", sale.Value.Totals.Total, null)
        }, ct).ConfigureAwait(false);

        if (finalised.IsSuccess) added++;

        return added;
    }

    private async Task<int> QuoteAsync(CancellationToken ct)
    {
        var opened = await _quotations
            .OpenAsync("Riverside Primary School", 30, ct)
            .ConfigureAwait(false);

        if (opened.IsFailure) return 0;

        var id = opened.Value.Id;

        await _quotations.AddLineAsync(id, "PLAST1", 20m, ct)
            .ConfigureAwait(false);

        await _quotations.AddLineAsync(id, "SURGM1", 10m, ct)
            .ConfigureAwait(false);

        await _quotations.AddLineAsync(id, DemoCatalogue.Counter, 12m, ct)
            .ConfigureAwait(false);

        var issued = await _quotations.IssueAsync(id,
            "Valid for 30 days. Delivery to the school office.", ct)
            .ConfigureAwait(false);

        return issued.IsSuccess ? 1 : 0;
    }

    /// <summary>
    /// An order raised against one supplier and received in full, and a
    /// second left outstanding so the purchase order list has both states
    /// on it.
    /// </summary>
    private async Task<int> BuyAsync(CancellationToken ct)
    {
        var suppliers = await _db.Suppliers.OrderBy(s => s.Code)
            .ToListAsync(ct).ConfigureAwait(false);

        if (suppliers.Count == 0) return 0;

        var added = 0;
        var first = suppliers[0];

        var order = await _purchasing.RaiseAsync(new PurchaseOrderEditDto(
            SupplierId: first.Id,
            ExpectedOn: _clock.Today.AddDays(2),
            Lines: new[]
            {
                new PurchaseOrderLineEditDto("DIS200", 100m, 17.60m),
                new PurchaseOrderLineEditDto("SURGM1", 50m, 55.00m)
            }), ct).ConfigureAwait(false);

        if (order.IsFailure)
        {
            _log.LogWarning("Demonstration order was not raised: {Error}",
                order.Error.Message);

            return 0;
        }

        added++;

        // Received against the order, so the order list has one that is
        // closed and the stock ledger has a delivery on it. The shelf is
        // set to its demonstration levels after this, by the caller.
        var receipt = await _receiving.ReceiveAsync(new GoodsReceiptEditDto(
            SupplierId: first.Id,
            PurchaseOrderId: order.Value.Id,
            SupplierInvoiceNumber: "INV-88214",
            ReceivedOn: _clock.Today,
            InvoiceTotal: null,
            Lines: new[]
            {
                new GoodsReceiptLineEditDto("DIS200", 100m, 17.60m, null,
                    null, PurchaseOrderLineNo: 1),
                new GoodsReceiptLineEditDto("SURGM1", 50m, 55.00m, null,
                    null, PurchaseOrderLineNo: 2)
            }), ct).ConfigureAwait(false);

        if (receipt.IsSuccess) added++;

        if (suppliers.Count < 2) return added;

        var outstanding = await _purchasing.RaiseAsync(
            new PurchaseOrderEditDto(
                SupplierId: suppliers[1].Id,
                ExpectedOn: _clock.Today.AddDays(7),
                Lines: new[]
                {
                    new PurchaseOrderLineEditDto(DemoCatalogue.Prescription,
                        60m, 73.40m),
                    new PurchaseOrderLineEditDto("INSUL1", 20m, 255.60m)
                }), ct).ConfigureAwait(false);

        if (outstanding.IsSuccess) added++;

        return added;
    }

    /// <summary>
    /// A posted stocktake, so that the stock movement report has an
    /// adjustment in it and the variance is one somebody counted rather
    /// than one the seeder wrote into the table.
    /// </summary>
    private async Task<int> CountAsync(CancellationToken ct)
    {
        var opened = await _stocktakes.OpenAsync(new StocktakeScopeDto(
            Description: "Front shop spot count",
            SearchTerm: null,
            BatchTrackedOnly: false,
            BelowReorderOnly: false), ct).ConfigureAwait(false);

        if (opened.IsFailure)
        {
            _log.LogWarning("Demonstration stocktake did not open: {Error}",
                opened.Error.Message);

            return 0;
        }

        var id = opened.Value.Id;
        var lines = opened.Value.Lines;

        // Counted as found. Two lines are deliberately out: one short,
        // which is what shrinkage looks like, and one over.
        for (var i = 0; i < lines.Count; i++)
        {
            var counted = i switch
            {
                0 => Math.Max(0m, lines[i].Expected - 3m),
                1 => lines[i].Expected + 1m,
                _ => lines[i].Expected
            };

            await _stocktakes.RecordCountAsync(id, lines[i].LineNo, counted,
                ct).ConfigureAwait(false);
        }

        await _stocktakes.ExplainVarianceAsync(id,
            "Three units of Disprin damaged in handling; one plaster pack "
            + "found behind the shelf.", ct).ConfigureAwait(false);

        var posted = await _stocktakes.PostAsync(id, ct)
            .ConfigureAwait(false);

        if (posted.IsSuccess) return 1;

        _log.LogWarning("Demonstration stocktake was not posted: {Error}",
            posted.Error.Message);

        return 0;
    }

    /// <summary>
    /// Part of the outstanding claims paid, so frmClaims opens with both
    /// a settled claim and a short-paid one rather than a flat list.
    /// </summary>
    /// <remarks>
    /// Depends on the seeded tariffs: a claim exists only where the
    /// dispensing carried a scheme contribution, which is the tariff's
    /// price for the product. Both dispensed patients are members and
    /// both products are priced, so there are claims here to settle.
    /// </remarks>
    private async Task<int> SettleClaimsAsync(CancellationToken ct)
    {
        // A remittance is paid by one scheme, so the claims are asked for
        // one scheme at a time rather than grouped afterwards: the claim
        // itself only carries the scheme's name, and settling against a
        // name matched back to an id is a bug waiting for two schemes
        // that share one.
        var schemes = await _db.MedicalAidSchemes.OrderBy(s => s.Code)
            .Select(s => s.Id).ToListAsync(ct).ConfigureAwait(false);

        foreach (var schemeId in schemes)
        {
            var outstanding = await _claims.OutstandingAsync(schemeId, ct)
                .ConfigureAwait(false);

            if (outstanding.IsFailure || outstanding.Value.Count == 0)
                continue;

            var lines = outstanding.Value.Select((claim, index) =>
                new RemittanceLineDto(
                    ClaimId: claim.Id,

                    // The first claim is short paid and the shortfall
                    // written off, which is the case the age analysis
                    // exists to surface.
                    Paid: index == 0
                        ? Math.Round(claim.Outstanding * 0.90m, 2,
                            MidpointRounding.AwayFromZero)
                        : claim.Outstanding,
                    WriteOffShortfall: index == 0)).ToList();

            var settled = await _claims.SettleAsync(schemeId, _clock.Today,
                "REM-2026-0914", lines, ct).ConfigureAwait(false);

            if (settled.IsSuccess) return 1;

            _log.LogWarning(
                "Demonstration remittance was not settled: {Error}",
                settled.Error.Message);

            return 0;
        }

        return 0;
    }

    /// <summary>
    /// A declared drawer and a closed session. The count is deliberately
    /// a little short of what the sales say it should be: a cash-up that
    /// always balances to the cent never shows the variance column doing
    /// its job.
    /// </summary>
    private async Task<int> CashUpAsync(CancellationToken ct)
    {
        var read = await _cashUp.XReadAsync(ct).ConfigureAwait(false);

        var expectedCash = read.Expectations
            .Where(e => string.Equals(e.TenderType, "Cash",
                StringComparison.OrdinalIgnoreCase))
            .Sum(e => e.Expected);

        var denominations = await _cashUp.DenominationsAsync(ct)
            .ConfigureAwait(false);

        var counted = CountOut(denominations, Math.Max(0m, expectedCash - 20m));

        await _cashUp.DeclareAsync(counted, ct).ConfigureAwait(false);

        var closed = await _cashUp.CloseAsync(
            "Twenty short; float recounted and the difference noted.", ct)
            .ConfigureAwait(false);

        return closed.IsSuccess ? 1 : 0;
    }

    /// <summary>
    /// Breaks an amount into notes and coins, largest first, the way
    /// somebody counting a drawer would.
    /// </summary>
    private static Dictionary<string, int> CountOut(
        IReadOnlyList<DenominationDto> denominations, decimal amount)
    {
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);
        var left = amount;

        foreach (var denomination in denominations
                     .Where(d => d.Value > 0m)
                     .OrderByDescending(d => d.Value))
        {
            var many = (int)Math.Floor(left / denomination.Value);

            if (many <= 0) continue;

            counted[denomination.Key] = many;
            left -= many * denomination.Value;
        }

        return counted;
    }
}
