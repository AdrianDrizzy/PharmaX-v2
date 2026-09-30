using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Claims;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Repositories;
using PharmaX.Application.Security;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Reference;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Specification Section 7.8: a line dispensed against a claimable
/// membership is priced from the tariff table rather than from the shelf,
/// and splits into what the society owes and what the patient is asked
/// for at the till.
/// </summary>
/// <remarks>
/// The numbers are Section 15.4's worked example - a tariff of 180.00, a
/// contribution of 150.00 and a co-payment of 30.00 - because that
/// example is the only place the split is written down, and it is what
/// fixes the reading of the two fields the tariff actually carries: the
/// society's price, and the dispensing fee it allows on top of it.
///
/// This had never worked. DispensingLine.SetSchemeContribution had no
/// caller outside three unit tests that set it themselves, which is
/// precisely why the gap was invisible: the tests constructed the state
/// that production never produced. Every dispensing charged the patient
/// the full amount, no contribution was ever recorded, and the claim
/// builder returned early on a zero contribution every single time - so
/// no claim could be raised against any society on any installation, and
/// frmClaims was empty everywhere by construction.
/// </remarks>
public class MedicalAidRatingTests : IClassFixture<PharmaXFixture>
{
    private readonly PharmaXFixture _fixture;

    public MedicalAidRatingTests(PharmaXFixture fixture) => _fixture = fixture;

    [Fact]
    public async Task A_rated_line_is_priced_from_the_tariff_not_the_shelf()
    {
        await SignInAsync();

        var schemeId = await GivenASocietyAsync("RATE1", "Rating Society");

        // Deliberately dearer on the shelf than the society pays, so
        // that the figure on the line can only have come from the
        // tariff.
        var code = await GivenAProductAsync("RATED1", "Rated item",
            retail: 200m);

        await GivenATariffAsync(schemeId, code, price: 150m,
            dispensingFee: 30m);

        var patientId = await GivenAPatientAsync("Mabaso", "Thandi",
            schemeId, "MEM-0001");

        var line = await DispenseAsync(patientId, code);

        line.TariffAmount.Amount.Should().Be(180m,
            "the tariff prices the line: 150.00 plus the 30.00 fee");

        line.SchemeContribution.Amount.Should().Be(150m,
            "the society owes its own price");

        line.CoPayment.Amount.Should().Be(30m,
            "and the patient is asked for the dispensing fee");
    }

    /// <remarks>
    /// The whole point of the fix. A contribution of zero is what made
    /// ClaimService.Build return null on every dispensing there has ever
    /// been.
    /// </remarks>
    [Fact]
    public async Task Issuing_a_rated_line_raises_a_claim_for_the_societys_share()
    {
        await SignInAsync();

        var schemeId = await GivenASocietyAsync("RATE2", "Claiming Society");
        var code = await GivenAProductAsync("RATED2", "Claimed item", 200m);

        await GivenATariffAsync(schemeId, code, 150m, 30m);

        var patientId = await GivenAPatientAsync("Naidoo", "Priya", schemeId,
            "MEM-0002");

        var line = await DispenseAsync(patientId, code);

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var claim = await db.MedicalAidClaims.AsNoTracking()
            .SingleAsync(c => c.DispensingId == line.DispensingId);

        claim.SchemeId.Should().Be(schemeId);
        claim.MembershipNumber.Should().Be("MEM-0002");
        claim.Claimed.Amount.Should().Be(150m);
        claim.Outstanding.Amount.Should().Be(150m,
            "nothing has been remitted against it yet");
    }

    /// <remarks>
    /// The other half of the rule, and the reason the fix cannot simply
    /// always apply a tariff: a patient who pays for themselves, or one
    /// whose society has no tariff on file for what they were handed,
    /// pays the shelf price and generates nothing to claim.
    /// </remarks>
    [Fact]
    public async Task A_self_payer_keeps_the_shelf_price_and_raises_no_claim()
    {
        await SignInAsync();

        var code = await GivenAProductAsync("UNRATED", "Unrated item", 200m);
        var patientId = await GivenAPatientAsync("Khumalo", "Sipho");

        var line = await DispenseAsync(patientId, code);

        line.TariffAmount.Amount.Should().Be(200m);
        line.SchemeContribution.Amount.Should().Be(0m);
        line.CoPayment.Amount.Should().Be(200m, "they pay for all of it");

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.MedicalAidClaims.CountAsync(c =>
                c.DispensingId == line.DispensingId))
            .Should().Be(0);
    }

    /// <remarks>
    /// A society on the patient is not on its own enough: the tariff is
    /// per product, and one the society has not priced falls back to the
    /// shelf rather than being given away.
    /// </remarks>
    [Fact]
    public async Task A_member_with_no_tariff_for_the_item_pays_the_shelf_price()
    {
        await SignInAsync();

        var schemeId = await GivenASocietyAsync("RATE3", "Sparse Society");
        var code = await GivenAProductAsync("UNPRICED", "Unpriced item", 200m);

        var patientId = await GivenAPatientAsync("Botha", "Annelie", schemeId,
            "MEM-0003");

        var line = await DispenseAsync(patientId, code);

        line.TariffAmount.Amount.Should().Be(200m);
        line.SchemeContribution.Amount.Should().Be(0m);
    }

    /// <remarks>
    /// The paper the member signs: everything on it comes from the claim,
    /// the dispensing behind it, the script and the patient file.
    /// </remarks>
    [Fact]
    public async Task The_claim_form_carries_the_member_the_script_and_the_medicine()
    {
        await SignInAsync();

        var schemeId = await GivenASocietyAsync("FORM1", "Form Society");
        var code = await GivenAProductAsync("FORMED1", "Form item", 200m);

        await GivenATariffAsync(schemeId, code, 150m, 30m);

        var patientId = await GivenAPatientAsync("Chirwa", "Grace Tariro",
            schemeId, "MEM-0004");

        var line = await DispenseAsync(patientId, code);

        using var scope = _fixture.NewScope();
        var forms = PharmaXFixture.Resolve<IClaimFormService>(scope);

        var built = await forms.ForDispensingAsync(line.DispensingId);
        built.IsSuccess.Should().BeTrue(built.Error.Message);

        var form = built.Value;

        _lastIssue!.ClaimNumber.Should().NotBeNullOrEmpty()
            .And.Be(form.ClaimNumber, "the issue names the claim it raised");

        form.SchemeName.Should().Be("Form Society");
        form.PatientName.Should().Be("Grace Tariro Chirwa");
        form.MemberSurname.Should().Be("Chirwa");
        form.MemberInitials.Should().Be("G T");
        form.MembershipNumber.Should().Be("MEM-0004");
        form.DependantCode.Should().Be("00");
        form.DateOfBirth.Should().Be(new DateOnly(1985, 4, 12));
        form.DoctorName.Should().Be("Dr Adams");
        form.DoctorNumber.Should().Be("MP123456");
        form.ScriptNumber.Should().Be(_lastIssue.ScriptNumber);
        form.DispensingNumber.Should().Be(_lastIssue.DocumentNumber);
        form.IsRepeat.Should().BeFalse("it is the script's first issue");

        var medicine = form.Lines.Should().ContainSingle().Subject;
        medicine.PriceCode.Should().Be("FORMED1");
        medicine.Quantity.Should().Be(20m);
        medicine.Charge.Should().Be(180m);
        medicine.Claimed.Should().Be(150m);
        medicine.Directions.Should().Be("Two tablets twice daily");

        form.Gross.Should().Be(180m);
        form.Claimed.Should().Be(150m);
        form.CoPayment.Should().Be(30m);

        (await forms.BuildAsync((await ClaimIdAsync(line.DispensingId))))
            .Value.Should().BeEquivalentTo(form,
                "the Claims screen prints the same form");
    }

    /// <remarks>
    /// The pharmacy the scheme pays is the one on the shop front, as set
    /// at the System Console - not the product's name the installation
    /// starts with.
    /// </remarks>
    [Fact]
    public async Task The_claim_form_is_headed_with_the_trading_name()
    {
        await SignInAsync();

        var schemeId = await GivenASocietyAsync("FORM2", "Named Society");
        var code = await GivenAProductAsync("FORMED2", "Named item", 200m);

        await GivenATariffAsync(schemeId, code, 150m, 30m);

        var patientId = await GivenAPatientAsync("Sibanda", "Rudo",
            schemeId, "MEM-0005");

        var line = await DispenseAsync(patientId, code);

        // One profile for the whole process: the scope only lends it.
        using var any = _fixture.NewScope();
        var profile = PharmaXFixture.Resolve<PharmacyProfile>(any);
        var installed = profile.Current;

        try
        {
            profile.Use(installed with { TradingName = "BethRapha Pharmacy" });

            using var scope = _fixture.NewScope();
            var forms = PharmaXFixture.Resolve<IClaimFormService>(scope);

            var form = await forms.ForDispensingAsync(line.DispensingId);

            form.Value.PharmacyName.Should().Be("BethRapha Pharmacy");
        }
        finally
        {
            profile.Use(installed);
        }
    }

    /// <remarks>
    /// The trailer follows the item labels at the issue, and a reprint
    /// from Dispensary > Labels prints the same one after them.
    /// </remarks>
    [Fact]
    public async Task Issuing_a_script_gives_it_a_trailer_label()
    {
        await SignInAsync();

        var code = await GivenAProductAsync("TRAIL1", "Trailer item", 200m);
        var patientId = await GivenAPatientAsync("Nyakunhuwa", "Josephat");

        var line = await DispenseAsync(patientId, code);

        var trailer = _lastIssue!.Trailer;

        trailer.Should().NotBeNull();
        trailer!.ScriptNumber.Should().Be(_lastIssue.ScriptNumber);
        trailer.PatientName.Should().Be("Josephat Nyakunhuwa");
        trailer.FillNumber.Should().Be(1, "it is the script's first issue");
        trailer.PrescriberName.Should().Be("Dr Adams");

        var item = trailer.Items.Should().ContainSingle().Subject;
        item.Description.Should().Be("Trailer item");
        item.Quantity.Should().Be(20m);

        // No printers in this fixture, so the reprint is handed one that
        // writes down what it was asked to print.
        using var scope = _fixture.NewScope();
        var printer = new RecordingLabelPrinter();

        var reprints = new LabelReprintService(
            PharmaXFixture.Resolve<IDispensingRepository>(scope),
            PharmaXFixture.Resolve<IScriptRepository>(scope),
            PharmaXFixture.Resolve<IPatientRepository>(scope),
            PharmaXFixture.Resolve<IUserRepository>(scope),
            printer,
            PharmaXFixture.Resolve<IPermissionService>(scope),
            PharmaXFixture.Resolve<IAuditWriter>(scope),
            PharmaXFixture.Resolve<IUnitOfWork>(scope));

        var rebuilt = await reprints.BuildTrailerAsync(line.DispensingId);

        rebuilt.IsSuccess.Should().BeTrue(rebuilt.Error.Message);
        rebuilt.Value.Should().BeEquivalentTo(trailer);

        var run = await reprints.ReprintAsync(line.DispensingId);

        run.IsSuccess.Should().BeTrue(run.Error.Message);
        run.Value.Printed.Should().Be(2, "the item label, then the trailer");
        printer.Labels.Should().ContainSingle();
        printer.Trailers.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(trailer);
    }

    private sealed class RecordingLabelPrinter : ILabelPrinter
    {
        public List<LabelDto> Labels { get; } = new();
        public List<TrailerLabelDto> Trailers { get; } = new();

        public bool IsAvailable => true;

        public Task<Application.Sales.PrintResult> PrintAsync(LabelDto label,
            CancellationToken ct = default)
        {
            Labels.Add(label);
            return Task.FromResult(
                Application.Sales.PrintResult.Ok("Test label printer"));
        }

        public Task<Application.Sales.PrintResult> PrintTrailerAsync(
            TrailerLabelDto trailer, CancellationToken ct = default)
        {
            Trailers.Add(trailer);
            return Task.FromResult(
                Application.Sales.PrintResult.Ok("Test label printer"));
        }
    }

    [Fact]
    public async Task A_self_payer_has_no_claim_form_to_print()
    {
        await SignInAsync();

        var code = await GivenAProductAsync("UNRATED2", "Unrated item", 200m);
        var patientId = await GivenAPatientAsync("Dube", "Nomsa");

        var line = await DispenseAsync(patientId, code);

        _lastIssue!.ClaimNumber.Should().BeNull();

        using var scope = _fixture.NewScope();
        var forms = PharmaXFixture.Resolve<IClaimFormService>(scope);

        var built = await forms.ForDispensingAsync(line.DispensingId);

        built.IsFailure.Should().BeTrue();
        built.Error.Code.Should().Be("CLM-010");
    }

    // ---- Steps -----------------------------------------------------------

    /// The last issue DispenseAsync made, as the dispensary was told of it.
    private IssueResultDto? _lastIssue;

    private async Task<Guid> ClaimIdAsync(Guid dispensingId)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        return await db.MedicalAidClaims.AsNoTracking()
            .Where(c => c.DispensingId == dispensingId)
            .Select(c => c.Id)
            .SingleAsync();
    }

    private async Task SignInAsync()
    {
        using var scope = _fixture.NewScope();
        var auth = PharmaXFixture.Resolve<IAuthenticationService>(scope);

        var result = await auth.AuthenticateAsync("admin", "ChangeMe#1");
        result.IsSuccess.Should().BeTrue(result.Error.Message);
    }

    /// <summary>
    /// Captures, dispenses and issues one item, and hands back the stored
    /// line so the figures can be read off the database rather than off
    /// the object that was just built in memory.
    /// </summary>
    private async Task<Domain.Dispensing.DispensingLine> DispenseAsync(
        Guid patientId, string productCode)
    {
        Guid dispensingId;

        using (var scope = _fixture.NewScope())
        {
            var scripts = PharmaXFixture.Resolve<IScriptService>(scope);
            var clock = PharmaXFixture.Resolve<IClock>(scope);

            var script = await scripts.CaptureAsync(new ScriptEditDto(
                PatientId: patientId,
                PrescriberName: "Dr Adams",
                PrescriberNumber: "MP123456",
                PrescribedOn: clock.Today,
                ValidUntil: null,
                RepeatsAuthorised: 1,
                Items: new[]
                {
                    new ScriptItemEditDto(productCode, 20m,
                                          "Two tablets twice daily")
                }));

            script.IsSuccess.Should().BeTrue(script.Error.Message);

            var dispensing = PharmaXFixture.Resolve<IDispensingService>(scope);

            var begun = await dispensing.BeginAsync(script.Value);
            begun.IsSuccess.Should().BeTrue(begun.Error.Message);
            dispensingId = begun.Value.Id;

            var added = await dispensing.AddItemAsync(dispensingId,
                productCode,
                new DirectionsDto(20m, "Two tablets twice daily"));

            added.IsSuccess.Should().BeTrue(added.Error.Message);

            var users = PharmaXFixture.Resolve<IUserRepository>(scope);
            var admin = await users.ByUsernameAsync("admin");

            var issued = await dispensing.IssueAsync(dispensingId, admin!.Id);
            issued.IsSuccess.Should().BeTrue(issued.Error.Message);
            _lastIssue = issued.Value;
        }

        using var check = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(check);

        var stored = await db.Dispensings.AsNoTracking()
            .Include(d => d.Lines)
            .SingleAsync(d => d.Id == dispensingId);

        return stored.Lines.Should().ContainSingle().Subject;
    }

    private async Task<Guid> GivenASocietyAsync(string code, string name)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.MedicalAidSchemes.FirstOrDefaultAsync(s => s.Code == code)
            is { } existing)
            return existing.Id;

        var scheme = MedicalAidScheme.Create(code, name);
        db.MedicalAidSchemes.Add(scheme);
        await db.SaveChangesAsync();

        return scheme.Id;
    }

    private async Task<string> GivenAProductAsync(string code,
        string description, decimal retail)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        if (await db.Products.AnyAsync(p => p.Code == code)) return code;

        var product = Product.Create(code, description, Money.Of(retail),
            schedule: 4, isBatchTracked: false);

        // The priced pack is the 20 units each of these tests dispenses,
        // so the line is exactly one pack at the tariff or shelf price.
        // Said explicitly since dispensing is priced by quantity (DEF-0021):
        // without a pack size each unit dispensed is a pack of its own.
        product.SetUnitsPerPack(20m);
        product.SetQuantityOnHand(200m);

        db.Products.Add(product);
        await db.SaveChangesAsync();

        return code;
    }

    private async Task GivenATariffAsync(Guid schemeId, string productCode,
        decimal price, decimal dispensingFee)
    {
        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var clock = PharmaXFixture.Resolve<IClock>(scope);

        var productId = await db.Products.Where(p => p.Code == productCode)
            .Select(p => p.Id).SingleAsync();

        var tariffs = PharmaXFixture.Resolve<ITariffService>(scope);

        var created = await tariffs.CreateAsync(new TariffEditDto(
            SchemeId: schemeId,
            ProductId: productId,
            Price: price,
            DispensingFee: dispensingFee,

            // Already in force today, which is the only case that prices
            // anything: IsEffectiveOn refuses one that starts tomorrow.
            EffectiveFrom: clock.Today.AddDays(-1),
            EffectiveTo: null));

        created.IsSuccess.Should().BeTrue(created.Error.Message);
    }

    private async Task<Guid> GivenAPatientAsync(string surname,
        string firstNames, Guid? schemeId = null, string? membership = null)
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
            Allergies: Array.Empty<AllergyDto>(),
            ChronicConditions: Array.Empty<string>(),
            MedicalAidSchemeId: schemeId,
            MembershipNumber: membership,
            DependantCode: schemeId is null ? null : "00"));

        result.IsSuccess.Should().BeTrue(result.Error.Message);
        return result.Value;
    }
}
