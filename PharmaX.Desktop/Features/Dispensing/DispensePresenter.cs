using PharmaX.Application.Claims;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Sales;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Find the script, check what goes on, issue, label.
/// </summary>
/// <remarks>
/// The flow asks for nobody else's password unless the business rule
/// needs one. It used to demand a pharmacist's credentials for every
/// issue, whoever was dispensing, so a pharmacist authorised their own
/// work and an assistant could do nothing without calling someone over.
/// A pharmacist, an assistant or the administrator now issues on their
/// own authority; a pharmacist's credentials are asked for only to
/// release a hard-stop clinical warning or a controlled medicine when the
/// person at the screen does not hold that authority themselves.
/// </remarks>
public sealed class DispensePresenter : PresenterBase<IDispenseView>
{
    private readonly IDispensingService _dispensing;
    private readonly IScriptService _scripts;
    private readonly Application.Patients.IPatientService _patients;
    private readonly ILabelPrinter _labels;
    private readonly IClaimFormService _claimForms;
    private readonly IClaimFormPrinter _claimFormPrinter;

    private Guid _dispensingId;

    public DispensePresenter(IDispensingService dispensing,
                             IScriptService scripts,
                             Application.Patients.IPatientService patients,
                             ILabelPrinter labels,
                             IClaimFormService claimForms,
                             IClaimFormPrinter claimFormPrinter)
    {
        _dispensing = dispensing;
        _scripts = scripts;
        _patients = patients;
        _labels = labels;
        _claimForms = claimForms;
        _claimFormPrinter = claimFormPrinter;
    }

    protected override void Subscribe()
    {
        View.ScriptRequested += (_, _) => Enqueue(BeginAsync);
        View.QuoteRequested += (_, _) => Enqueue(QuoteAsync);
        View.ProductScanned += (_, code) => Enqueue(() => AddItemAsync(code));
        View.RemoveItemRequested += (_, _) => Enqueue(RemoveItemAsync);
        View.ChangeItemRequested += (_, _) => Enqueue(ChangeItemAsync);
        View.IssueRequested += (_, _) => Enqueue(IssueAsync);
        View.LabelReprintRequested += (_, _) => View.ShowLabelScreen();
        View.CloseRequested += (_, _) => Enqueue(CloseAsync);
        View.SuspendedRequested += (_, _) => Enqueue(ResumeAsync);
    }

    /// <summary>
    /// A dispensing begun on this screen and not issued from it.
    /// </summary>
    public bool HasUnissuedDispensing => _dispensingId != Guid.Empty;

    /// <summary>
    /// Closes the screen, keeping a dispensing that was begun and never
    /// issued to finish later or setting it aside, as the pharmacist
    /// says (ADM-OBS-14).
    /// </summary>
    private Task CloseAsync() => GuardAsync(async () =>
    {
        await AbandonCurrentAsync();
        View.CloseView(true);
    });

    private async Task AbandonCurrentAsync()
    {
        if (_dispensingId == Guid.Empty) return;

        if (!View.Confirm("Keep this script to finish later?\n\n"
                          + "Yes keeps it under Suspended. No sets it aside."))
            await _dispensing.AbandonAsync(_dispensingId);

        _dispensingId = Guid.Empty;
    }

    /// A script kept to finish later, picked up where it was left.
    private Task ResumeAsync() => GuardAsync(async () =>
    {
        var kept = await _dispensing.SuspendedAsync();

        if (kept.Count == 0)
        {
            View.ShowInfo("No scripts are kept to finish later.");
            return;
        }

        if (View.PromptSuspended(kept) is var (patientId, scriptId))
            await BeginOnAsync(patientId, scriptId);
    });

    /// A script captured from a quote on the way in is begun at once.
    public override Task InitialiseAsync() =>
        View.StartWith is var (patientId, scriptId)
            ? GuardAsync(() => BeginOnAsync(patientId, scriptId))
            : Task.CompletedTask;

    private Task BeginAsync() => GuardAsync(async () =>
    {
        var patientId = View.PromptPatientSearch();
        if (patientId is null) return;

        var scriptId = View.PromptScriptSearch(patientId.Value);
        if (scriptId is null) return;

        await BeginOnAsync(patientId.Value, scriptId.Value);
    });

    /// <summary>
    /// Prices the medicine for a customer who has not decided, and
    /// dispenses the script it becomes if they go ahead.
    /// </summary>
    private Task QuoteAsync() => GuardAsync(async () =>
    {
        if (View.PromptQuote() is var (patientId, scriptId))
            await BeginOnAsync(patientId, scriptId);
    });

    private async Task BeginOnAsync(Guid patientId, Guid scriptId)
    {
        // Another script loaded over one not issued: the first is set
        // aside, not left behind.
        await AbandonCurrentAsync();

        var result = await _dispensing.BeginAsync(scriptId);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _dispensingId = result.Value.Id;

        // The header comes from the patient and the script rather than
        // from the dispensing, because it is what the pharmacist checks
        // before the first item goes on.
        var patient = await _patients.GetAsync(patientId);
        if (patient.IsSuccess)
            View.ShowPatient(ToSummary(patient.Value));

        var script = await _scripts.GetAsync(scriptId);
        if (script.IsSuccess) View.ShowScript(script.Value.Script);

        // Kept to finish later: back as it was left, items and all.
        if (result.Value.Items.Count > 0)
        {
            Render(result.Value);
            return;
        }

        // What the prescriber wrote goes straight on, quantity and
        // directions included: re-keying a script that was captured a
        // minute ago is how a 21 becomes a 1 (DEF-0020).
        var added = await _dispensing.AddScriptItemsAsync(_dispensingId);

        if (added.IsFailure)
        {
            View.ShowError(added.Error.Message);
            Render(result.Value);
            return;
        }

        Render(added.Value.State);

        if (added.Value.Problems.Count > 0)
            View.ShowError("Not everything on the script could go on:\n\n"
                           + string.Join("\n", added.Value.Problems));
    }

    private Task AddItemAsync(string code) => GuardAsync(async () =>
    {
        if (_dispensingId == Guid.Empty)
        {
            View.ShowError("Find the patient and the script first.");
            return;
        }

        // The script's own quantity and directions for this product, as
        // the starting point of the prompts rather than a blank.
        var proposal = await _dispensing.ProposeItemAsync(_dispensingId, code);

        if (proposal.IsFailure)
        {
            View.ShowError(proposal.Error.Message);
            return;
        }

        var directions = View.PromptDirections(proposal.Value);
        if (directions is null) return;

        var result = await _dispensing.AddItemAsync(
            _dispensingId, proposal.Value.ProductCode, directions);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        // Warnings are computed by the service from the clinical profile
        // and the interaction table; the presenter only routes them.
        Render(result.Value);
    });

    private Task RemoveItemAsync() => GuardAsync(async () =>
    {
        if (_dispensingId == Guid.Empty || View.SelectedLineNo is not int lineNo)
        {
            View.ShowInfo("Choose the item to take off.");
            return;
        }

        var result = await _dispensing.RemoveItemAsync(_dispensingId, lineNo);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        Render(result.Value);
    });

    /// What is on the dispensing now, for the questions a change asks.
    private IReadOnlyList<DispensingLineDto> _items = Array.Empty<DispensingLineDto>();

    /// <summary>
    /// A different quantity or amount for an item before the issue. It is
    /// priced, taken from stock and screened again as a new item is.
    /// </summary>
    private Task ChangeItemAsync() => GuardAsync(async () =>
    {
        if (_dispensingId == Guid.Empty || _items.Count == 0)
        {
            View.ShowInfo("Put an item on first.");
            return;
        }

        if (View.PromptItemChange(_items, View.SelectedLineNo) is not { } answer)
            return;

        var result = await _dispensing.AmendItemAsync(_dispensingId,
            answer.LineNo, answer.Quantity, answer.Amount, reason: null);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        Render(result.Value.State);
    });

    /// <summary>
    /// The patient DTO the patient screen returns carries more than the
    /// dispensing header needs, so it is narrowed here rather than the
    /// view being handed a type from another feature.
    /// </summary>
    private static PatientSummaryDto ToSummary(
        Application.Patients.Dtos.PatientDto p) =>
        new(p.Id, p.FileNumber, $"{p.FirstNames} {p.Surname}".Trim(),
            p.Age, p.Telephone, null, null,
            p.Allergies.Select(a => $"{a.Substance} ({a.Severity})").ToList(),
            p.ChronicConditions);

    private void Render(DispensingStateDto state)
    {
        _items = state.Items;
        View.ShowWarnings(state.Warnings);
        View.ShowItems(state.Items);
        View.ShowCoPayment(state.TariffTotal, state.ContributionTotal,
                           state.CoPaymentTotal);
    }

    private Task IssueAsync() => GuardAsync(async () =>
    {
        if (_dispensingId == Guid.Empty)
        {
            View.ShowError("Find the patient and the script first.");
            return;
        }

        if (!await ClearWarningsAsync()) return;

        // A pharmacist's credentials only where something on the
        // dispensing needs one and the operator cannot give it.
        var authority = await _dispensing.IssueAuthorityAsync(_dispensingId);

        if (authority.IsFailure)
        {
            View.ShowError(authority.Error.Message);
            return;
        }

        Guid? authorisedBy = null;

        if (!authority.Value.OperatorMayIssue)
        {
            authorisedBy = View.PromptPharmacistAuthorisation(
                authority.Value.Reason ?? "A pharmacist must authorise this.");

            if (authorisedBy is null)
            {
                View.ShowError("Not issued: no pharmacist authorisation.");
                return;
            }
        }

        var result = await _dispensing.IssueAsync(_dispensingId, authorisedBy);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        var printed = new List<PrintResult>();

        foreach (var label in result.Value.Labels)
            printed.Add(await _labels.PrintAsync(label));

        // The whole script on one more label, after the item labels.
        if (result.Value.Trailer is { } trailer)
            printed.Add(await _labels.PrintTrailerAsync(trailer));

        // Issued, so there is nothing to set aside on the way out.
        var issued = _dispensingId;
        _dispensingId = Guid.Empty;

        // The member signs the claim form at the counter, so it is
        // offered while they are still standing there. Asked rather than
        // printed every time: some schemes take the claim electronically
        // and want no paper at all.
        var claimForm = "";

        if (result.Value.ClaimNumber is string claimNumber
            && View.Confirm($"Claim {claimNumber} has been raised on the "
                            + "medical aid. Print the claim form for the "
                            + "member to sign?"))
            claimForm = "\n\n" + await PrintClaimFormAsync(issued);

        View.ShowInfo($"Issued. Script {result.Value.ScriptNumber}. "
                      + $"Co-payment {result.Value.CoPayment:N2} "
                      + "has been added to the collection queue.\n\n"
                      + DescribeLabels(printed) + claimForm);

        View.CloseView(true);
    });

    /// <summary>
    /// Deals with every warning still standing. False if the pharmacist
    /// backed out of one.
    /// </summary>
    private async Task<bool> ClearWarningsAsync()
    {
        var pending = await _dispensing.PendingWarningsAsync(_dispensingId);

        foreach (var warning in pending)
        {
            if (!warning.IsHardStop)
            {
                var soft = await _dispensing.AcknowledgeWarningAsync(
                    warning.Id, null);

                if (soft.IsFailure)
                {
                    View.ShowError(soft.Error.Message);
                    return false;
                }

                continue;
            }

            // A hard stop is passed only with a written reason, kept on the
            // clinical record (DSP-007).
            var reason = View.PromptWarningOverride(warning);

            if (string.IsNullOrWhiteSpace(reason))
            {
                View.ShowError("Not issued: the warning was not overridden.");
                return false;
            }

            Guid? pharmacist = null;

            if (await _dispensing.OverrideNeedsPharmacistAsync(warning.Id))
            {
                pharmacist = View.PromptPharmacistAuthorisation(
                    "A pharmacist must authorise overriding this warning:\n"
                    + warning.Message);

                if (pharmacist is null)
                {
                    View.ShowError("Not issued: no pharmacist authorised the "
                                   + "override.");
                    return false;
                }
            }

            var acknowledged = await _dispensing.AcknowledgeWarningAsync(
                warning.Id, reason, pharmacist);

            if (acknowledged.IsFailure)
            {
                View.ShowError(acknowledged.Error.Message);
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Prints the claim form for a dispensing and says what happened.
    /// </summary>
    private async Task<string> PrintClaimFormAsync(Guid dispensingId)
    {
        var form = await _claimForms.ForDispensingAsync(dispensingId);

        if (form.IsFailure) return form.Error.Message;

        var printed = await _claimFormPrinter.PrintAsync(form.Value);
        var what = $"The claim form for {form.Value.ClaimNumber}";

        return printed.Printed
            ? printed.Describe(what)
            : printed.Describe(what)
              + " Print it from Dispensary > Labels once the printer is ready.";
    }

    /// <summary>
    /// What happened to the labels, honestly: printed where, or why not
    /// and where to reprint them.
    /// </summary>
    private static string DescribeLabels(IReadOnlyList<PrintResult> results)
    {
        if (results.Count == 0) return "";

        var failed = results.Where(r => !r.Printed).ToList();

        if (failed.Count == 0)
        {
            var printer = results[0].PrinterName;
            var note = results.Select(r => r.Problem)
                              .FirstOrDefault(n => n is not null);

            return $"{results.Count} label(s) printed"
                   + (printer is null ? "." : $" on {printer}.")
                   + (note is null ? "" : "\n" + note);
        }

        return $"{failed.Count} of {results.Count} label(s) did not print: "
               + failed[0].Problem
               + "\nReprint them from Dispensary > Labels once the printer "
               + "is ready.";
    }
}
