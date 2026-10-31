using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Assistant.Guided;

/// <summary>
/// "Dispense amoxicillin 500mg for John Moyo": the patient, the allergy
/// check, the script and its items asked in turn; then the script is
/// captured and dispensed through the script and dispensing services, so
/// the clinical warnings, pricing and the pharmacist's authority all come
/// from them. Nothing is issued until the operator says yes; a stopped
/// dispensing is abandoned and the script it captured cancelled.
/// </summary>
public sealed partial class DispenseFlow : GuidedFlow
{
    private readonly IPatientService _patients;
    private readonly IScriptService _scripts;
    private readonly IDispensingService _dispensing;
    private readonly IProductService _products;
    private readonly IReferenceDataService _reference;
    private readonly IPermissionService _permissions;
    private readonly IClock _clock;
    private readonly IGuidedPrompts _prompts;
    private readonly Func<GuidedFlow?> _newPatient;
    private readonly ILabelPrinter? _labels;
    private readonly IPharmacySettings? _settings;

    /// One item of the new script.
    public sealed record Item(ProductListItemDto Product, decimal Quantity, string Directions);

    private string? _personSaid;
    private string? _term;
    private decimal? _askedQuantity;

    private IReadOnlyList<(PatientListItemDto Patient, DateOnly? Born)> _patientMatches =
        Array.Empty<(PatientListItemDto, DateOnly?)>();
    private PatientDto? _patient;
    private bool _offerNew;

    private IReadOnlyList<ScriptDto> _existingScripts = Array.Empty<ScriptDto>();
    private ScriptDto? _existing;
    private IReadOnlyList<ReferenceItemDto> _prescriberMatches = Array.Empty<ReferenceItemDto>();
    private string? _prescriber;
    private string? _prescriberNumber;
    private DateOnly _scriptDate;
    private int _repeats;

    private readonly List<Item> _items = new();
    private IReadOnlyList<ProductListItemDto> _productMatches = Array.Empty<ProductListItemDto>();
    private ProductListItemDto? _product;
    private decimal _quantity;
    private string _directions = "";

    private Guid? _capturedScriptId;
    private Guid? _dispensingId;
    private DispensingStateDto? _state;
    private int? _chargeLine;
    private decimal? _chargeAmount;
    private ClinicalWarningDto? _warning;
    private readonly List<string> _overrides = new();
    private bool _printLabels = true;
    private bool _printTrailer = true;

    /// Most matches listed for "which".
    public const int MostMatches = 8;

    public DispenseFlow(string? item, decimal? quantity, string? person, IPatientService patients,
                        IScriptService scripts, IDispensingService dispensing, IProductService products,
                        IReferenceDataService reference, IPermissionService permissions, IClock clock,
                        IGuidedPrompts prompts, Func<GuidedFlow?> newPatient, ILabelPrinter? labels = null,
                        IPharmacySettings? settings = null)
    {
        _patients = patients;
        _scripts = scripts;
        _dispensing = dispensing;
        _products = products;
        _reference = reference;
        _permissions = permissions;
        _clock = clock;
        _prompts = prompts;
        _newPatient = newPatient;
        _labels = labels;
        _settings = settings;
        _term = string.IsNullOrWhiteSpace(item) ? null : item.Trim();
        _askedQuantity = quantity;
        _personSaid = string.IsNullOrWhiteSpace(person) ? null : person.Trim();
        _scriptDate = clock.Today;

        Add("patient", PatientPromptAsync, PatientAsync);
        Add("allergy", AllergyPrompt, Allergy);
        Add("script", ScriptPromptAsync, Script);
        Add("prescriber", PrescriberPrompt, PrescriberAsync, () => _existing is null);
        Add("script-date", () => $"Script date? Press Enter for today, {Answers.Show(_clock.Today)}.", ScriptDate,
            () => _existing is null);
        Add("repeats", () => "Repeats? (how many, or Enter for none)", Repeats, () => _existing is null);
        Add("item", ItemPromptAsync, ItemAsync, () => _existing is null);
        Add("quantity", QuantityPrompt, QuantityAsync, () => _existing is null);
        Add("directions", () => "Directions? e.g. 1 TDS 5/7", Directions_, () => _existing is null);
        Add("label-extra", () => "Any extra label instructions? e.g. complete the course, take with food. "
                                 + "(Enter for none)", LabelExtra, () => _existing is null);
        Add("another", () => "Another item on this script?", Another, () => _existing is null);
        Add("charges", ChargesPromptAsync, ChargesAsync);
        Add("charge-reason", () => "Why is the charge being changed? A reason is required.", ChargeReasonAsync,
            () => _chargeLine is not null);
        Add("warning", WarningPromptAsync, WarningAsync);
        Add("labels", () => "Print labels? Print the trailer? (Enter for both)", Labels);
        AddConfirm(_ => Task.FromResult(Summary()), "Issue and print the labels?", IssueAsync, changeable: false);
    }

    protected override string NothingDone => "Nothing was dispensed.";

    public IReadOnlyList<Item> Items => _items;

    protected override Task<string> IntroAsync(CancellationToken ct) =>
        Task.FromResult("Dispensing. Say \"cancel\" at any time; nothing is issued until you say so.");

    protected override async Task AbandonAsync(CancellationToken ct)
    {
        if (_dispensingId is Guid dispensing) await _dispensing.AbandonAsync(dispensing, ct);
        if (_capturedScriptId is Guid script) await _scripts.CancelAsync(script, "Stopped in Ask PharmaX", ct);
    }

    // ---- The patient ------------------------------------------------------

    private async Task<string> PatientPromptAsync(CancellationToken ct)
    {
        if (_patientMatches.Count > 0)
            return "Which patient?" + Environment.NewLine + Numbered() + Environment.NewLine + "Or say new.";

        if (_personSaid is null) return "Who is it for? Say the patient's name or file number.";

        var said = _personSaid;
        _personSaid = null;
        await FindPatientsAsync(said, ct);

        if (_patientMatches.Count == 0)
        {
            _offerNew = true;
            return $"No patient called {said} is on file. Add them as a new patient?";
        }

        return $"Patients matching {said}:" + Environment.NewLine + Numbered() + Environment.NewLine
               + "Which one is it? Or say new.";
    }

    private string Numbered() =>
        Answers.Numbered(_patientMatches, m =>
            $"{m.Patient.FullName}, {(m.Born is DateOnly b ? "born " + Answers.Show(b) : "no date of birth")}, "
            + $"file {m.Patient.FileNumber}");

    /// <remarks>
    /// The patient search matches the surname and the file number
    /// (ADM-OBS-12), so a full name is looked up by its surname and the
    /// first names are matched here.
    /// </remarks>
    private async Task FindPatientsAsync(string said, CancellationToken ct)
    {
        var words = said.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        var found = await _patients.SearchAsync(said, take: 50, ct: ct);
        var list = found.IsSuccess ? found.Value.ToList() : new List<PatientListItemDto>();

        if (list.Count == 0 && words.Length > 1)
        {
            var bySurname = await _patients.SearchAsync(words[^1], take: 200, ct: ct);
            var first = words[..^1];

            list = bySurname.IsSuccess
                ? bySurname.Value.Where(p => first.All(f => p.FirstNames.Split(' ')
                      .Any(n => n.StartsWith(f, StringComparison.OrdinalIgnoreCase)))).ToList()
                : new List<PatientListItemDto>();
        }

        var matches = new List<(PatientListItemDto, DateOnly?)>();
        foreach (var p in list.Take(MostMatches))
        {
            var file = await _patients.GetAsync(p.Id, ct);
            matches.Add((p, file.IsSuccess ? file.Value.DateOfBirth : null));
        }

        _patientMatches = matches;
    }

    private async Task<Reply> PatientAsync(string answer, CancellationToken ct)
    {
        var words = Answers.Words(answer);

        if ((_offerNew && Answers.IsYes(answer)) || words.Contains("new"))
        {
            if (_newPatient() is not { } child)
                return Reply.Retry("Adding patients needs the patient permission. Choose someone on file.");

            _offerNew = false;
            return new Reply.Handover(child, NewPatientDoneAsync);
        }

        if (_offerNew && Answers.IsNo(answer)) return Reply.Finish("Nothing was dispensed.");

        _offerNew = false;
        var picked = _patientMatches.Count > 0
            ? Answers.Pick(answer, _patientMatches.Select(m => m.Patient).ToList(), p => p.FullName)
            : null;

        if (picked is null)
        {
            _patientMatches = Array.Empty<(PatientListItemDto, DateOnly?)>();
            _personSaid = answer.Trim();
            return new Reply.Jump("patient");
        }

        return await ChoosePatientAsync(picked.Id, ct);
    }

    private async Task<Reply> NewPatientDoneAsync(GuidedFlow child, CancellationToken ct) =>
        child.Result is { } saved
            ? await ChoosePatientAsync(saved.Id, ct, "Back to dispensing.")
            : Reply.Finish("No patient was added, so nothing was dispensed.");

    private async Task<Reply> ChoosePatientAsync(Guid id, CancellationToken ct, string? note = null)
    {
        var file = await _patients.GetAsync(id, ct);
        if (file.IsFailure) return Reply.Retry(file.Error.Message);

        _patient = file.Value;
        _patientMatches = Array.Empty<(PatientListItemDto, DateOnly?)>();
        return Reply.Ok(note);
    }

    // ---- Allergies first --------------------------------------------------

    private string FirstName => _patient!.FirstNames.Split(' ', StringSplitOptions.RemoveEmptyEntries)
        .FirstOrDefault() ?? _patient.FirstNames;

    private string Clinical()
    {
        var p = _patient!;
        var allergies = p.Allergies.Count == 0
            ? "no known allergies"
            : "allergic to " + string.Join(", ", p.Allergies.Select(a => $"{a.Substance} ({a.Severity.ToLowerInvariant()})"));
        var conditions = p.ChronicConditions.Count == 0 ? "" : "; conditions: " + string.Join(", ", p.ChronicConditions);

        return $"{p.FirstNames} {p.Surname}, file {p.FileNumber}{(p.Age is int age ? $", age {age}" : "")}: "
               + allergies + conditions + ".";
    }

    /// What the patient's allergies say about a medicine, by what it is.
    private IReadOnlyList<AllergyMatch> Clashes(string medicine, string? generic = null) =>
        AllergyScreen.Check(_patient!.Allergies.Select(a => Domain.Patients.Allergy.Create(Guid.Empty, a.Substance,
            a.Severity, a.Reaction)), medicine, generic);

    private string AllergyPrompt()
    {
        var clashes = _term is null ? Array.Empty<AllergyMatch>() : Clashes(_term);

        if (clashes.Count == 0)
        {
            Say(Clinical());
            return "";
        }

        var clash = clashes[0];
        var what = clash.Kind == AllergyMatchKind.Direct
            ? $"{Capital(_term!)} contains it"
            : $"{Capital(_term!)} {ClassOf(clash.Explanation)}";

        return $"{Clinical()}{Environment.NewLine}{FirstName} is allergic to {clash.Allergy.Substance}. {what}. Continue?";
    }

    private static string ClassOf(string explanation)
    {
        var match = ClassPattern().Match(explanation);
        return match.Success ? $"is a {match.Groups["c"].Value}" : "may contain it";
    }

    private static string Capital(string text) =>
        text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static Reply Allergy(string answer)
    {
        if (Answers.IsYes(answer)) return Reply.Ok("Noted. The warning still has to be overridden with a reason before issue.");
        if (Answers.IsNo(answer)) return Reply.Finish("Stopped at the allergy check. Nothing was dispensed.");

        return Reply.Retry("Yes to carry on, or no to stop.");
    }

    // ---- The script -------------------------------------------------------

    private async Task<string> ScriptPromptAsync(CancellationToken ct)
    {
        var scripts = await _scripts.ForPatientAsync(_patient!.Id, ct);

        _existingScripts = scripts.IsSuccess
            ? scripts.Value.Where(s => s.RepeatsRemaining > 0 && (s.ValidUntil is null || s.ValidUntil >= _clock.Today))
                .OrderByDescending(s => s.PrescribedOn).Take(MostMatches).ToList()
            : Array.Empty<ScriptDto>();

        if (_existingScripts.Count == 0)
        {
            Say("No script with repeats left is on file, so this is a new script.");
            return "";
        }

        return "New script or an existing one?" + Environment.NewLine + Answers.Numbered(_existingScripts, s =>
            $"{s.ScriptNumber} from {s.PrescriberName}, {Answers.Show(s.PrescribedOn)}, {s.RepeatsRemaining} repeat(s) "
            + $"left: {s.Items.Replace(Environment.NewLine, "; ", StringComparison.Ordinal)}")
            + Environment.NewLine + "Say new, or the number of the script to repeat.";
    }

    private Reply Script(string answer)
    {
        if (answer.Length == 0 || Answers.Words(answer).Contains("new")) return Reply.Ok();

        var chosen = Answers.Pick(answer, _existingScripts, s => s.ScriptNumber);
        if (chosen is null) return Reply.Retry("Say new, or choose a script by its number in the list.");

        _existing = chosen;
        return Reply.Ok($"Repeating {chosen.ScriptNumber}: its items go on as prescribed.");
    }

    private string PrescriberPrompt() =>
        _prescriberMatches.Count > 1
            ? "Which prescriber?" + Environment.NewLine
              + Answers.Numbered(_prescriberMatches, p => $"{p.Name}{(p.Detail is null ? "" : " - " + p.Detail)}")
            : "Prescriber? Say their name.";

    private async Task<Reply> PrescriberAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("A script needs its prescriber.");

        var chosen = _prescriberMatches.Count > 1 ? Answers.Pick(answer, _prescriberMatches, p => p.Name) : null;

        if (chosen is null)
        {
            var found = await _reference.ListAsync(ReferenceKind.Prescriber, answer.Trim(), includeInactive: false, ct);
            _prescriberMatches = found.IsSuccess ? found.Value.Take(MostMatches).ToList() : Array.Empty<ReferenceItemDto>();

            if (_prescriberMatches.Count > 1) return new Reply.Jump("prescriber");

            if (_prescriberMatches.Count == 0)
            {
                var typed = answer.Trim();
                return Reply.YesNo($"{typed} is not on the prescriber list. Use the name as typed?", _ =>
                {
                    (_prescriber, _prescriberNumber) = (typed, null);
                    return Task.FromResult(Reply.Ok());
                }, "Then who is the prescriber?");
            }

            chosen = _prescriberMatches[0];
        }

        _prescriber = chosen.Name;
        _prescriberNumber = PracticePattern().Match(chosen.Detail ?? "") is { Success: true } m ? m.Groups["n"].Value : null;
        _prescriberMatches = Array.Empty<ReferenceItemDto>();

        return Reply.Ok($"Prescriber {chosen.Name}{(_prescriberNumber is null ? "" : $", practice {_prescriberNumber}")}.");
    }

    private Reply ScriptDate(string answer)
    {
        if (answer.Length == 0)
        {
            _scriptDate = _clock.Today;
            return Reply.Ok();
        }

        if (Answers.Date(answer) is not DateOnly date) return Reply.Retry("Say the date as dd/mm/yyyy.");
        if (date > _clock.Today) return Reply.Retry("A script can't be dated in the future.");

        _scriptDate = date;
        return date < _clock.Today.AddMonths(-6)
            ? Reply.Ok("That script is over six months old. Check it is still valid.")
            : Reply.Ok();
    }

    private Reply Repeats(string answer)
    {
        if (Answers.IsSkip(answer))
        {
            _repeats = 0;
            return Reply.Ok();
        }

        if (Answers.Number(answer) is not decimal n || n < 0 || n > 12 || n != Math.Floor(n))
            return Reply.Retry("How many repeats, 0 to 12?");

        _repeats = (int)n;
        return Reply.Ok();
    }

    // ---- Each item --------------------------------------------------------

    private async Task<string> ItemPromptAsync(CancellationToken ct)
    {
        _product = null;
        if (_term is null) return "Which medicine?";

        var found = await _products.SearchAsync(_term, take: MostMatches, includeInactive: false, ct: ct);
        _productMatches = found.IsSuccess ? found.Value.Items : Array.Empty<ProductListItemDto>();

        if (_productMatches.Count == 0) return $"Nothing matches \"{_term}\". Which medicine?";

        return $"Which {_term}?" + Environment.NewLine + Answers.Numbered(_productMatches, p =>
            $"{p.Description}{(string.IsNullOrWhiteSpace(p.PackSize) ? "" : " " + p.PackSize)} - "
            + $"{Answers.Quantity(p.QuantityOnHand)} on hand - {Answers.Show(p.RetailPrice)}");
    }

    private async Task<Reply> ItemAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("Which medicine?");

        if (Answers.IsOffList(answer, _productMatches.Count))
            return Reply.Retry($"Choose 1 to {_productMatches.Count}.");

        var chosen = _productMatches.Count > 0 ? Answers.Pick(answer, _productMatches, p => p.Description) : null;

        if (chosen is null)
        {
            var scanned = await _products.FindAsync(answer.Trim(), ct);
            chosen = scanned.IsSuccess ? scanned.Value : null;
        }

        if (chosen is null)
        {
            _term = answer.Trim();
            return new Reply.Jump("item");
        }

        _product = chosen;

        // Checked again against the medicine actually chosen.
        var clashes = Clashes(chosen.Description);
        if (clashes.Count > 0 && (_term is null || Clashes(_term).Count == 0))
            return Reply.YesNo($"{clashes[0].Explanation} Continue?", _ => Task.FromResult(Reply.Ok()),
                "Then which medicine?");

        return Reply.Ok(chosen.QuantityOnHand <= 0m ? $"There is none of {chosen.Description} on hand." : null);
    }

    private string Unit => Directions.UnitOf(_product!.PackSize, _product.Description);

    private decimal UnitsInPack => _product!.UnitsPerPack is decimal u && u > 0m ? u : 1m;

    private string QuantityPrompt()
    {
        var enter = Dispensing.DispensingCharge.WhatToEnter(Unit, UnitsInPack);

        return _askedQuantity is decimal asked
            ? $"Quantity? {enter} Press Enter for {Answers.Quantity(asked)}."
            : $"Quantity? {enter}";
    }

    private async Task<Reply> QuantityAsync(string answer, CancellationToken ct)
    {
        decimal? quantity = answer.Length == 0 ? _askedQuantity : Answers.Number(answer);
        if (quantity is not > 0m) return Reply.Retry("How many, more than nought?");

        _quantity = quantity.Value;
        _askedQuantity = null;

        // The charge as the dispensing will work it out; a medical aid's
        // tariff, where it has one, decides it instead.
        _roundTo ??= _settings is null ? Dispensing.DispensingCharge.DefaultRoundTo
            : await Dispensing.DispensingCharge.RoundToAsync(_settings, ct);

        var charge = Dispensing.DispensingCharge.Explain(_product!.RetailPrice, UnitsInPack, Unit, _quantity,
                                                         _roundTo.Value);

        return Reply.Ok(_patient?.IsOnMedicalAid == true
            ? $"{charge} (A medical aid tariff, where there is one, prices it instead.)"
            : charge);
    }

    private decimal? _roundTo;

    private Reply Directions_(string answer)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("Directions are required for the label, e.g. 1 TDS 5/7.");

        var written = Directions.Read(answer, Unit);
        _directions = written.Text;

        if (written.Needed is decimal needed && needed != _quantity)
            return Reply.YesNo(
                $"\"{written.Text}\" needs {Answers.Quantity(needed)}, but the quantity is "
                + $"{Answers.Quantity(_quantity)}. Keep {Answers.Quantity(_quantity)}?",
                _ => Task.FromResult(Reply.Ok(written.Text)),
                _ => Task.FromResult<Reply>(new Reply.Jump("quantity", "Then the quantity again.")));

        return Reply.Ok(written.Text);
    }

    private Reply LabelExtra(string answer)
    {
        if (!Answers.IsSkip(answer))
            _directions = $"{_directions.TrimEnd('.')}. {Capital(answer.Trim().TrimEnd('.'))}.";

        _items.Add(new Item(_product!, _quantity, _directions));
        return Reply.Ok();
    }

    private Reply Another(string answer)
    {
        if (Answers.IsNo(answer) || Answers.IsSkip(answer)) return Reply.Ok();

        _term = Answers.IsYes(answer) ? null : answer.Trim();
        return new Reply.Jump("item");
    }

    // ---- Charges ----------------------------------------------------------

    /// <summary>
    /// The script captured and put on a dispensing, which is where the
    /// tariff, the medical aid's share and the clinical warnings come from.
    /// </summary>
    private async Task<string?> BeginAsync(CancellationToken ct)
    {
        Guid scriptId;

        if (_existing is { } existing)
        {
            scriptId = existing.Id;
        }
        else
        {
            var captured = await _scripts.CaptureAsync(new ScriptEditDto(_patient!.Id, _prescriber ?? "",
                _prescriberNumber, _scriptDate, null, _repeats,
                _items.Select(i => new ScriptItemEditDto(i.Product.Code, i.Quantity, i.Directions)).ToList()), ct);

            if (captured.IsFailure) return captured.Error.Message;
            _capturedScriptId = scriptId = captured.Value;
        }

        var begun = await _dispensing.BeginAsync(scriptId, ct);
        if (begun.IsFailure) return begun.Error.Message;
        _dispensingId = begun.Value.Id;

        var added = await _dispensing.AddScriptItemsAsync(begun.Value.Id, ct);
        if (added.IsFailure) return added.Error.Message;

        _state = added.Value.State;
        return added.Value.Problems.Count == 0
            ? null
            : "Not everything could go on: " + string.Join("; ", added.Value.Problems);
    }

    private bool MayAdjust => _permissions.Has(PermissionCode.DispensingChargeAdjust);

    private string Charges()
    {
        var text = new StringBuilder();

        foreach (var line in _state!.Items)
            text.Append(
                $"{line.LineNo}. {line.Description} x{Answers.Quantity(line.Quantity)}: tariff {Answers.Show(line.Tariff)}, "
                + $"medical aid {Answers.Show(line.Contribution)}, patient pays {Answers.Show(line.CoPayment)}"
                + Environment.NewLine);

        text.Append(CultureInfo.InvariantCulture,
            $"Total: tariff {Answers.Show(_state.TariffTotal)}, medical aid {Answers.Show(_state.ContributionTotal)}, "
            + $"co-payment {Answers.Show(_state.CoPaymentTotal)}.");

        return text.ToString();
    }

    private async Task<string> ChargesPromptAsync(CancellationToken ct)
    {
        if (_dispensingId is null)
        {
            var problem = await BeginAsync(ct);
            if (_state is null) return $"It could not be put on a dispensing: {problem} Say cancel to stop.";
            Say(problem);
        }

        if (!MayAdjust)
        {
            Say(Charges());
            return "";
        }

        return Charges() + Environment.NewLine + "Adjust the charge? Say the item and the amount, e.g. \"1 to 12.00\", "
               + "or press Enter to leave it.";
    }

    private Reply ChargesAsync(string answer)
    {
        if (_state is null) return Reply.Retry("Say cancel to stop.");
        if (Answers.IsSkip(answer) || Answers.IsNo(answer)) return Reply.Ok();

        var numbers = NumbersPattern().Matches(answer).Select(m =>
            decimal.Parse(m.Value, CultureInfo.InvariantCulture)).ToList();

        var (lineNo, amount) = numbers.Count switch
        {
            1 when _state.Items.Count == 1 => (_state.Items[0].LineNo, numbers[0]),
            >= 2 => ((int)numbers[0], numbers[1]),
            _ => (0, 0m)
        };

        if (_state.Items.All(l => l.LineNo != lineNo) || amount < 0m)
            return Reply.Retry("Say the item number and the new amount, e.g. \"1 to 12.00\".");

        (_chargeLine, _chargeAmount) = (lineNo, amount);
        return Reply.Ok();
    }

    private async Task<Reply> ChargeReasonAsync(string answer, CancellationToken ct)
    {
        if (Answers.IsSkip(answer)) return Reply.Retry("A reason is required to change the charge.");

        var line = _state!.Items.First(l => l.LineNo == _chargeLine);
        var amended = await _dispensing.AmendItemAsync(_dispensingId!.Value, line.LineNo, line.Quantity,
            _chargeAmount, answer.Trim(), ct: ct);

        if (amended.IsFailure && amended.Error.Code == DispensingService.ChargeNeedsAuthority)
        {
            var authoriser = await _prompts.AuthoriseAsync(PermissionCode.DispensingChargeAdjust, amended.Error.Message, ct);
            if (authoriser is not null)
                amended = await _dispensing.AmendItemAsync(_dispensingId.Value, line.LineNo, line.Quantity,
                    _chargeAmount, answer.Trim(), authoriser, ct);
        }

        _chargeLine = null;
        if (amended.IsFailure) return new Reply.Jump("charges", $"The charge was not changed: {amended.Error.Message}");

        _state = amended.Value.State;
        return new Reply.Jump("charges", "Charge changed.");
    }

    // ---- Warnings ---------------------------------------------------------

    private async Task<string> WarningPromptAsync(CancellationToken ct)
    {
        _warning = null;
        if (_dispensingId is not Guid id) return "";

        foreach (var warning in await _dispensing.PendingWarningsAsync(id, ct))
        {
            if (!warning.IsHardStop)
            {
                // Said, and acknowledged as the dispensing screen does.
                var soft = await _dispensing.AcknowledgeWarningAsync(warning.Id, null, ct: ct);
                if (soft.IsSuccess) Say($"Note: {warning.Message}");
                continue;
            }

            _warning = warning;
            return $"Warning: {warning.Message}{Environment.NewLine}Override it? Give the reason, or say no to stop.";
        }

        return "";
    }

    private async Task<Reply> WarningAsync(string answer, CancellationToken ct)
    {
        var warning = _warning!;

        if (Answers.IsNo(answer)) return Reply.Finish("Not dispensed: the warning was not overridden.");
        if (Answers.IsSkip(answer) || Answers.IsYes(answer))
            return Reply.Retry("Overriding needs a written reason, e.g. prescriber confirmed by phone.");

        Guid? pharmacist = null;
        if (await _dispensing.OverrideNeedsPharmacistAsync(warning.Id, ct))
        {
            pharmacist = await _prompts.AuthoriseAsync(PermissionCode.ScriptAuthorise,
                "A pharmacist must authorise overriding this warning:" + Environment.NewLine + warning.Message, ct);

            if (pharmacist is null) return Reply.Finish("Not dispensed: no pharmacist authorised the override.");
        }

        var done = await _dispensing.AcknowledgeWarningAsync(warning.Id, answer.Trim(), pharmacist, ct);
        if (done.IsFailure) return Reply.Retry(done.Error.Message);

        _overrides.Add($"{warning.Message} - overridden: {answer.Trim()}");

        // The next warning, if there is one.
        return new Reply.Jump("warning");
    }

    // ---- Labels, and issue ------------------------------------------------

    private Reply Labels(string answer)
    {
        var words = Answers.Words(answer);

        if (answer.Length == 0 || Answers.IsYes(answer) || words.Contains("both"))
            (_printLabels, _printTrailer) = (true, true);
        else if (Answers.IsNo(answer) || words.Contains("neither") || words.Contains("none"))
            (_printLabels, _printTrailer) = (false, false);
        else if (words.Contains("trailer") && (words.Contains("no") || words.Contains("without")))
            (_printLabels, _printTrailer) = (true, false);
        else if (words.Contains("labels") || words.Contains("only"))
            (_printLabels, _printTrailer) = (true, false);
        else
            return Reply.Retry("Both, labels only, or neither?");

        return Reply.Ok();
    }

    public string Summary()
    {
        var text = new StringBuilder($"For {_patient?.FirstNames} {_patient?.Surname} (file {_patient?.FileNumber}), ");
        text.Append(_existing is { } e
            ? $"repeat of {e.ScriptNumber} from {e.PrescriberName}:"
            : $"script from {_prescriber}, {Answers.Show(_scriptDate)}, {_repeats} repeat(s):");

        foreach (var line in _state?.Items ?? Array.Empty<DispensingLineDto>())
            text.Append(CultureInfo.InvariantCulture,
                $"{Environment.NewLine}{line.LineNo}. {line.Description} x{Answers.Quantity(line.Quantity)} - "
                + $"{line.Directions}{(line.BatchNumber is null ? "" : $" (batch {line.BatchNumber})")}");

        if (_state is not null)
            text.Append(CultureInfo.InvariantCulture,
                $"{Environment.NewLine}Tariff {Answers.Show(_state.TariffTotal)}, medical aid "
                + $"{Answers.Show(_state.ContributionTotal)}, co-payment {Answers.Show(_state.CoPaymentTotal)}");

        foreach (var overridden in _overrides)
            text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}Warning: {overridden}");

        text.Append(CultureInfo.InvariantCulture, $"{Environment.NewLine}Labels: {(_printLabels ? "yes" : "no")}, "
                                                  + $"trailer: {(_printTrailer ? "yes" : "no")}");
        return text.ToString();
    }

    private async Task<Reply> IssueAsync(CancellationToken ct)
    {
        if (_dispensingId is not Guid id) return Reply.Finish("Nothing was put on a dispensing.");
        if (_state is null || _state.Items.Count == 0)
            return Reply.Finish("Nothing could go on the dispensing, so nothing was issued.");

        // Schedule 3 and up, and anything else the dispensing says needs
        // it, is confirmed by a pharmacist's own sign-in.
        var authority = await _dispensing.IssueAuthorityAsync(id, ct);
        if (authority.IsFailure) return Reply.Retry(authority.Error.Message);

        Guid? pharmacist = null;
        if (!authority.Value.OperatorMayIssue)
        {
            pharmacist = await _prompts.AuthoriseAsync(PermissionCode.ScriptAuthorise,
                authority.Value.Reason ?? "A pharmacist must authorise this.", ct);

            if (pharmacist is null)
                return Reply.Retry("Not issued: a pharmacist has to confirm it. Say yes to ask again, or cancel.");
        }

        var issued = await _dispensing.IssueAsync(id, pharmacist, ct);
        if (issued.IsFailure) return Reply.Retry($"It was not issued: {issued.Error.Message}");

        Result = new GuidedResult(AppTaskKind.Dispense, id, issued.Value.ScriptNumber);

        var printed = new List<string>();
        if (_printLabels && _labels is { } labels)
        {
            foreach (var label in issued.Value.Labels)
                printed.Add((await labels.PrintAsync(label, ct)).Describe($"Label for {label.ProductDescription}"));

            if (_printTrailer && issued.Value.Trailer is { } trailer)
                printed.Add((await labels.PrintTrailerAsync(trailer, ct)).Describe("The trailer"));
        }

        return Reply.Finish(Join(
            $"Issued script {issued.Value.ScriptNumber}. Co-payment {Answers.Show(issued.Value.CoPayment)} is in the "
            + "collection queue.",
            issued.Value.ClaimNumber is string claim ? $"Claim {claim} was raised on the medical aid." : null,
            _printLabels && _labels is not null
                ? string.Join(Environment.NewLine, printed)
                : "No labels printed: reprint them from Dispensary > Labels."));
    }

    [GeneratedRegex(@"is an? (?<c>[a-z ]+?)(\s*\(|\.|,)")]
    private static partial Regex ClassPattern();

    [GeneratedRegex(@"Practice\s+(?<n>[\w/-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex PracticePattern();

    [GeneratedRegex(@"\d+(\.\d+)?")]
    private static partial Regex NumbersPattern();
}