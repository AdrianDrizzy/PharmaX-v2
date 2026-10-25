using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Assistant;
using PharmaX.Application.Assistant.Guided;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Results;
using PharmaX.Domain.Security;
using static PharmaX.Tests.Unit.Assistant.GuidedTestKit;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// "Dispense amoxicillin 500mg for John Moyo", talked through: the patient,
/// the allergy check first, the script and its items, the charges and the
/// warnings, then issued on a yes - with a pharmacist's sign-in where the
/// dispensing needs one.
/// </summary>
public class DispenseFlowTests
{
    private readonly IPatientService _patients = Substitute.For<IPatientService>();
    private readonly IScriptService _scripts = Substitute.For<IScriptService>();
    private readonly IDispensingService _dispensing = Substitute.For<IDispensingService>();
    private readonly IProductService _products = Substitute.For<IProductService>();
    private readonly IReferenceDataService _reference = Substitute.For<IReferenceDataService>();

    private static readonly Guid John = Guid.NewGuid();
    private static readonly Guid Johnny = Guid.NewGuid();
    private static readonly Guid Amoxil = Guid.NewGuid();
    private static readonly Guid ScriptId = Guid.NewGuid();
    private static readonly Guid DispensingId = Guid.NewGuid();
    private static readonly Guid Pharmacist = Guid.NewGuid();

    private readonly List<ClinicalWarningDto> _warnings = new();
    private readonly List<ScriptDto> _onFile = new();
    private bool _operatorMayIssue = true;

    public DispenseFlowTests()
    {
        _patients.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<IReadOnlyList<PatientListItemDto>>.Ok(
                ((string)ci[0]).Contains("Moyo", StringComparison.OrdinalIgnoreCase)
                    ? new[]
                    {
                        new PatientListItemDto(John, "0279", "Moyo", "John", "John Moyo", 46, null, true, true),
                        new PatientListItemDto(Johnny, "1475", "Moyo", "Johnny", "Johnny Moyo", 9, null, false, true)
                    }
                    : Array.Empty<PatientListItemDto>()));
        _patients.GetAsync(John, Arg.Any<CancellationToken>()).Returns(Result<PatientDto>.Ok(Patient(John, "John",
            "0279", new DateOnly(1980, 2, 1), new AllergyDto("penicillin", "Severe", null))));
        _patients.GetAsync(Johnny, Arg.Any<CancellationToken>()).Returns(Result<PatientDto>.Ok(Patient(Johnny, "Johnny",
            "1475", null)));

        _products.SearchAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result<ProductPageDto>.Ok(new ProductPageDto(new[]
            {
                Listed(Amoxil, "AMX500", "Amoxicillin 500mg capsules 21s", 118.40m, 48m, schedule: 3, pack: "CAP 21")
            }, 1)));
        _products.FindAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result<ProductListItemDto?>.Ok(null));

        _reference.ListAsync(ReferenceKind.Prescriber, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReferenceItemDto>>.Ok(new[]
            {
                new ReferenceItemDto(Guid.NewGuid(), "158", "J P Bell", "Practice 79855 · Harare", true),
                new ReferenceItemDto(Guid.NewGuid(), "159", "Jonathan P Bell", "Practice 02798 · Harare", true)
            }));

        _scripts.ForPatientAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<IReadOnlyList<ScriptDto>>.Ok(_onFile.ToList()));
        _scripts.CaptureAsync(Arg.Any<ScriptEditDto>(), Arg.Any<CancellationToken>()).Returns(Result<Guid>.Ok(ScriptId));
        _scripts.CancelAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Result.Ok());

        _dispensing.BeginAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Result<DispensingStateDto>.Ok(State(0)));
        _dispensing.AddScriptItemsAsync(DispensingId, Arg.Any<CancellationToken>())
            .Returns(Result<ScriptItemsAddedDto>.Ok(new ScriptItemsAddedDto(State(1), 1, Array.Empty<string>())));
        _dispensing.PendingWarningsAsync(DispensingId, Arg.Any<CancellationToken>())
            .Returns(_ => _warnings.Where(w => !w.IsAcknowledged).ToList());
        _dispensing.AcknowledgeWarningAsync(Arg.Any<Guid>(), Arg.Any<string?>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var i = _warnings.FindIndex(w => w.Id == (Guid)ci[0]);
                _warnings[i] = _warnings[i] with { IsAcknowledged = true };
                return Result.Ok();
            });
        _dispensing.IssueAuthorityAsync(DispensingId, Arg.Any<CancellationToken>())
            .Returns(_ => Result<IssueAuthorityDto>.Ok(new IssueAuthorityDto(_operatorMayIssue,
                _operatorMayIssue ? null : "Schedule 3: a pharmacist must authorise this.")));
        _dispensing.IssueAsync(DispensingId, Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(Result<IssueResultDto>.Ok(new IssueResultDto("SC-001-000009", "D-1", 26.50m,
                Array.Empty<LabelDto>(), "C-001-000004")));
        _dispensing.AbandonAsync(DispensingId, Arg.Any<CancellationToken>()).Returns(Result.Ok());
    }

    private static PatientDto Patient(Guid id, string first, string file, DateOnly? born, params AllergyDto[] allergies) =>
        new(id, file, "Moyo", first, null, born, born is null ? null : 46, null, null, true, null, false, false, null,
            allergies, new[] { "hypertension" });

    private static DispensingStateDto State(int items) =>
        new(DispensingId,
            Enumerable.Range(1, items).Select(n => new DispensingLineDto(n, Amoxil, "Amoxicillin 500mg capsules 21s",
                15m, "Take ONE capsule THREE times a day for 5 days", "AMX1", null, 94.16m, 67.66m, 26.50m, false))
                .ToList(),
            Array.Empty<ClinicalWarningDto>(), 94.16m * items, 67.66m * items, 26.50m * items);

    private DispenseFlow Flow(GuidedTestKit.Prompts? prompts = null, string person = "John Moyo",
                              Func<GuidedFlow?>? newPatient = null, params string[] granted) =>
        new("amoxicillin 500mg", null, person, _patients, _scripts, _dispensing, _products, _reference,
            Granting(granted.Length == 0 ? new[] { PermissionCode.ScriptCapture, PermissionCode.ScriptDispense } : granted),
            Clock(), prompts ?? new GuidedTestKit.Prompts(null), newPatient ?? (() => null));

    /// John, past his allergy, a new script, one item, no changes.
    private static readonly string[] ToCharges =
        { "1", "yes", "bell", "2", "", "", "1", "15", "1 TDS 5/7", "complete the course", "no" };

    [Fact]
    public async Task Asks_the_questions_in_the_order_given()
    {
        _warnings.Add(new ClinicalWarningDto(Guid.NewGuid(), "Allergy", "Severe", "Allergic to penicillin.", true, false));

        var asked = await KeysAsync(Flow(granted: new[]
            {
                PermissionCode.ScriptCapture, PermissionCode.ScriptDispense, PermissionCode.DispensingChargeAdjust
            }),
            ToCharges.Concat(new[] { "", "Prescriber confirmed by phone", "" }).ToArray());

        asked.Should().Equal("patient", "allergy", "prescriber", "prescriber", "script-date", "repeats", "item",
            "quantity", "directions", "label-extra", "another", "charges", "warning", "labels",
            GuidedFlow.ConfirmKey);
    }

    [Fact]
    public async Task The_patients_found_show_their_date_of_birth_and_file()
    {
        (await Flow().StartAsync()).Should()
            .Contain("1. John Moyo, born 01/02/1980, file 0279")
            .And.Contain("2. Johnny Moyo, no date of birth, file 1475")
            .And.Contain("Which one is it? Or say new.");
    }

    [Fact]
    public async Task The_allergy_check_comes_first_and_stops_everything_until_answered()
    {
        var flow = Flow();
        await flow.StartAsync();

        var said = await flow.ReplyAsync("1");

        said.Should().Contain("allergic to penicillin (severe); conditions: hypertension")
            .And.Contain("John is allergic to penicillin. Amoxicillin 500mg is a penicillin. Continue?");
        flow.Waiting.Should().Be("allergy");

        (await flow.ReplyAsync("no")).Should().StartWith("Stopped at the allergy check");
        await _scripts.DidNotReceive().CaptureAsync(Arg.Any<ScriptEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_allergy_to_it_is_said_and_passed()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await flow.ReplyAsync("2")).Should().Contain("Johnny Moyo, file 1475: no known allergies");
        flow.Waiting.Should().Be("prescriber");
    }

    [Fact]
    public async Task No_match_offers_a_new_patient_and_comes_back()
    {
        var child = new OneQuestion(Johnny);
        var flow = Flow(person: "Tendai Chuma", newPatient: () => child);

        (await flow.StartAsync()).Should().Contain("No patient called Tendai Chuma is on file. Add them as a new patient?");
        (await flow.ReplyAsync("yes")).Should().Be($"New patient.{Environment.NewLine}Save?");
        flow.Waiting.Should().Be("child:save");

        var back = await flow.ReplyAsync("yes");

        back.Should().Contain("Saved.").And.Contain("Back to dispensing.").And.Contain("Johnny Moyo, file 1475");
        flow.Waiting.Should().Be("prescriber");
    }

    /// A stand-in for the new-patient flow: one yes, and the patient saved.
    private sealed class OneQuestion : GuidedFlow
    {
        public OneQuestion(Guid saved) =>
            Add("save", () => "Save?", _ =>
            {
                Result = new GuidedResult(AppTaskKind.NewPatient, saved, "1475");
                return Reply.Finish("Saved.");
            });

        protected override Task<string> IntroAsync(CancellationToken ct) => Task.FromResult("New patient.");
    }

    [Fact]
    public async Task A_script_with_repeats_left_can_be_repeated_without_asking_the_items()
    {
        _onFile.Add(new ScriptDto(ScriptId, "SC-001-000001", John, "J P Bell", new DateOnly(2026, 9, 1), null, 2, 1,
            "Dispensed", "Amoxicillin 500mg capsules 21s  x15"));
        var flow = Flow();
        await flow.StartAsync();
        await flow.ReplyAsync("1");

        (await flow.ReplyAsync("yes")).Should().Contain("New script or an existing one?")
            .And.Contain("1. SC-001-000001 from J P Bell, 01/09/2026, 1 repeat(s) left: Amoxicillin 500mg capsules 21s  x15");

        (await flow.ReplyAsync("1")).Should().Contain("Repeating SC-001-000001");

        flow.Waiting.Should().Be("labels");
        await _dispensing.Received(1).BeginAsync(ScriptId, Arg.Any<CancellationToken>());
        await _scripts.DidNotReceive().CaptureAsync(Arg.Any<ScriptEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Directions_are_written_out_and_checked_against_the_quantity()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, "1", "yes", "bell", "2", "", "", "1", "10");

        (await flow.ReplyAsync("1 TDS 5/7")).Should()
            .Contain("\"Take ONE capsule THREE times a day for 5 days\" needs 15, but the quantity is 10. Keep 10?");
        await flow.ReplyAsync("no");

        flow.Waiting.Should().Be("quantity");
    }

    [Fact]
    public async Task The_charges_are_shown_but_only_adjusted_with_the_permission()
    {
        var flow = Flow();
        await flow.StartAsync();

        var said = await SayAsync(flow, ToCharges);

        said.Should().Contain("tariff 94.16, medical aid 67.66, patient pays 26.50").And.NotContain("Adjust the charge?");
        flow.Waiting.Should().Be("labels");
        await _scripts.Received(1).CaptureAsync(Arg.Is<ScriptEditDto>(s =>
            s.PatientId == John && s.PrescriberName == "Jonathan P Bell" && s.PrescriberNumber == "02798"
            && s.Items.Single().Quantity == 15m
            && s.Items.Single().Directions == "Take ONE capsule THREE times a day for 5 days. Complete the course."),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Adjusting_the_charge_needs_a_reason_and_whoever_may_authorise_it()
    {
        _dispensing.AmendItemAsync(DispensingId, 1, 15m, 20m, "Loyal customer", null, Arg.Any<CancellationToken>())
            .Returns(Result<ItemAmendedDto>.Fail(DispensingService.ChargeNeedsAuthority, "Needs authority."));
        _dispensing.AmendItemAsync(DispensingId, 1, 15m, 20m, "Loyal customer", Pharmacist, Arg.Any<CancellationToken>())
            .Returns(Result<ItemAmendedDto>.Ok(new ItemAmendedDto(State(1), false, 26.50m, 20m, false)));
        var prompts = new GuidedTestKit.Prompts(Pharmacist);
        var flow = Flow(prompts, granted: new[]
        {
            PermissionCode.ScriptCapture, PermissionCode.ScriptDispense, PermissionCode.DispensingChargeAdjust
        });
        await flow.StartAsync();
        await SayAsync(flow, ToCharges);

        await flow.ReplyAsync("1 to 20.00");
        (await flow.ReplyAsync("")).Should().StartWith("A reason is required");
        (await flow.ReplyAsync("Loyal customer")).Should().StartWith("Charge changed.");

        prompts.Asked.Should().Equal(PermissionCode.DispensingChargeAdjust);
    }

    [Fact]
    public async Task A_warning_is_overridden_only_with_a_reason()
    {
        _warnings.Add(new ClinicalWarningDto(Guid.NewGuid(), "Allergy", "Severe", "Allergic to penicillin.", true, false));
        var flow = Flow();
        await flow.StartAsync();

        (await SayAsync(flow, ToCharges)).Should().Contain("Warning: Allergic to penicillin.");
        (await flow.ReplyAsync("yes")).Should().StartWith("Overriding needs a written reason");

        await flow.ReplyAsync("Prescriber confirmed by phone");

        await _dispensing.Received(1).AcknowledgeWarningAsync(Arg.Any<Guid>(), "Prescriber confirmed by phone", null,
            Arg.Any<CancellationToken>());
        flow.Waiting.Should().Be("labels");
    }

    [Fact]
    public async Task Not_overriding_the_warning_stops_and_undoes_the_dispensing()
    {
        _warnings.Add(new ClinicalWarningDto(Guid.NewGuid(), "Allergy", "Severe", "Allergic to penicillin.", true, false));
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, ToCharges);

        (await flow.ReplyAsync("no")).Should().StartWith("Not dispensed");

        await _dispensing.Received(1).AbandonAsync(DispensingId, Arg.Any<CancellationToken>());
        await _scripts.Received(1).CancelAsync(ScriptId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Nothing_is_issued_until_yes()
    {
        var flow = Flow();
        await flow.StartAsync();

        var summary = await SayAsync(flow, ToCharges.Append("").ToArray());

        summary.Should().Contain("For John Moyo (file 0279), script from Jonathan P Bell")
            .And.Contain("1. Amoxicillin 500mg capsules 21s x15 - Take ONE capsule THREE times a day for 5 days")
            .And.EndWith("Issue and print the labels?");
        await _dispensing.DidNotReceive().IssueAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());

        (await flow.ReplyAsync("yes")).Should().StartWith("Issued script SC-001-000009")
            .And.Contain("Claim C-001-000004 was raised");
        flow.Result!.Kind.Should().Be(AppTaskKind.Dispense);
    }

    [Fact]
    public async Task A_pharmacist_confirms_what_the_operator_may_not_issue()
    {
        _operatorMayIssue = false;
        var prompts = new GuidedTestKit.Prompts(Pharmacist);
        var flow = Flow(prompts);
        await flow.StartAsync();
        await SayAsync(flow, ToCharges.Append("").ToArray());

        await flow.ReplyAsync("yes");

        prompts.Asked.Should().Equal(PermissionCode.ScriptAuthorise);
        await _dispensing.Received(1).IssueAsync(DispensingId, Pharmacist, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Without_the_pharmacist_it_is_not_issued()
    {
        _operatorMayIssue = false;
        var flow = Flow(new GuidedTestKit.Prompts(null));
        await flow.StartAsync();
        await SayAsync(flow, ToCharges.Append("").ToArray());

        (await flow.ReplyAsync("yes")).Should().StartWith("Not issued: a pharmacist has to confirm it.");
        await _dispensing.DidNotReceive().IssueAsync(Arg.Any<Guid>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Cancel_after_the_charges_undoes_the_script_and_the_dispensing()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, ToCharges);

        await flow.ReplyAsync("cancel");

        await _dispensing.Received(1).AbandonAsync(DispensingId, Arg.Any<CancellationToken>());
        await _scripts.Received(1).CancelAsync(ScriptId, Arg.Any<string>(), Arg.Any<CancellationToken>());
    }
}
