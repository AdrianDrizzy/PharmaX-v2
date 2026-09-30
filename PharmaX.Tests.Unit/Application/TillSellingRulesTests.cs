using FluentAssertions;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// What the till will not sell, whoever is standing at it.
/// </summary>
/// <remarks>
/// Found by the administrator end-to-end run: every one of these went
/// through for a user who could see the whole catalogue.
/// </remarks>
public class TillSellingRulesTests
{
    // ---- Medical aid (ADM-DEF-10) -----------------------------------

    [Fact]
    public async Task A_medical_aid_is_not_a_till_tender()
    {
        var builder = new SaleServiceBuilder();
        var sut = builder.Build();

        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "PAR0500", 1m);

        var result = await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("MedicalAid", 24.00m, "CIMAS 1234")
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-021");
        result.Error.Message.Should().Contain("claim");

        builder.Ledger.Posted.Should().BeEmpty(
            "nothing is parked in debtors that no scheme owes");
    }

    // ---- Scheduled medicines (ADM-DEF-11) ---------------------------

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task A_scheduled_medicine_is_dispensed_not_sold(int schedule)
    {
        var builder = new SaleServiceBuilder();
        Stocked(builder, "COD030", "Codeine phosphate 30mg", schedule);

        var sut = builder.Build();
        var opened = await sut.OpenAsync();

        var result = await sut.AddLineAsync(opened.Value.Id, "COD030", 1m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-022");
        result.Error.Message.Should().Contain($"schedule {schedule}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Over_the_counter_schedules_still_sell(int schedule)
    {
        var builder = new SaleServiceBuilder();
        Stocked(builder, "IBU200", "Ibuprofen 200mg", schedule);

        var sut = builder.Build();
        var opened = await sut.OpenAsync();

        var result = await sut.AddLineAsync(opened.Value.Id, "IBU200", 1m);

        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    // ---- Stock on untracked products (ADM-DEF-12) -------------------

    [Fact]
    public async Task Nothing_on_the_shelf_cannot_be_sold()
    {
        var builder = new SaleServiceBuilder();
        Stocked(builder, "SOAP01", "Antiseptic soap", 0, onHand: 0m);

        var sut = builder.Build();
        var opened = await sut.OpenAsync();

        var result = await sut.AddLineAsync(opened.Value.Id, "SOAP01", 1m);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-023");
        result.Error.Message.Should().Contain("only 0 in stock");
    }

    [Fact]
    public async Task Two_lines_cannot_between_them_take_more_than_there_is()
    {
        var builder = new SaleServiceBuilder();
        Stocked(builder, "SOAP01", "Antiseptic soap", 0, onHand: 3m);

        var sut = builder.Build();
        var opened = await sut.OpenAsync();

        (await sut.AddLineAsync(opened.Value.Id, "SOAP01", 2m))
            .IsSuccess.Should().BeTrue();

        var second = await sut.AddLineAsync(opened.Value.Id, "SOAP01", 2m);

        second.IsFailure.Should().BeTrue();
        second.Error.Code.Should().Be("POS-023");
    }

    [Fact]
    public async Task A_quantity_cannot_be_raised_past_the_shelf()
    {
        var builder = new SaleServiceBuilder();
        Stocked(builder, "SOAP01", "Antiseptic soap", 0, onHand: 3m);

        var sut = builder.Build();
        var opened = await sut.OpenAsync();

        var line = await sut.AddLineAsync(opened.Value.Id, "SOAP01", 1m);

        (await sut.SetLineQuantityAsync(opened.Value.Id, line.Value.LineNo, 3m))
            .IsSuccess.Should().BeTrue("all three are on the shelf");

        var over = await sut.SetLineQuantityAsync(opened.Value.Id,
                                                  line.Value.LineNo, 4m);

        over.IsFailure.Should().BeTrue();
        over.Error.Code.Should().Be("POS-023");
    }

    /// <remarks>
    /// A sale held while another till sold the last one is checked again
    /// when it is paid, not just when it was rung up.
    /// </remarks>
    [Fact]
    public async Task Stock_is_checked_again_at_payment()
    {
        var builder = new SaleServiceBuilder();
        var soap = Stocked(builder, "SOAP01", "Antiseptic soap", 0, onHand: 2m);

        var sut = builder.Build();
        var opened = await sut.OpenAsync();
        await sut.AddLineAsync(opened.Value.Id, "SOAP01", 2m);

        soap.SetQuantityOnHand(1m);   // sold elsewhere in the meantime

        var result = await sut.FinaliseAsync(opened.Value.Id, new[]
        {
            new PaymentDto("Cash", 100m, null)
        });

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-023");
    }

    private static Product Stocked(SaleServiceBuilder builder, string code,
        string description, int schedule, decimal onHand = 50m)
    {
        var product = Product.Create(code, description, Money.Of(20m),
                                     schedule: schedule);

        product.SetQuantityOnHand(onHand);

        return builder.Products.Add(product);
    }
}
