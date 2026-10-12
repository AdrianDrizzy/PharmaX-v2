using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

public interface IPatientListView : IView
{
    string SearchText { get; }
    Guid? SelectedId { get; }

    void ShowPatients(IReadOnlyList<PatientListItemDto> patients);

    /// <param name="page">From nought.</param>
    /// <param name="shown">How many are on this page.</param>
    void ShowPage(int page, int pageSize, int shown, int total);

    /// <summary>
    /// True where the screen was opened to pick a patient for another
    /// screen rather than to maintain the list. The presenter uses it to
    /// decide whether choosing a row closes the window.
    /// </summary>
    bool IsPickerMode { get; }

    event EventHandler? SearchChanged;
    event EventHandler? AddRequested;
    event EventHandler? EditRequested;
    event EventHandler? DeactivateRequested;
    event EventHandler? ChooseRequested;
    event EventHandler? NextPageRequested;
    event EventHandler? PreviousPageRequested;
}

public interface IPatientEditView : IView
{
    Guid? PatientId { get; }

    void ShowPatient(PatientDto patient);

    /// The schemes a patient can belong to, filled before ShowPatient.
    void ShowSchemes(IReadOnlyList<Application.Reference.ReferenceItemDto>
        schemes);

    PatientEditDto Collect();

    event EventHandler? SaveRequested;
    event EventHandler? CancelRequested;
}

public sealed class PatientListPresenter : PresenterBase<IPatientListView>
{
    private readonly IPatientService _patients;
    private readonly ViewFactory _views;

    public PatientListPresenter(IPatientService patients, ViewFactory views)
    {
        _patients = patients;
        _views = views;
    }

    protected override void Subscribe()
    {
        // A new search starts again at the top.
        View.SearchChanged += (_, _) => Enqueue(FirstPageAsync);
        View.NextPageRequested += (_, _) => Enqueue(() => TurnAsync(+1));
        View.PreviousPageRequested += (_, _) => Enqueue(() => TurnAsync(-1));
        View.AddRequested += (_, _) => Enqueue(() => OpenEditorAsync(null));
        View.EditRequested += (_, _) => Enqueue(() => OpenEditorAsync(View.SelectedId));
        View.DeactivateRequested += (_, _) => Enqueue(DeactivateAsync);

        View.ChooseRequested += (_, _) =>
        {
            // In picker mode the caller reads SelectedId off the form, so
            // all this has to do is close with a success result.
            if (View.SelectedId is not null) View.CloseView(true);
            else View.ShowInfo("Choose a patient first.");
        };
    }

    public override Task InitialiseAsync() => LoadAsync();

    /// Patients to a page, as on the product list.
    internal const int PageSize = 100;

    /// The page on screen, from nought.
    private int _page;

    /// How many patients the search matched, as of the last page read.
    private int _total;

    private Task FirstPageAsync()
    {
        _page = 0;
        return LoadAsync();
    }

    private Task TurnAsync(int by)
    {
        var page = Math.Clamp(_page + by, 0, LastPage(_total));
        if (page == _page) return Task.CompletedTask;

        _page = page;
        return LoadAsync();
    }

    private static int LastPage(int total) =>
        Math.Max(0, (total - 1) / PageSize);

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var count = await _patients.CountAsync(View.SearchText);

        if (count.IsFailure)
        {
            View.ShowError(count.Error.Message);
            return;
        }

        // Patients deactivated off the end of the last page leave it
        // empty; the page before it is the one to show.
        _total = count.Value;
        _page = Math.Min(_page, LastPage(_total));

        var result = await _patients.SearchAsync(View.SearchText,
            take: PageSize, skip: _page * PageSize);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowPatients(result.Value);
        View.ShowPage(_page, PageSize, result.Value.Count, _total);
    });

    private Task OpenEditorAsync(Guid? id) => GuardAsync(async () =>
    {
        using var editor = _views.Create<frmPatientEdit>();
        editor.LoadPatient(id);

        if (editor.ShowDialog((Form)View) == DialogResult.OK)
            await LoadAsync();
    });

    private Task DeactivateAsync() => GuardAsync(async () =>
    {
        if (View.SelectedId is not Guid id)
        {
            View.ShowInfo("Choose a patient first.");
            return;
        }

        if (!View.Confirm(
                "Deactivate this patient? Their dispensing history is "
                + "kept - it is a legal record - but they will not appear "
                + "in searches."))
            return;

        var result = await _patients.DeactivateAsync(id);

        if (result.IsFailure) View.ShowError(result.Error.Message);
        await LoadAsync();
    });
}

public sealed class PatientEditPresenter : PresenterBase<IPatientEditView>
{
    private readonly IPatientService _patients;
    private readonly Application.Reference.IReferenceDataService _reference;

    public PatientEditPresenter(IPatientService patients,
        Application.Reference.IReferenceDataService reference)
    {
        _patients = patients;
        _reference = reference;
    }

    protected override void Subscribe()
    {
        View.SaveRequested += (_, _) => Enqueue(SaveAsync);
        View.CancelRequested += (_, _) => View.CloseView(false);
    }

    public override Task InitialiseAsync() => GuardAsync(async () =>
    {
        // The scheme list first, and for a new patient as well as an
        // existing one: somebody adding a patient at the counter is
        // usually holding their medical aid card.
        var schemes = await _reference.ListAsync(
            Application.Reference.ReferenceKind.MedicalAidScheme,
            includeInactive: false);

        if (schemes.IsSuccess) View.ShowSchemes(schemes.Value);

        if (View.PatientId is not Guid id) return;   // adding a new one

        var result = await _patients.GetAsync(id);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            View.CloseView(false);
            return;
        }

        View.ShowPatient(result.Value);
    });

    private Task SaveAsync() => GuardAsync(async () =>
    {
        var edit = View.Collect();

        // Somebody with these names and this birthday already has a file.
        // Asked, not refused: two people can share both.
        if (View.PatientId is null)
        {
            var same = await _patients.PossibleDuplicatesAsync(edit);

            if (same.IsSuccess && same.Value.Count > 0
                && !View.Confirm(
                    "This may be a patient already on file - the same name "
                    + "and date of birth, or the same telephone number:\n\n"
                    + string.Join("\n", same.Value.Select(p =>
                        $"  {p.FileNumber}   {p.Surname}, {p.FirstNames}"
                        + (p.Telephone is null ? "" : $"   {p.Telephone}")
                        + (p.Address is null ? "" : $"   {p.Address}")))
                    + "\n\nOpen a new file for them anyway?"))
                return;
        }

        var result = View.PatientId is Guid id
            ? await _patients.UpdateAsync(id, edit)
            : (await _patients.CreateAsync(edit)) as Application.Results.Result;

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.CloseView(true);
    });
}

/// <summary>
/// What patients owe, and what is done about it.
/// </summary>
public interface IAccountsView : IView
{
    string SearchText { get; }
    bool OwingOnly { get; }
    AccountDto? Selected { get; }

    void ShowAccounts(IReadOnlyList<AccountDto> accounts);

    /// <param name="print">Prints the statement, where it can be.</param>
    /// <param name="saveAs">Exports it to a workbook, where it can be.</param>
    void ShowStatement(StatementDto statement, Action? print = null,
                       Action? saveAs = null);

    (decimal Amount, string Tender, string? Reference)? PromptPayment(
        AccountDto account);

    decimal? PromptCreditLimit(AccountDto account);
    string? PromptHoldReason();

    event EventHandler? RefreshRequested;
    event EventHandler? OpenRequested;
    event EventHandler? PaymentRequested;
    event EventHandler? StatementRequested;
    event EventHandler? CreditLimitRequested;
    event EventHandler? HoldRequested;
}

public sealed class AccountsPresenter : PresenterBase<IAccountsView>
{
    private readonly IAccountService _accounts;
    private readonly IPatientService _patients;
    private readonly ViewFactory _views;
    private readonly Application.Abstractions.IClock _clock;
    private readonly Application.Sales.IDocumentPrinter? _printer;
    private readonly Features.Sales.ReceiptHeaderFactory? _headers;
    private readonly Application.Reporting.IWorkbookWriter? _workbooks;
    private readonly Application.Abstractions.ICurrentUser? _user;

    /// Runtime. Resolved by the container, which picks the fullest
    /// constructor it can satisfy.
    public AccountsPresenter(IAccountService accounts,
        IPatientService patients, ViewFactory views,
        Application.Abstractions.IClock clock,
        Application.Sales.IDocumentPrinter printer,
        Features.Sales.ReceiptHeaderFactory headers,
        Application.Reporting.IWorkbookWriter workbooks,
        Application.Abstractions.ICurrentUser user)
        : this(accounts, patients, views, clock)
    {
        _printer = printer;
        _headers = headers;
        _workbooks = workbooks;
        _user = user;
    }

    public AccountsPresenter(IAccountService accounts,
        IPatientService patients, ViewFactory views,
        Application.Abstractions.IClock clock)
    {
        _accounts = accounts;
        _patients = patients;
        _views = views;
        _clock = clock;
    }

    protected override void Subscribe()
    {
        View.RefreshRequested += (_, _) => Enqueue(LoadAsync);
        View.OpenRequested += (_, _) => Enqueue(OnOpenAsync);
        View.PaymentRequested += (_, _) => Enqueue(OnPaymentAsync);
        View.StatementRequested += (_, _) => Enqueue(OnStatementAsync);
        View.CreditLimitRequested += (_, _) => Enqueue(OnCreditLimitAsync);
        View.HoldRequested += (_, _) => Enqueue(OnHoldAsync);
    }

    public override Task InitialiseAsync() => LoadAsync();

    private Task LoadAsync() => GuardAsync(async () =>
    {
        // "Owing only" is the working list; the search is how somebody
        // reaches an account that owes nothing.
        var result = View.OwingOnly && View.SearchText.Length == 0
            ? await _accounts.OwingAsync()
            : await _accounts.SearchAsync(View.SearchText);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowAccounts(result.Value);
    });

    /// <summary>
    /// Opens an account for a patient who has not got one.
    /// </summary>
    /// <remarks>
    /// Through the patient picker, because an account belongs to a
    /// person and the alternative is typing a name that then does not
    /// match the patient the scripts are against.
    /// </remarks>
    private Task OnOpenAsync() => GuardAsync(async () =>
    {
        using var picker = _views.Create<frmPatientList>();
        picker.UseAsPicker();

        if (picker.ShowDialog((Form)View) != DialogResult.OK) return;
        if (picker.SelectedId is not Guid patientId) return;

        var limit = Controls.frmNumberPrompt.Ask((Form)View,
            "New account", "What may this customer owe at most?", 0m,
            decimals: 2);

        if (limit is not decimal creditLimit) return;

        var result = await _accounts.OpenAsync(patientId, creditLimit);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowInfo($"Account {result.Value.AccountNumber} opened for "
                      + result.Value.Name + ".");

        await LoadAsync();
    });

    private Task OnPaymentAsync() => GuardAsync(async () =>
    {
        if (View.Selected is not AccountDto account) return;

        if (View.PromptPayment(account) is not var (amount, tender, reference))
            return;

        var result = await _accounts.TakePaymentAsync(account.Id, amount,
            tender, _clock.Today, reference);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        // The customer's receipt for it. A payment off an account printed
        // nothing, so the one who settled in cash had no proof of it
        // (ADM-OBS-17).
        var printed = await PrintAsync(AccountDocuments.PaymentReceipt(
            result.Value, amount, Describe(tender), reference, _clock.Today));

        View.ShowInfo($"{Format.Money(amount)} received. "
                      + account.Name + " now owes "
                      + Format.Money(result.Value.Balance) + "."
                      + (printed is null ? "" : "\n\n" + printed));

        await LoadAsync();
    });

    private static string Describe(string tender) =>
        Enum.TryParse<Domain.Sales.TenderType>(tender, out var t)
            ? Domain.Sales.TenderTypeExtensions.DisplayName(t)
            : tender;

    /// Prints on the receipt printer and says what happened, or null
    /// where this screen has no printer to hand.
    private async Task<string?> PrintAsync(
        Application.Sales.PrintableDocument document)
    {
        if (_printer is null || _headers is null) return null;

        var result = await _printer.PrintAsync(document, _headers.Build());
        return result.Describe(document.Title);
    }

    private Task OnStatementAsync() => GuardAsync(async () =>
    {
        if (View.Selected is not AccountDto account) return;

        // A year back: long enough to cover any dispute anybody is
        // likely to raise, short enough to stay readable.
        var result = await _accounts.StatementAsync(account.Id,
            _clock.Today.AddYears(-1), _clock.Today);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        var statement = result.Value;

        // Run straight from the viewer's buttons rather than queued: the
        // statement is still on screen, and a queued print would wait for
        // it to close.
        View.ShowStatement(statement,
            print: _printer is null ? null : () => _ = PrintStatementAsync(statement),
            saveAs: _workbooks is null || _user is null ? null : () =>
                ExportStatement(statement));
    });

    private async Task PrintStatementAsync(StatementDto statement)
    {
        try
        {
            var printed = await PrintAsync(AccountDocuments.Statement(statement));
            if (printed is not null) View.ShowInfo(printed);
        }
        catch (Domain.Exceptions.DomainException ex)
        {
            View.ShowError(ex.Message);
        }
    }

    private void ExportStatement(StatementDto statement)
    {
        if (_workbooks is null || _user is null) return;

        using var save = new SaveFileDialog
        {
            DefaultExt = "xlsx",
            FileName = $"Statement {statement.Account.AccountNumber} "
                       + $"{_clock.Today:yyyy-MM-dd}.xlsx",
            Filter = "Excel workbook (*.xlsx)|*.xlsx",
            OverwritePrompt = true,
            Title = "Export statement",
            InitialDirectory = GridExport.Downloads
        };

        if (save.ShowDialog((Form)View) != DialogResult.OK) return;

        _workbooks.WriteTo(AccountDocuments.StatementWorkbook(statement,
            _user.Username, DateTime.Now), save.FileName);

        View.ShowSaved(save.FileName);
    }

    private Task OnCreditLimitAsync() => GuardAsync(async () =>
    {
        if (View.Selected is not AccountDto account) return;
        if (View.PromptCreditLimit(account) is not decimal limit) return;

        var result = await _accounts.ChangeCreditLimitAsync(account.Id,
                                                            limit);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        await LoadAsync();
    });

    private Task OnHoldAsync() => GuardAsync(async () =>
    {
        if (View.Selected is not AccountDto account) return;

        var result = account.IsOnHold
            ? await _accounts.ReleaseAsync(account.Id)
            : await Hold(account);

        if (result is null) return;

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        await LoadAsync();
    });

    private async Task<Application.Results.Result?> Hold(AccountDto account)
    {
        var reason = View.PromptHoldReason();

        return string.IsNullOrWhiteSpace(reason)
            ? null
            : await _accounts.HoldAsync(account.Id, reason);
    }
}
