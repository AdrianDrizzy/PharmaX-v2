using System.Globalization;
using System.Text;
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

    private Guid? _patientId;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptAuthorise;

    /// Designer only. Never called at runtime.
    public frmLabels()
    {
        InitializeComponent();

        btnChoosePatient.Click += (_, _) => ChoosePatient();
        btnPreview.Click += (_, _) => _ = PreviewAsync();
        btnReprint.Click += (_, _) => _ = ReprintAsync();
        btnClose.Click += (_, _) => CloseView(true);

        grdDispensings.DoubleClick += (_, _) => _ = PreviewAsync();
    }

    /// Runtime. Resolved by the container.
    public frmLabels(ILabelReprintService labels, ViewFactory views) : this()
    {
        _labels = labels;
        _views = views;
    }

    private void ChoosePatient()
    {
        if (_views is null) return;

        using var picker = _views.Create<frmPatientList>();
        picker.UseAsPicker();

        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (picker.SelectedId is not Guid id) return;

        _patientId = id;
        _ = LoadAsync();
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

        foreach (var label in result.Value)
        {
            text.AppendLine(CultureInfo.CurrentCulture,
                $"{label.ProductDescription}  x{label.Quantity:N0}");
            text.AppendLine(label.Directions);

            if (label.BatchNumber is not null)
            {
                var expiry = label.Expiry is DateOnly e
                    ? string.Format(CultureInfo.CurrentCulture,
                                    ", expires {0:MM/yyyy}", e)
                    : "";

                text.AppendLine($"Batch {label.BatchNumber}{expiry}");
            }

            text.AppendLine($"{label.PatientName} - {label.ScriptNumber} - "
                            + label.PharmacistName);

            text.AppendLine();
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
            "{0:N0} label(s) sent to the label printer.", result.Value));
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
