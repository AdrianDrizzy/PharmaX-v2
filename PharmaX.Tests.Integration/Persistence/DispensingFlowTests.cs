using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Patient, script, dispensing and issue against a real database. This
/// is the path DSP-004 through DSP-007 live on, and it is the one that
/// cannot be checked by unit tests alone: the clinical screen reads a
/// profile that has to have survived a save.
/// </summary>
public class DispensingFlowTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public DispensingFlowTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_script_is_captured_dispensed_and_issued()
    {
        await SignInAsync();
        var productCode = await GivenAProductAsync("PANADO", "Panado 500mg");
        var patientId = await GivenAPatientAsync("Mabaso", "Thandi");

        var scriptId = await GivenAScriptAsync(patientId, productCode);

        Guid dispensingId;

        using (var scope = _fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            var begun = await dispensing.BeginAsync(scriptId);
            begun.IsSuccess.Should().BeTrue(begun.Error.Message);
            dispensingId = begun.Value.Id;

            var added = await dispensing.AddItemAsync(dispensingId,
                productCode, new DirectionsDto(20m, "Two tablets twice daily"));

            added.IsSuccess.Should().BeTrue(added.Error.Message);
            added.Value.Items.Should().HaveCount(1);
            added.Value.Warnings.Should().BeEmpty(
                "nothing about this patient should raise a warning");
        }

        using (var scope = _fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);
            var pharmacistId = await PharmacistIdAsync(scope);

            var issued = await dispensing.IssueAsync(dispensingId,
                                                     pharmacistId);

            issued.IsSuccess.Should().BeTrue(issued.Error.Message);
            issued.Value.DocumentNumber.Should().MatchRegex(@"^D-001-\d{6}$");

            // DSP-004: a label carries the directions, the batch and the
            // pharmacist who signed for it.
            issued.Value.Labels.Should().ContainSingle();
            issued.Value.Labels[0].Directions
                  .Should().Be("Two tablets twice daily");
            issued.Value.Labels[0].PharmacistName.Should().NotBeEmpty();
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var stored = await db.Dispensings.AsNoTracking()
                .Include(d => d.Lines)
                .SingleAsync(d => d.Id == dispensingId);

            stored.Status.Should()
                  .Be(Domain.Dispensing.DispensingStatus.Issued);
            stored.PharmacistId.Should().NotBeNull();
            stored.Lines.Should().ContainSingle();
        }
    }

    /// <summary>
    /// DSP-006 in one line: no pharmacist, no issue. The rule lives on
    /// the aggregate, so it throws rather than returning a refusal - a
    /// screen that gets here has skipped a step it was told about.
    /// </summary>
    [Fact]
    public async Task Nothing_is_issued_without_a_pharmacist()
    {
        await SignInAsync();
        var productCode = await GivenAProductAsync("DISPRIN", "Disprin 300mg");
        var patientId = await GivenAPatientAsync("Naidoo", "Priya");
        var scriptId = await GivenAScriptAsync(patientId, productCode);

        using var scope = _fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var begun = await dispensing.BeginAsync(scriptId);
        await dispensing.AddItemAsync(begun.Value.Id, productCode,
            new DirectionsDto(10m, "One tablet daily"));

        var act = () => dispensing.IssueAsync(begun.Value.Id, Guid.Empty);

        await act.Should()
            .ThrowAsync<Domain.Exceptions.DomainException>()
            .WithMessage("*DSP-006*");
    }

    /// <summary>
    /// The clinical screen has to read a profile written by another
    /// scope. An allergy that does not survive the save is a hard stop
    /// that never fires, which is the worst possible failure here.
    /// </summary>
    [Fact]
    public async Task A_recorded_allergy_raises_a_hard_stop_on_the_item()
    {
        await SignInAsync();

        var productCode = await GivenAProductAsync("PENICILLIN",
            "Penicillin V 250mg");

        var patientId = await GivenAPatientAsync("Botha", "Johan",
            allergies: new[] { new AllergyDto("Penicillin", "Severe",
                                              "Anaphylaxis") });

        var scriptId = await GivenAScriptAsync(patientId, productCode);

        using var scope = _fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var begun = await dispensing.BeginAsync(scriptId);

        var added = await dispensing.AddItemAsync(begun.Value.Id,
            productCode, new DirectionsDto(20m, "One four times daily"));

        added.IsSuccess.Should().BeTrue(added.Error.Message);

        var warning = added.Value.Warnings.Should().ContainSingle().Subject;
        warning.Kind.Should().Be("Allergy");
        warning.IsHardStop.Should().BeTrue();

        // And the issue is refused while it stands unacknowledged.
        var pharmacistId = await PharmacistIdAsync(scope);
        var act = () => dispensing.IssueAsync(begun.Value.Id, pharmacistId);

        await act.Should()
            .ThrowAsync<Domain.Exceptions.DomainException>()
            .WithMessage("*not been acknowledged*");
    }

    /// <summary>
    /// DSP-007: a hard stop can be passed, but only with a written
    /// reason, and the reason is kept.
    /// </summary>
    [Fact]
    public async Task A_hard_stop_can_be_overridden_with_a_reason()
    {
        await SignInAsync();

        var productCode = await GivenAProductAsync("AMOXIL", "Amoxil 500mg");

        var patientId = await GivenAPatientAsync("Khumalo", "Sipho",
            allergies: new[] { new AllergyDto("Amoxil", "Moderate", null) });

        var scriptId = await GivenAScriptAsync(patientId, productCode);

        using var scope = _fixture.NewScope();
        var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

        var begun = await dispensing.BeginAsync(scriptId);
        var added = await dispensing.AddItemAsync(begun.Value.Id,
            productCode, new DirectionsDto(15m, "One three times daily"));

        var warning = added.Value.Warnings[0];

        var acknowledged = await dispensing.AcknowledgeWarningAsync(
            warning.Id, "Patient has tolerated this before; prescriber "
                        + "consulted.");

        acknowledged.IsSuccess.Should().BeTrue(acknowledged.Error.Message);

        var pharmacistId = await PharmacistIdAsync(scope);
        var issued = await dispensing.IssueAsync(begun.Value.Id, pharmacistId);

        issued.IsSuccess.Should().BeTrue(issued.Error.Message);

        using var check = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.Set<Domain.Dispensing.ClinicalWarning>()
            .AsNoTracking()
            .SingleAsync(w => w.Id == warning.Id);

        stored.IsAcknowledged.Should().BeTrue();
        stored.OverrideReason.Should().Contain("prescriber consulted");
    }

    [Fact]
    public async Task A_batch_tracked_item_takes_the_nearest_expiry()
    {
        await SignInAsync();

        var productCode = await GivenAProductAsync("INSULIN",
            "Insulin glargine 100u", batchTracked: true);

        Guid soonerId;

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var product = await db.Products
                .SingleAsync(p => p.Code == productCode);

            var later = Batch.Receive(product.Id, "I-LATER",
                clock.Today.AddMonths(12), 5m, Money.Of(200m), clock.UtcNow);

            var sooner = Batch.Receive(product.Id, "I-SOONER",
                clock.Today.AddMonths(2), 5m, Money.Of(200m), clock.UtcNow);

            db.Batches.AddRange(later, sooner);
            soonerId = sooner.Id;

            await db.SaveChangesAsync();
        }

        var patientId = await GivenAPatientAsync("Dlamini", "Nomsa");
        var scriptId = await GivenAScriptAsync(patientId, productCode);

        using (var scope = _fixture.NewScope())
        {
            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            var begun = await dispensing.BeginAsync(scriptId);
            var added = await dispensing.AddItemAsync(begun.Value.Id,
                productCode, new DirectionsDto(2m, "Ten units at night"));

            added.IsSuccess.Should().BeTrue(added.Error.Message);
            added.Value.Items[0].BatchNumber.Should().Be("I-SOONER");

            var pharmacistId = await PharmacistIdAsync(scope);
            await dispensing.IssueAsync(begun.Value.Id, pharmacistId);
        }

        using (var scope = _fixture.NewScope())
        {
            var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

            var sooner = await db.Batches.AsNoTracking()
                .SingleAsync(b => b.Id == soonerId);

            sooner.QuantityOnHand.Should().Be(3m);
        }
    }

    /// <summary>
    /// DSP-004 again, from the other direction: a script item without
    /// directions cannot be captured at all.
    /// </summary>
    [Fact]
    public async Task A_script_item_without_directions_is_refused()
    {
        await SignInAsync();
        var productCode = await GivenAProductAsync("BRUFEN", "Brufen 400mg");
        var patientId = await GivenAPatientAsync("Pillay", "Anand");

        using var scope = _fixture.NewScope();
        var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var result = await scripts.CaptureAsync(new ScriptEditDto(
            patientId, "Dr Adams", null, clock.Today, null, 0,
            new[] { new ScriptItemEditDto(productCode, 30m, "   ") }));

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("DSP-025");
    }

    [Fact]
    public async Task A_patient_file_number_is_allocated_not_typed()
    {
        await SignInAsync();

        var first = await GivenAPatientAsync("Zulu", "Ayanda");
        var second = await GivenAPatientAsync("Zulu", "Bongani");

        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var one = await patients.GetAsync(first);
        var two = await patients.GetAsync(second);

        one.Value.FileNumber.Should().MatchRegex(@"^P-001-\d{6}$");
        two.Value.FileNumber.Should().NotBe(one.Value.FileNumber);
    }

    // ---- Steps -----------------------------------------------------------

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    /// <summary>
    /// The seeded administrator holds every permission, so it stands in
    /// for the pharmacist here. What matters to the rule is that the
    /// authoriser holds dsp.script.authorise, not what they are called.
    /// </summary>
    private static async Task<Guid> PharmacistIdAsync(IServiceScope scope)
    {
        var users = PharmaXFixture.Resolve<IUserRepository>(scope);
        var admin = await users.ByUsernameAsync("admin");

        admin.Should().NotBeNull();
        return admin!.Id;
    }

    private async Task<string> GivenAProductAsync(string code,
        string description, bool batchTracked = false)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == code)) return code;

        var product = Product.Create(code, description, Money.Of(50m),
            schedule: 4, isBatchTracked: batchTracked);

        product.SetQuantityOnHand(200m);

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return code;
    }

    private async Task<Guid> GivenAPatientAsync(string surname,
        string firstNames, IReadOnlyList<AllergyDto>? allergies = null)
    {
        using var scope = _fixture.NewScope();
        var patients = PharmaXFixture.Resolve<IPatientService>(scope);

        var result = await patients.CreateAsync(new PatientEditDto(
            Surname: surname,
            FirstNames: firstNames,
            IdNumber: null,
            DateOfBirth: new DateOnly(1985, 4, 12),
            Telephone: "0110000000",
            Email: null,
            WeightKg: 70m,
            IsPregnant: false,
            IsBreastfeeding: false,
            ClinicalNotes: null,
            Allergies: allergies ?? Array.Empty<AllergyDto>(),
            ChronicConditions: Array.Empty<string>()));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value;
    }

    private async Task<Guid> GivenAScriptAsync(Guid patientId,
        string productCode)
    {
        using var scope = _fixture.NewScope();
        var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var result = await scripts.CaptureAsync(new ScriptEditDto(
            PatientId: patientId,
            PrescriberName: "Dr Adams",
            PrescriberNumber: "MP123456",
            PrescribedOn: clock.Today,
            ValidUntil: null,
            RepeatsAuthorised: 2,
            Items: new[]
            {
                new ScriptItemEditDto(productCode, 20m,
                                      "Two tablets twice daily")
            }));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value;
    }
}
