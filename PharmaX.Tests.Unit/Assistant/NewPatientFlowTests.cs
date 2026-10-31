using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Assistant;
using PharmaX.Application.Assistant.Guided;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Results;

namespace PharmaX.Tests.Unit.Assistant;

/// <summary>
/// "Add a patient called Mary Banda", talked through in Ask PharmaX.
/// </summary>
public class NewPatientFlowTests
{
    private readonly IPatientService _patients = Substitute.For<IPatientService>();
    private readonly IReferenceDataService _reference = Substitute.For<IReferenceDataService>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly List<PatientListItemDto> _similar = new();
    private static readonly DateOnly Today = new(2026, 10, 25);
    private static readonly ReferenceItemDto Cimas = new(Guid.NewGuid(), "CIMAS", "Cimas Medical Aid Society", null, true);
    private readonly Guid _saved = Guid.NewGuid();

    public NewPatientFlowTests()
    {
        _clock.Today.Returns(Today);

        _reference.ListAsync(ReferenceKind.MedicalAidScheme, Arg.Any<string>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(Result<IReadOnlyList<ReferenceItemDto>>.Ok(new[] { Cimas }));
        _patients.PossibleDuplicatesAsync(Arg.Any<PatientEditDto>(), Arg.Any<Guid?>(), Arg.Any<CancellationToken>())
            .Returns(_ => Result<IReadOnlyList<PatientListItemDto>>.Ok(_similar.ToList()));
        _patients.CreateAsync(Arg.Any<PatientEditDto>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Ok(_saved));
        _patients.GetAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(ci => Result<PatientDto>.Ok(new PatientDto((Guid)ci[0], (Guid)ci[0] == _saved ? "7122" : "0412",
                "Banda", "Mary", null, new DateOnly(1985, 3, 12), 41, null, null, true, null, false, false, null,
                Array.Empty<AllergyDto>(), Array.Empty<string>())));
    }

    private NewPatientFlow Flow(string? name = "Mary Banda") => new(name, _patients, _reference, _clock);

    private static async Task<string> SayAsync(GuidedFlow flow, params string[] answers)
    {
        var last = "";
        foreach (var answer in answers) last = await flow.ReplyAsync(answer);
        return last;
    }

    /// A woman of 41 on cash, up to the notes.
    private static readonly string[] Adult =
    {
        "12/03/1985", "female", "", "0772 123 456, mary@example.com", "12 Main St, Bulawayo", "cash",
        "penicillin severe, sulfa", "hypertension", "", "no", "no", ""
    };

    [Fact]
    public async Task Opens_with_the_names_from_the_request_and_asks_the_date_of_birth_first()
    {
        var flow = Flow();

        var said = await flow.StartAsync();

        said.Should().Contain("first names Mary, surname Banda").And.Contain("Date of birth?");
        flow.Waiting.Should().Be("born");
    }

    [Fact]
    public async Task Asks_the_questions_in_the_order_given()
    {
        var flow = Flow();
        await flow.StartAsync();
        var asked = new List<string?> { flow.Waiting };

        foreach (var answer in new[] { "12/03/1985", "female", "63-123456A78", "0772123456", "12 Main St",
                     "cimas", "123456", "01", "none known", "none", "", "no", "no", "" })
        {
            await flow.ReplyAsync(answer);
            asked.Add(flow.Waiting);
        }

        asked.Should().Equal("born", "sex", "id-number", "phone-email", "address", "medical-aid", "membership",
            "dependant", "allergies", "conditions", "weight", "pregnant", "breastfeeding", "notes",
            GuidedFlow.ConfirmKey);
    }

    [Fact]
    public async Task A_date_of_birth_shows_the_age()
    {
        var flow = Flow();
        await flow.StartAsync();

        (await flow.ReplyAsync("12/03/1985")).Should().StartWith("Born 12/03/1985, age 41.");
    }

    [Fact]
    public async Task An_age_is_taken_instead_of_the_date_of_birth()
    {
        var flow = Flow();
        await flow.StartAsync();

        var said = await flow.ReplyAsync("38 years old");

        said.Should().Contain("born about 25/10/1988 (age 38)");
        flow.Edit().DateOfBirth.Should().Be(new DateOnly(1988, 10, 25));
    }

    [Theory]
    [InlineData("01/01/2030")]
    [InlineData("next tuesday")]
    public async Task A_date_of_birth_in_the_future_or_unreadable_is_asked_again(string answer)
    {
        var flow = Flow();
        await flow.StartAsync();

        await flow.ReplyAsync(answer);

        flow.Waiting.Should().Be("born");
    }

    [Fact]
    public async Task A_man_is_not_asked_about_pregnancy()
    {
        var flow = Flow("John Moyo");
        await flow.StartAsync();

        await SayAsync(flow, "01/01/1980", "male", "", "0772123456", "", "cash", "none", "none", "");

        flow.Waiting.Should().Be("notes");
    }

    [Fact]
    public async Task A_child_must_be_weighed()
    {
        var flow = Flow("Tendai Banda");
        await flow.StartAsync();
        await SayAsync(flow, "6 years old", "male", "", "0772123456", "", "cash", "none", "none");

        (await flow.ReplyAsync("")).Should().Contain("A child's weight is needed");
        flow.Waiting.Should().Be("weight");

        await flow.ReplyAsync("21");
        flow.Edit().WeightKg.Should().Be(21m);
    }

    [Fact]
    public async Task An_adult_may_skip_the_weight()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, Adult.Take(8).ToArray());

        await flow.ReplyAsync("");

        flow.Waiting.Should().Be("pregnant");
    }

    [Fact]
    public async Task Allergies_are_kept_as_allergies_not_notes()
    {
        var flow = Flow();
        await flow.StartAsync();

        await SayAsync(flow, Adult);

        var edit = flow.Edit();
        edit.Allergies.Should().Equal(new AllergyDto("penicillin", "Severe", null),
                                      new AllergyDto("sulfa", "Moderate", null));
        edit.ChronicConditions.Should().Equal("hypertension");
        edit.ClinicalNotes.Should().Be("Sex: female");
        edit.Telephone.Should().Be("0772123456");
        edit.Email.Should().Be("mary@example.com");
        edit.MedicalAidSchemeId.Should().BeNull();
    }

    [Fact]
    public async Task A_medical_aid_patient_is_asked_for_the_membership_number()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, Adult.Take(5).ToArray());

        await flow.ReplyAsync("Cimas");
        (await flow.ReplyAsync("")).Should().Contain("needs a membership number");
        await SayAsync(flow, "cm123456", "01");

        var edit = flow.Edit();
        edit.MedicalAidSchemeId.Should().Be(Cimas.Id);
        edit.MembershipNumber.Should().Be("CM123456");
        edit.DependantCode.Should().Be("01");
    }

    [Fact]
    public async Task An_unknown_scheme_is_asked_again()
    {
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, Adult.Take(5).ToArray());

        (await flow.ReplyAsync("Acme Health")).Should().Contain("can't find \"Acme Health\"");
        flow.Waiting.Should().Be("medical-aid");
    }

    [Fact]
    public async Task A_possible_duplicate_offers_the_existing_file_instead()
    {
        var existing = Guid.NewGuid();
        _similar.Add(new PatientListItemDto(existing, "0412", "Banda", "Mary", "Mary Banda", 41, null, false, true));
        var flow = Flow();
        await flow.StartAsync();

        var said = await SayAsync(flow, Adult);

        said.Should().Be("Mary Banda, born 12/03/1985, already exists (file 0412). Open that one instead?");
        (await flow.ReplyAsync("yes")).Should().Contain("Opening file 0412 instead");
        flow.Result.Should().Be(new GuidedResult(AppTaskKind.NewPatient, existing, "0412"));
        await _patients.DidNotReceive().CreateAsync(Arg.Any<PatientEditDto>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task No_to_the_duplicate_goes_on_to_the_summary()
    {
        _similar.Add(new PatientListItemDto(Guid.NewGuid(), "0412", "Banda", "Mary", "Mary Banda", 41, null, false, true));
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, Adult);

        (await flow.ReplyAsync("no")).Should().EndWith("Save it?");
    }

    [Fact]
    public async Task The_file_is_shown_and_saved_only_on_yes()
    {
        var flow = Flow();
        await flow.StartAsync();

        var said = await SayAsync(flow, Adult);

        said.Should().Contain("Name: Mary Banda").And.Contain("Allergies: penicillin (severe), sulfa (moderate)")
            .And.EndWith("Save it?");
        await _patients.DidNotReceive().CreateAsync(Arg.Any<PatientEditDto>(), Arg.Any<CancellationToken>());

        (await flow.ReplyAsync("yes")).Should().Be("Saved Mary Banda as file 7122.");
        await _patients.Received(1).CreateAsync(
            Arg.Is<PatientEditDto>(e => e.Surname == "Banda" && e.FirstNames == "Mary"), Arg.Any<CancellationToken>());
        flow.Result.Should().Be(new GuidedResult(AppTaskKind.NewPatient, _saved, "7122"));
    }

    [Fact]
    public async Task An_id_number_already_on_file_is_said_and_nothing_saved()
    {
        _patients.CreateAsync(Arg.Any<PatientEditDto>(), Arg.Any<CancellationToken>())
            .Returns(Result<Guid>.Fail("PAT-014", "ID number 63-123456A78 is already on file 0412 for Mary Banda."));
        var flow = Flow();
        await flow.StartAsync();
        await SayAsync(flow, Adult);

        (await flow.ReplyAsync("yes")).Should().StartWith("It was not saved: ID number");

        flow.IsFinished.Should().BeFalse();
    }
}
