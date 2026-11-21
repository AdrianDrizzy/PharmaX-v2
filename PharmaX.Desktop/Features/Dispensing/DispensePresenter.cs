using PharmaX.Application.Abstractions;
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
    private readonly IWorkstationSettings _settings;

    private Guid _dispensingId;

    public DispensePresenter(IDispensingService dispensing,
                             IScriptService scripts,
                             Application.Patients.IPatientService patients,
                             ILabelPrinter labels,
                             IClaimFormService claimForms,
                             IClaimFormPrinter claimFormPrinter,
                             IWorkstationSettings settings)
    {
        _settings = settings;
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
        View.PrintLabelsChanged += (_, _) => Enqueue(() => GuardAsync(() =>
            _settings.SetAsync(WorkstationSettingKey.PrintLabels,
                View.PrintLabels ? "on" : "off")));
        View.PrintTrailerChanged += (_, _) => Enqueue(() => GuardAsync(() =>
            TrailerLabels.SetWantedAsync(_settings, View.PrintTrailer)));
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

    /// <summary>
    /// Opens straight onto the work. A script captured from a quote on the
    /// way in is begun at once; otherwise the patient and the script are
    /// asked for, which every dispensing starts with and which used to
    /// wait for a button. Cancelled, the screen waits on Find script.
    /// </summary>
    public override async Task InitialiseAsync()
    {
        await GuardAsync(async () =>
        {
            View.PrintLabels = !string.Equals(
                await _settings.GetAsync(WorkstationSettingKey.PrintLabels),
                "off", StringComparison.OrdinalIgnoreCase);
            View.PrintTrailer = await TrailerLabels.WantedAsync(_settings);
            View.ShowStage(Stage);
        });

        if (View.StartWith is var (patientId, scriptId))
            await GuardAsync(() => BeginOnAsync(patientId, scriptId));
        else
            await BeginAsync();
    }

    /// Where the dispensing on the screen has got to.
    private DispenseStage Stage =>
        _dispensingId == Guid.Empty ? DispenseStage.NoScript
        : _items.Count == 0 ? DispenseStage.NoItems
        : DispenseStage.Ready;

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
            View.ShowStage(Stage);
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

        // No batch in stock: the box in hand has one. Cancelled, the item
        // goes on as before and what is missing is owed.
        if (proposal.Value.NeedsBatchFromBox
            && View.AskBatchFromBox(proposal.Value.Description) is { } box)
        {
            var found = await _dispensing.RecordFoundBatchAsync(
                proposal.Value.ProductId, box.Batch, box.Expiry, directions.Quantity);

            if (found.IsFailure)
            {
                View.ShowError(found.Error.Message);
                return;
            }
        }

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
        var lineNos = View.SelectedLineNos;

        if (_dispensingId == Guid.Empty || lineNos.Count == 0)
        {
            View.ShowInfo("Choose the item to take off.");
            return;
        }

        var failed = new List<string>();
        DispensingStateDto? latest = null;

        // Bottom up, in case taking one off numbers the rest again.
        foreach (var lineNo in lineNos.Reverse())
        {
            var result = await _dispensing.RemoveItemAsync(_dispensingId, lineNo);

            if (result.IsFailure)
                failed.Add(lineNos.Count == 1
                    ? result.Error.Message
                    : $"Item {lineNo}: {result.Error.Message}");
            else
                latest = result.Value;
        }

        if (latest is not null) Render(latest);
        if (failed.Count > 0) View.ShowError(string.Join("\n", failed));
    });

    /// What is on the dispensing now, for the questions a change asks.
    private IReadOnlyList<DispensingLineDto> _items = Array.Empty<DispensingLineDto>();

    /// <summary>
    /// A different quantity or amount for an item before the issue. It is
    /// priced, taken from stock and screened again as a new item is.
    /// </summary>
    /// <remarks>
    /// A charge other than the price needs dsp.charge.adjust. Where the
    /// operator does not hold it, someone who does - a pharmacist, an
    /// assistant, a manager - authorises it with their own sign-in.
    /// </remarks>
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
            answer.LineNo, answer.Quantity, answer.Amount, answer.Reason);

        if (result.IsFailure
            && result.Error.Code == DispensingService.ChargeNeedsAuthority)
        {
            if (View.PromptChargeAuthorisation(result.Error.Message)
                is not Guid authoriser)
            {
                View.ShowError("The charge was not changed.");
                return;
            }

            result = await _dispensing.AmendItemAsync(_dispensingId,
                answer.LineNo, answer.Quantity, answer.Amount, answer.Reason,
                authoriser);
        }

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
        View.ShowStage(Stage);
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

        // Cleared on purpose: the script is still issued, and Dispensary
        // > Labels prints them later. A trailer is a label too.
        if (View.PrintLabels
            && await CheckExpiriesAsync(result.Value.Labels) is { } toPrint)
        {
            foreach (var label in toPrint)
                printed.Add(await _labels.PrintAsync(label));

            if (result.Value.Trailer is { } trailer && View.PrintTrailer)
                printed.Add(await _labels.PrintTrailerAsync(trailer));
        }

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

        var labels = !View.PrintLabels
            ? "No labels printed. Reprint them from Dispensary > Labels "
              + "if they are needed."
            : _labelsHeld
                ? "Labels not printed: an item has no expiry. Print them from "
                  + "Dispensary > Labels once it is known."
                : DescribeLabels(printed);

        View.ShowInfo($"Issued. Script {result.Value.ScriptNumber}. "
                      + $"Co-payment {result.Value.CoPayment:N2} "
                      + "has been added to the collection queue.\n\n"
                      + labels + claimForm);

        View.CloseView(true);
    });

    private bool _labelsHeld;

    /// <summary>
    /// A label without an expiry never prints unnoticed: the pharmacist
    /// types it in or prints without it, on the record under their name.
    /// Null holds the labels back.
    /// </summary>
    private async Task<IReadOnlyList<LabelDto>?> CheckExpiriesAsync(
        IReadOnlyList<LabelDto> labels)
    {
        var missing = LabelExpiryCheck.Missing(labels);
        _labelsHeld = false;
        if (missing.Count == 0) return labels;

        if (View.ConfirmMissingExpiry(missing) is not { } typed)
        {
            _labelsHeld = true;
            return null;
        }

        // Kept on the line as well as printed, so a reprint carries it.
        foreach (var label in missing)
            await _dispensing.RecordLabelExpiryAsync(_dispensingId, label.ItemNumber,
                typed.TryGetValue(label.ItemNumber, out var d) ? d : null);

        return labels.Select(l => typed.TryGetValue(l.ItemNumber, out var d)
                ? l with { Expiry = d }
                : l)
            .ToList();
    }

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
