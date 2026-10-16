using FluentAssertions;
using PharmaX.Application.Abstractions;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Tests.Unit.Adapters;

/// The logo and colours the admin sets are the pharmacy's: every till and
/// every user gets them.
public sealed class SharedBrandingTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("phx-brand").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Store : IPharmacySettings, IWorkstationSettings
    {
        public Dictionary<string, string?> Values { get; } = new();

        public Task<string?> GetAsync(string key, CancellationToken ct = default) =>
            Task.FromResult(Values.GetValueOrDefault(key));

        public Task SetAsync(string key, string? value, CancellationToken ct = default)
        {
            Values[key] = value;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task A_till_that_had_its_own_logo_shares_it_with_every_other_till()
    {
        var file = Path.Combine(_dir, "logo.png");
        using (var bitmap = new Bitmap(20, 10)) bitmap.Save(file);

        var pharmacy = new Store();
        var first = new Store { Values = { [WorkstationSettingKey.LogoPath] = file, [WorkstationSettingKey.Palette] = "teal-amber" } };

        using var firstTill = new Branding { CacheFolder = Path.Combine(_dir, "a") };
        (await firstTill.RefreshAsync(pharmacy, first)).Should().Be("teal-amber");
        pharmacy.Values[PharmacySettingKey.LogoImage].Should().NotBeNullOrEmpty();

        // Another till, never set up: the pharmacy's logo and colours.
        using var otherTill = new Branding { CacheFolder = Path.Combine(_dir, "b") };
        (await otherTill.RefreshAsync(pharmacy, new Store())).Should().Be("teal-amber");
        otherTill.HasLogo.Should().BeTrue();
        otherTill.Logo!.Width.Should().Be(20);

        // Removed by the admin: gone everywhere, and not handed back by a
        // till that still remembers its old one.
        pharmacy.Values[PharmacySettingKey.LogoImage] = "";
        await firstTill.RefreshAsync(pharmacy, first);
        firstTill.HasLogo.Should().BeFalse();
        pharmacy.Values[PharmacySettingKey.LogoImage].Should().BeEmpty();
    }

    [Fact]
    public async Task The_logo_chosen_at_setup_becomes_the_pharmacys_until_it_is_removed()
    {
        var file = Path.Combine(_dir, "chosen.png");
        using (var bitmap = new Bitmap(30, 12)) bitmap.Save(file);

        var pharmacy = new Store();

        using var till = new Branding { CacheFolder = Path.Combine(_dir, "c") };
        await till.RefreshAsync(pharmacy, new Store(), installedLogo: file);

        till.Logo!.Width.Should().Be(30);
        pharmacy.Values[PharmacySettingKey.LogoImage].Should().NotBeNullOrEmpty();

        pharmacy.Values[PharmacySettingKey.LogoImage] = "";
        await till.RefreshAsync(pharmacy, new Store(), installedLogo: file);
        till.HasLogo.Should().BeFalse();
    }
}
