using System.Globalization;
using PharmaX.Application.Patients;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Inventory;
using PharmaX.Desktop.Features.Patients;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Desktop.Features.Dispensing;

/// <summary>
/// The dispensary's quote: what a script would cost, priced before
/// anybody gives their name, saved, and taken up as the script when the
/// customer goes ahead.
/// </summary>
/// <remarks>
/// <para>
/// The customer at the dispensary counter asks what their medicine will
/// cost before they hand over a script or a name. The items are found by
/// code or by the first letters of their name, counted the way the script
/// will count them - 21 tablets, 100 ml - and priced as the pack's share,
/// exactly as a dispensing prices them. Nothing is dispensed and no stock
/// moves.
/// </para>
/// <para>
/// Saved, it can be resumed from the list when the customer comes back.
/// Taken up, the patient is found or added and the script is captured
/// from the quote, prefilled; the quote is then marked taken up in the
/// same save, so the one quote cannot become two scripts.
/// </para>
/// </remarks>
public partial class frmDispensaryQuote : PhxFormBase
{
    private readonly IQuotationService? _quotations;
    private readonly IPatientService? _patients;
    private readonly ViewFactory? _views;
    private readonly IDocumentPrinter? _printer;
    private readonly ReceiptHeaderFactory? _headers;
    private readonly IPermissionService? _permissions;

    private QuotationDto? _current;
    private Guid? _patientId;

    protected override string? RequiredPermission =>
        PermissionCode.ScriptCapture;

    /// Opened by those who capture scripts and those who dispense them.
    protected override bool MayOpen(IPermissionService permissions) =>
        permissions.HasAny(PermissionCode.ScriptCapture,
                           PermissionCode.ScriptDispense);

    protected override bool ClosesOnEscape => true;

    /// Designer only. Never called at runtime.
    public frmDispensaryQuote()
    {
        InitializeComponent();

        btnFind.Click += (_, _) => Operations.Enqueue(() => FindAsync());
        btnRemove.Click += (_, _) => Operations.Enqueue(RemoveAsync);
        btnSave.Click += (_, _) => Operations.Enqueue(SaveAsync);
        btnResume.Click += (_, _) => Operations.Enqueue(ResumeAsync);
        btnPrint.Click += (_, _) => Operations.Enqueue(PrintAsync);
        btnCreateScript.Click += (_, _) => Operations.Enqueue(CreateScriptAsync);
        btnClose.Click += (_, _) => CloseView(false);

        scanBar.CodeEntered += (_, code) => Operations.Enqueue(() => AddAsync(code));

        ShowNothing();
    }

    /// Runtime. Resolved by the container.
    public frmDispensaryQuote(IQuotationService quotations,
        IPatientService patients, ViewFactory views,
        IDocumentPrinter printer, ReceiptHeaderFactory headers,
        IPermissionService permissions,
        Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes)
        : this()
    {
        _quotations = quotations;
        _patients = patients;
        _views = views;
        _printer = printer;
        _headers = headers;
        _permissions = permissions;

        // Found from the first letters of the name, as at the till.
        scanBar.SuggestFrom(Suggest.Products(scopes));
    }

    /// <summary>
    /// The script the quote became, once the customer went ahead. Read by
    /// whoever opened the screen, to dispense it.
    /// </summary>
    public Guid? CreatedScriptId { get; private set; }

    /// The patient the script was captured for.
    public Guid? CreatedForPatientId { get; private set; }

    /// <summary>
    /// Quotes for a patient already known - opened from their scripts -
    /// so taking it up does not ask who they are again. Called before
    /// ShowDialog.
    /// </summary>
    public void ForPatient(Guid patientId) => _patientId = patientId;

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        // Capturing the script is its own authority; a dispenser without
        // it can still price and save.
        if (_permissions is not null
            && !_permissions.Has(PermissionCode.ScriptCapture))
            btnCreateScript.Visible = false;

        if (_patientId is Guid patientId && _patients is not null)
        {
            var patient = await _patients.GetAsync(patientId);

            if (patient.IsSuccess)
            {
                lblPatient.Text = $"Patient: {patient.Value.Header()}";

                if (txtCustomer.Text.Length == 0)
                    txtCustomer.Text = patient.Value.FullNameOrFile();
            }
        }

        scanBar.TakeFocus();
    }

    private void ShowNothing()
    {
        lblNumber.Text = "Not saved";
        lblTotalValue.Text = Format.Money(0m);
        lblValid.Text = "Scan or type the items the customer asked about. "
            + "The quote is numbered with the first item; nobody's name "
            + "is needed to give a price.";

        btnRemove.Enabled = false;
        btnSave.Enabled = false;
        btnPrint.Enabled = false;
        btnCreateScript.Enabled = false;
    }

    // ---- Items -----------------------------------------------------------

    private Task AddAsync(string code) => GuardAsync(() => AddCodeAsync(code, true));

    private Task FindAsync(string? term = null) => GuardAsync(async () =>
    {
        if (_views is null) return;

        using var picker = _views.Create<frmProductList>();
        picker.UseAsPicker(term);

        if (picker.ShowDialog(this) != DialogResult.OK) return;

        await AddPickedAsync(picker.SelectedCodes);
    });

    /// <summary>
    /// Asks how many - counted as the script counts them - and puts the
    /// item on, opening the quote with its first item.
    /// </summary>
    /// <param name="refused">
    /// Where a refusal goes when several were picked, to be reported once.
    /// </param>
    private async Task AddCodeAsync(string code, bool mayFind,
                                    List<string>? refused = null)
    {
        if (_quotations is null) return;

        void Refuse(string message)
        {
            if (refused is null) ShowError(message);
            else refused.Add($"{code}: {message}");
        }

        if (_current is { IsOpen: false })
        {
            ShowInfo($"Quote {_current.QuotationNumber} has been saved, so "
                     + "its prices are the offer. Close it to quote again.");
            return;
        }

        var found = await _quotations.FindProductAsync(code,
            QuotationKind.Dispensary);

        if (found.IsFailure)
        {
            // A code that matched nothing is a search for what was typed,
            // as it is at the till.
            if (mayFind && string.Equals(found.Error.Code, "POS-001",
                                         StringComparison.Ordinal))
            {
                await FindWithinAsync(code);
                return;
            }

            Refuse(found.Error.Message);
            scanBar.TakeFocus();
            return;
        }

        var product = found.Value;

        // A full pack to start from: the usual answer to "how much is a
        // month of these".
        var quantity = frmNumberPrompt.Ask(this, "Quantity",
            string.Format(CultureInfo.CurrentCulture,
                "{0}: quantity to quote ({1:0.###} in a pack at {2})",
                product.Description, product.UnitsInPack,
                Format.Money(product.PackPrice)),
            product.UnitsInPack, decimals: 3);

        if (quantity is not decimal amount || amount <= 0m)
        {
            scanBar.TakeFocus();
            return;
        }

        if (_current is null)
        {
            var opened = await _quotations.OpenAsync(txtCustomer.Text.Trim(),
                kind: QuotationKind.Dispensary);

            if (opened.IsFailure)
            {
                Refuse(opened.Error.Message);
                return;
            }

            Show(opened.Value);
        }

        var added = await _quotations.AddLineAsync(_current!.Id,
                                                   product.Code, amount);

        if (added.IsFailure)
        {
            Refuse(added.Error.Message);
            return;
        }

        Show(added.Value);
        scanBar.TakeFocus();
    }

    /// <summary>
    /// Each picked product as if it were picked alone, its quantity asked
    /// for in turn; a refusal does not stop the rest.
    /// </summary>
    private async Task AddPickedAsync(IReadOnlyList<string> codes)
    {
        if (codes.Count == 1)
        {
            await AddCodeAsync(codes[0], mayFind: false);
            return;
        }

        var refused = new List<string>();

        foreach (var code in codes)
        {
            await AddCodeAsync(code, mayFind: false, refused);

            // A saved quote takes nothing more, and has said so once.
            if (_current is { IsOpen: false }) break;
        }

        if (refused.Count > 0) ShowError(string.Join("\n", refused));
    }

    /// The product list, opened on what was typed, from inside the guard.
    private async Task FindWithinAsync(string term)
    {
        if (_views is null) return;

        using var picker = _views.Create<frmProductList>();
        picker.UseAsPicker(term);

        if (picker.ShowDialog(this) != DialogResult.OK) return;

        await AddPickedAsync(picker.SelectedCodes);
    }

    private Task RemoveAsync() => GuardAsync(async () =>
    {
        if (_quotations is null || _current is null) return;

        if (grdLines.CurrentRow?.DataBoundItem is not QuotationLineDto line)
        {
            ShowInfo("Choose the line to take off.");
            return;
        }

        var result = await _quotations.RemoveLineAsync(_current.Id,
                                                       line.LineNo);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        Show(result.Value);
    });

    // ---- Saved quotes ----------------------------------------------------

    /// <summary>
    /// Saves the quote as the offer, under the name typed if there is one,
    /// and offers the customer a copy.
    /// </summary>
    private Task SaveAsync() => GuardAsync(async () =>
    {
        if (_quotations is null) return;

        if (_current is null || _current.Lines.Count == 0)
        {
            ShowError("Quote at least one item before saving.");
            return;
        }

        if (!_current.IsOpen)
        {
            ShowInfo($"Quote {_current.QuotationNumber} is already saved.");
            return;
        }

        var result = await _quotations.IssueAsync(_current.Id, null,
            txtCustomer.Text.Trim());

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        Show(result.Value);

        if (Confirm(string.Format(CultureInfo.CurrentCulture,
                "Quote {0} saved for {1}, valid until {2:dd/MM/yyyy}.\n\n"
                + "Print a copy for the customer?",
                result.Value.QuotationNumber, Format.Money(result.Value.Total),
                result.Value.ValidUntil)))
            await PrintCurrentAsync();
    });

    /// <summary>
    /// A quote saved earlier, chosen from the recent ones by its number,
    /// its date and whoever it was for.
    /// </summary>
    private Task ResumeAsync() => GuardAsync(async () =>
    {
        if (_quotations is null) return;

        var recent = await _quotations.RecentAsync(QuotationKind.Dispensary);

        if (recent.IsFailure)
        {
            ShowError(recent.Error.Message);
            return;
        }

        var choices = recent.Value
            .Select(q => (Describe(q), q.Id))
            .ToList();

        if (choices.Count == 0)
        {
            ShowInfo("No dispensary quote has been saved yet.");
            return;
        }

        if (frmChoicePrompt.Ask(this, "Resume quote",
                "Which quote has the customer come back for?", choices)
            is not Guid id)
            return;

        var chosen = await _quotations.GetAsync(id);

        if (chosen.IsFailure)
        {
            ShowError(chosen.Error.Message);
            return;
        }

        txtCustomer.Text = chosen.Value.CustomerName;
        Show(chosen.Value);
    });

    private static string Describe(QuotationDto quote) =>
        string.Format(CultureInfo.CurrentCulture, "{0}  {1:dd/MM}  {2}  {3}  {4}",
            quote.QuotationNumber, quote.QuotedOn,
            quote.CustomerName.Length > 0 ? quote.CustomerName : "(no name)",
            Format.Money(quote.Total),
            quote.ScriptId is not null ? "became a script" : quote.Status);

    private Task PrintAsync() => GuardAsync(PrintCurrentAsync);

    /// <summary>
    /// Prints the quote on this till's printer, under the pharmacy's
    /// header, and says honestly whether it printed.
    /// </summary>
    private async Task PrintCurrentAsync()
    {
        if (_current is null || _printer is null || _headers is null) return;

        var result = await _printer.PrintAsync(
            QuotationDocument.For(_current), _headers.Build());

        if (result.Printed)
            ShowInfo(result.Describe($"Quote {_current.QuotationNumber}"));
        else
            ShowError(result.Describe($"Quote {_current.QuotationNumber}"));
    }

    // ---- Taking it up ----------------------------------------------------

    /// <summary>
    /// The customer goes ahead: the patient is found - or added, the
    /// details they had not given yet - and the script is captured from
    /// the quote.
    /// </summary>
    private Task CreateScriptAsync() => GuardAsync(async () =>
    {
        if (_quotations is null || _views is null) return;

        if (_current is null || _current.Lines.Count == 0)
        {
            ShowError("Quote at least one item before creating the script.");
            return;
        }

        if (!_current.CanBecomeScript)
        {
            ShowInfo(_current.ScriptId is not null
                ? $"Quote {_current.QuotationNumber} has already become a script."
                : $"Quote {_current.QuotationNumber} is "
                  + $"{_current.Status.ToLowerInvariant()} and cannot become a script.");
            return;
        }

        var patientId = _patientId ?? PickPatient();
        if (patientId is not Guid chosen) return;

        using (var editor = _views.Create<frmScriptEdit>())
        {
            editor.LoadFromQuote(chosen, _current);

            if (editor.ShowDialog(this) == DialogResult.OK
                && editor.CapturedScriptId is Guid scriptId)
            {
                CreatedScriptId = scriptId;
                CreatedForPatientId = chosen;
                CloseView(true);
                return;
            }
        }

        // Not captured: the quote is read again in case it changed
        // underneath, and stays as it was.
        var again = await _quotations.GetAsync(_current.Id);
        if (again.IsSuccess) Show(again.Value);
    });

    /// <summary>
    /// The patient list as a picker, where a patient not yet on file is
    /// added without leaving the flow.
    /// </summary>
    private Guid? PickPatient()
    {
        if (_views is null) return null;

        using var picker = _views.Create<frmPatientList>();
        picker.UseAsPicker();

        return picker.ShowDialog(this) == DialogResult.OK
            ? picker.SelectedId
            : null;
    }

    // ---- Display ---------------------------------------------------------

    private void Show(QuotationDto quote)
    {
        _current = quote;

        lblNumber.Text = $"{quote.QuotationNumber}  ({(quote.ScriptId is null ? quote.Status : "Script")})";
        grdLines.DataSource = quote.Lines.ToList();
        lblTotalValue.Text = Format.Money(quote.Total);

        lblValid.Text = string.Format(CultureInfo.CurrentCulture,
            "Quoted {0:dd/MM/yyyy}, valid until {1:dd/MM/yyyy}. Priced as "
            + "the pack's share, as it will be dispensed; a medical aid "
            + "may pay part of it. No stock is reserved.",
            quote.QuotedOn, quote.ValidUntil);

        var open = quote.IsOpen;

        scanBar.Enabled = open;
        btnFind.Enabled = open;
        btnRemove.Enabled = open && quote.Lines.Count > 0;
        btnSave.Enabled = open && quote.Lines.Count > 0;

        // Printed once it is the offer, not while it is being put
        // together.
        btnPrint.Enabled = !open && quote.Lines.Count > 0;
        btnCreateScript.Enabled = quote.CanBecomeScript && quote.Lines.Count > 0;
        txtCustomer.ReadOnly = !open;
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
        catch (PermissionDeniedException ex)
        {
            ShowError(ex.Message);
        }
        finally
        {
            SetBusy(false);
        }
    }
}
