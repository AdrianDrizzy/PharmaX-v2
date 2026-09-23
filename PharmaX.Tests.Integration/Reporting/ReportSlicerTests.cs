using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Reporting;
using PharmaX.Application.Security;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Reporting;

/// <summary>
/// The slicers whose choices are rows in this pharmacy's database rather
/// than a fixed list, and the filters behind them.
/// </summary>
/// <remarks>
/// The catalogue declares these with no options, because a static table
/// cannot know what departments a pharmacy has. The screen read that
/// emptiness as "let them type it", so filtering by department meant
/// typing a name exactly and filtering by operator or till session meant
/// typing a GUID. And the department filter was worse than unusable: no
/// report read the value, so whatever was typed changed nothing.
/// </remarks>
public class ReportSlicerTests : IClassFixture<PharmaXFixture>
{
    private const string InDepartment = "RSL-IN";
    private const string OutOfDepartment = "RSL-OUT";

    private readonly PharmaXFixture _fixture;

    public ReportSlicerTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task Departments_are_offered_by_name_and_chosen_by_id()
    {
        var departmentId = await GivenADepartmentAsync("RSL", "Rosella ward");

        using var scope = _fixture.NewScope();
        var lookups = PharmaXFixture.Resolve<IReportLookups>(scope);

        var options = await lookups.OptionsForAsync("department");

        var chosen = options.Should()
            .ContainSingle(o => o.Label == "Rosella ward").Subject;

        chosen.Value.Should().Be(departmentId.ToString(),
            "the report filters on the id, whatever the name reads");
    }

    [Fact]
    public async Task Staff_are_offered_by_display_name()
    {
        using var scope = _fixture.NewScope();
        var lookups = PharmaXFixture.Resolve<IReportLookups>(scope);

        var operators = await lookups.OptionsForAsync("operator");

        operators.Should().NotBeEmpty("the seeded administrator is active");
        operators.Should().OnlyContain(o => !string.IsNullOrWhiteSpace(o.Label));
        operators.All(o => Guid.TryParse(o.Value, out _))
                 .Should().BeTrue("the report filters on the id");
    }

    /// <remarks>
    /// Matched through the aggregate rather than on the stored text,
    /// because "Pharmacist" is a prefix of "Pharmacist Assistant" and a
    /// text match would offer the wrong staff.
    /// </remarks>
    [Fact]
    public async Task The_pharmacist_list_is_not_everybody()
    {
        using var scope = _fixture.NewScope();
        var lookups = PharmaXFixture.Resolve<IReportLookups>(scope);

        var everyone = await lookups.OptionsForAsync("operator");
        var pharmacists = await lookups.OptionsForAsync("pharmacist");

        pharmacists.Count.Should().BeLessOrEqualTo(everyone.Count);

        // The seeded administrator holds every permission but is not a
        // pharmacist, so it must not be offered as one.
        pharmacists.Should().NotContain(p =>
            everyone.Any(e => e.Value == p.Value && e.Label == "Administrator"));
    }

    [Fact]
    public async Task A_slicer_with_nothing_behind_it_offers_nothing()
    {
        using var scope = _fixture.NewScope();
        var lookups = PharmaXFixture.Resolve<IReportLookups>(scope);

        // Free-text and fixed-list slicers alike: the screen leaves them
        // as they were rather than showing an empty dropdown.
        (await lookups.OptionsForAsync("reason")).Should().BeEmpty();
        (await lookups.OptionsForAsync("tender")).Should().BeEmpty();
    }

    /// <remarks>
    /// The half that makes the dropdown worth having. Offering a choice
    /// that no report reads is a control that lies about what it does.
    /// </remarks>
    [Theory]
    [InlineData("inv.stock.on-hand")]
    [InlineData("inv.stock.by-batch")]
    public async Task Choosing_a_department_actually_narrows_the_report(
        string preset)
    {
        var departmentId = await GivenADepartmentAsync("RSL", "Rosella ward");
        var categoryId = await GivenACategoryAsync("RSLC", "Rosella lines");

        await GivenStockAsync(departmentId, categoryId);

        var all = await RunAsync(preset, null);
        var filtered = await RunAsync(preset, departmentId);

        Codes(all).Should().Contain(InDepartment).And.Contain(OutOfDepartment);

        Codes(filtered).Should().Contain(InDepartment);
        Codes(filtered).Should().NotContain(OutOfDepartment,
            "it is filed under no department");

        filtered.RowCount.Should().BeLessThan(all.RowCount);
    }

    /// <remarks>
    /// The axis a department is not. A product is filed under both, and
    /// each has to narrow the report on its own.
    /// </remarks>
    [Theory]
    [InlineData("inv.stock.on-hand")]
    [InlineData("inv.stock.by-batch")]
    public async Task Choosing_a_category_narrows_the_report_too(string preset)
    {
        var departmentId = await GivenADepartmentAsync("RSL", "Rosella ward");
        var categoryId = await GivenACategoryAsync("RSLC", "Rosella lines");

        await GivenStockAsync(departmentId, categoryId);

        var all = await RunAsync(preset, null);
        var filtered = await RunAsync(preset, null, categoryId);

        Codes(filtered).Should().Contain(InDepartment);
        Codes(filtered).Should().NotContain(OutOfDepartment);
        filtered.RowCount.Should().BeLessThan(all.RowCount);
    }

    [Fact]
    public async Task Categories_are_offered_by_name_and_chosen_by_id()
    {
        var categoryId = await GivenACategoryAsync("RSLC", "Rosella lines");

        using var scope = _fixture.NewScope();
        var lookups = PharmaXFixture.Resolve<IReportLookups>(scope);

        var options = await lookups.OptionsForAsync("category");

        options.Should().ContainSingle(o => o.Label == "Rosella lines")
               .Which.Value.Should().Be(categoryId.ToString());
    }

    // ---- Steps -----------------------------------------------------------

    private static IEnumerable<string?> Codes(ReportResult result) =>
        result.Rows.Select(r => r["Code"] as string);

    private async Task<ReportResult> RunAsync(string preset, Guid? department,
        Guid? category = null)
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var runner = PharmaXFixture.Resolve<IReportRunner>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var slicers = new Dictionary<string, string?>();

        if (department is Guid id) slicers["department"] = id.ToString();
        if (category is Guid c) slicers["category"] = c.ToString();

        return await runner.RunAsync(new ReportRequest(
            PresetKey: preset,
            BranchCode: "1",
            From: clock.Today.AddMonths(-1),
            To: clock.Today,
            Slicers: slicers));
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    private async Task<Guid> GivenACategoryAsync(string code, string name)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Categories.FirstOrDefaultAsync(c => c.Code == code)
            is { } existing)
            return existing.Id;

        var category = Category.Create(code, name);
        db.Categories.Add(category);
        await db.SaveChangesAsync();

        return category.Id;
    }

    private async Task<Guid> GivenADepartmentAsync(string code, string name)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Departments.FirstOrDefaultAsync(d => d.Code == code)
            is { } existing)
            return existing.Id;

        var department = Department.Create(code, name);
        db.Departments.Add(department);
        await db.SaveChangesAsync();

        return department.Id;
    }

    private async Task GivenStockAsync(Guid departmentId, Guid categoryId)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == InDepartment)) return;

        var inside = Product.Create(InDepartment, "RSL filed item",
            Money.Of(20m));

        inside.Classify(departmentId, categoryId, null);
        inside.SetQuantityOnHand(10m);

        var outside = Product.Create(OutOfDepartment, "RSL unfiled item",
            Money.Of(20m));

        outside.SetQuantityOnHand(10m);

        db.Products.AddRange(inside, outside);
        await db.SaveChangesAsync();
    }
}
