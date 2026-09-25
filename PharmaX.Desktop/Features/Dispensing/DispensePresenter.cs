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

    private Guid _dispensingId;

    public DispensePresenter(IDispensingService dispensing,
                             IScriptService scripts,
                             Application.Patients.IPatientService patients,
                             ILabelPrinter labels)
    {
        _dispensing = dispensing;
        _scripts = scripts;
        _patients = patients;
        _labels = labels;
    }

    protected override void Subscribe()
    {
        View.ScriptRequested += (_, _) => Enqueue(BeginAsync);
        View.ProductScanned += (_, code) => Enqueue(() => AddItemAsync(code));
        View.RemoveItemRequested += (_, _) => Enqueue(RemoveItemAsync);
        View.IssueRequested += (_, _) => Enqueue(IssueAsync);
        View.LabelReprintRequested += (_, _) => View.ShowLabelScreen();
        View.CloseRequested += (_, _) => View.CloseView(true);
    }

    private Task BeginAsync() => GuardAsync(async () =>
    {
        var patientId = View.PromptPatientSearch();
        if (patientId is null) return;

        var scriptId = View.PromptScriptSearch(patientId.Value);
        if (scriptId is null) return;

        var result = await _dispensing.BeginAsync(scriptId.Value);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        _dispensingId = result.Value.Id;

        // The header comes from the patient and the script rather than
        // from the dispensing, because it is what the pharmacist checks
        // before the first item goes on.
        var patient = await _patients.GetAsync(patientId.Value);
        if (patient.IsSuccess)
            View.ShowPatient(ToSummary(patient.Value));

        var script = await _scripts.GetAsync(scriptId.Value);
        if (script.IsSuccess) View.ShowScript(script.Value.Script);

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
    });

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

        View.ShowInfo($"Issued. Script {result.Value.ScriptNumber}. "
                      + $"Co-payment {result.Value.CoPayment:N2} "
                      + "has been added to the collection queue.\n\n"
                      + DescribeLabels(printed));

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
