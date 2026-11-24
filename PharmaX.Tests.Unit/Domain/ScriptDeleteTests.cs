using FluentAssertions;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Exceptions;

namespace PharmaX.Tests.Unit.Domain;

/// <summary>
/// A deleted script is gone for good: from any state, and never back.
/// </summary>
public sealed class ScriptDeleteTests
{
    private static readonly DateOnly Today = new(2026, 11, 24);

    [Fact]
    public void A_completed_script_can_be_deleted_and_is_then_never_valid()
    {
        var script = Script.Capture("SC-1", Guid.NewGuid(), "Dr Ncube", Today);
        script.RecordDispensing(Today);
        script.Status.Should().Be(ScriptStatus.Completed);

        script.Delete("Captured twice");

        script.Status.Should().Be(ScriptStatus.Deleted);
        script.IsValidOn(Today).Should().BeFalse();
    }

    [Fact]
    public void A_deleted_script_is_not_cancelled_or_reopened_by_a_reversal()
    {
        var script = Script.Capture("SC-2", Guid.NewGuid(), "Dr Ncube", Today, repeatsAuthorised: 2);
        script.RecordDispensing(Today);
        script.Delete("Wrong patient");

        script.Invoking(s => s.Cancel("late")).Should().Throw<DomainException>();

        script.UndoDispensing();
        script.Status.Should().Be(ScriptStatus.Deleted);
    }

    [Fact]
    public void Deleting_needs_a_reason() =>
        Script.Capture("SC-3", Guid.NewGuid(), "Dr Ncube", Today)
            .Invoking(s => s.Delete(" "))
            .Should().Throw<DomainException>();
}
