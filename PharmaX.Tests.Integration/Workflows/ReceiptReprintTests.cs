using FluentAssertions;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// Finding a receipt to print again: the last one the till printed, or
/// any one by the number on it.
/// </summary>
/// <remarks>
/// The till window kept the last sale in memory and nothing else, so a
/// till closed and opened again said no sale had been completed on it,
/// and a customer who came back for a copy of an earlier receipt could
/// not be given one (OCT-DEF-05).
/// </remarks>
public class ReceiptReprintTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public ReceiptReprintTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Fact]
    public async Task The_last_receipt_survives_the_till_being_closed_and_any_can_be_found_by_number()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("RRPA", "Reprint plasters 20s", 9.50m);
        await _steps.SignInAsync("rrp.cash1", Role.Standard.Cashier);

        var first = await SellAsync();
        var second = await SellAsync();

        // A fresh scope is a till window opened afresh: nothing held over.
        using var scope = _steps.Fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var last = await sales.LastReceiptNumberAsync();
        last.IsSuccess.Should().BeTrue(last.Error.Message);
        last.Value.Should().Be(second, "the last one this till's session printed");

        // As the customer reads it off the slip, in whatever case.
        var earlier = await sales.FindReceiptAsync($"  {first.ToLowerInvariant()} ");
        earlier.IsSuccess.Should().BeTrue(earlier.Error.Message);
        earlier.Value.DocumentNumber.Should().Be(first);
        earlier.Value.OperatorName.Should().Be("rrp.cash1", "a copy names who sold it");
        earlier.Value.Lines.Should().ContainSingle()
            .Which.Description.Should().Be("Reprint plasters 20s");

        (await sales.FindReceiptAsync("S-001-999999")).Error.Code.Should().Be("POS-024");
        (await sales.FindReceiptAsync("")).Error.Code.Should().Be("POS-024");
    }

    private async Task<string> SellAsync()
    {
        using var scope = _steps.Fixture.NewScope();
        var sales = PharmaXFixture.Resolve<ISaleService>(scope);

        var sale = await sales.OpenAsync();
        sale.IsSuccess.Should().BeTrue(sale.Error.Message);

        (await sales.AddLineAsync(sale.Value.Id, "RRPA", 1m)).IsSuccess.Should().BeTrue();

        var paid = await sales.FinaliseAsync(sale.Value.Id,
            new[] { new PaymentDto("Cash", 9.50m, null) });

        paid.IsSuccess.Should().BeTrue(paid.Error.Message);
        return paid.Value.DocumentNumber;
    }
}
