using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Finance;

/// <summary>
/// A day's takings turned into journals, against a real database.
/// </summary>
/// <remarks>
/// Its own fixture rather than the shared one: it sells against a known
/// empty day and then asserts on totals for that day, which another
/// test's sales would quietly change. Automatic posting is off, because
/// a day-end batch is how an installation without it gets its takings
/// into the ledger.
/// </remarks>
public sealed class DayEndPostingTests : IDisposable
{
    private PharmaXFixture _fixture = PharmaXFixture.Trading(autoPost: false);

    public void Dispose() => _fixture.Dispose();

    /// <remarks>
    /// With automatic posting on, each sale already journalled itself.
    /// A day-end batch on top counted the same takings twice
    /// (ADM-DEF-19).
    /// </remarks>
    [Fact]
    public async Task A_day_already_posted_as_it_traded_is_not_posted_again()
    {
        _fixture.Dispose();
        _fixture = PharmaXFixture.Trading(autoPost: true);

        await GivenASaleAsync("PAR0500", "Paracetamol 500mg", 115m, "Cash");

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        var summary = await posting.SummariseAsync(Today);
        summary.Value.PostedAsTraded.Should().BeTrue();

        var prepared = await posting.PrepareAsync(Today);

        prepared.IsFailure.Should().BeTrue();
        prepared.Error.Code.Should().Be("FIN-004");

        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.JournalBatches.AsNoTracking()
                .CountAsync(b => b.Source == DayEndPostingService.Source))
            .Should().Be(0, "nothing is built for a day already journalled");
    }

    [Fact]
    public async Task A_days_takings_become_a_balanced_journal()
    {
        await GivenASaleAsync("PAR0500", "Paracetamol 500mg", 115m, "Cash");

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        var prepared = await posting.PrepareAsync(Today);
        prepared.IsSuccess.Should().BeTrue(prepared.Error.Message);

        var batch = prepared.Value;

        batch.IsBalanced.Should().BeTrue(
            $"debits {batch.TotalDebits} should equal credits "
            + $"{batch.TotalCredits}");

        batch.TotalDebits.Should().Be(115.00m);
        batch.IsPosted.Should().BeFalse("preparing does not post");
    }

    /// <summary>
    /// The VAT was never the pharmacy's money, so it credits a liability
    /// rather than income. At 15% inclusive, 115.00 is 100.00 of income
    /// and 15.00 owed to the revenue service.
    /// </summary>
    [Fact]
    public async Task Vat_is_split_out_of_income_and_credited_separately()
    {
        await GivenASaleAsync("VIT0100", "Vitamin C", 115m, "Cash");

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        var prepared = await posting.PrepareAsync(Today);
        prepared.IsSuccess.Should().BeTrue(prepared.Error.Message);

        var lines = prepared.Value.Lines;

        lines.Should().Contain(l => l.AccountCode == LedgerCode.CashOnHand
                                    && l.Debit == 115.00m,
            "cash taken is debited to cash on hand");

        lines.Should().Contain(l => l.AccountCode == LedgerCode.Sales
                                    && l.Credit == 100.00m,
            "income excludes the VAT");

        lines.Should().Contain(l => l.AccountCode == LedgerCode.VatControl
                                    && l.Credit == 15.00m,
            "VAT collected is a liability, not income");
    }

    /// <summary>
    /// Reconciliation is done per tender, so a single lump sum could not
    /// be reconciled against anything.
    /// </summary>
    [Fact]
    public async Task Each_tender_is_debited_to_its_own_account()
    {
        await GivenASaleAsync("SOAP0001", "Antiseptic soap", 50m, "Cash");
        await GivenASaleAsync("SOAP0001", "Antiseptic soap", 50m, "Card");

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        var prepared = await posting.PrepareAsync(Today);
        prepared.IsSuccess.Should().BeTrue(prepared.Error.Message);

        // Named per tender rather than by literal, so that the assertion
        // says which tender landed where. Two codes each holding 50.00
        // would pass even if the two tenders had been posted to each
        // other's account.
        prepared.Value.Lines.Should()
            .Contain(l => l.AccountCode == LedgerCode.CashOnHand
                          && l.Debit == 50.00m)
            .And.Contain(l => l.AccountCode == LedgerCode.Bank
                              && l.Debit == 50.00m);

        prepared.Value.IsBalanced.Should().BeTrue();
    }

    /// A day posted twice is a day counted twice.
    [Fact]
    public async Task A_day_cannot_be_prepared_twice()
    {
        await GivenASaleAsync("BAN0010", "Plasters", 20m, "Cash");

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        (await posting.PrepareAsync(Today)).IsSuccess.Should().BeTrue();

        var again = await posting.PrepareAsync(Today);

        again.IsFailure.Should().BeTrue();
        again.Error.Code.Should().Be("FIN-002");
    }

    [Fact]
    public async Task A_day_that_sold_nothing_has_nothing_to_post()
    {
        await SignInAsAdministratorAsync();

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        var result = await posting.PrepareAsync(Today.AddDays(-30));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("FIN-001");
    }

    [Fact]
    public async Task Posting_makes_the_journal_final()
    {
        await GivenASaleAsync("MET0850", "Metformin 850mg", 60m, "Cash");

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        var prepared = await posting.PrepareAsync(Today);
        prepared.IsSuccess.Should().BeTrue(prepared.Error.Message);

        var posted = await posting.PostAsync(prepared.Value.Id);

        posted.IsSuccess.Should().BeTrue(posted.Error.Message);
        posted.Value.IsPosted.Should().BeTrue();

        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var stored = await db.JournalBatches.AsNoTracking()
            .SingleAsync(b => b.Id == prepared.Value.Id);

        stored.IsPosted.Should().BeTrue();
        stored.PostedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task The_summary_reports_the_day_before_anything_is_built()
    {
        await GivenASaleAsync("DIS0300", "Disprin 300mg", 115m, "Cash");

        using var scope = _fixture.NewScope();
        var posting = PharmaXFixture.Resolve<IDayEndPostingService>(scope);

        var summary = await posting.SummariseAsync(Today);

        summary.IsSuccess.Should().BeTrue(summary.Error.Message);
        summary.Value.Sales.Should().BeGreaterThan(0);
        summary.Value.Gross.Should().Be(115.00m);
        summary.Value.Vat.Should().Be(15.00m);
        summary.Value.Net.Should().Be(100.00m);
        summary.Value.AlreadyPosted.Should().BeFalse();
        summary.Value.ByTender.Should().ContainKey("Cash");
    }

    // ---- given ----------------------------------------------------------

    private static DateOnly Today => DateOnly.FromDateTime(DateTime.Now);

    private async Task GivenASaleAsync(string code, string description,
        decimal price, string tender)
    {
        await GivenAProductAsync(code, description, price);
        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync();

        using var scope = _fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var opened = await sales.OpenAsync();
        opened.IsSuccess.Should().BeTrue(opened.Error.Message);

        var line = await sales.AddLineAsync(opened.Value.Id, code, 1m);
        line.IsSuccess.Should().BeTrue(line.Error.Message);

        // No reference on any tender: there is no gateway, and the
        // posting must not depend on one being copied out by hand.
        var finalised = await sales.FinaliseAsync(opened.Value.Id,
            new[] { new PaymentDto(tender, price, null) });

        finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);
    }

    private async Task GivenAProductAsync(string code, string description,
        decimal price)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == code)) return;

        var product = Product.Create(code, description, Money.Of(price));
        product.SetQuantityOnHand(100m);

        db.Products.Add(product);
        await db.SaveChangesAsync();
    }

    private async Task SignInAsAdministratorAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task OpenTillSessionAsync()
    {
        using var scope = _fixture.NewScope();
        var sessions = PharmaXFixture.Resolve<ITillSessionService>(scope);

        if (await sessions.CurrentAsync() is not null) return;

        var result = await sessions.OpenAsync(500m);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
