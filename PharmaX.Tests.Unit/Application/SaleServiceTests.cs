using FluentAssertions;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

public class SaleServiceTests
{
    [Fact]
    public async Task Cannot_open_a_sale_without_a_till_session()
    {
        var sut = new SaleServiceBuilder().WithNoTillSession().Build();

        var result = await sut.OpenAsync();

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-013");
    }

    [Fact]
    public async Task Cannot_open_a_sale_without_the_permission()
    {
        var sut = new SaleServiceBuilder()
            .WithPermissions(granted: Array.Empty<string>())
            .Build();

        var act = () => sut.OpenAsync();

        await act.Should().ThrowAsync<PermissionDeniedException>();
    }

    /// <summary>
    /// SEC-035: a refusal is as informative as a change, because a
    /// pattern of attempted overrides is something the manager should be
    /// able to see.
    /// </summary>
    [Fact]
    public async Task A_refused_permission_is_audited()
    {
        var builder = new SaleServiceBuilder()
            .WithPermissions(granted: Array.Empty<string>());

        var sut = builder.Build();

        try { await sut.OpenAsync(); }
        catch (PermissionDeniedException) { /* expected */ }

        builder.Audit.Recorded(PermissionCode.SaleCreate, "Refused")
               .Should().BeTrue();
    }

    [Fact]
    public async Task An_unknown_code_does_not_add_a_line()
    {
        var sut = new SaleServiceBuilder().Build();
        var opened = await sut.OpenAsync();

        var result = await sut.AddLineAsync(opened.Value.Id, "NOSUCH", 1m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-001");
    }

    [Fact]
    public async Task Adding_a_line_prices_it_from_the_product()
    {
        var sut = new SaleServiceBuilder().Build();
        var opened = await sut.OpenAsync();

        var result = await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        result.IsSuccess.Should().BeTrue();
        result.Value.UnitPrice.Should().Be(24.00m);
        result.Value.LineTotal.Should().Be(24.00m);
    }

    [Fact]
    public async Task Finalising_twice_returns_the_same_document_number()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var payments = new[] { new PaymentDto("Cash", 24.00m, null) };

        var first = await sut.FinaliseAsync(opened.Value.Id, payments);
        var second = await sut.FinaliseAsync(opened.Value.Id, payments);

        // Idempotency: a retry after a communications failure must not
        // produce a second sale (NET-006).
        first.IsSuccess.Should().BeTrue();
        second.Value.DocumentNumber.Should().Be(first.Value.DocumentNumber);
        builder.Numbering.IssuedCount.Should().Be(1);
    }

    [Fact]
    public async Task Finalising_writes_the_outbox_row_and_the_audit_entry()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        await sut.FinaliseAsync(opened.Value.Id,
            new[] { new PaymentDto("Cash", 24.00m, null) });

        // NET-023 and SEC-031: both written inside the transaction that
        // committed the sale.
        builder.Outbox.Entries.Should().ContainSingle(e => e.EntityType == "Sale");
        builder.Audit.Recorded("pos.sale.create", "Applied").Should().BeTrue();
        builder.UnitOfWork.Committed.Should().BeTrue();
    }

    [Fact]
    public async Task Finalising_moves_the_stock()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 2m);

        await sut.FinaliseAsync(opened.Value.Id,
            new[] { new PaymentDto("Cash", 48.00m, null) });

        builder.Stock.Issues.Should().ContainSingle(i => i.Lines == 1);
    }

    [Fact]
    public async Task An_unbalanced_tender_is_refused()
    {
        var sut = new SaleServiceBuilder().Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var act = () => sut.FinaliseAsync(opened.Value.Id,
            new[] { new PaymentDto("Cash", 20.00m, null) });

        // The rule lives on the aggregate, so it throws rather than
        // returning a failure: a form that gets here has bypassed its
        // own check, which is a defect and not a refusal.
        await act.Should()
            .ThrowAsync<PharmaX.Domain.Exceptions.DomainException>();
    }

    [Fact]
    public async Task A_discount_within_the_operator_limit_needs_no_authoriser()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        // The default fake operator has a 10 per cent limit, so 2.00 off
        // a 24.00 line is inside it.
        var result = await sut.ApplyLineDiscountAsync(
            opened.Value.Id, lineNo: 1, amount: 2.00m, authoriserId: null);

        result.IsSuccess.Should().BeTrue();
        builder.Audit.Recorded("pos.sale.discount", "Applied")
               .Should().BeTrue();
    }

    [Fact]
    public async Task Voiding_a_line_records_the_reason()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        await sut.VoidLineAsync(opened.Value.Id, 1, "Wrong item scanned.");

        builder.Audit.Entries.Should().Contain(e =>
            e.Action == "pos.sale.line.void"
            && e.Reason == "Wrong item scanned.");
    }
}
