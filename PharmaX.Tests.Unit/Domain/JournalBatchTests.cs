using FluentAssertions;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Primitives;

namespace PharmaX.Tests.Unit.Domain;

/// <summary>
/// The batch, not the line, is the unit of posting, which is what makes
/// "debits equal credits" a rule the system can actually enforce.
/// </summary>
public class JournalBatchTests
{
    private static readonly DateOnly Day = new(2026, 9, 10);
    private static readonly DateTime Now = new(2026, 9, 11, 6, 0, 0,
                                               DateTimeKind.Utc);

    private static JournalBatch Balanced()
    {
        var batch = JournalBatch.Create("JNL-0001", Day, "PointOfSale");
        batch.AddLine("1000", Money.Of(115m), isDebit: true, "Cash takings");
        batch.AddLine("4000", Money.Of(100m), isDebit: false, "Sales");
        batch.AddLine("2200", Money.Of(15m), isDebit: false, "VAT");
        return batch;
    }

    [Fact]
    public void Debits_and_credits_are_totalled_separately()
    {
        var batch = Balanced();

        batch.TotalDebits.Amount.Should().Be(115m);
        batch.TotalCredits.Amount.Should().Be(115m);
        batch.IsBalanced.Should().BeTrue();
    }

    [Fact]
    public void An_unbalanced_batch_is_never_posted()
    {
        var batch = JournalBatch.Create("JNL-0002", Day, "PointOfSale");
        batch.AddLine("1000", Money.Of(115m), isDebit: true);
        batch.AddLine("4000", Money.Of(100m), isDebit: false);

        var post = () => batch.Post(Now);

        post.Should().Throw<DomainException>()
            .WithMessage("*do not equal*");

        batch.IsPosted.Should().BeFalse();
    }

    [Fact]
    public void An_empty_batch_cannot_be_posted()
    {
        var post = () => JournalBatch.Create("JNL-0003", Day, "X").Post(Now);

        post.Should().Throw<DomainException>()
            .WithMessage("*empty batch*");
    }

    [Fact]
    public void A_posted_batch_is_immutable()
    {
        var batch = Balanced();
        batch.Post(Now);

        var add = () => batch.AddLine("1000", Money.Of(1m), isDebit: true);

        add.Should().Throw<DomainException>()
           .WithMessage("*posted batch cannot be changed*");
    }

    [Fact]
    public void A_batch_cannot_be_posted_twice()
    {
        var batch = Balanced();
        batch.Post(Now);

        var again = () => batch.Post(Now);

        again.Should().Throw<DomainException>()
             .WithMessage("*already posted*");
    }

    [Fact]
    public void Posting_stamps_when_it_happened()
    {
        var batch = Balanced();
        batch.Post(Now);

        batch.IsPosted.Should().BeTrue();
        batch.PostedAtUtc.Should().Be(Now);
    }

    [Fact]
    public void Lines_are_numbered_in_the_order_they_are_added()
    {
        Balanced().Lines.Select(l => l.LineNo).Should().Equal(1, 2, 3);
    }

    /// <summary>
    /// Amount is always positive and direction is a flag: a signed
    /// amount plus a direction gives two ways to say the same thing, and
    /// eventually they disagree.
    /// </summary>
    [Fact]
    public void A_negative_amount_is_refused()
    {
        var batch = JournalBatch.Create("JNL-4", Day, "X");

        var add = () => batch.AddLine("1000", Money.Of(-5m), isDebit: true);

        add.Should().Throw<DomainException>()
           .WithMessage("*always positive*");
    }

    [Fact]
    public void A_line_of_zero_posts_nothing_and_is_refused()
    {
        var batch = JournalBatch.Create("JNL-5", Day, "X");

        var add = () => batch.AddLine("1000", Money.Zero(), isDebit: true);

        add.Should().Throw<DomainException>()
           .WithMessage("*posts nothing*");
    }

    [Fact]
    public void A_line_needs_an_account_to_post_to()
    {
        var batch = JournalBatch.Create("JNL-6", Day, "X");

        var add = () => batch.AddLine("  ", Money.Of(5m), isDebit: true);

        add.Should().Throw<DomainException>()
           .WithMessage("*account code*");
    }
}

public class LedgerAccountTests
{
    [Theory]
    [InlineData(LedgerAccountType.Asset, true)]
    [InlineData(LedgerAccountType.Expense, true)]
    [InlineData(LedgerAccountType.Liability, false)]
    [InlineData(LedgerAccountType.Income, false)]
    [InlineData(LedgerAccountType.Equity, false)]
    public void A_debit_increases_assets_and_expenses_and_nothing_else(
        LedgerAccountType type, bool debitPositive)
    {
        LedgerAccount.Create("1000", "Cash", type)
                     .IsDebitPositive.Should().Be(debitPositive);
    }

    [Fact]
    public void An_account_requires_a_code()
    {
        var create = () => LedgerAccount.Create(" ", "Cash",
                                                LedgerAccountType.Asset);

        create.Should().Throw<DomainException>()
              .WithMessage("*requires a code*");
    }
}
