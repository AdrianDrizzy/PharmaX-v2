using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Reference;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// "Add a patient called Mary Banda": the patient file asked one field at
/// a time, then saved through the patient service.
/// </summary>
public sealed partial class NewPatientFlow : GuidedFlow
{
    private readonly IPatientService _patients;
    private readonly IReferenceDataService _reference;
    private readonly IClock _clock;

    private string _surname = "";
    private string _firstNames = "";
    private DateOnly? _born;
    private bool _bornEstimated;
    private char? _sex;
    private string? _idNumber;
    private string? _telephone;
    private string? _email;
    private string? _address;
    private ReferenceItemDto? _scheme;
    private string? _membership;
    private string? _dependant;
    private readonly List<AllergyDto> _allergies = new();
    private readonly List<string> _conditions = new();
    private decimal? _weight;
    private bool _pregnant;
    private bool _breastfeeding;
    private string? _notes;

    private IReadOnlyList<ReferenceItemDto>? _schemes;

    /// Under this age the weight is asked for, not offered.
    public const int ChildUnder = 12;

    /// The ages asked about pregnancy and breastfeeding.
    public const int ChildBearingFrom = 12, ChildBearingTo = 50;

    public NewPatientFlow(string? name, IPatientService patients, IReferenceDataService reference, IClock clock)
    {
        _patients = patients;
        _reference = reference;
        _clock = clock;

        var named = SplitName(name);

        Add("name", () => "What is the patient's name? First names, then surname.", Name, () => !named);
        Add("born", () => "Date of birth? e.g. 12/03/1985, or the age, e.g. \"38 years old\".", Born);
        Add("sex", () => "Sex? Female or male?", Sex);
        Add("id-number", () => "National ID number? (optional, Enter to skip)", IdNumber);
        Add("phone-email", () => "Phone number? And email, which is optional.", Contact);
        Add("address", () => "Address? (Enter to skip)", Address);
        Add("medical-aid", MedicalAidPromptAsync, MedicalAidAsync);
        Add("membership", () => $"{_scheme!.Name} membership number?", Membership, () => _scheme is not null);
        Add("dependant", () => "Dependant code? (Enter to skip)", Dependant, () => _scheme is not null);
        Add("allergies", () => "Any allergies? e.g. penicillin, sulfa, none known. Add \"severe\" or \"mild\" "
                               + "after one where it matters.", Allergies);
        Add("conditions", () => "Chronic conditions? e.g. hypertension, diabetes, asthma, none.", Conditions);
        Add("weight", WeightPrompt, Weight);
        Add("pregnant", () => "Pregnant?", Pregnant, () => MayBePregnant);
        Add("breastfeeding", () => "Breastfeeding?", Breastfeeding, () => MayBePregnant);
        Add("notes", () => "Anything else to note? (Enter for nothing)", Notes);
        Add("duplicate", DuplicatePromptAsync, Duplicate);
        AddConfirm(_ => Task.FromResult(Summary()), "Save it?", SaveAsync);
    }

    protected override string ResumeKey => "duplicate";

    private int? Age => _born is DateOnly born ? AgeOn(born, _clock.Today) : null;

    private bool IsChild => Age is int age && age < ChildUnder;

    private bool MayBePregnant =>
        _sex == 'F' && (Age is not int age || age is >= ChildBearingFrom and <= ChildBearingTo);

    public static int AgeOn(DateOnly born, DateOnly today)
    {
        var age = today.Year - born.Year;
        if (born > today.AddYears(-age)) age--;
        return age;
    }

    private string They => _sex switch { 'F' => "she", 'M' => "he", _ => "they" };

    /// "Mary Banda": the last word the surname, the rest the first names,
    /// the way the patient editor reads a name said aloud.
    private bool SplitName(string? name)
    {
        var words = (name ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 2) return false;

        _surname = words[^1];
        _firstNames = string.Join(' ', words[..^1]);
        return true;
    }

    protected override Task<string> IntroAsync(CancellationToken ct) =>
        Task.FromResult(_surname.Length == 0
            ? "Let's open a new patient file. Say \"cancel\" at any time; nothing is saved until you say so."
            : $"New patient: first names {_firstNames}, surname {_surname}. I'll ask a few questions; say "
              + "\"cancel\" at any time, and nothing is saved until you say so.");

    private Reply Name(string answer) =>
        SplitName(answer.Trim()) ? Reply.Ok() : Reply.Retry("I need first names and a surname, e.g. Mary Banda.");

    private Reply Born(string answer)
    {
        var today = _clock.Today;

        if (Answers.IsSkip(answer))
        {
            (_born, _bornEstimated) = (null, false);
            return Reply.Ok("No date of birth. Doses for children are checked by age, so add it when you can.");
        }

        if (Answers.Age(answer) is (int years, int months))
        {
            if (years > 120) return Reply.Retry("That age is too high. What is the date of birth?");

            (_born, _bornEstimated) = (today.AddYears(-years).AddMonths(-months), true);
            return Reply.Ok($"Taken as born about {Answers.Show(_born.Value)} (age {Age}). Put the real date "
                           + "on the file when you have it.");
        }

        if (Answers.Date(answer) is not DateOnly born)
            return Reply.Retry("Say the date as dd/mm/yyyy, e.g. 12/03/1985, or the age, e.g. 38 years old.");

        if (born > today) return Reply.Retry("A date of birth can't be in the future.");
        if (AgeOn(born, today) > 120) return Reply.Retry($"{Answers.Show(born)} would make them over 120. Check the year.");

        (_born, _bornEstimated) = (born, false);
        return Reply.Ok($"Born {Answers.Show(born)}, age {Age}.");
    }

    private Reply Sex(string answer)
    {
        var words = Answers.Words(answer);

        if (words.Any(w => w is "f" or "female" or "woman" or "girl" or "lady"))
            _sex = 'F';
        else if (words.Any(w => w is "m" or "male" or "man" or "boy"))
            _sex = 'M';
        else
            return Reply.Retry("Female or male?");

        return Reply.Ok();
    }

    private Reply IdNumber(string answer)
    {
        if (Answers.IsSkip(answer))
        {
            _idNumber = null;
            return Reply.Ok();
        }

        var id = Regex.Replace(answer.Trim().ToUpperInvariant(), @"\s+", "");
        if (id.Length is < 5 or > 30 || !id.Any(char.IsAsciiDigit))
            return Reply.Retry("That doesn't look like an ID number, e.g. 63-123456A78. Enter to skip.");

        _idNumber = id;
        return Reply.Ok();
    }

    private Reply Contact(string answer)
    {
        _telephone = null;
        _email = null;
        if (Answers.IsSkip(answer)) return Reply.Ok("No phone number.");

        var email = EmailPattern().Match(answer);
        if (email.Success) _email = email.Value.ToLowerInvariant();

        var rest = email.Success ? answer.Remove(email.Index, email.Length) : answer;
        var digits = new string(rest.Where(char.IsAsciiDigit).ToArray());

        if (digits.Length > 0)
        {
            if (PatientIdentity.Phone(rest) is not string phone)
                return Reply.Retry("That phone number is too short. Say it again, e.g. 0772 123 456.");

            _telephone = phone;
        }

        if (_telephone is null && _email is null)
            return Reply.Retry("I couldn't read a phone number or an email there.");

        return Reply.Ok();
    }

    private Reply Address(string answer)
    {
        _address = Answers.IsSkip(answer) ? null : Regex.Replace(answer.Trim(), @"\s+", " ");
        return Reply.Ok();
    }

    private async Task<IReadOnlyList<ReferenceItemDto>> SchemesAsync(CancellationToken ct)
    {
        if (_schemes is not null) return _schemes;

        var result = await _reference.ListAsync(ReferenceKind.MedicalAidScheme, includeInactive: false, ct: ct);
        _schemes = result.IsSuccess
            ? result.Value.OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase).ToList()
            : Array.Empty<ReferenceItemDto>();
        return _schemes;
    }

    private async Task<string> MedicalAidPromptAsync(CancellationToken ct)
    {
        var schemes = await SchemesAsync(ct);
        return "Medical aid? Name the scheme or say cash." + (schemes.Count == 0
            ? ""
            : Environment.NewLine + string.Join(", ", schemes.Select(s => s.Code)));
    }

    private async Task<Reply> MedicalAidAsync(string answer, CancellationToken ct)
    {
        var schemes = await SchemesAsync(ct);

        if (Answers.IsSkip(answer) || Answers.Words(answer).Any(w => w is "cash" or "private" or "self"))
        {
            (_scheme, _membership, _dependant) = (null, null, null);
            return Reply.Ok("Cash patient.");
        }

        var scheme = Answers.Pick(answer, schemes, s => s.Code)
                     ?? Answers.Pick(answer, schemes, s => s.Name);

        if (scheme is null)
            return Reply.Retry($"I can't find \"{answer.Trim()}\" in the medical aid list. Say its name or code, "
                              + "or cash.");

        _scheme = scheme;
        return Reply.Ok($"{scheme.Name}.");
    }

    private Reply Membership(string answer)
    {
        if (Answers.IsSkip(answer))
            return Reply.Retry("A medical aid patient needs a membership number, or say \"cancel\" and add it later.");

        _membership = answer.Trim().ToUpperInvariant();
        return Reply.Ok();
    }

    private Reply Dependant(string answer)
    {
        _dependant = Answers.IsSkip(answer) ? null : answer.Trim().ToUpperInvariant();
        return Reply.Ok();
    }

    private static readonly string[] Severities = { "Mild", "Moderate", "Severe" };

    private Reply Allergies(string answer)
    {
        _allergies.Clear();

        foreach (var item in Answers.List(answer))
        {
            // "penicillin (severe)", "severe penicillin"
            var severity = Severities.FirstOrDefault(s =>
                Answers.Words(item).Contains(s.ToLowerInvariant())) ?? "Moderate";

            var substance = Regex.Replace(item, @"\(?\b(mild|moderate|severe)\b\)?", "",
                RegexOptions.IgnoreCase).Trim(' ', '-', ':');

            if (substance.Length == 0) continue;
            _allergies.Add(new AllergyDto(substance.ToLowerInvariant(), severity, null));
        }

        return Reply.Ok(_allergies.Count == 0
            ? "No known allergies."
            : "Allergies: " + string.Join(", ", _allergies.Select(a => $"{a.Substance} ({a.Severity.ToLowerInvariant()})"))
              + ". The dispensary checks every item against these.");
    }

    private Reply Conditions(string answer)
    {
        _conditions.Clear();
        _conditions.AddRange(Answers.List(answer).Select(c => c.ToLowerInvariant()).Distinct());
        return Reply.Ok();
    }

    private string WeightPrompt() =>
        IsChild
            ? $"Weight in kg? Children's doses are worked out by weight."
            : "Weight in kg? (optional, Enter to skip)";

    private Reply Weight(string answer)
    {
        if (Answers.IsSkip(answer))
        {
            if (IsChild) return Reply.Retry("A child's weight is needed for dosing. Weigh them, or say \"cancel\".");
            _weight = null;
            return Reply.Ok();
        }

        if (Answers.Number(answer) is not decimal kg || kg is <= 0m or > 500m)
            return Reply.Retry("Say the weight in kilograms, e.g. 64.");

        if (IsChild && kg > 120m)
            return Reply.Retry($"{Answers.Quantity(kg)} kg is a lot for a child of {Age}. Check it.");

        _weight = Math.Round(kg, 1);
        return Reply.Ok();
    }

    private Reply Pregnant(string answer)
    {
        if (Answers.IsYes(answer)) _pregnant = true;
        else if (Answers.IsNo(answer) || answer.Length == 0) _pregnant = false;
        else return Reply.Retry("Yes or no?");

        return Reply.Ok(_pregnant ? $"Noted: {They} is pregnant. The dispensary warns on unsuitable medicines." : null);
    }

    private Reply Breastfeeding(string answer)
    {
        if (Answers.IsYes(answer)) _breastfeeding = true;
        else if (Answers.IsNo(answer) || answer.Length == 0) _breastfeeding = false;
        else return Reply.Retry("Yes or no?");

        return Reply.Ok();
    }

    private Reply Notes(string answer)
    {
        _notes = Answers.IsSkip(answer) ? null : answer.Trim();
        return Reply.Ok();
    }

    /// <summary>
    /// The notes as saved. The file has no field for sex yet, so it is the
    /// first line of the notes, where the dispensary reads it.
    /// </summary>
    private string? SavedNotes() =>
        Join(_sex switch { 'F' => "Sex: female", 'M' => "Sex: male", _ => null },
             _bornEstimated ? "Date of birth estimated from the age given." : null,
             _notes) is { Length: > 0 } text ? text : null;

    /// The edit the patient editor would send for the same answers.
    public PatientEditDto Edit() => new(
        Surname: _surname,
        FirstNames: _firstNames,
        IdNumber: _idNumber,
        DateOfBirth: _born,
        Telephone: _telephone,
        Email: _email,
        WeightKg: _weight,
        IsPregnant: _pregnant,
        IsBreastfeeding: _breastfeeding,
        ClinicalNotes: SavedNotes(),
        Allergies: _allergies.ToList(),
        ChronicConditions: _conditions.ToList(),
        MedicalAidSchemeId: _scheme?.Id,
        MembershipNumber: _scheme is null ? null : _membership,
        DependantCode: _scheme is null ? null : _dependant,
        Address: _address ?? "");

    private PatientListItemDto? _existing;

    private async Task<string> DuplicatePromptAsync(CancellationToken ct)
    {
        _existing = null;

        var found = await _patients.PossibleDuplicatesAsync(Edit(), ct: ct);
        if (found.IsFailure || found.Value.Count == 0) return "";

        _existing = found.Value[0];

        var born = "";
        var file = await _patients.GetAsync(_existing.Id, ct);
        if (file.IsSuccess && file.Value.DateOfBirth is DateOnly date) born = $", born {Answers.Show(date)},";

        var others = found.Value.Count > 1 ? $" (and {found.Value.Count - 1} more that may match)" : "";

        return $"{_existing.FullName}{born} already exists (file {_existing.FileNumber}){others}. "
               + "Open that one instead?";
    }

    private Reply Duplicate(string answer)
    {
        if (Answers.IsYes(answer) && _existing is { } existing)
        {
            Result = new GuidedResult(AppTaskKind.NewPatient, existing.Id, existing.FileNumber);
            return Reply.Finish($"Opening file {existing.FileNumber} instead. No new file was made.");
        }

        if (Answers.IsNo(answer)) return Reply.Ok("Then it will be a new file.");

        return Reply.Retry("Yes to open the existing file, or no to make a new one.");
    }

    public string Summary()
    {
        var text = new StringBuilder("The new patient file:");

        void Line(string label, string? value) =>
            text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}{label}: {value ?? "-"}");

        Line("Name", $"{_firstNames} {_surname}");
        Line("Date of birth", _born is DateOnly born
            ? $"{Answers.Show(born)} (age {Age}{(_bornEstimated ? ", estimated" : "")})"
            : null);
        Line("Sex", _sex switch { 'F' => "female", 'M' => "male", _ => null });
        Line("ID number", _idNumber);
        Line("Phone", _telephone);
        Line("Email", _email);
        Line("Address", _address);
        Line("Medical aid", _scheme is null
            ? "cash"
            : $"{_scheme.Name}, member {_membership}{(_dependant is null ? "" : $", dependant {_dependant}")}");
        Line("Allergies", _allergies.Count == 0
            ? "none known"
            : string.Join(", ", _allergies.Select(a => $"{a.Substance} ({a.Severity.ToLowerInvariant()})")));
        Line("Chronic conditions", _conditions.Count == 0 ? "none" : string.Join(", ", _conditions));
        Line("Weight", _weight is decimal kg ? $"{Answers.Quantity(kg)} kg" : null);
        if (MayBePregnant)
        {
            Line("Pregnant", _pregnant ? "yes" : "no");
            Line("Breastfeeding", _breastfeeding ? "yes" : "no");
        }

        Line("Notes", _notes);
        text.Append(Environment.NewLine + "Say \"change\" and what to change, e.g. \"change the phone\".");
        return text.ToString();
    }

    private async Task<Reply> SaveAsync(CancellationToken ct)
    {
        var saved = await _patients.CreateAsync(Edit(), ct);

        if (saved.IsFailure)
            return Reply.Retry($"It was not saved: {saved.Error.Message}");

        var file = await _patients.GetAsync(saved.Value, ct);
        var number = file.IsSuccess ? file.Value.FileNumber : "";

        Result = new GuidedResult(AppTaskKind.NewPatient, saved.Value, number);
        return Reply.Finish($"Saved {_firstNames} {_surname} as file {number}.");
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}")]
    private static partial Regex EmailPattern();
}
