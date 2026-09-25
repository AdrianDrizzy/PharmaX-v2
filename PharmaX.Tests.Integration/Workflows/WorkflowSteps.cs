using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Workflows;

/// <summary>
/// The steps the role workflow tests are written in: a user in a standard
/// role, signed in; a product with batches; a patient; a script. Each one
/// goes through the real container against the real database, so a test
/// that passes has walked the path a person at a till walks.
/// </summary>
public sealed class WorkflowSteps
{
    public const string Password = "Workflow#1";

    private readonly PharmaXFixture _fixture;

    public WorkflowSteps(PharmaXFixture fixture) => _fixture = fixture;

    /// <summary>
    /// A user holding exactly one standard role, created once per fixture.
    /// </summary>
    public async Task<Guid> UserAsync(string username, string role)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var existing = await db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Username == username);

        if (existing is not null) return existing.Id;

        var hasher = PharmaXFixture.Resolve<IPasswordHasher>(scope);

        var user = User.Create(username, $"Test {role}",
            hasher.Hash(Password), mustChangePassword: false);

        user.AssignRole(role);
        user.SetDiscountLimit(10m);

        db.Users.Add(user);
        await db.SaveChangesAsync();

        return user.Id;
    }

    /// Signs in as the user, with the till's open session attached.
    public async Task<Guid> SignInAsync(string username, string role)
    {
        var id = await UserAsync(username, role);

        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync(username, Password);
        result.IsSuccess.Should().BeTrue(result.Error.Message);

        // Re-attaches the till's open session, as the shell does.
        await PharmaXFixture.Resolve<ITillSessionService>(scope).CurrentAsync();

        return id;
    }

    public async Task SignInAsAdministratorAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    /// An open till session, opened by the administrator if none is.
    public async Task OpenTillAsync()
    {
        await SignInAsAdministratorAsync();

        using var scope = _fixture.NewScope();
        var sessions = PharmaXFixture.Resolve<ITillSessionService>(scope);

        if (await sessions.CurrentAsync() is not null) return;

        var opened = await sessions.OpenAsync(500m);
        opened.IsSuccess.Should().BeTrue(opened.Error.Message);
    }

    /// <summary>
    /// A product on the shelf. Batch-tracked where batches are given, with
    /// the product's own on-hand equal to their sum and an opening
    /// movement for each, as a real delivery would leave it.
    /// </summary>
    public async Task<Guid> ProductAsync(string code, string description,
        decimal price, int schedule = 0, decimal? unitsPerPack = null,
        string? genericName = null, decimal quantity = 100m,
        params (string Number, int MonthsToExpiry, decimal Quantity)[] batches)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var existing = await db.Products.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Code == code);

        if (existing is not null) return existing.Id;

        var product = Product.Create(code, description, Money.Of(price),
            schedule: schedule, isBatchTracked: batches.Length > 0);

        product.ChangeCostPrice(Money.Of(Math.Round(price * 0.6m, 2)));
        product.SetUnitsPerPack(unitsPerPack);
        product.SetGenericName(genericName);

        var onHand = batches.Length > 0 ? batches.Sum(b => b.Quantity)
                                        : quantity;
        product.SetQuantityOnHand(onHand);
        db.Products.Add(product);

        var running = 0m;

        foreach (var (number, months, qty) in batches)
        {
            var batch = Batch.Receive(product.Id, number,
                clock.Today.AddMonths(months), qty, product.CostPrice,
                clock.UtcNow);

            db.Batches.Add(batch);
            running += qty;

            db.StockMovements.Add(StockMovement.Record(product.Id, batch.Id,
                MovementType.Receipt, qty, running, clock.UtcNow,
                reasonCode: "OPENING"));
        }

        await db.SaveChangesAsync();
        return product.Id;
    }

    public async Task<Guid> PatientAsync(string surname, string names,
        params (string Substance, string Severity)[] allergies)
    {
        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var created = await patients.CreateAsync(new PatientEditDto(
            Surname: surname, FirstNames: names, IdNumber: null,
            DateOfBirth: new DateOnly(1980, 1, 1), Telephone: "0110000000",
            Email: null, WeightKg: 70m, IsPregnant: false,
            IsBreastfeeding: false, ClinicalNotes: null,
            Allergies: allergies
                .Select(a => new AllergyDto(a.Substance, a.Severity, null))
                .ToList(),
            ChronicConditions: Array.Empty<string>()));

        created.IsSuccess.Should().BeTrue(created.Error.Message);
        return created.Value;
    }

    public async Task<Guid> ScriptAsync(Guid patientId,
        params (string Code, decimal Quantity, string Directions)[] items)
    {
        using var scope = _fixture.NewScope();
        var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var captured = await scripts.CaptureAsync(new ScriptEditDto(
            patientId, "Dr Test", "MP000001", clock.Today, null, 1,
            items.Select(i => new ScriptItemEditDto(i.Code, i.Quantity,
                                                    i.Directions)).ToList()));

        captured.IsSuccess.Should().BeTrue(captured.Error.Message);
        return captured.Value;
    }

    /// The product's own on-hand and the sum of its batches, read afresh.
    public async Task<(decimal Product, decimal Batches)> StockOfAsync(
        Guid productId)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var product = await db.Products.AsNoTracking()
            .SingleAsync(p => p.Id == productId);

        // Summed here: SQLite cannot aggregate a decimal column.
        var batches = (await db.Batches.AsNoTracking()
            .Where(b => b.ProductId == productId)
            .Select(b => b.QuantityOnHand)
            .ToListAsync()).Sum();

        return (product.QuantityOnHand, batches);
    }

    public PharmaXFixture Fixture => _fixture;
}
