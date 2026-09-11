using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// A sale from sign-in to receipt number, against a real database. This
/// is the path NET-002 depends on: it runs with no host configured at
/// all, which is what "the pharmacy can trade with the host switched
/// off" means in practice.
/// </summary>
public class SaleFlowTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public SaleFlowTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_whole_sale_completes_with_no_host_configured()
    {
        var productCode = await GivenAProductAsync("PAR0500",
            "Paracetamol 500mg 20 tablets", 24m);

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 500m);

        using var scope = _fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var opened = await sales.OpenAsync();
        opened.IsSuccess.Should().BeTrue(opened.Error.Message);

        var line = await sales.AddLineAsync(opened.Value.Id, productCode, 2m);
        line.IsSuccess.Should().BeTrue(line.Error.Message);
        line.Value.LineTotal.Should().Be(48.00m);

        var finalised = await sales.FinaliseAsync(opened.Value.Id,
            new[] { new PaymentDto("Cash", 48.00m, null) });

        finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);

        // R-002-014873 : series, till, sequence (Section 13.4).
        finalised.Value.DocumentNumber.Should().MatchRegex(@"^S-001-\d{6}$");

        await ThenTheSaleIsOnDiskAsync(opened.Value.Id,
                                       finalised.Value.DocumentNumber);
    }

    [Fact]
    public async Task Money_survives_a_round_trip_through_the_database()
    {
        var productCode = await GivenAProductAsync("ASP0300",
            "Aspirin 300mg 24 tablets", 19.99m);

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 100m);

        Guid saleId;

        using (var scope = _fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var opened = await sales.OpenAsync();
            saleId = opened.Value.Id;

            await sales.AddLineAsync(saleId, productCode, 3m);
        }

        // A fresh scope, so nothing is served from the change tracker.
        using (var scope = _fixture.NewScope())
        {
            var repository = PharmaXFixture.Resolve<ISaleRepository>(scope);
            var sale = await repository.GetAsync(saleId);

            sale.Should().NotBeNull();
            sale!.Lines.Should().HaveCount(1);
            sale.Lines[0].UnitPrice.Amount.Should().Be(19.99m);
            sale.Lines[0].UnitPrice.Currency.Should()
                .Be(Money.DefaultCurrency);
            sale.Total.Amount.Should().Be(59.97m);
        }
    }

    /// <summary>
    /// FEFO through the whole stack: the till picks the batch that
    /// expires first, and the stock comes off that batch and no other.
    /// </summary>
    [Fact]
    public async Task A_batch_tracked_sale_takes_stock_from_the_nearest_expiry()
    {
        Guid productId;
        Guid soonerBatchId;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var product = Product.Create("AMX0250", "Amoxicillin 250mg caps",
                Money.Of(60m), isBatchTracked: true);

            product.SetQuantityOnHand(20m);
            db.Products.Add(product);
            productId = product.Id;

            var later = Batch.Receive(productId, "B-LATER",
                clock.Today.AddMonths(18), 10m, Money.Of(30m), clock.UtcNow);

            var sooner = Batch.Receive(productId, "B-SOONER",
                clock.Today.AddMonths(3), 10m, Money.Of(30m), clock.UtcNow);

            db.Batches.AddRange(later, sooner);
            soonerBatchId = sooner.Id;

            await db.SaveChangesAsync();
        }

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 100m);

        using (var scope = _fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var opened = await sales.OpenAsync();
            var line = await sales.AddLineAsync(opened.Value.Id, "AMX0250", 4m);

            line.IsSuccess.Should().BeTrue(line.Error.Message);

            var finalised = await sales.FinaliseAsync(opened.Value.Id,
                new[] { new PaymentDto("Cash", 240.00m, null) });

            finalised.IsSuccess.Should().BeTrue(finalised.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var sooner = await db.Batches.AsNoTracking()
                .SingleAsync(b => b.Id == soonerBatchId);

            sooner.QuantityOnHand.Should().Be(6m);

            var later = await db.Batches.AsNoTracking()
                .SingleAsync(b => b.ProductId == productId
                                  && b.BatchNumber == "B-LATER");

            later.QuantityOnHand.Should().Be(10m);

            var movement = await db.StockMovements.AsNoTracking()
                .SingleAsync(m => m.BatchId == soonerBatchId);

            // Signed negative: stock left the shelf.
            movement.Quantity.Should().Be(-4m);
            movement.SourceDocumentType.Should().Be("Sale");
        }
    }

    /// <summary>
    /// SEC-031 and NET-023 together: the audit entry and the outbox row
    /// commit with the sale, in the same transaction.
    /// </summary>
    [Fact]
    public async Task Finalising_commits_the_audit_entry_and_the_outbox_row()
    {
        var productCode = await GivenAProductAsync("VIT0100",
            "Vitamin C 1000mg", 45m);

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 200m);

        Guid saleId;

        using (var scope = _fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var opened = await sales.OpenAsync();
            saleId = opened.Value.Id;

            await sales.AddLineAsync(saleId, productCode, 1m);
            await sales.FinaliseAsync(saleId,
                new[] { new PaymentDto("Cash", 45.00m, null) });
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            (await db.OutboxEntries.AsNoTracking()
                .AnyAsync(o => o.EntityId == saleId
                               && o.EntityType == "Sale"))
                .Should().BeTrue();

            (await db.AuditEntries.AsNoTracking()
                .AnyAsync(a => a.EntityId == saleId
                               && a.Action == "pos.sale.create"
                               && a.Outcome == "Applied"))
                .Should().BeTrue();
        }
    }

    /// <summary>
    /// Nothing consumes the outbox in Release 1.0, so a finalised sale
    /// stays Pending. That is the state the reconciler of Release 2.0
    /// will find, and it is deliberate.
    /// </summary>
    [Fact]
    public async Task An_outbox_row_stays_pending_with_no_endpoint()
    {
        var productCode = await GivenAProductAsync("BAN0010",
            "Plasters, assorted", 12m);

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 50m);

        using (var scope = _fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var opened = await sales.OpenAsync();

            await sales.AddLineAsync(opened.Value.Id, productCode, 1m);
            await sales.FinaliseAsync(opened.Value.Id,
                new[] { new PaymentDto("Cash", 12.00m, null) });
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var entries = await db.OutboxEntries.AsNoTracking().ToListAsync();

            entries.Should().NotBeEmpty();
            entries.Should().OnlyContain(o => o.Status == "Pending");
            entries.Should().OnlyContain(o => o.SentAtUtc == null);
        }
    }

    [Fact]
    public async Task Two_sales_never_share_a_document_number()
    {
        var productCode = await GivenAProductAsync("SOAP0001",
            "Antiseptic soap", 30m);

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 100m);

        var numbers = new List<string>();

        for (var i = 0; i < 3; i++)
        {
            using var scope = _fixture.NewScope();
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var opened = await sales.OpenAsync();
            await sales.AddLineAsync(opened.Value.Id, productCode, 1m);

            var finalised = await sales.FinaliseAsync(opened.Value.Id,
                new[] { new PaymentDto("Cash", 30.00m, null) });

            numbers.Add(finalised.Value.DocumentNumber);
        }

        numbers.Should().OnlyHaveUniqueItems();
    }

    /// <remarks>
    /// A cashier who starts a sale, walks away and starts another is
    /// ordinary. It used to fail: the draft carried an empty document
    /// number, the unique index over workstation and document number
    /// treated two empty strings as a clash, and the second sale the
    /// till ever opened was refused by the database.
    /// </remarks>
    [Fact]
    public async Task A_till_can_hold_more_than_one_unfinished_sale()
    {
        await GivenAProductAsync("TOWEL001", "Paper towel", 20m);

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 100m);

        var ids = new List<Guid>();

        for (var i = 0; i < 3; i++)
        {
            using var scope = _fixture.NewScope();
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var opened = await sales.OpenAsync();
            opened.IsSuccess.Should().BeTrue(opened.Error.Message);
            ids.Add(opened.Value.Id);
        }

        using var check = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var drafts = await db.Sales.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToListAsync();

        drafts.Should().HaveCount(3);
        drafts.Should().OnlyContain(s => s.Status == SaleStatus.Open);
        drafts.Should().OnlyContain(s => s.DocumentNumber == "");
    }

    /// <remarks>
    /// The number, not only the identifier: a recall is published
    /// against a batch number, and the line has to answer it years
    /// after the batch row itself may have gone.
    /// </remarks>
    [Fact]
    public async Task A_batch_tracked_line_records_the_batch_it_came_from()
    {
        var code = await GivenABatchTrackedProductAsync("SYRUP001",
            "Cough syrup 100ml", 60m, "SYR-2026-A");

        await SignInAsAdministratorAsync();
        await OpenTillSessionAsync(float_: 100m);

        using var scope = _fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var opened = await sales.OpenAsync();
        var line = await sales.AddLineAsync(opened.Value.Id, code, 1m);

        line.IsSuccess.Should().BeTrue(line.Error.Message);
        line.Value.BatchNumber.Should().Be("SYR-2026-A");

        using var check = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.SaleLines.AsNoTracking()
            .SingleAsync(l => l.SaleId == opened.Value.Id);

        stored.BatchNumber.Should().Be("SYR-2026-A");
    }

    // ---- Steps -----------------------------------------------------------

    private async Task<string> GivenAProductAsync(string code,
        string description, decimal price)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var existing = await db.Products
            .FirstOrDefaultAsync(p => p.Code == code);

        if (existing is not null) return code;

        var product = Product.Create(code, description, Money.Of(price));
        product.SetQuantityOnHand(100m);

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return code;
    }

    private async Task<string> GivenABatchTrackedProductAsync(string code,
        string description, decimal price, string batchNumber)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == code)) return code;

        var product = Product.Create(code, description, Money.Of(price),
                                     isBatchTracked: true);

        product.SetQuantityOnHand(50m);
        db.Products.Add(product);

        db.Batches.Add(Batch.Receive(product.Id, batchNumber,
            DateOnly.FromDateTime(DateTime.UtcNow).AddMonths(9), 50m,
            Money.Of(30m), DateTime.UtcNow));

        await db.SaveChangesAsync();

        return code;
    }

    private async Task SignInAsAdministratorAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task OpenTillSessionAsync(decimal float_)
    {
        using var scope = _fixture.NewScope();
        var sessions = PharmaXFixture.Resolve<ITillSessionService>(scope);

        // Already open from an earlier test in the same fixture is fine:
        // one session serves the whole till, which is the real behaviour.
        if (await sessions.CurrentAsync() is not null) return;

        var result = await sessions.OpenAsync(float_);
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task ThenTheSaleIsOnDiskAsync(Guid saleId,
                                                string documentNumber)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var sale = await db.Sales.AsNoTracking()
            .Include(s => s.Lines)
            .Include(s => s.Payments)
            .SingleAsync(s => s.Id == saleId);

        sale.Status.Should().Be(SaleStatus.Finalised);
        sale.DocumentNumber.Should().Be(documentNumber);
        sale.Lines.Should().HaveCount(1);
        sale.Payments.Should().HaveCount(1);
        sale.Payments[0].Tender.Should().Be(TenderType.Cash);
        sale.Total.Amount.Should().Be(48.00m);
        sale.Tendered.Amount.Should().Be(48.00m);
    }
}
