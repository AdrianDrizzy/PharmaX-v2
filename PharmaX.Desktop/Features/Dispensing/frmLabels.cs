using System.Globalization;
using System.Text;
using PharmaX.Application.Claims;
using PharmaX.Application.Dispensing;
using PharmaX.Desktop.Features.Patients;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Dispensary's Labels: printing the labels for something already
/// dispensed again, because the bottle was relabelled, the printer
/// jammed, or the patient lost the box.
/// </summary>
/// <remarks>
/// Reprinting only. A label is the pharmacist's instruction to the
/// patient and is produced when the dispensing is issued; this screen
/// reproduces one that was issued, it does not author a new one.
/// </remarks>
public partial class frmLabels : PhxFormBase
{
    private readonly ILabelReprintService? _labels;
    private readonly ViewFactory? _views;
    private readonly IDispensingService? _dispensing;
    private readonly Application.Security.IPermissionService? _permissions;
    private readonly IClaimFormService? _claimForms;
    private readonly IClaimFormPrinter? _claimFormPrinter;
    private readonly string? _pharmacy;
    private readonly Adapters.Printing.PharmacyImprint? _imprint;

    private Guid? _patientId;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptDispense;

    /// Designer only. Never called at runtime.
    public frmLabels()
    {
        InitializeComponent();

        btnChoosePatient.Click += (_, _) => ChoosePatient();
        btnPreview.Click += (_, _) => Operations.Enqueue(PreviewAsync);
        btnReprint.Click += (_, _) => Operations.Enqueue(ReprintAsync);
        btnReverse.Click += (_, _) => Operations.Enqueue(ReverseAsync);
        btnClaimForm.Click += (_, _) => Operations.Enqueue(PrintClaimFormAsync);
        btnClose.Click += (_, _) => CloseView(true);

        grdDispensings.DoubleClick += (_, _) => Operations.Enqueue(PreviewAsync);
    }

    /// Runtime. Resolved by the container.
    public frmLabels(ILabelReprintService labels, ViewFactory views,
                     IDispensingService dispensing,
                     Application.Security.IPermissionService permissions,
                     IClaimFormService claimForms,
                     IClaimFormPrinter claimFormPrinter,
                     Application.Abstractions.PharmacyProfile pharmacy)
        : this()
    {
        _pharmacy = Adapters.Printing.LabelDocument.DispensedBy(
            pharmacy.TradingName, pharmacy.Address);

        // The same imprint the label printer is handed, so the preview
        // shows the foot of the label as well as the top of it.
        _imprint = new Adapters.Printing.PharmacyImprint(
            pharmacy.TradingName, pharmacy.Address, pharmacy.Telephone);
        _labels = labels;
        _views = views;
        _dispensing = dispensing;
        _permissions = permissions;
        _claimForms = claimForms;
        _claimFormPrinter = claimFormPrinter;

        btnReverse.Visible = Application.Security.PermissionServiceExtensions
            .Has(permissions, Domain.Security.PermissionCode.ScriptReverse);
    }

    private void ChoosePatient()
    {
        if (_views is null) return;

        using var picker = _views.Create<frmPatientList>();
        picker.UseAsPicker();

        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (picker.SelectedId is not Guid id) return;

        _patientId = id;
        Operations.Enqueue(LoadAsync);
    }

    private Task LoadAsync() => GuardAsync(async () =>
    {
        if (_labels is null || _patientId is not Guid id) return;

        var result = await _labels.ForPatientAsync(id);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        grdDispensings.DataSource = result.Value.ToList();

        txtPatient.Text = result.Value.Count > 0
            ? result.Value[0].PatientName
            : txtPatient.Text;

        if (result.Value.Count == 0)
            ShowInfo("Nothing has been issued to this patient yet, so there "
                   + "are no labels to reprint.");
    });

    /// <summary>
    /// Shows what would print before anything does. A label printer is
    /// often at the other end of the shop, and a wasted label is a
    /// wasted trip.
    /// </summary>
    private Task PreviewAsync() => GuardAsync(async () =>
    {
        if (_labels is null || Selected() is not Guid id) return;

        var result = await _labels.BuildAsync(id);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        var text = new StringBuilder();

        // Each label as the label printer draws it, row for row, from the
        // document that prints it. The preview used to be written out
        // here separately, in the wording of the old label, so it showed
        // neither the prescriber, the item and practice numbers nor the
        // pharmacy, and gave the expiry as a month (OCT-DEF-04).
        foreach (var label in result.Value)
        {
            using var document = new Adapters.Printing.LabelDocument(label,
                                                                     _imprint);

            foreach (var row in document.Rows())
                text.AppendLine(row.Style switch
                {
                    Adapters.Printing.LabelStyle.Rule => new string('-', 40),
                    Adapters.Printing.LabelStyle.Gap => "",
                    _ => row.Text
                });

            text.AppendLine();
        }

        // Then the trailer, exactly as it will print.
        var trailer = await _labels.BuildTrailerAsync(id);

        if (trailer.IsSuccess)
        {
            text.AppendLine("Trailer label:");

            foreach (var line in Adapters.Printing.TrailerLabelDocument
                         .Lines(trailer.Value, _pharmacy))
                text.AppendLine("  " + line);
        }

        ShowInfo(text.ToString().TrimEnd());
    });

    private Task ReprintAsync() => GuardAsync(async () =>
    {
        if (_labels is null || Selected() is not Guid id) return;

        if (!Confirm("Print these labels again? A reprinted label carries "
                   + "the same script number as the first one."))
            return;

        var result = await _labels.ReprintAsync(id);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        ShowInfo(string.Format(CultureInfo.CurrentCulture,
            "{0:N0} label(s) printed on {1}.{2}", result.Value.Printed,
            result.Value.PrinterName ?? "the label printer",
            result.Value.Note is null ? "" : "\n" + result.Value.Note));
    });

    /// <summary>
    /// Prints the medical aid claim form for the selected dispensing.
    /// </summary>
    /// <remarks>
    /// Here as well as on the Claims screen, which is the claims office's:
    /// the dispensary is where the member signs, and it cannot open that
    /// screen.
    /// </remarks>
    private Task PrintClaimFormAsync() => GuardAsync(async () =>
    {
        if (_claimForms is null || _claimFormPrinter is null
            || Selected() is not Guid id) return;

        var form = await _claimForms.ForDispensingAsync(id);

        if (form.IsFailure)
        {
            ShowInfo(form.Error.Message);
            return;
        }

        var printed = await _claimFormPrinter.PrintAsync(form.Value);
        var what = $"The claim form for {form.Value.ClaimNumber}";

        if (printed.Printed) ShowInfo(printed.Describe(what));
        else ShowError(printed.Describe(what));
    });

    /// <summary>
    /// Takes back an issued dispensing that has not been collected.
    /// </summary>
    /// <remarks>
    /// There was no way to: the domain could reverse a dispensing and no
    /// screen or service asked it to (ADM-DEF-22).
    /// </remarks>
    private Task ReverseAsync() => GuardAsync(async () =>
    {
        if (_dispensing is null || Selected() is not Guid id) return;

        var row = grdDispensings.CurrentRow?.DataBoundItem as DispensedItemDto;

        var reason = PharmaX.Desktop.Controls.frmTextPrompt.Ask(this,
            "Reverse dispensing",
            $"Why is {row?.DocumentNumber} being reversed? The medicine goes "
            + "back into stock, the repeat back to the script, and any "
            + "claim on the medical aid is withdrawn.");

        if (string.IsNullOrWhiteSpace(reason)) return;

        if (!Confirm($"Reverse {row?.DocumentNumber}? This cannot be undone."))
            return;

        var result = await _dispensing.ReverseAsync(id, reason);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        ShowInfo($"{row?.DocumentNumber} reversed. The stock is back on the "
               + "shelf.");

        await LoadAsync();
    });

    private Guid? Selected()
    {
        if (grdDispensings.CurrentRow?.DataBoundItem is DispensedItemDto dto)
            return dto.DispensingId;

        ShowInfo("Choose a dispensing first.");
        return null;
    }

    /// The same guard the presenters use; this screen has no presenter.
    private async Task GuardAsync(Func<Task> work)
    {
        try
        {
            SetBusy(true);
            await work();
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
}
