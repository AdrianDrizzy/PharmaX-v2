using System.Globalization;
using PharmaX.Application.Patients;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Patients;

/// <summary>
/// Dispensary's Merge Patient Files. Two files, one person: the history
/// moves onto the file being kept and the duplicate is retired.
/// </summary>
/// <remarks>
/// Deliberately a Supervising Controller rather than a presenter pair.
/// The screen is two pickers and a confirmation, and the one rule worth
/// keeping out of it - what may be merged into what - lives in the
/// service, which refuses on its own account.
/// </remarks>
public partial class frmPatientMerge : PhxFormBase
{
    private readonly IPatientMergeService? _merges;
    private readonly ViewFactory? _views;

    private Guid? _keepId;
    private Guid? _mergeId;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ScriptAuthorise;

    /// Designer only. Never called at runtime.
    public frmPatientMerge()
    {
        InitializeComponent();

        btnChooseKeep.Click += (_, _) => Choose(keep: true);
        btnChooseMerge.Click += (_, _) => Choose(keep: false);
        btnMerge.Click += (_, _) => _ = MergeAsync();
        btnCancel.Click += (_, _) => CloseView(false);

        btnMerge.Enabled = false;
    }

    /// Runtime. Resolved by the container.
    public frmPatientMerge(IPatientMergeService merges, ViewFactory views)
        : this()
    {
        _merges = merges;
        _views = views;
    }

    private void Choose(bool keep)
    {
        if (_views is null) return;

        using var picker = _views.Create<frmPatientList>();
        picker.UseAsPicker();

        if (picker.ShowDialog(this) != DialogResult.OK) return;
        if (picker.SelectedId is not Guid id) return;

        if (keep) _keepId = id; else _mergeId = id;

        _ = PreviewAsync();
    }

    /// <summary>
    /// Asks the service what the merge would move and says so, before
    /// anything is moved.
    /// </summary>
    private Task PreviewAsync() => GuardAsync(async () =>
    {
        btnMerge.Enabled = false;

        if (_merges is null) return;
        if (_keepId is not Guid keep || _mergeId is not Guid merge)
        {
            lblEffect.Text = "Choose both files to see what will move.";
            return;
        }

        var result = await _merges.PreviewAsync(keep, merge);

        if (result.IsFailure)
        {
            lblEffect.Text = result.Error.Message;
            return;
        }

        Describe(result.Value);
        btnMerge.Enabled = true;
    });

    private void Describe(MergePreviewDto p)
    {
        txtKeep.Text = $"{p.KeepFileNumber}  {p.KeepName}";
        txtMerge.Text = $"{p.MergeFileNumber}  {p.MergeName}";

        var what = string.Format(CultureInfo.CurrentCulture,
            "{0:N0} script(s) and {1:N0} dispensing record(s) will move "
            + "from {2} onto {3}, and {2} will be retired.",
            p.Scripts, p.Dispensings, p.MergeFileNumber, p.KeepFileNumber);

        if (p.MovesAccount)
            what += " The duplicate's account will follow, because the file "
                  + "being kept has none.";

        if (p.KeepAlreadyHasAccount)
            what += " Both files have an account: the balances are left "
                  + "where they are, and will need settling by hand.";

        lblEffect.Text = what;
    }

    private Task MergeAsync() => GuardAsync(async () =>
    {
        if (_merges is null) return;
        if (_keepId is not Guid keep || _mergeId is not Guid merge) return;

        var reason = txtReason.Text.Trim();

        if (string.IsNullOrWhiteSpace(reason))
        {
            ShowError("Give a reason. A merge cannot be undone from here.");
            txtReason.Focus();
            return;
        }

        if (!Confirm(lblEffect.Text + Environment.NewLine
                     + Environment.NewLine + "Merge these files?")) return;

        var result = await _merges.MergeAsync(keep, merge, reason);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        ShowInfo(string.Format(CultureInfo.CurrentCulture,
            "Merged. {0:N0} script(s) and {1:N0} dispensing record(s) moved "
            + "onto {2}.",
            result.Value.Scripts, result.Value.Dispensings,
            result.Value.KeepFileNumber));

        CloseView(true);
    });

    /// <summary>
    /// The same guard the presenters use, kept here because this screen
    /// has no presenter of its own.
    /// </summary>
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
