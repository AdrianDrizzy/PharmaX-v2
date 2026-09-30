using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Every maintained list, through the screen's own service: create it,
/// find it, rename it, retire it, bring it back.
/// </summary>
/// <remarks>
/// Driven from the enum rather than from a list written out here, so a
/// kind added later is covered the day it is added. That is not a
/// hypothetical: adding Category needed a case in four switches across
/// two layers, and the one that was missed - the factory in
/// ReferenceDataService - threw ArgumentOutOfRangeException at the
/// moment somebody tried to create one. Nothing failed to compile,
/// because the switch ended in a throwing default.
/// </remarks>
public class ReferenceCrudTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public ReferenceCrudTests(PharmaXFixture fixture) => _fixture = fixture;

    public static TheoryData<ReferenceKind> Kinds()
    {
        var data = new TheoryData<ReferenceKind>();
        foreach (var kind in Enum.GetValues<ReferenceKind>()) data.Add(kind);
        return data;
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public async Task Every_kind_can_be_created_renamed_and_retired(
        ReferenceKind kind)
    {
        await SignInAsync();

        var code = $"CRUD{(int)kind}";
        Guid id;

        using (var scope = _fixture.NewScope())
        {
            var reference = PharmaXFixture.Resolve<IReferenceDataService>(scope);

            var created = await reference.CreateAsync(kind,
                new ReferenceEditDto(code, "Created by a test", "Detail"));

            created.IsSuccess.Should().BeTrue(created.Error.Message);
            id = created.Value;
        }

        using (var scope = _fixture.NewScope())
        {
            var reference = PharmaXFixture.Resolve<IReferenceDataService>(scope);

            var listed = await reference.ListAsync(kind);
            listed.IsSuccess.Should().BeTrue(listed.Error.Message);

            listed.Value.Should().Contain(i => i.Id == id,
                "what was created has to come back on the list");

            var renamed = await reference.UpdateAsync(kind, id,
                new ReferenceEditDto(code, "Renamed by a test", null));

            renamed.IsSuccess.Should().BeTrue(renamed.Error.Message);
        }

        using (var scope = _fixture.NewScope())
        {
            var reference = PharmaXFixture.Resolve<IReferenceDataService>(scope);

            var fetched = await reference.GetAsync(kind, id);
            fetched.IsSuccess.Should().BeTrue(fetched.Error.Message);
            fetched.Value.Name.Should().Be("Renamed by a test");
            fetched.Value.Detail.Should().BeNull("it was cleared");

            // Retired rather than deleted, so history still reads.
            var retired = await reference.SetActiveAsync(kind, id, false);
            retired.IsSuccess.Should().BeTrue(retired.Error.Message);
        }

        using var check = _fixture.NewScope();
        var service = PharmaXFixture.Resolve<IReferenceDataService>(check);

        (await service.ListAsync(kind, includeInactive: false))
            .Value.Should().NotContain(i => i.Id == id);

        (await service.ListAsync(kind, includeInactive: true))
            .Value.Should().Contain(i => i.Id == id);
    }

    /// <remarks>
    /// The list screen keeps one scope open while the edit dialog saves in
    /// another. The list kept showing the old name after Refresh, and
    /// retiring the entry straight after the edit failed as a concurrency
    /// conflict, because the list's context still held the row version
    /// from before (ADM-DEF-01).
    /// </remarks>
    [Fact]
    public async Task A_list_left_open_sees_an_edit_and_can_retire_after_it()
    {
        await SignInAsync();

        Guid id;

        using (var scope = _fixture.NewScope())
        {
            var created = await PharmaXFixture
                .Resolve<IReferenceDataService>(scope)
                .CreateAsync(ReferenceKind.Branch,
                    new ReferenceEditDto("STALE1", "Luveve Road", null));

            created.IsSuccess.Should().BeTrue(created.Error.Message);
            id = created.Value;
        }

        using var list = _fixture.NewScope();
        var screen = PharmaXFixture.Resolve<IReferenceDataService>(list);

        // The list screen opens, and the operator selects the row.
        (await screen.ListAsync(ReferenceKind.Branch)).IsSuccess.Should().BeTrue();
        (await screen.GetAsync(ReferenceKind.Branch, id)).IsSuccess.Should().BeTrue();

        // The edit dialog saves in a scope of its own.
        using (var dialog = _fixture.NewScope())
        {
            var renamed = await PharmaXFixture
                .Resolve<IReferenceDataService>(dialog)
                .UpdateAsync(ReferenceKind.Branch, id,
                    new ReferenceEditDto("STALE1", "Luveve Road Branch", null));

            renamed.IsSuccess.Should().BeTrue(renamed.Error.Message);
        }

        var refreshed = await screen.ListAsync(ReferenceKind.Branch);

        refreshed.Value.Single(i => i.Id == id).Name
            .Should().Be("Luveve Road Branch", "Refresh shows what was saved");

        var retired = await screen.SetActiveAsync(ReferenceKind.Branch, id,
                                                  false);

        retired.IsSuccess.Should().BeTrue(retired.Error.Message);
    }

    /// <remarks>
    /// A duplicate code is the one thing these screens have to refuse:
    /// the code is what a product is filed against.
    /// </remarks>
    [Fact]
    public async Task A_code_cannot_be_used_twice_in_one_list()
    {
        await SignInAsync();

        using var scope = _fixture.NewScope();
        var reference = PharmaXFixture.Resolve<IReferenceDataService>(scope);

        var first = await reference.CreateAsync(ReferenceKind.Category,
            new ReferenceEditDto("DUPE", "First", null));

        first.IsSuccess.Should().BeTrue(first.Error.Message);

        var second = await reference.CreateAsync(ReferenceKind.Category,
            new ReferenceEditDto("dupe", "Second", null));

        second.IsFailure.Should().BeTrue("codes are compared case-insensitively");
    }

    /// <summary>
    /// Who may maintain the lists.
    /// </summary>
    /// <remarks>
    /// These screens used to demand inv.price.change, so a stock
    /// controller - whose whole job is filing stock - could not add a
    /// department to file it in, and anyone who could had been handed the
    /// shelf prices as well. The two are separate authorities now.
    /// </remarks>
    [Theory]
    [InlineData(Role.Standard.Administrator, true)]
    [InlineData(Role.Standard.Manager, true)]
    [InlineData(Role.Standard.StockController, true)]
    [InlineData(Role.Standard.Cashier, false)]
    [InlineData(Role.Standard.Pharmacist, false)]
    public async Task The_right_roles_may_maintain_the_lists(string roleName,
        bool expected)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var role = await db.Roles.AsNoTracking()
            .SingleAsync(r => r.Name == roleName);

        role.Has(PermissionCode.ReferenceMaintain).Should().Be(expected);
    }

    [Fact]
    public async Task Maintaining_a_list_no_longer_carries_the_price_permission()
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var stock = await db.Roles.AsNoTracking()
            .SingleAsync(r => r.Name == Role.Standard.StockController);

        stock.Has(PermissionCode.ReferenceMaintain).Should().BeTrue();

        stock.Has(PermissionCode.PriceChange).Should().BeFalse(
            "filing stock is not repricing it");
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }
}
