using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Security;
using PharmaX.Domain.Finance;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Tills;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// A cash-up that does not balance says why, and above five dollars a
/// supervisor puts their name to it. The session closed at $0 with $20
/// expected and no reason is the case these exist for.
/// </summary>
public sealed class CashUpRulesTests
{
    private static Dictionary<string, int> Cash => new() { ["$1"] = 144 };

    private static Dictionary<TenderType, decimal> EcoCash(decimal declared) =>
        new() { [TenderType.EcoCash] = declared };

    [Fact]
    public async Task Any_difference_without_a_reason_is_refused()
    {
        var (_, _, cashUp) = await EcoCashTests.TradedSessionAsync();
        await cashUp.DeclareAsync(Cash, EcoCash(23.50m));

        (await cashUp.CloseAsync(null)).Error.Code.Should().Be(CashUpService.NeedsReason);
    }

    [Fact]
    public async Task A_difference_over_five_dollars_needs_a_supervisor()
    {
        var (_, _, cashUp) = await EcoCashTests.TradedSessionAsync();
        await cashUp.DeclareAsync(Cash, EcoCash(18.99m));

        (await cashUp.CloseAsync("Wallet short")).Error.Code
            .Should().Be(CashUpService.NeedsSupervisor);
    }

    [Fact]
    public async Task A_supervisor_closes_it_and_is_named_on_the_record()
    {
        var (builder, _, cashUp) = await EcoCashTests.TradedSessionAsync();
        await cashUp.DeclareAsync(Cash, EcoCash(18.99m));
        var supervisor = Guid.NewGuid();

        var closed = await cashUp.CloseAsync("Wallet short", authorisedBy: supervisor);

        closed.IsSuccess.Should().BeTrue(closed.Error.Message);
        builder.Audit.Entries.Should().Contain(e => e.Action == "pos.session.close"
            && e.Reason!.Contains(supervisor.ToString(),
            StringComparison.Ordinal));
    }

    [Fact]
    public async Task Exactly_five_dollars_needs_no_supervisor()
    {
        var (_, _, cashUp) = await EcoCashTests.TradedSessionAsync();
        await cashUp.DeclareAsync(Cash, EcoCash(19m));

        (await cashUp.CloseAsync("Wallet short")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Nobody_authorises_their_own_shortfall()
    {
        var (builder, _, cashUp) = await EcoCashTests.TradedSessionAsync();
        await cashUp.DeclareAsync(Cash, EcoCash(18.99m));

        (await cashUp.CloseAsync("Wallet short", authorisedBy: builder.User.Id))
            .Error.Code.Should().Be(CashUpService.NeedsSupervisor);
    }

    [Fact]
    public void A_session_closed_three_minutes_after_opening_is_short()
    {
        var opened = new DateTime(2026, 11, 21, 8, 0, 0, DateTimeKind.Utc);
        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1",
            Guid.NewGuid(), Money.Of(60m), opened);

        CashUpService.IsShortSession(session, opened.AddMinutes(3)).Should().BeTrue();
        CashUpService.IsShortSession(session, opened.AddMinutes(6)).Should().BeFalse();
    }
}
