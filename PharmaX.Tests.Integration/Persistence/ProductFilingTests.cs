using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Filing a product under a department, a category and a manufacturer,
/// through the same service the product editor uses.
/// </summary>
/// <remarks>
/// None of this could be set from anywhere before: ProductEditDto
/// carried no filing at all, so the three lists existed, the reports
/// offered to filter on them, and nothing in the application could put a
/// product into one. The department a product was filed under could only
/// ever have been written by a seeder.
/// </remarks>
public class ProductFilingTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ProductFilingTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_product_is_created_filed_and_reads_back_filed()
    {
        await SignInAsync();

        var department = await GivenAsync(ReferenceKind.Department, "PF-D",
            "Filing department");

        var category = await GivenAsync(ReferenceKind.Category, "PF-C",
            "Filing category");

        var manufacturer = await GivenAsync(ReferenceKind.Manufacturer,
            "PF-M", "Filing manufacturer");

        Guid id;

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            var created = await products.CreateAsync(
                Edit("PF-NEW", department, category, manufacturer));

            created.IsSuccess.Should().BeTrue(created.Error.Message);
            id = created.Value;
        }

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);
            var fetched = await products.GetAsync(id);

            fetched.IsSuccess.Should().BeTrue(fetched.Error.Message);
            fetched.Value.DepartmentId.Should().Be(department);
            fetched.Value.CategoryId.Should().Be(category);
            fetched.Value.ManufacturerId.Should().Be(manufacturer);
        }
    }

    /// <remarks>
    /// Editing a product must be able to move it, and to take it out of
    /// a list altogether - a product filed in the wrong category is a
    /// product somebody has to be able to unfile.
    /// </remarks>
    [Fact]
    public async Task An_edit_can_refile_a_product_and_can_unfile_it()
    {
        await SignInAsync();

        var first = await GivenAsync(ReferenceKind.Category, "PF-C1",
            "First category");

        var second = await GivenAsync(ReferenceKind.Category, "PF-C2",
            "Second category");

        Guid id;

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);
            var created = await products.CreateAsync(
                Edit("PF-MOVE", null, first, null));

            created.IsSuccess.Should().BeTrue(created.Error.Message);
            id = created.Value;
        }

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            var moved = await products.UpdateAsync(id,
                Edit("PF-MOVE", null, second, null));

            moved.IsSuccess.Should().BeTrue(moved.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);
            (await products.GetAsync(id)).Value.CategoryId
                .Should().Be(second);
        }

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            var cleared = await products.UpdateAsync(id,
                Edit("PF-MOVE", null, null, null));

            cleared.IsSuccess.Should().BeTrue(cleared.Error.Message);
        }

        using var check = _fixture.NewScope();
        var service = PharmaXFixture.Resolve<IProductService>(check);

        (await service.GetAsync(id)).Value.CategoryId.Should().BeNull(
            "null clears the filing rather than being ignored");
    }

    /// <remarks>
    /// A category is not a renamed department. A product carries both,
    /// and moving it between departments must not disturb its category.
    /// </remarks>
    [Fact]
    public async Task Department_and_category_are_independent_of_each_other()
    {
        await SignInAsync();

        var departmentA = await GivenAsync(ReferenceKind.Department, "PF-DA",
            "Department A");

        var departmentB = await GivenAsync(ReferenceKind.Department, "PF-DB",
            "Department B");

        var category = await GivenAsync(ReferenceKind.Category, "PF-CI",
            "Independent category");

        Guid id;

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);
            var created = await products.CreateAsync(
                Edit("PF-BOTH", departmentA, category, null));

            id = created.Value;
        }

        using (var scope = _fixture.NewScope())
        {
            var products = PharmaXFixture.Resolve<IProductService>(scope);

            await products.UpdateAsync(id,
                Edit("PF-BOTH", departmentB, category, null));
        }

        using var check = _fixture.NewScope();
        var service = PharmaXFixture.Resolve<IProductService>(check);
        var fetched = await service.GetAsync(id);

        fetched.Value.DepartmentId.Should().Be(departmentB);
        fetched.Value.CategoryId.Should().Be(category,
            "the category did not move with the department");
    }

    // ---- Steps -----------------------------------------------------------

    private static ProductEditDto Edit(string code, Guid? department,
        Guid? category, Guid? manufacturer) =>
        new(Code: code,
            Description: $"{code} description",
            GenericName: null,
            RetailPrice: 25m,
            CostPrice: 15m,
            VatRatePercent: 15m,
            TaxCode: "A",
            Schedule: 0,
            IsBatchTracked: false,
            ReorderLevel: 5m,
            PackSize: null,
            DepartmentId: department,
            CategoryId: category,
            ManufacturerId: manufacturer);

    private async Task<Guid> GivenAsync(ReferenceKind kind, string code,
        string name)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var reference = PharmaXFixture.Resolve<IReferenceDataService>(scope);

        var existing = await reference.ListAsync(kind, includeInactive: true);

        if (existing.IsSuccess
            && existing.Value.FirstOrDefault(i =>
                   string.Equals(i.Code, code, StringComparison.OrdinalIgnoreCase))
               is { } found)
            return found.Id;

        var created = await reference.CreateAsync(kind,
            new ReferenceEditDto(code, name, null));

        created.IsSuccess.Should().BeTrue(created.Error.Message);
        return created.Value;
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
