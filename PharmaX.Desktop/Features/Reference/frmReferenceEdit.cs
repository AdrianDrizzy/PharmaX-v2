using PharmaX.Application.Reference;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Reference;

public partial class frmReferenceEdit : PhxFormBase, IReferenceEditView
{
    private readonly ReferenceEditPresenter? _presenter;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.PriceChange;

    /// Designer only. Never called at runtime.
    public frmReferenceEdit()
    {
        InitializeComponent();

        btnSave.Click += (_, _) => SaveRequested?.Invoke(this, EventArgs.Empty);
        btnCancel.Click += (_, _) =>
            CancelRequested?.Invoke(this, EventArgs.Empty);
    }

    /// Runtime. Resolved by the container.
    public frmReferenceEdit(ReferenceEditPresenter presenter) : this()
    {
        _presenter = presenter;
        _presenter.Attach(this);
    }

    public ReferenceKind Kind { get; private set; } = ReferenceKind.Department;
    public Guid? ItemId { get; private set; }

    /// Called by the list screen before the dialog is shown.
    public void Prepare(ReferenceKind kind, Guid? id)
    {
        Kind = kind;
        ItemId = id;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
        txtCode.Focus();
    }

    public void ApplyLabels(ReferenceLabels labels)
    {
        ArgumentNullException.ThrowIfNull(labels);

        Text = ItemId is null
            ? $"PharmaX - New {labels.Singular}"
            : $"PharmaX - Edit {labels.Singular}";

        lblDetail.Text = $"&{labels.DetailCaption}";
    }

    public void ShowItem(ReferenceItemDto item)
    {
        ArgumentNullException.ThrowIfNull(item);

        txtCode.Text = item.Code;
        txtName.Text = item.Name;
        txtDetail.Text = item.Detail ?? "";
    }

    public void LockCode()
    {
        // Everything else in the system points at this entry by code, so
        // changing it would orphan those references silently.
        txtCode.ReadOnly = true;
        txtCode.TabStop = false;
        txtName.Focus();
    }

    public ReferenceEditDto Collect() =>
        new(txtCode.Text.Trim(), txtName.Text.Trim(), txtDetail.Text.Trim());

    public event EventHandler? SaveRequested;
    public event EventHandler? CancelRequested;
}
