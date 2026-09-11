using FluentAssertions;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;

namespace PharmaX.Tests.Unit.Domain;

public class ReferenceEntryTests
{
    [Fact]
    public void A_code_is_stored_upper_case_so_a_lookup_cannot_miss_on_case()
    {
        var department = Department.Create(" disp ", "  Dispensary  ");

        department.Code.Should().Be("DISP");
        department.Name.Should().Be("Dispensary");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_code_is_refused(string code)
    {
        var create = () => Manufacturer.Create(code, "Acme");

        create.Should().Throw<DomainException>()
              .WithMessage("*code is required*");
    }

    [Fact]
    public void A_blank_name_is_refused()
    {
        var create = () => Branch.Create("MAIN", "  ");

        create.Should().Throw<DomainException>()
              .WithMessage("*name is required*");
    }

    /// <summary>
    /// Blank detail is held as null rather than an empty string, so a
    /// screen showing "no address set" does not have to test for both.
    /// </summary>
    [Fact]
    public void Blank_detail_becomes_nothing_at_all()
    {
        MedicalAidScheme.Create("DIS", "Discovery", "   ")
                        .Detail.Should().BeNull();
    }

    [Fact]
    public void Retiring_an_entry_leaves_it_in_place()
    {
        var branch = Branch.Create("MAIN", "Main Street");

        branch.Deactivate();
        branch.IsActive.Should().BeFalse();

        branch.Reactivate();
        branch.IsActive.Should().BeTrue();
    }
}

public class TariffTests
{
    private static readonly Guid Scheme = Guid.NewGuid();
    private static readonly Guid Product = Guid.NewGuid();
    private static readonly DateOnly March = new(2026, 3, 1);

    private static Tariff Create(DateOnly from, DateOnly? to = null) =>
        Tariff.Create(Scheme, Product, Money.Of(80m), Money.Of(12m), from, to);

    [Fact]
    public void The_total_is_the_price_plus_the_dispensing_fee()
    {
        Create(March).Total.Amount.Should().Be(92m);
    }

    [Fact]
    public void A_tariff_cannot_stop_before_it_starts()
    {
        var create = () => Create(March, March.AddDays(-1));

        create.Should().Throw<DomainException>()
              .WithMessage("*cannot stop before it starts*");
    }

    [Fact]
    public void A_negative_price_is_refused()
    {
        var create = () => Tariff.Create(Scheme, Product, Money.Of(-1m),
            Money.Zero(), March);

        create.Should().Throw<DomainException>()
              .WithMessage("*cannot be negative*");
    }

    [Fact]
    public void An_open_ended_tariff_applies_from_its_start_onwards()
    {
        var tariff = Create(March);

        tariff.IsEffectiveOn(March.AddDays(-1)).Should().BeFalse();
        tariff.IsEffectiveOn(March).Should().BeTrue();
        tariff.IsEffectiveOn(March.AddYears(5)).Should().BeTrue();
    }

    [Fact]
    public void A_closed_tariff_applies_on_its_last_day_and_not_after()
    {
        var tariff = Create(March, March.AddDays(30));

        tariff.IsEffectiveOn(March.AddDays(30)).Should().BeTrue();
        tariff.IsEffectiveOn(March.AddDays(31)).Should().BeFalse();
    }

    /// <summary>
    /// A retired tariff prices nothing, whatever its dates say. The
    /// lookup asks this one question, so the flag has to be part of it.
    /// </summary>
    [Fact]
    public void A_retired_tariff_is_never_effective()
    {
        var tariff = Create(March);
        tariff.Deactivate();

        tariff.IsEffectiveOn(March).Should().BeFalse();
    }

    [Fact]
    public void Repricing_moves_both_the_price_and_the_fee()
    {
        var tariff = Create(March);

        tariff.Reprice(Money.Of(95m), Money.Of(15m));

        tariff.Price.Amount.Should().Be(95m);
        tariff.DispensingFee.Amount.Should().Be(15m);
        tariff.Total.Amount.Should().Be(110m);
    }
}
