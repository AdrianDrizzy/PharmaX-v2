using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Pharmacy;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The trading name, registered name, VAT number and address edited at
/// the System Console, the rule that swaps "PharmaX" for the trading name in
/// window titles, and the heading the sign-in and home screens show.
/// </summary>
public class PharmacyDetailsTests
{
    private static PharmaXOptions Installed() => new()
    {
        Pharmacy = new PharmacyOptions
        {
            TradingName = "PharmaX",
            RegisteredName = "PharmaX (Pty) Ltd",
            VatNumber = "4000000000",
            Address = "1 Example Street"
        }
    };

    private sealed class InMemoryPharmacySettings : IPharmacySettings
    {
        public Dictionary<string, string?> Values { get; } = new();

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(Values.TryGetValue(key, out var v) ? v : null);

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
    }

    private readonly InMemoryPharmacySettings _settings = new();
    private readonly IPermissionService _permissions =
        Substitute.For<IPermissionService>();
    private readonly FakeAuditWriter _audit = new();
    private readonly PharmaXOptions _options = Installed();
    private readonly PharmacyProfile _profile;

    public PharmacyDetailsTests() => _profile = new PharmacyProfile(_options);

    private PharmacyDetailsService CreateSut() =>
        new(_settings, _profile, _options, _permissions, _audit);

    // ---- titles -----------------------------------------------------------

    [Theory]
    [InlineData("PharmaX - Sign in", "Riverside Pharmacy - Sign in")]
    [InlineData("PharmaX", "Riverside Pharmacy")]
    [InlineData("PharmaX  -  TEST DATA (seeded accounts)",
                "Riverside Pharmacy  -  TEST DATA (seeded accounts)")]
    public void Titles_starting_with_the_product_name_carry_the_trading_name(
        string title, string expected)
    {
        _profile.Use(new PharmacyDetails("Riverside Pharmacy", "", "", ""));

        _profile.Brand(title).Should().Be(expected);
    }

    [Theory]
    [InlineData("Quantity")]
    [InlineData("Reports for PharmaX")]
    [InlineData("PharmaXtra - Reports")]
    public void Other_titles_are_left_alone(string title)
    {
        _profile.Use(new PharmacyDetails("Riverside Pharmacy", "", "", ""));

        _profile.Brand(title).Should().Be(title);
    }

    [Fact]
    public void A_saved_trading_name_titles_every_window_without_a_switch()
    {
        // The switch that once had to be found at the console first is
        // gone: saving the name is what re-titles the windows.
        _profile.Use(new PharmacyDetails("BethRapha Pharmacy", "", "", ""));

        _profile.Brand("PharmaX - Point of Sale")
            .Should().Be("BethRapha Pharmacy - Point of Sale");
    }

    [Fact]
    public void A_blank_trading_name_leaves_the_title_as_written()
    {
        _profile.Use(new PharmacyDetails("  ", "", "", ""));

        _profile.Brand("PharmaX - Sign in").Should().Be("PharmaX - Sign in");
    }

    [Fact]
    public void A_new_installation_looks_like_the_product()
    {
        new PharmacyProfile(_options).Brand("PharmaX - Point of Sale")
            .Should().Be("PharmaX - Point of Sale");
    }

    // ---- load -------------------------------------------------------------

    [Fact]
    public async Task Nothing_stored_falls_back_to_the_installed_file()
    {
        var details = await CreateSut().LoadAsync();

        details.Should().Be(new PharmacyDetails(
            "PharmaX", "PharmaX (Pty) Ltd", "4000000000", "1 Example Street"));
    }

    [Fact]
    public async Task Stored_values_win_including_a_deliberately_blank_address()
    {
        _settings.Values[PharmacySettingKey.TradingName] = "Riverside Pharmacy";
        _settings.Values[PharmacySettingKey.RegisteredName] =
            "Riverside Health (Pvt) Ltd";
        _settings.Values[PharmacySettingKey.VatNumber] = "4123456789";
        _settings.Values[PharmacySettingKey.Address] = "";

        var details = await CreateSut().LoadAsync();

        details.Should().Be(new PharmacyDetails(
            "Riverside Pharmacy", "Riverside Health (Pvt) Ltd",
            "4123456789", ""));
    }

    [Fact]
    public async Task A_registered_name_cleared_on_purpose_stays_cleared()
    {
        // A sole trader has no company name, and the installed
        // "PharmaX (Pty) Ltd" must not creep back onto the receipts.
        _settings.Values[PharmacySettingKey.RegisteredName] = "";

        var details = await CreateSut().LoadAsync();

        details.RegisteredName.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pharmacy_with_only_a_registered_name_is_known_by_it()
    {
        _settings.Values[PharmacySettingKey.RegisteredName] = "BethRapha Pharmacy (Pvt) Ltd";

        var details = await CreateSut().LoadAsync();

        details.TradingName.Should().Be("BethRapha Pharmacy (Pvt) Ltd");
    }

    // ---- save -------------------------------------------------------------

    [Fact]
    public async Task Saving_stores_applies_and_audits_the_change()
    {
        var result = await CreateSut().SaveAsync(new PharmacyDetails(
            "  Riverside Pharmacy ", " Riverside Health (Pvt) Ltd ",
            "4123456789", "Shop 4, Riverside Mall\n\n  Johannesburg  "));

        result.IsSuccess.Should().BeTrue();

        _settings.Values[PharmacySettingKey.TradingName]
            .Should().Be("Riverside Pharmacy");
        _settings.Values[PharmacySettingKey.RegisteredName]
            .Should().Be("Riverside Health (Pvt) Ltd");
        _settings.Values[PharmacySettingKey.Address]
            .Should().Be("Shop 4, Riverside Mall" + Environment.NewLine
                         + "Johannesburg");

        _profile.TradingName.Should().Be("Riverside Pharmacy");
        _profile.RegisteredName.Should().Be("Riverside Health (Pvt) Ltd");
        _profile.VatNumber.Should().Be("4123456789");
        _profile.Brand("PharmaX - Sign in")
            .Should().Be("Riverside Pharmacy - Sign in");

        _audit.Recorded("adm.pharmacy.details", "Applied").Should().BeTrue();
        _audit.Entries.Single().Reason.Should()
            .Contain("'PharmaX' -> 'Riverside Pharmacy'")
            .And.Contain("registered name: 'PharmaX (Pty) Ltd' -> "
                         + "'Riverside Health (Pvt) Ltd'")
            .And.Contain("'4000000000' -> '4123456789'");
        _permissions.Received().Demand(PermissionCode.SystemConsole);
    }

    [Fact]
    public async Task Saving_tells_open_windows_to_re_title()
    {
        var raised = 0;
        _profile.Changed += (_, _) => raised++;

        await CreateSut().SaveAsync(
            new PharmacyDetails("Riverside Pharmacy", "", "", ""));

        raised.Should().Be(1);
    }

    [Theory]
    [InlineData("", "", "4123456789", "PHX-001")]
    [InlineData("Riverside Pharmacy", "", "4123/456", "PHX-004")]
    [InlineData("Riverside Pharmacy", "long", "", "PHX-006")]
    public async Task Invalid_details_are_refused_and_nothing_changes(
        string name, string registered, string vat, string code)
    {
        if (registered == "long")
            registered = new string('R', PharmacyDetailsService.MaxRegisteredName + 1);

        var result = await CreateSut().SaveAsync(
            new PharmacyDetails(name, registered, vat, ""));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be(code);
        _settings.Values.Should().BeEmpty();
        _profile.TradingName.Should().Be("PharmaX");
        _audit.Entries.Should().BeEmpty();
    }

    [Fact]
    public async Task A_user_without_the_settings_permission_cannot_save()
    {
        _permissions.When(p => p.Demand(PermissionCode.SystemConsole))
            .Do(_ => throw new PermissionDeniedException(
                "You do not have permission for this action."));

        var act = () => CreateSut().SaveAsync(
            new PharmacyDetails("Riverside Pharmacy", "", "", ""));

        await act.Should().ThrowAsync<PermissionDeniedException>();
        _settings.Values.Should().BeEmpty();
        _profile.TradingName.Should().Be("PharmaX");
    }

    // ---- heading ----------------------------------------------------------

    [Theory]
    [InlineData("BethRapha Pharmacy", "BethRapha Pharmacy")]
    [InlineData("  BethRapha Pharmacy ", "BethRapha Pharmacy")]
    [InlineData("PharmaX", "Sign in")]
    [InlineData("pharmax", "Sign in")]
    [InlineData("", "Sign in")]
    public void The_sign_in_heading_never_shows_the_product_name(
        string tradingName, string expected)
    {
        var details = new PharmacyDetails(tradingName, "", "", "");

        Desktop.Mvp.PharmacyHeading.Name(details, "Sign in")
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("Shop 4|Riverside Mall", "4123456789",
                "Shop 4, Riverside Mall   -   VAT 4123456789")]
    [InlineData("Shop 4", "", "Shop 4")]
    [InlineData("", "4123456789", "VAT 4123456789")]
    [InlineData("", "", "")]
    public void The_particulars_are_the_address_on_one_line_and_the_vat_number(
        string address, string vat, string expected)
    {
        // Stored one line per address line, as the service saves it.
        var details = new PharmacyDetails("BethRapha Pharmacy", "", vat,
            address.Replace("|", Environment.NewLine, StringComparison.Ordinal));

        Desktop.Mvp.PharmacyHeading.Particulars(details).Should().Be(expected);
    }
}
