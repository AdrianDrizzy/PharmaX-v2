using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

public interface IPatientListView : IView
{
    string SearchText { get; }
    Guid? SelectedId { get; }

    void ShowPatients(IReadOnlyList<PatientListItemDto> patients);
    void ShowCount(int count);

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
        View.SearchChanged += (_, _) => Enqueue(LoadAsync);
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

    private Task LoadAsync() => GuardAsync(async () =>
    {
        var result = await _patients.SearchAsync(View.SearchText);

        if (result.IsFailure)
        {
            View.ShowError(result.Error.Message);
            return;
        }

        View.ShowPatients(result.Value);
        View.ShowCount(result.Value.Count);
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
    void ShowStatement(StatementDto statement);

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

        View.ShowInfo($"{Format.Money(amount)} received. "
                      + account.Name + " now owes "
                      + Format.Money(result.Value.Balance) + ".");

        await LoadAsync();
    });

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

        View.ShowStatement(result.Value);
    });

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
