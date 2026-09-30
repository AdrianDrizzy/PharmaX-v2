using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Sales;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// The dispensary's quote: priced before anybody gives a name, saved,
/// and taken up once as the script when the customer goes ahead.
/// </summary>
public class DispensaryQuoteTests : IClassFixture<PharmaXFixture>
{
    private readonly WorkflowSteps _steps;

    public DispensaryQuoteTests(PharmaXFixture fixture) =>
        _steps = new WorkflowSteps(fixture);

    [Fact]
    public async Task A_quote_given_without_a_name_becomes_one_script()
    {
        await _steps.OpenTillAsync();
        await _steps.ProductAsync("DQMET", "Metformin 850mg 60s", 120m,
            schedule: 3, unitsPerPack: 60m, quantity: 200m);

        await _steps.SignInAsync("dq.pharm1", Role.Standard.Pharmacist);

        Guid quoteId;
        string number;

        // Priced first: no name, the quantity in tablets, the pack's share.
        using (var scope = _steps.Fixture.NewScope())
        {
            var quotes = PharmaXFixture.Resolve<IQuotationService>(scope);

            var opened = await quotes.OpenAsync(null,
                kind: QuotationKind.Dispensary);
            opened.IsSuccess.Should().BeTrue(opened.Error.Message);
            opened.Value.CustomerName.Should().BeEmpty();
            quoteId = opened.Value.Id;

            var product = await quotes.FindProductAsync("DQMET",
                QuotationKind.Dispensary);
            product.Value.UnitsInPack.Should().Be(60m);
            product.Value.PackPrice.Should().Be(120m);

            var added = await quotes.AddLineAsync(quoteId, "DQMET", 21m);
            added.IsSuccess.Should().BeTrue(added.Error.Message);

            var line = added.Value.Lines.Single();
            line.ProductCode.Should().Be("DQMET");
            line.UnitsInPack.Should().Be(60m);
            line.LineTotal.Should().Be(42m, "21 of a 60-tablet pack at 120.00");
            added.Value.Total.Should().Be(42m);

            // Saved, under the name given once the customer gave it.
            var saved = await quotes.IssueAsync(quoteId, null, "Mrs Dlamini");
            saved.IsSuccess.Should().BeTrue(saved.Error.Message);
            saved.Value.CustomerName.Should().Be("Mrs Dlamini");
            saved.Value.CanBecomeScript.Should().BeTrue();
            number = saved.Value.QuotationNumber;

            var printed = QuotationDocument.For(saved.Value).Lines;
            printed.Should().Contain(l => l.Contains("pack of 60 @ 120.00", StringComparison.Ordinal)
                                          && l.Contains("42.00", StringComparison.Ordinal));
            printed.Should().OnlyContain(l => l.Length <= 42);

            (await quotes.RecentAsync(QuotationKind.Dispensary)).Value
                .Should().Contain(q => q.Id == quoteId);
            (await quotes.RecentAsync(QuotationKind.OverTheCounter)).Value
                .Should().NotContain(q => q.Id == quoteId);
        }

        // The till will not ring it up: 21 tablets would sell as 21 packs.
        using (var scope = _steps.Fixture.NewScope())
        {
            var sales = PharmaXFixture.Resolve<ISaleService>(scope);
            var sale = await sales.OpenAsync();
            sale.IsSuccess.Should().BeTrue(sale.Error.Message);

            var converted = await sales.ConvertQuotationAsync(sale.Value.Id,
                number);
            converted.Error.Code.Should().Be("QUO-028");

            await sales.AbandonAsync(sale.Value.Id, "Test sale");
        }

        // The customer goes ahead: the patient, then the script.
        var patientId = await _steps.PatientAsync("Dlamini", "Nomsa");
        Guid scriptId;

        using (var scope = _steps.Fixture.NewScope())
        {
            var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var captured = await scripts.CaptureAsync(new ScriptEditDto(
                patientId, "Dr Quote", null, clock.Today, null, 0,
                new[] { new ScriptItemEditDto("DQMET", 21m, "One twice daily") },
                QuotationId: quoteId));

            captured.IsSuccess.Should().BeTrue(captured.Error.Message);
            scriptId = captured.Value;
        }

        using (var scope = _steps.Fixture.NewScope())
        {
            var quote = (await PharmaXFixture.Resolve<IQuotationService>(scope)
                .GetAsync(quoteId)).Value;

            quote.ScriptId.Should().Be(scriptId);
            quote.PatientId.Should().Be(patientId);
            quote.Status.Should().Be(nameof(QuotationStatus.Accepted));
            quote.CanBecomeScript.Should().BeFalse();
        }

        // Taken up once: a second script from it is refused, and spends
        // no script number.
        using (var scope = _steps.Fixture.NewScope())
        {
            var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var twice = await scripts.CaptureAsync(new ScriptEditDto(
                patientId, "Dr Quote", null, clock.Today, null, 0,
                new[] { new ScriptItemEditDto("DQMET", 21m, "One twice daily") },
                QuotationId: quoteId));

            twice.Error.Code.Should().Be("DSP-037");
            twice.Error.Message.Should().Contain("already been taken up");

            (await scripts.ForPatientAsync(patientId)).Value
                .Should().ContainSingle(s => s.Id == scriptId);
        }
    }
}
