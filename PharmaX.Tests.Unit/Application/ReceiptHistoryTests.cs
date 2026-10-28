using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The Receipts history: any day, a page at a time, and a receipt can be
/// taken off the list with a reason while its sale stays as it was.
/// </summary>
public class ReceiptHistoryTests
{
    private static readonly Guid Cashier = Guid.NewGuid();
    private static readonly DateOnly Today = new(2026, 10, 25);

    private readonly InMemorySaleRepository _sales = new();
    private readonly IUserRepository _users = Substitute.For<IUserRepository>();
    private readonly IReceiptParticularsService _particulars = Substitute.For<IReceiptParticularsService>();
    private readonly IAuditWriter _audit = Substitute.For<IAuditWriter>();
    private readonly IPermissionService _permissions = Substitute.For<IPermissionService>();
    private readonly HashSet<string> _granted = new() { PermissionCode.ReceiptHide };

    public ReceiptHistoryTests()
    {
        _users.ListAsync(Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<User>());

        // Granted is the enum's default, so a bare substitute grants everything.
        _permissions.Check(Arg.Any<string>())
            .Returns(ci => _granted.Contains((string)ci[0]) ? PermissionOutcome.Granted : PermissionOutcome.Denied);
        _permissions.When(p => p.Demand(Arg.Any<string>())).Do(ci =>
        {
            if (!_granted.Contains((string)ci[0])) throw new PermissionDeniedException("No.");
        });
        _particulars.ForReceiptAsync(Arg.Any<SaleDto>(), Arg.Any<CancellationToken>())
            .Returns(ci => (SaleDto)ci[0]);
    }

    private ReceiptHistoryService Service() =>
        new(_sales, _users, _particulars, _audit, Substitute.For<IUnitOfWork>(), _permissions);

    private Sale Sold(string number, DateOnly day, int minute = 0)
    {
        var at = day.ToDateTime(new TimeOnly(10, minute), DateTimeKind.Local).ToUniversalTime();
        var sale = Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Cashier, at);
        sale.AddLine(SaleLine.Create(Guid.NewGuid(), "Panado 500mg 20s", 1m, Money.Of(10m), 15m,
            "A", PriceSource.StandardRetail));
        sale.TakePayments(new[] { SalePayment.Create(TenderType.Cash, Money.Of(20m)) });
        sale.Finalise(number, at);
        _sales.AddAsync(sale).GetAwaiter().GetResult();
        return sale;
    }

    private static ReceiptQuery Days(DateOnly from, DateOnly to, string? find = null, bool hidden = false,
                                     int skip = 0, int take = 100) =>
        new(from, to, find, hidden, skip, take);

    [Fact]
    public async Task Receipts_across_days_come_a_page_at_a_time_newest_first()
    {
        for (var i = 1; i <= 5; i++) Sold($"S-{i}", Today.AddDays(-i));

        var page = await Service().PageAsync(Days(Today.AddDays(-10), Today, skip: 2, take: 2));

        page.TotalCount.Should().Be(5);
        page.Items.Select(r => r.DocumentNumber).Should().Equal("S-3", "S-4");
    }

    [Fact]
    public async Task Days_outside_the_span_are_left_out_and_find_narrows_by_number()
    {
        Sold("S-OLD", Today.AddDays(-30));
        Sold("S-101", Today);
        Sold("S-202", Today);

        var page = await Service().PageAsync(Days(Today.AddDays(-7), Today, find: "101"));

        page.Items.Should().ContainSingle().Which.DocumentNumber.Should().Be("S-101");
    }

    [Fact]
    public async Task A_hidden_receipt_leaves_the_list_but_its_sale_is_untouched()
    {
        var sale = Sold("S-1", Today);
        var service = Service();

        (await service.HideAsync(sale.Id, "Printed in error")).IsSuccess.Should().BeTrue();

        (await service.PageAsync(Days(Today, Today))).Items.Should().BeEmpty();
        (await service.DayAsync(Today)).Should().BeEmpty();
        sale.Status.Should().Be(SaleStatus.Finalised);
        sale.Total.Amount.Should().Be(10m);
        sale.Change.Amount.Should().Be(10m);
        _audit.Received().Applied(PermissionCode.ReceiptHide, "Sale", sale.Id,
            Arg.Is<string>(s => s.Contains("Printed in error", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Show_hidden_lists_it_with_its_reason_and_it_can_be_put_back()
    {
        var sale = Sold("S-1", Today);
        var service = Service();
        await service.HideAsync(sale.Id, "Printed in error");

        var shown = await service.PageAsync(Days(Today, Today, hidden: true));
        shown.Items.Should().ContainSingle().Which.HiddenReason.Should().Be("Printed in error");

        await service.ShowAsync(sale.Id);
        (await service.PageAsync(Days(Today, Today))).Items.Should().ContainSingle();
    }

    [Fact]
    public async Task Hiding_needs_a_reason()
    {
        var sale = Sold("S-1", Today);

        var result = await Service().HideAsync(sale.Id, "  ");

        result.IsFailure.Should().BeTrue();
        sale.IsReceiptHidden.Should().BeFalse();
    }

    [Fact]
    public async Task Without_the_permission_hidden_receipts_are_neither_hidden_nor_shown()
    {
        var sale = Sold("S-1", Today);
        await Service().HideAsync(sale.Id, "Printed in error");
        _granted.Clear();
        var service = Service();

        service.MayHide.Should().BeFalse();
        (await service.PageAsync(Days(Today, Today, hidden: true))).Items.Should().BeEmpty();
        await service.Invoking(s => s.ShowAsync(sale.Id)).Should().ThrowAsync<PermissionDeniedException>();
    }

    [Fact]
    public async Task A_hidden_receipt_is_not_printed_again()
    {
        var sale = Sold("S-1", Today);
        var service = Service();
        await service.HideAsync(sale.Id, "Printed in error");

        var found = await service.ReceiptAsync("S-1");

        found.IsFailure.Should().BeTrue();
        found.Error.Message.Should().Contain("Printed in error");
    }
}
