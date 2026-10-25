using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Tills;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;
using PharmaX.Domain.Tills;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// Money out of the drawer, end to end: a cashier records lunch and the
/// owner's drawings, voids one entered twice, and the X-read, the count,
/// the close and the books all leave that money out - with nothing left
/// over for the cashier to explain (23 October 2026).
/// </summary>
public class TillPayoutTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public TillPayoutTests(PharmaXFixture fixture) => _steps = new WorkflowSteps(fixture);

    [Fact]
    public async Task Money_out_of_the_drawer_comes_off_the_cash_up_and_the_books()
    {
        await _steps.OpenTillAsync();                       // float 500
        await _steps.ProductAsync("PAY-PAN", "Panado 500mg 24s", 20m);
        await _steps.SignInAsync("pay.cashier", Role.Standard.Cashier);

        using (var scope = _steps.Fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var sale = (await sales.OpenAsync()).Value;
            await sales.AddLineAsync(sale.Id, "PAY-PAN", 1m);
            (await sales.FinaliseAsync(sale.Id, new[] { new PaymentDto("Cash", 20m, null) }))
                .IsSuccess.Should().BeTrue();
        }

        Guid bread;

        using (var scope = _steps.Fixture.NewScope())
        {
            var payouts = PharmaXFixture.Resolve<ITillPayoutService>(scope);

            (await payouts.RecordAsync(new PayoutEditDto(PayoutKind.Lunch, 12.50m,
                "Sandwiches for three"))).IsSuccess.Should().BeTrue();

            var tooMuch = await payouts.RecordAsync(new PayoutEditDto(PayoutKind.Drawings,
                1000m, "Owner"));
            tooMuch.Error.Code.Should().Be("POS-032", "the drawer holds 507.50");

            var noNote = await payouts.RecordAsync(new PayoutEditDto(PayoutKind.Expense, 5m, " "));
            noNote.Error.Code.Should().Be("POS-031");

            (await payouts.RecordAsync(new PayoutEditDto(PayoutKind.Drawings, 100m, "Owner")))
                .IsSuccess.Should().BeTrue();

            bread = (await payouts.RecordAsync(new PayoutEditDto(PayoutKind.Expense, 5m,
                "Bread"))).Value.Id;
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var payouts = PharmaXFixture.Resolve<ITillPayoutService>(scope);
            (await payouts.VoidAsync(bread, "Entered twice")).IsSuccess.Should().BeTrue();

            var listed = await payouts.ListAsync();
            listed.Should().HaveCount(3);
            listed.Single(p => p.Id == bread).IsVoided.Should().BeTrue();
            Application.Tills.Dtos.PayoutDto.Total(listed).Should().Be(112.50m);
        }

        // The X-read and the count expect the float and the takings less
        // what came out: 500 + 20 - 12.50 - 100.
        using (var scope = _steps.Fixture.NewScope())
        {
            var cashUp = PharmaXFixture.Resolve<ICashUpService>(scope);
            var read = await cashUp.XReadAsync();

            read.Expectations.Single(e => e.TenderType == "Cash").Expected.Should().Be(407.50m);
        }

        await _steps.SignInAsync("pay.pharm", Role.Standard.Pharmacist);

        Guid sessionId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var cashUp = PharmaXFixture.Resolve<ICashUpService>(scope);
            var sessions = PharmaXFixture.Resolve<ITillSessionService>(scope);
            sessionId = (await sessions.CurrentAsync())!.Id;

            var declared = await cashUp.DeclareAsync(Count(await cashUp.DenominationsAsync(), 407.50m));
            declared.Variances.Single(v => v.TenderType == "Cash").Variance.Should().Be(0m);

            (await cashUp.CloseAsync(null)).IsSuccess.Should().BeTrue();
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var shift = await PharmaXFixture.Resolve<IShiftCashUpService>(scope).ShiftAsync(sessionId);

            shift.PaidOut.Should().Be(112.50m);
            shift.ExpectedCash.Should().Be(407.50m);
            shift.Variance.Should().Be(0m, "nothing is left for the cashier to explain");
            shift.Transactions.Count(t => t.Kind == "Paid out").Should().Be(2);

            var slip = CashUpSlip.Document(shift, everyTransaction: true, DateTime.Now);
            // A long note wraps, its amount on the last of its lines.
            slip.Lines.Should().Contain(l => l.Contains("Lunch: Sandwiches"));
            slip.Lines.Should().Contain(l => l.Contains("for three") && l.EndsWith("-12.50"));
            slip.Lines.Should().OnlyContain(l => l.Length <= 32);
            slip.Lines.Should().NotContain(l => l.Contains("Bread"), "a voided entry is not paid out");

            // Closed: what came out stands as counted.
            var payouts = PharmaXFixture.Resolve<ITillPayoutService>(scope);
            (await payouts.RecordAsync(new PayoutEditDto(PayoutKind.Lunch, 1m, "Late"), sessionId))
                .Error.Code.Should().Be("POS-016");
        }

        using var check = _steps.Fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        // This till's payouts only: the other test pays out of till 7.
        var batches = await db.JournalBatches.AsNoTracking()
            .Where(b => b.Source == "PAYOUT" && b.Narration!.StartsWith("Till 1 "))
            .Select(b => b.Id)
            .ToListAsync();

        batches.Should().HaveCount(4, "lunch, drawings, the bread, and the bread voided");

        var lines = await db.JournalLines.AsNoTracking()
            .Where(l => batches.Contains(l.JournalBatchId))
            .ToListAsync();

        lines.Select(l => l.AccountCode).Distinct().Should().BeEquivalentTo(
            new[] { LedgerCode.Drawings, LedgerCode.TillExpenses, LedgerCode.CashOnHand });
        Net(LedgerCode.CashOnHand).Should().Be(-112.50m, "the drawer gave out 112.50");

        decimal Net(string code) => lines.Where(l => l.AccountCode == code)
            .Sum(l => l.IsDebit ? l.Amount.Amount : -l.Amount.Amount);

        Net(LedgerCode.Drawings).Should().Be(100m);
        Net(LedgerCode.TillExpenses).Should().Be(12.50m, "the bread was posted and reversed");

        (await db.AuditEntries.AsNoTracking()
            .CountAsync(a => a.Action == "pos.session.pay-out" && a.EntityId == sessionId))
            .Should().Be(3);
    }

    [Fact]
    public async Task Another_cashiers_till_is_paid_out_of_only_by_a_supervisor()
    {
        var owner = await _steps.UserAsync("pay.other", Role.Standard.Cashier);
        TillSession theirs;

        using (var scope = _steps.Fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            theirs = TillSession.Open(Guid.Parse(_steps.Fixture.Options.Workstation.BranchId),
                Guid.NewGuid(), "7", owner, Money.Of(50m), clock.UtcNow);
            db.TillSessions.Add(theirs);
            await db.SaveChangesAsync();
        }

        var edit = new PayoutEditDto(PayoutKind.Lunch, 5m, "Lunch");

        await _steps.SignInAsync("pay.cashier2", Role.Standard.Cashier);

        using (var scope = _steps.Fixture.NewScope())
        {
            var refused = await PharmaXFixture.Resolve<ITillPayoutService>(scope)
                .RecordAsync(edit, theirs.Id);

            refused.Error.Code.Should().Be("POS-027");
        }

        await _steps.SignInAsync("pay.assist", Role.Standard.PharmacistAssistant);

        using (var scope = _steps.Fixture.NewScope())
        {
            var recorded = await PharmaXFixture.Resolve<ITillPayoutService>(scope)
                .RecordAsync(edit, theirs.Id);

            recorded.IsSuccess.Should().BeTrue(recorded.IsFailure ? recorded.Error.Message : "");
            recorded.Value.TillSessionId.Should().Be(theirs.Id);
        }
    }

    /// The drawer counted note by note, largest first, to the amount.
    private static Dictionary<string, int> Count(
        IReadOnlyList<Application.Tills.Dtos.DenominationDto> denominations, decimal amount)
    {
        var counted = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var d in denominations.OrderByDescending(d => d.Value))
        {
            var n = (int)Math.Floor(amount / d.Value);
            if (n <= 0) continue;

            counted[d.Key] = n;
            amount -= n * d.Value;
        }

        amount.Should().Be(0m, "the denominations can make the amount up");
        return counted;
    }
}
