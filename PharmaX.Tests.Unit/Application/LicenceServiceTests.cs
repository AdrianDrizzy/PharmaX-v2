using System.Security.Cryptography;
using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Licensing;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

public sealed class LicenceServiceTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);

    private readonly MemorySettings _settings = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 11, 18, 9, 0, 0, DateTimeKind.Utc));
    private readonly LicenceGate _gate = new();
    private readonly MemoryCache _cache = new();
    private DateOnly Today => _clock.Today;

    private LicenceService Service() =>
        new(_settings, _clock, _gate, _cache, code => LicenceCode.Verify(code, Key));

    private async Task<string> CodeFor(DateOnly until, Guid? pharmacy = null) =>
        LicenceCode.Issue(new LicencePayload(MembershipPlan.Monthly,
            pharmacy ?? await Service().GetPharmacyIdAsync(), until, Today), Key);

    [Fact]
    public async Task First_run_starts_14_day_trial()
    {
        var s = await Service().RefreshAsync();

        s.IsTrial.Should().BeTrue();
        s.ValidUntil.Should().Be(Today.AddDays(14));
        s.Status.Should().Be(LicenceStatus.Active);
    }

    [Fact]
    public async Task Trial_is_not_restarted()
    {
        await Service().RefreshAsync();
        _clock.Advance(TimeSpan.FromDays(30));

        (await Service().RefreshAsync()).Status.Should().Be(LicenceStatus.ReadOnly);
    }

    [Fact]
    public async Task Activating_valid_code_sets_valid_until_and_plan()
    {
        var result = await Service().ActivateAsync(await CodeFor(Today.AddMonths(1)));

        result.Ok.Should().BeTrue();
        result.Message.Should().Be("Membership active until 18 Dec 2026 (Monthly).");
        _gate.Current.ValidUntil.Should().Be(Today.AddMonths(1));
        _gate.Current.Plan.Should().Be(MembershipPlan.Monthly);
        _gate.Current.IsTrial.Should().BeFalse();
    }

    [Fact]
    public async Task Older_code_does_not_shorten_membership()
    {
        await Service().ActivateAsync(await CodeFor(new DateOnly(2026, 12, 31)));
        await Service().ActivateAsync(await CodeFor(new DateOnly(2026, 11, 30)));

        _gate.Current.ValidUntil.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task Code_for_other_pharmacy_is_refused()
    {
        var result = await Service().ActivateAsync(
            await CodeFor(Today.AddMonths(1), Guid.NewGuid()));

        result.Ok.Should().BeFalse();
        result.Message.Should().Be("This code was made for a different pharmacy.");
    }

    [Fact]
    public async Task Expired_code_is_refused() =>
        (await Service().ActivateAsync(await CodeFor(Today.AddDays(-1))))
            .Message.Should().Be("This code has already expired (it ran until 17 Nov 2026).");

    [Fact]
    public async Task Garbage_is_refused() =>
        (await Service().ActivateAsync("hello")).Message.Should().Be(
            "That is not a valid PharmaX code. Paste the whole code exactly as you received it.");

    [Fact]
    public async Task Settings_unreachable_uses_cached_valid_until()
    {
        _cache.Write(Today.AddDays(30), Today);
        _settings.Broken = true;

        var s = await Service().RefreshAsync();

        s.Status.Should().Be(LicenceStatus.Active);
        _gate.Current.ValidUntil.Should().Be(Today.AddDays(30));
    }

    [Fact]
    public async Task Settings_unreachable_without_cache_leaves_gate_alone()
    {
        _settings.Broken = true;

        await Service().RefreshAsync();

        _gate.Current.IsReadOnly.Should().BeFalse();
    }

    [Fact]
    public async Task Refresh_writes_cache()
    {
        await Service().RefreshAsync();

        _cache.Read().Should().Be(((DateOnly?)Today.AddDays(14), (DateOnly?)Today));
    }

    [Fact]
    public async Task Clock_set_back_is_caught()
    {
        await Service().RefreshAsync();
        _clock.Advance(TimeSpan.FromDays(-5));

        (await Service().RefreshAsync()).Status.Should().Be(LicenceStatus.ClockWrong);
    }

    [Fact]
    public async Task Pharmacy_id_is_created_once_and_kept() =>
        (await Service().GetPharmacyIdAsync()).Should()
            .Be(await Service().GetPharmacyIdAsync()).And.NotBe(Guid.Empty);

    [Fact]
    public async Task Edited_end_date_row_is_ignored_when_a_code_is_stored()
    {
        await Service().ActivateAsync(await CodeFor(new DateOnly(2026, 12, 31)));
        await _settings.SetAsync("licence.valid-until", "2099-12-31");

        (await Service().RefreshAsync()).ValidUntil.Should().Be(new DateOnly(2026, 12, 31));
    }

    [Fact]
    public async Task Activation_clears_a_wrong_future_date()
    {
        _clock.Advance(TimeSpan.FromDays(400));   // someone's clock was a year out
        await Service().RefreshAsync();
        _clock.Advance(TimeSpan.FromDays(-400));
        (await Service().RefreshAsync()).Status.Should().Be(LicenceStatus.ClockWrong);

        (await Service().ActivateAsync(await CodeFor(Today.AddMonths(1)))).Ok.Should().BeTrue();

        _gate.Current.Status.Should().Be(LicenceStatus.Active);
    }

    private static readonly Guid MainPc = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private LicenceService On(bool isMain, Guid? pc = null) =>
        new(_settings, _clock, _gate, _cache, code => LicenceCode.Verify(code, Key),
            new LicenceMachine(isMain, pc ?? MainPc));

    private string CodeForMainPc(MembershipPlan plan = MembershipPlan.Monthly) =>
        LicenceCode.Issue(new LicencePayload(plan, MainPc, Today.AddMonths(1), Today), Key);

    [Theory]
    [InlineData(MembershipPlan.Monthly)]
    [InlineData(MembershipPlan.Lifetime)]
    public async Task Code_is_entered_on_the_main_computer_and_the_till_takes_it(MembershipPlan plan)
    {
        (await On(isMain: true).ActivateAsync(CodeForMainPc(plan))).Ok.Should().BeTrue();

        var till = await On(isMain: false, pc: Guid.NewGuid()).RefreshAsync();

        till.IsTrial.Should().BeFalse();
        till.Plan.Should().Be(plan);
    }

    [Fact]
    public async Task A_till_cannot_activate_a_code()
    {
        var till = On(isMain: false, pc: Guid.NewGuid());

        till.CanActivate.Should().BeFalse();
        (await till.ActivateAsync(CodeForMainPc())).Ok.Should().BeFalse();
    }

    [Fact]
    public async Task The_main_computer_shows_its_own_pharmacy_id() =>
        (await On(isMain: true).GetPharmacyIdAsync()).Should().Be(MainPc);

    [Fact]
    public async Task A_code_does_not_follow_the_database_to_another_main_computer()
    {
        await On(isMain: true).ActivateAsync(CodeForMainPc());

        // The same database, now opened by a different main computer.
        var elsewhere = await On(isMain: true, pc: Guid.NewGuid()).RefreshAsync();

        elsewhere.Plan.Should().BeNull();
        (await On(isMain: true, pc: Guid.NewGuid()).ActivateAsync(CodeForMainPc()))
            .Message.Should().Be("This code was made for a different pharmacy.");
    }

    [Fact]
    public async Task A_till_goes_read_only_when_the_main_computer_stops_checking_in()
    {
        await On(isMain: true).ActivateAsync(CodeForMainPc(MembershipPlan.Lifetime));
        var till = On(isMain: false, pc: Guid.NewGuid());

        _clock.Advance(TimeSpan.FromDays(LicenceService.MainAwayDays));
        (await till.RefreshAsync()).Status.Should().Be(LicenceStatus.Active);

        _clock.Advance(TimeSpan.FromDays(1));
        var away = await till.RefreshAsync();
        away.Status.Should().Be(LicenceStatus.MainAway);
        away.IsReadOnly.Should().BeTrue();
        LicenceState.BannerText(away).Should().StartWith("PharmaX is read-only: the main computer");

        // The main computer runs PharmaX again and the till comes back.
        await On(isMain: true).RefreshAsync();
        (await till.RefreshAsync()).Status.Should().Be(LicenceStatus.Active);
    }

    [Fact]
    public async Task A_copied_database_without_its_main_computer_leaves_tills_read_only()
    {
        await On(isMain: true).ActivateAsync(CodeForMainPc());
        await _settings.SetAsync("licence.main-seen", null);

        (await On(isMain: false, pc: Guid.NewGuid()).RefreshAsync())
            .Status.Should().Be(LicenceStatus.MainAway);
    }

    private sealed class MemorySettings : IPharmacySettings
    {
        private readonly Dictionary<string, string?> _values = new();
        public bool Broken { get; set; }

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Broken ? throw new InvalidOperationException("offline")
                   : Task.FromResult(_values.GetValueOrDefault(key));

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            if (Broken) throw new InvalidOperationException("offline");
            _values[key] = value;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryCache : ILicenceCache
    {
        private (DateOnly? ValidUntil, DateOnly? LastSeen)? _value;
        public (DateOnly? ValidUntil, DateOnly? LastSeen)? Read() => _value;
        public void Write(DateOnly? validUntil, DateOnly lastSeen) => _value = (validUntil, lastSeen);
    }
}
