using System.Globalization;
using System.Text;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Claims;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Domain.Security;
using PharmaX.Desktop.Features.Patients;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// Dispensary's Labels: printing the labels for something already
/// dispensed again, because the bottle was relabelled, the printer
/// jammed, or the patient lost the box.
/// </summary>
/// <remarks>
/// A patient's label is the pharmacist's instruction to the patient and
/// is produced when the dispensing is issued; this screen reproduces one
/// that was issued, it does not author a new one. The exception is the
/// pre-pack sticker, made up for the shelf with nobody's name on it, whose
/// wording the dispensary types in (Pre-pack stickers).
/// </remarks>
public partial class frmLabels : PhxFormBase
{
    private readonly ILabelReprintService? _labels;
    private readonly ViewFactory? _views;
    private readonly IDispensingService? _dispensing;
    private readonly Application.Security.IPermissionService? _permissions;
    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? _scopes;
    private readonly Application.Security.IAuthenticationService? _auth;
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
        btnToday.Click += (_, _) => Operations.Enqueue(ShowTodayAsync);
        btnStickers.Click += (_, _) => Operations.Enqueue(StickersAsync);
        btnPreview.Click += (_, _) => Operations.Enqueue(PreviewAsync);
        btnReprint.Click += (_, _) => Operations.Enqueue(ReprintAsync);
        btnReverse.Click += (_, _) => Operations.Enqueue(ReverseAsync);
        btnClaimForm.Click += (_, _) => Operations.Enqueue(PrintClaimFormAsync);
        btnChangeItem.Click += (_, _) => Operations.Enqueue(ChangeItemAsync);
        btnUseAgain.Click += (_, _) => Operations.Enqueue(UseSelectedAgainAsync);
        btnClose.Click += (_, _) => CloseView(true);

        grdDispensings.DoubleClick += (_, _) => Operations.Enqueue(PreviewAsync);
        grdPrepacks.DoubleClick += (_, _) => Operations.Enqueue(UsePrepackAgainAsync);
        grdTemplates.DoubleClick += (_, _) => Operations.Enqueue(UseTemplateAgainAsync);
        grdDispensings.SelectionChanged += (_, _) => ApplyActions();
        grdPrepacks.SelectionChanged += (_, _) => ApplyActions();
        grdTemplates.SelectionChanged += (_, _) => ApplyActions();
        tabLabels.SelectedIndexChanged += (_, _) => ApplyActions();
        btnDeactivate.Click += (_, _) => Operations.Enqueue(DeactivateAsync);

        AddListStrip(pagePrepacks, _showRetiredPrepacks, _deletePrepack);
        AddListStrip(pageTemplates, _showRetiredTemplates, _deleteTemplate);
        _showRetiredPrepacks.CheckedChanged += (_, _) => Operations.Enqueue(ShowPrepacksAsync);
        _showRetiredTemplates.CheckedChanged += (_, _) => Operations.Enqueue(ShowTemplatesAsync);
        _deletePrepack.Click += (_, _) => Operations.Enqueue(DeleteSavedAsync);
        _deleteTemplate.Click += (_, _) => Operations.Enqueue(DeleteSavedAsync);
        ApplyActions();
    }

    // Deactivated stickers are hidden until asked for, and a sticker made
    // in error can be deleted outright: nothing else points at one.
    private readonly CheckBox _showRetiredPrepacks = new() { Name = "chkShowRetiredPrepacks" };
    private readonly CheckBox _showRetiredTemplates = new() { Name = "chkShowRetiredTemplates" };
    private readonly Button _deletePrepack = new() { Name = "btnDeletePrepack" };
    private readonly Button _deleteTemplate = new() { Name = "btnDeleteTemplate" };

    private static void AddListStrip(TabPage page, CheckBox show, Button delete)
    {
        show.AutoSize = true;
        show.Margin = new Padding(3, 7, 18, 3);
        show.Text = "Show &deactivated";

        delete.AutoSize = true;
        delete.Padding = new Padding(6, 0, 6, 0);
        delete.Text = "Dele&te";
        delete.Enabled = false;

        var strip = new FlowLayoutPanel
        {
            AutoSize = true,
            Dock = DockStyle.Top,
            Padding = new Padding(4, 2, 4, 2),
            WrapContents = false
        };
        strip.Controls.Add(show);
        strip.Controls.Add(delete);

        page.Controls.Add(strip);
        strip.SendToBack(); // docks above the grid
    }

    private Task DeleteSavedAsync() => GuardAsync(async () =>
    {
        if (!MayDeactivate()) return;

        if (tabLabels.SelectedTab == pagePrepacks)
        {
            if (_prepacks is null
                || grdPrepacks.CurrentRow?.DataBoundItem is not PrepackLabelDto prepack)
                return;

            if (!Confirm($"Delete the pre-pack sticker for {prepack.Medicine}? "
                         + "It is removed from the list for good. Stock is not changed."))
                return;

            await _prepacks.DeleteAsync(prepack.Id);
            await ShowPrepacksAsync();
            return;
        }

        if (tabLabels.SelectedTab != pageTemplates || _templates is null
            || grdTemplates.CurrentRow?.DataBoundItem is not TemplateLabelDto template)
            return;

        if (!Confirm($"Delete the template label for {template.Medicine}? "
                     + "It is removed from the list for good."))
            return;

        await _templates.DeleteAsync(template.Id);
        await ShowTemplatesAsync();
    });

    /// Runtime. Resolved by the container.
    public frmLabels(ILabelReprintService labels, ViewFactory views,
                     IDispensingService dispensing,
                     Application.Security.IPermissionService permissions,
                     IClaimFormService claimForms,
                     IClaimFormPrinter claimFormPrinter,
                     Application.Abstractions.PharmacyProfile pharmacy,
                     IScriptService scripts,
                     Application.Abstractions.IWorkstationSettings settings,
                     ILabelPrinter printer,
                     IReferenceRepository reference,
                     PrepackLabelHistory prepacks,
                     Application.Suppliers.ISupplierService suppliers,
                     ICurrentUser user,
                     TemplateLabelHistory templates,
                     Application.Security.IAuthenticationService auth,
                     Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes)
        : this()
    {
        _scopes = scopes;
        _ = user;
        _auth = auth;
        _templates = templates;

        if (!Application.Security.PermissionServiceExtensions
                .Has(permissions, Domain.Security.PermissionCode.LabelTemplates))
            tabLabels.TabPages.Remove(pageTemplates);
        _reference = reference;
        _prepacks = prepacks;
        _suppliers = suppliers;
        _printer = printer;
        _scripts = scripts;
        _settings = settings;
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
        btnChangeItem.Visible = Application.Security.PermissionServiceExtensions
            .Has(permissions, Domain.Security.PermissionCode.DispensingAmend);
        _mayRepeatScripts = Application.Security.PermissionServiceExtensions
            .Has(permissions, Domain.Security.PermissionCode.ScriptCapture);
        ApplyActions();
    }

    private readonly IScriptService? _scripts;
    private readonly Application.Abstractions.IWorkstationSettings? _settings;
    private readonly ILabelPrinter? _printer;
    private readonly IReferenceRepository? _reference;
    private readonly PrepackLabelHistory? _prepacks;
    private readonly TemplateLabelHistory? _templates;
    private readonly Application.Suppliers.ISupplierService? _suppliers;
    private IReadOnlyDictionary<string, string> _dosage = DosageShorthand.Catalogue();
    private IReadOnlyDictionary<string, string> _warnings =
        DosageShorthand.Catalogue(WarningCodes.Standard);
    private bool _mayRepeatScripts;

    protected override async void OnShown(EventArgs e)
    {
        base.OnShown(e);
        await LoadDosageAsync();
        Operations.Enqueue(ShowPrepacksAsync);
        Operations.Enqueue(ShowTemplatesAsync);
        Operations.Enqueue(ShowTodayAsync);
        btnChoosePatient.Focus();
    }

    private async Task LoadDosageAsync()
    {
        if (_reference is null) return;

        try
        {
            _dosage = await _reference.CatalogueAsync();
            _warnings = await _reference.WarningCatalogueAsync();
        }
#pragma warning disable CA1031 // The standard codes still expand.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// Pre-pack and template use-again is part of labelling. Repeating a script is not.
    private void ShowUseAgain() =>
        btnUseAgain.Visible = tabLabels.SelectedTab == pagePrepacks
                              || tabLabels.SelectedTab == pageTemplates
                              || _mayRepeatScripts;

    /// Print, reverse and the rest apply to the row that is selected.
    /// Until then they stay off, so the first click is Find patient.
    private void ApplyActions()
    {
        var onTemplates = tabLabels.SelectedTab == pageTemplates;
        var dispensed = tabLabels.SelectedTab == pageDispensed
                        && grdDispensings.CurrentRow?.DataBoundItem is DispensedItemDto;
        var prepack = tabLabels.SelectedTab == pagePrepacks
                      && grdPrepacks.CurrentRow?.DataBoundItem is PrepackLabelDto row
                      && row.IsActive;
        var template = onTemplates
                       && grdTemplates.CurrentRow?.DataBoundItem is TemplateLabelDto saved
                       && saved.IsActive;

        btnPreview.Enabled = dispensed;
        btnReprint.Enabled = dispensed;
        btnReverse.Enabled = dispensed;
        btnClaimForm.Enabled = dispensed;
        btnChangeItem.Enabled = dispensed;
        ShowUseAgain();
        btnUseAgain.Enabled = prepack || template || (dispensed && _mayRepeatScripts);
        btnStickers.Text = onTemplates ? "Template &label..." : "Pre-pack stic&kers...";

        // Deactivating a saved sticker keeps it on the list. A sale, a
        // script and a patient's label are not deactivated from here.
        var canDeactivate = MayDeactivate()
                            && (prepack || template);
        btnDeactivate.Visible = MayDeactivate()
                                && (tabLabels.SelectedTab == pagePrepacks || onTemplates);
        btnDeactivate.Enabled = canDeactivate;

        // Delete, deactivated or not, by the same people who deactivate.
        _deletePrepack.Visible = _deleteTemplate.Visible = MayDeactivate();
        _deletePrepack.Enabled = grdPrepacks.CurrentRow?.DataBoundItem is PrepackLabelDto;
        _deleteTemplate.Enabled = grdTemplates.CurrentRow?.DataBoundItem is TemplateLabelDto;
    }

    /// <summary>
    /// Whoever holds dsp.label.deactivate. Deactivating a dispensed label
    /// is not offered: that record is the dispensing itself.
    /// </summary>
    private bool MayDeactivate() =>
        _permissions is not null
        && Application.Security.PermissionServiceExtensions
            .Has(_permissions, Domain.Security.PermissionCode.LabelDeactivate);

    private Task UseSelectedAgainAsync() =>
        tabLabels.SelectedTab == pagePrepacks
            ? UsePrepackAgainAsync()
            : tabLabels.SelectedTab == pageTemplates
                ? UseTemplateAgainAsync()
                : UseAgainAsync();

    /// <summary>
    /// Stickers for a run of pre-packs - twenty for twenty packs of
    /// paracetamol - with what goes on them typed in, all in one job on
    /// the label printer.
    /// </summary>
    private Task StickersAsync() =>
        tabLabels.SelectedTab == pageTemplates
            ? TemplateAsync()
            : PrepackAsync();

    private Task TemplateAsync() => GuardAsync(async () =>
    {
        if (_printer is null || _templates is null) return;

        var suppliers = await SupplierNamesAsync();

        if (frmTemplateLabel.Ask(this, _imprint, _dosage, suppliers, warnings: _warnings) is not var (sticker, copies))
            return;

        try
        {
            await _templates.RememberAsync(sticker, copies);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ShowError("The label was not added to the template list: " + ex.Message);
        }

        var printed = await _printer.PrintCopiesAsync(sticker, copies);
        await ShowTemplatesAsync();
        var what = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} template label(s) for {1}", copies, sticker.ProductDescription);

        if (printed.Printed) ShowInfo(printed.Describe(what));
        else ShowError(printed.Describe(what));
    });

    private Task UseTemplateAgainAsync() => GuardAsync(async () =>
    {
        if (_printer is null || _templates is null) return;

        if (grdTemplates.CurrentRow?.DataBoundItem is not TemplateLabelDto again)
        {
            ShowInfo("Choose a template label first.");
            return;
        }

        if (!again.IsActive)
        {
            ShowInfo("That template label is deactivated. Print a new one.");
            return;
        }

        var suppliers = await SupplierNamesAsync();

        if (frmTemplateLabel.Ask(this, _imprint, _dosage, suppliers, again, _warnings)
            is not var (sticker, copies))
            return;

        try
        {
            await _templates.RememberAsync(sticker, copies);
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ShowError("The label was not added to the template list: " + ex.Message);
        }

        var printed = await _printer.PrintCopiesAsync(sticker, copies);
        await ShowTemplatesAsync();

        var what = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} template label(s) for {1}", copies, sticker.ProductDescription);

        if (printed.Printed) ShowInfo(printed.Describe(what));
        else ShowError(printed.Describe(what));
    });

    private Task DeactivateAsync() => GuardAsync(async () =>
    {
        if (!MayDeactivate()) return;

        if (tabLabels.SelectedTab == pagePrepacks)
        {
            if (_prepacks is null
                || grdPrepacks.CurrentRow?.DataBoundItem is not PrepackLabelDto prepack
                || !prepack.IsActive)
                return;

            if (!Confirm($"Deactivate the pre-pack sticker for {prepack.Medicine}? "
                         + "It stays on the list, and it will not print again from here."))
                return;

            await _prepacks.DeactivateAsync(prepack.Id);
            await ShowPrepacksAsync();
            return;
        }

        if (tabLabels.SelectedTab != pageTemplates || _templates is null
            || grdTemplates.CurrentRow?.DataBoundItem is not TemplateLabelDto template
            || !template.IsActive)
            return;

        if (!Confirm($"Deactivate the template label for {template.Medicine}? "
                     + "It stays on the list."))
            return;

        await _templates.DeactivateAsync(template.Id);
        await ShowTemplatesAsync();
    });

    private Task PrepackAsync() => GuardAsync(async () =>
    {
        if (_printer is null) return;

        var suppliers = await SupplierNamesAsync();

        if (frmPrepackStickers.Ask(this, _imprint, _dosage, suppliers,
                medicines: _scopes is null ? null : Suggest.Products(_scopes))
            is not var (sticker, copies))
            return;

        await RememberAsync(sticker, copies);

        var printed = await _printer.PrintCopiesAsync(sticker, copies);
        await ShowPrepacksAsync();
        var what = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} sticker(s) for {1}", copies, sticker.ProductDescription);

        if (printed.Printed) ShowInfo(printed.Describe(what));
        else ShowError(printed.Describe(what));
    });

    /// <summary>
    /// Prints an old pre-pack again, with its wording already filled in
    /// so only what has changed needs typing.
    /// </summary>
    private Task UsePrepackAgainAsync() => GuardAsync(async () =>
    {
        if (_printer is null) return;

        if (grdPrepacks.CurrentRow?.DataBoundItem is not PrepackLabelDto again)
        {
            ShowInfo("Choose a pre-pack first.");
            return;
        }

        if (!again.IsActive)
        {
            ShowInfo("That pre-pack sticker is deactivated.");
            return;
        }

        var suppliers = await SupplierNamesAsync();

        if (frmPrepackStickers.Ask(this, _imprint, _dosage, suppliers, again,
                _scopes is null ? null : Suggest.Products(_scopes))
            is not var (sticker, copies))
            return;

        await RememberAsync(sticker, copies);

        var printed = await _printer.PrintCopiesAsync(sticker, copies);
        await ShowPrepacksAsync();

        var what = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} sticker(s) for {1}", copies, sticker.ProductDescription);

        if (printed.Printed) ShowInfo(printed.Describe(what));
        else ShowError(printed.Describe(what));
    });

    /// Active suppliers, as purchasing stores them. No list still prints.
    private async Task<IReadOnlyList<string>> SupplierNamesAsync()
    {
        if (_suppliers is null || _permissions is null
            || !Application.Security.PermissionServiceExtensions.Has(
                _permissions, Domain.Security.PermissionCode.ProductRead))
            return Array.Empty<string>();

        var result = await _suppliers.ListAsync();

        return result.IsSuccess
            ? result.Value
                .Select(s => s.Name.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()
            : Array.Empty<string>();
    }

    private async Task RememberAsync(LabelDto sticker, int copies)
    {
        if (_prepacks is null) return;

        try
        {
            await _prepacks.RememberAsync(sticker, copies);
        }
#pragma warning disable CA1031 // Printing still goes ahead; the list is a convenience.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ShowError("The sticker was not added to the pre-pack list: "
                      + ex.Message);
        }
    }

    private async Task ShowPrepacksAsync()
    {
        if (_prepacks is null) return;

        var rows = await _prepacks.ListAsync();
        GridPager.Attach(grdPrepacks).Show(_showRetiredPrepacks.Checked
            ? rows
            : rows.Where(r => r.IsActive).ToList());
        ApplyActions();
    }

    private async Task ShowTemplatesAsync()
    {
        if (_templates is null || !tabLabels.TabPages.Contains(pageTemplates))
            return;

        var rows = await _templates.ListAsync();
        GridPager.Attach(grdTemplates).Show(_showRetiredTemplates.Checked
            ? rows
            : rows.Where(r => r.IsActive).ToList());
        ApplyActions();
    }

    /// <summary>
    /// Corrects one item of what was issued: the quantity handed over, the
    /// amount charged, or both. The stock moves by the difference and the
    /// labels can be printed again to match.
    /// </summary>
    private Task ChangeItemAsync() => GuardAsync(async () =>
    {
        if (_dispensing is null || Selected() is not Guid id) return;

        var state = await _dispensing.GetAsync(id);

        if (state.IsFailure)
        {
            ShowError(state.Error.Message);
            return;
        }

        if (ItemChange.Ask(this, state.Value.Items, null, issued: true)
            is not { } answer)
            return;

        var result = await _dispensing.AmendItemAsync(id, answer.LineNo,
            answer.Quantity, answer.Amount, answer.Reason);

        // A charge other than the price, from someone who may not change
        // one: whoever may authorises it with their own sign-in.
        if (result.IsFailure
            && result.Error.Code == DispensingService.ChargeNeedsAuthority
            && _auth is not null)
        {
            using var prompt = new Login.frmSupervisorPrompt(_auth,
                result.Error.Message, PermissionCode.DispensingChargeAdjust);

            if (prompt.ShowDialog(this) != DialogResult.OK
                || prompt.AuthoriserId is not Guid authoriser)
            {
                ShowError("The item was not changed.");
                return;
            }

            result = await _dispensing.AmendItemAsync(id, answer.LineNo,
                answer.Quantity, answer.Amount, answer.Reason, authoriser);
        }

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        await LoadAsync();
        Suggest.SelectRow(grdDispensings,
            item => item is DispensedItemDto d && d.DispensingId == id);

        if (Confirm("Item changed. " + ItemChange.Describe(result.Value)
                    + "\n\nPrint the labels again to match?"))
            await PrintAgainAsync(id);
    });

    /// <summary>
    /// A new script from the chosen one, to change and capture, ready for
    /// dispensing.
    /// </summary>
    private Task UseAgainAsync() => GuardAsync(async () =>
    {
        if (_scripts is null || _views is null
            || grdDispensings.CurrentRow?.DataBoundItem is not DispensedItemDto row)
        {
            Selected();
            return;
        }

        var patientId = row.PatientId != Guid.Empty
            ? row.PatientId
            : _patientId;

        if (patientId is not Guid id)
        {
            ShowInfo("Find the patient, then use the script again.");
            return;
        }

        var draft = await _scripts.RepeatAsync(row.ScriptId);

        if (draft.IsFailure)
        {
            ShowError(draft.Error.Message);
            return;
        }

        using var editor = _views.Create<frmScriptEdit>();
        editor.LoadDraft(id, draft.Value);

        if (editor.ShowDialog(this) != DialogResult.OK) return;

        // The script is captured on this screen. Dispensing opens on it,
        // rather than sending the pharmacist back to the menu to find it.
        if (editor.CapturedScriptId is not Guid scriptId)
        {
            ShowInfo("The new script is captured. Open Dispensing to dispense it.");
            return;
        }

        using var dispense = _views.Create<frmDispense>();
        dispense.BeginOn(id, scriptId);
        dispense.ShowDialog(this);
    });

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

    /// Issued today, across every patient. The list the screen opens on.
    private Task ShowTodayAsync() => GuardAsync(async () =>
    {
        if (_labels is null) return;

        _patientId = null;
        var result = await _labels.ForDayAsync(DateOnly.FromDateTime(DateTime.Today));

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        ShowDispensed(result.Value, string.Format(CultureInfo.CurrentCulture,
            "Labels dispensed {0:dd/MM/yyyy}", DateTime.Today));
    });

    private Task LoadAsync() => GuardAsync(async () =>
    {
        if (_labels is null || _patientId is not Guid id) return;

        var result = await _labels.ForPatientAsync(id);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        var heading = result.Value.Count > 0
            ? result.Value[0].PatientName
            : txtPatient.Text;

        ShowDispensed(result.Value, heading);

        if (result.Value.Count == 0)
        {
            ShowInfo("Nothing has been issued to this patient yet, so there "
                   + "are no labels to reprint.");
            return;
        }

        grdDispensings.Focus();
    });

    private void ShowDispensed(IReadOnlyList<DispensedItemDto> rows, string heading)
    {
        GridPager.Attach(grdDispensings).Show(rows);
        txtPatient.Text = heading;

        if (rows.Count > 0)
            grdDispensings.CurrentCell = grdDispensings.Rows[0].Cells[0];

        ApplyActions();
    }

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

            foreach (var line in document.Lines())
                text.AppendLine(line);

            text.AppendLine();
        }

        // Then the trailer, exactly as it will print - where this
        // computer prints one.
        var trailer = _settings is not null
                      && await TrailerLabels.WantedAsync(_settings)
            ? await _labels.BuildTrailerAsync(id)
            : null;

        if (trailer is { IsSuccess: true })
        {
            text.AppendLine("Trailer label:");

            foreach (var line in Adapters.Printing.TrailerLabelDocument
                         .Lines(trailer.Value, _pharmacy, _imprint?.Telephone))
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

        await PrintAgainAsync(id);
    });

    private async Task PrintAgainAsync(Guid id)
    {
        if (_labels is null) return;

        // A label held back at the issue for want of an expiry comes here
        // to be printed: the same check, and the expiry kept on the line.
        if (_dispensing is not null
            && await _labels.BuildAsync(id) is { IsSuccess: true } built
            && await MissingExpiryPrompt.CheckAsync(this, _dispensing, id, built.Value) is null)
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
    }

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
            + "claim on the medical aid is withdrawn.",
            hint: "e.g. Wrong strength dispensed");

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
