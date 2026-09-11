using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Reporting;
using PharmaX.Domain.Security;

namespace PharmaX.Tests.Integration.Reporting;

/// <summary>
/// Forty-eight incumbent report names reach a handful of definitions, so
/// the two lists have to be kept in step. This is the test that notices
/// when they are not: a preset naming a definition nobody wrote is a
/// menu item that throws in front of a manager.
/// </summary>
public class PresetCatalogueTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public PresetCatalogueTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public void Every_preset_resolves_to_a_registered_definition()
    {
        using var scope = _fixture.NewScope();

        var catalogue = PharmaXFixture.Resolve<IPresetCatalogue>(scope);

        var registered = scope.ServiceProvider
            .GetServices<IReportDefinition>()
            .Select(d => d.Key)
            .ToHashSet(StringComparer.Ordinal);

        var missing = catalogue.All()
            .Select(p => catalogue.Resolve(p.Key))
            .Where(p => !registered.Contains(p.DefinitionKey))
            .Select(p => $"{p.Key} needs {p.DefinitionKey}")
            .ToList();

        missing.Should().BeEmpty();
    }

    [Fact]
    public void Every_preset_demands_a_permission_that_exists()
    {
        using var scope = _fixture.NewScope();
        var catalogue = PharmaXFixture.Resolve<IPresetCatalogue>(scope);

        var offenders = catalogue.All()
            .Where(p => !PermissionCode.All.Contains(p.PermissionCode,
                                                     StringComparer.Ordinal))
            .Select(p => p.Key)
            .ToList();

        offenders.Should().BeEmpty();
    }

    [Fact]
    public void Every_preset_key_is_unique_and_grouped()
    {
        using var scope = _fixture.NewScope();
        var presets = PharmaXFixture.Resolve<IPresetCatalogue>(scope).All();

        presets.Should().NotBeEmpty();
        presets.Select(p => p.Key).Should().OnlyHaveUniqueItems();
        presets.Should().OnlyContain(p => p.Group != "Other");
    }

    [Fact]
    public void An_unknown_preset_key_says_so_rather_than_returning_null()
    {
        using var scope = _fixture.NewScope();
        var catalogue = PharmaXFixture.Resolve<IPresetCatalogue>(scope);

        var act = () => catalogue.Resolve("nope.does.not.exist");

        act.Should().Throw<KeyNotFoundException>()
           .WithMessage("*drifted apart*");
    }

    /// <summary>
    /// A preset that declares a slicer with no definition promises the
    /// operator a filter the screen cannot show them.
    /// </summary>
    [Fact]
    public void Every_slicer_a_preset_declares_reaches_the_screen()
    {
        using var scope = _fixture.NewScope();
        var catalogue = PharmaXFixture.Resolve<IPresetCatalogue>(scope);

        foreach (var preset in catalogue.All())
        {
            var declared = catalogue.Resolve(preset.Key).UserSlicers;
            var offered = catalogue.SlicersFor(preset.Key)
                                   .Select(s => s.Key)
                                   .ToList();

            declared.Should().BeEquivalentTo(offered,
                $"preset {preset.Key} declares a slicer with no definition");
        }
    }
}
