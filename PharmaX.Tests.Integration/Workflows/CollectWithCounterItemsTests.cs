using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// A script dispensed, then collected at the till with something from the
/// shop added to the same sale and paid for in one go.
public sealed class CollectWithCounterItemsTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();
    private readonly WorkflowSteps _steps;

    public CollectWithCounterItemsTests() => _steps = new WorkflowSteps(_fixture);

    public void Dispose() => _fixture.Dispose();

    [Fact]
    public async Task The_script_and_the_counter_items_are_paid_together_and_stock_moves_once()
    {
        await _steps.OpenTillAsync();

        var amoxil = await _steps.ProductAsync("CWAMOX", "Amoxicillin 500mg caps 21s", 60m,
            schedule: 4, unitsPerPack: 21m, batches: new[] { ("AMX-1", 12, 5m) });
        var panado = await _steps.ProductAsync("CWPAN", "Panado 500mg tablets 20s", 25m,
            quantity: 50m);

        // The pharmacist dispenses the script.
        await _steps.SignInAsync("cw.pharm1", Role.Standard.Pharmacist);
        var patientId = await _steps.PatientAsync("Counter", "Chipo");
        var scriptId = await _steps.ScriptAsync(patientId,
            ("CWAMOX", 21m, "One capsule three times a day"));

        Guid dispensingId;
        using (var scope = _fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);
            dispensingId = (await dispensing.BeginAsync(scriptId)).Value.Id;
            (await dispensing.AddScriptItemsAsync(dispensingId)).IsSuccess.Should().BeTrue();
            var issued = await dispensing.IssueAsync(dispensingId);
            issued.IsSuccess.Should().BeTrue(issued.Error.Message);
        }

        (await _steps.StockOfAsync(amoxil)).Product.Should().Be(4m);

        // The cashier collects it and adds two packs of Panado.
        await _steps.SignInAsync("cw.cash1", Role.Standard.Cashier);

        Guid saleId;
        using (var scope = _fixture.NewScope())
        {
            var collections = PharmaXFixture.Resolve<ICollectionService>(scope);
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);

            var started = await collections.StartAsync(dispensingId);
            started.IsSuccess.Should().BeTrue(started.Error.Message);
            saleId = started.Value.Id;

            var added = await sales.AddLineAsync(saleId, "CWPAN", 2m);
            added.IsSuccess.Should().BeTrue(added.Error.Message);

            var total = (await sales.GetAsync(saleId)).Value.Totals.Total;
            total.Should().Be(60m + 50m, "the script's co-payment and two packs of Panado");

            var paid = await sales.FinaliseAsync(saleId, new[] { new PaymentDto("Cash", total, null) });
            paid.IsSuccess.Should().BeTrue(paid.Error.Message);
        }

        (await _steps.StockOfAsync(amoxil)).Product.Should().Be(4m, "the script's stock moved at the issue, once");
        (await _steps.StockOfAsync(panado)).Product.Should().Be(48m, "the counter items leave the shelf at the sale");

        using var check = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);
        (await db.Dispensings.SingleAsync(d => d.Id == dispensingId)).SaleId.Should().Be(saleId);
        (await db.Sales.Include(s => s.Lines).SingleAsync(s => s.Id == saleId)).Lines
            .Select(l => l.IsScript).Should().BeEquivalentTo(new[] { true, false });
    }
}
