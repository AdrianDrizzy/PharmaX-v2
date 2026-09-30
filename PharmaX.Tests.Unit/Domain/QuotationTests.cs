using FluentAssertions;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;

namespace PharmaX.Tests.Unit.Domain;

/// <summary>
/// A quotation is deliberately not a sale with a flag: it moves no
/// stock, takes no receipt number, and must not be able to do either by
/// accident.
/// </summary>
public class QuotationTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);
    private static readonly Guid Operator = Guid.NewGuid();

    private static Quotation Open(int validForDays = 30) =>
        Quotation.Open("Q-0001", "Mrs Khumalo", Operator, Today,
                       validForDays);

    private static Quotation WithOneLine()
    {
        var quotation = Open();
        quotation.AddLine(Guid.NewGuid(), "Panado 500mg", 2m,
                          Money.Of(34.95m), "A");
        return quotation;
    }

    /// <remarks>
    /// A customer who asks what something costs has not given a name, and
    /// may never. The price is quoted first; the name comes if they go
    /// ahead.
    /// </remarks>
    [Fact]
    public void A_price_can_be_quoted_before_the_customer_gives_a_name()
    {
        var quotation = Quotation.Open("Q-1", "  ", Operator, Today, 30);

        quotation.CustomerName.Should().BeEmpty();

        quotation.AddressTo("Mrs Khumalo");
        quotation.CustomerName.Should().Be("Mrs Khumalo");
    }

    /// <remarks>
    /// The dispensary counts tablets and prices the pack's share, as a
    /// dispensing does: 21 of a 60-tablet pack at 120.00 is 42.00.
    /// </remarks>
    [Fact]
    public void A_dispensary_line_is_priced_as_the_packs_share()
    {
        var quotation = Quotation.Open("Q-2", null, Operator, Today, 30,
            kind: QuotationKind.Dispensary);

        var line = quotation.AddLine(Guid.NewGuid(), "Metformin 850mg 60s",
            21m, Money.Of(120m), "A", unitsInPack: 60m);

        line.LineTotal.Amount.Should().Be(42m);
        quotation.Total.Amount.Should().Be(42m);
    }

    [Fact]
    public void A_dispensary_quote_becomes_one_script_once()
    {
        var quotation = Quotation.Open("Q-3", null, Operator, Today, 30,
            kind: QuotationKind.Dispensary);
        quotation.AddLine(Guid.NewGuid(), "Amoxicillin 500mg 21s", 21m,
                          Money.Of(94.72m), "A", unitsInPack: 21m);

        var script = Guid.NewGuid();
        var patient = Guid.NewGuid();

        quotation.ConvertToScript(script, patient, Today);

        quotation.ScriptId.Should().Be(script);
        quotation.PatientId.Should().Be(patient);
        quotation.Status.Should().Be(QuotationStatus.Accepted);

        var again = () => quotation.ConvertToScript(Guid.NewGuid(), patient,
                                                    Today);
        again.Should().Throw<DomainException>().WithMessage("*already*");
    }

    [Fact]
    public void A_lapsed_or_counter_quote_does_not_become_a_script()
    {
        var lapsed = Quotation.Open("Q-4", null, Operator, Today, 7,
            kind: QuotationKind.Dispensary);
        lapsed.AddLine(Guid.NewGuid(), "Item", 1m, Money.Of(1m), "A");

        var late = () => lapsed.ConvertToScript(Guid.NewGuid(),
            Guid.NewGuid(), Today.AddDays(8));
        late.Should().Throw<DomainException>().WithMessage("*lapsed*");

        var counter = () => Open().ConvertToScript(Guid.NewGuid(),
            Guid.NewGuid(), Today);
        counter.Should().Throw<DomainException>().WithMessage("*dispensary*");
    }

    [Fact]
    public void It_is_valid_for_the_days_it_was_opened_for()
    {
        Open(14).ValidUntil.Should().Be(Today.AddDays(14));
    }

    [Fact]
    public void It_must_be_valid_for_at_least_a_day()
    {
        var open = () => Quotation.Open("Q-1", "Mrs Khumalo", Operator,
                                        Today, 0);

        open.Should().Throw<DomainException>()
            .WithMessage("*at least a day*");
    }

    [Fact]
    public void The_total_is_the_sum_of_its_lines()
    {
        WithOneLine().Total.Amount.Should().Be(69.90m);
    }

    [Fact]
    public void A_line_needs_a_quantity_above_zero()
    {
        var quotation = Open();

        var add = () => quotation.AddLine(Guid.NewGuid(), "Panado", 0m,
                                          Money.Of(1m), "A");

        add.Should().Throw<DomainException>()
            .WithMessage("*quantity above zero*");
    }

    /// <summary>
    /// The printed quotation has to read 1, 2, 3. Explaining to a
    /// customer why it skips from 1 to 3 is not a conversation anyone
    /// should have to have.
    /// </summary>
    [Fact]
    public void Removing_a_line_renumbers_the_rest()
    {
        var quotation = Open();
        var product = Guid.NewGuid();

        quotation.AddLine(product, "First", 1m, Money.Of(10m), "A");
        quotation.AddLine(product, "Second", 1m, Money.Of(20m), "A");
        quotation.AddLine(product, "Third", 1m, Money.Of(30m), "A");

        quotation.RemoveLine(2);

        quotation.Lines.Select(l => l.LineNo).Should().Equal(1, 2);
        quotation.Lines.Select(l => l.Description)
                 .Should().Equal("First", "Third");
    }

    [Fact]
    public void An_empty_quotation_cannot_be_issued()
    {
        var issue = () => Open().Issue();

        issue.Should().Throw<DomainException>()
             .WithMessage("*nothing on it*");
    }

    [Fact]
    public void An_issued_quotation_cannot_be_changed()
    {
        var quotation = WithOneLine();
        quotation.Issue();

        var add = () => quotation.AddLine(Guid.NewGuid(), "Another", 1m,
                                          Money.Of(5m), "A");

        add.Should().Throw<DomainException>()
           .WithMessage("*has been issued*");
    }

    [Fact]
    public void It_lapses_the_day_after_it_stops_being_valid()
    {
        var quotation = Open(7);

        quotation.HasExpiredOn(Today.AddDays(7)).Should().BeFalse();
        quotation.HasExpiredOn(Today.AddDays(8)).Should().BeTrue();
    }

    [Fact]
    public void A_lapsed_quotation_cannot_be_accepted()
    {
        var quotation = WithOneLine();
        quotation.Issue();

        var accept = () => quotation.Accept(Today.AddDays(31));

        accept.Should().Throw<DomainException>()
              .WithMessage("*lapsed*");
    }

    [Fact]
    public void Only_an_issued_quotation_can_be_accepted()
    {
        var accept = () => WithOneLine().Accept(Today);

        accept.Should().Throw<DomainException>()
              .WithMessage("*issued quotation*");
    }

    [Fact]
    public void Accepting_one_in_date_marks_it_accepted()
    {
        var quotation = WithOneLine();
        quotation.Issue();

        quotation.Accept(Today.AddDays(5));

        quotation.Status.Should().Be(QuotationStatus.Accepted);
    }
}
