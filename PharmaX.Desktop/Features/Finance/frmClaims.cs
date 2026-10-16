using PharmaX.Application.Reporting;
using PharmaX.Application.Abstractions;
using System.Globalization;
using System.Text;
using PharmaX.Application.Claims;
using PharmaX.Application.Reference;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

/// <summary>
/// What the medical aid schemes owe, and where their payments are
/// recorded.
/// </summary>
/// <remarks>
/// Built around the piece of paper it is used with. A scheme sends one
/// remittance advice listing many claims and pays one lump sum, so the
/// screen is a list of that scheme's claims with a column to type what
/// each was paid - not a form that settles one claim at a time.
/// </remarks>
public partial class frmClaims : PhxFormBase, IClaimsView
{
    private readonly ClaimsPresenter? _presenter;
    private readonly IWorkbookWriter? _workbooks;
    private readonly ICurrentUser? _user;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.LedgerRead;

    /// Designer only. Never called at runtime.
    public frmClaims()
    {
        InitializeComponent();

        dtpReceived.Value = DateTime.Today;

        cboScheme.SelectedIndexChanged += (_, _) =>
        {
            GridPager.Of(grdClaims)?.Restart();
            SchemeChanged?.Invoke(this, EventArgs.Empty);
        };

        grdClaims.CellValueChanged += (_, _) => ShowTotals();

        // A checkbox commits only when the cell loses focus, which is a
        // keystroke too late for a running total.
        grdClaims.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grdClaims.IsCurrentCellDirty)
                grdClaims.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };

        btnSettle.Click += (_, _) =>
            SettleRequested?.Invoke(this, EventArgs.Empty);

        btnPayAll.Click += (_, _) => PayEverythingInFull();

        btnPayAmount.Click += (_, _) => PaySelectedClaim();

        btnReject.Click += (_, _) =>
            RejectRequested?.Invoke(this, EventArgs.Empty);

        btnAges.Click += (_, _) =>
            AgeAnalysisRequested?.Invoke(this, EventArgs.Empty);

        btnClaimForm.Click += (_, _) =>
            ClaimFormRequested?.Invoke(this, EventArgs.Empty);

        btnRefresh.Click += (_, _) =>
            RefreshRequested?.Invoke(this, EventArgs.Empty);

        btnExport.Click += (_, _) => Export();

        btnClose.Click += (_, _) => CloseView(false);
    }

    /// Runtime. Resolved by the container.
    public frmClaims(ClaimsPresenter presenter, IWorkbookWriter workbooks,
                       ICurrentUser user) : this()
    {
        _presenter = presenter;
        _workbooks = workbooks;
        _user = user;
        _presenter.Attach(this);
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        if (_presenter is not null) await _presenter.InitialiseAsync();
    }

    public Guid? SelectedSchemeId =>
        cboScheme.SelectedItem is SchemeChoice choice ? choice.Id : null;

    public DateOnly ReceivedOn => DateOnly.FromDateTime(dtpReceived.Value);

    public string Reference => txtReference.Text.Trim();

    public Guid? SelectedClaimId =>
        grdClaims.CurrentRow?.DataBoundItem is ClaimRow row ? row.Id : null;

    public void ShowSchemes(IReadOnlyList<ReferenceItemDto> schemes)
    {
        ArgumentNullException.ThrowIfNull(schemes);

        cboScheme.Items.Clear();
        cboScheme.Items.Add(new SchemeChoice(null, "All schemes"));

        foreach (var scheme in schemes)
            cboScheme.Items.Add(new SchemeChoice(scheme.Id,
                $"{scheme.Code}  {scheme.Name}"));

        cboScheme.SelectedIndex = 0;
    }

    public void ShowClaims(IReadOnlyList<ClaimDto> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        GridPager.Attach(grdClaims).Show(claims.Select(ClaimRow.From));

        foreach (DataGridViewRow row in grdClaims.Rows)
        {
            if (row.DataBoundItem is not ClaimRow claim) continue;

            // Over ninety days is money the pharmacy is unlikely to see
            // without a phone call, so it is coloured like a problem.
            if (claim.AgeInDays > 90)
                PhxTheme.Flag(row, PhxTheme.Problem);
            else if (claim.AgeInDays > 60)
                PhxTheme.Flag(row, PhxTheme.Attention);
        }

        ShowTotals();

        // Settling needs a named scheme: a remittance comes from one
        // scheme, and the service refuses a mixed one anyway.
        btnSettle.Enabled = SelectedSchemeId is not null && claims.Count > 0;
        btnPayAll.Enabled = claims.Count > 0;
        PhxTheme.MakePrimary(btnSettle);
    }

    /// <summary>
    /// Fills every Paying cell with what that claim is owed.
    /// </summary>
    /// <remarks>
    /// The commonest remittance is one that pays everything in full, and
    /// typing forty identical figures by hand is how a mistake gets
    /// made.
    /// </remarks>
    private void PayEverythingInFull()
    {
        foreach (DataGridViewRow row in grdClaims.Rows)
        {
            if (row.DataBoundItem is not ClaimRow claim) continue;

            row.Cells["Paying"].Value =
                claim.Outstanding.ToString("0.00",
                    CultureInfo.CurrentCulture);
        }

        ShowTotals();
    }

    /// <summary>
    /// Asks what the scheme paid on the selected claim, and whether the
    /// rest is written off, instead of typing into the grid.
    /// </summary>
    /// <remarks>
    /// The Paying cell takes typing only with the keyboard focus in it,
    /// which a screen reader and UI Automation cannot give, so a part
    /// payment could not be recorded without the mouse (ADM-BLK-G02).
    /// </remarks>
    private void PaySelectedClaim()
    {
        if (grdClaims.CurrentRow is not { DataBoundItem: ClaimRow claim } row)
        {
            ShowInfo("Choose a claim first.");
            return;
        }

        // Starts at what is already typed against it, or else the whole
        // amount owed, which is what most remittances pay.
        var typed = Amount(row.Cells["Paying"].Value);

        var paid = frmNumberPrompt.Ask(this, "Part payment",
            $"What was paid on {claim.ClaimNumber}? "
            + $"{Format.Money(claim.Outstanding)} is owed.",
            typed > 0m ? typed : claim.Outstanding, decimals: 2);

        if (paid is not decimal amount) return;

        if (amount < 0m || amount > claim.Outstanding)
        {
            ShowError($"Between nothing and the "
                      + $"{Format.Money(claim.Outstanding)} owed.");
            return;
        }

        row.Cells["Paying"].Value = amount.ToString("0.00",
            CultureInfo.CurrentCulture);

        row.Cells["WriteOffRest"].Value = amount < claim.Outstanding
            && Confirm($"Write off the other "
                       + $"{Format.Money(claim.Outstanding - amount)}? Choose "
                       + "No to leave it owing.");

        ShowTotals();
    }

    private void ShowTotals()
    {
        var owed = 0m;
        var paying = 0m;
        var writingOff = 0m;

        foreach (DataGridViewRow row in grdClaims.Rows)
        {
            if (row.DataBoundItem is not ClaimRow claim) continue;

            owed += claim.Outstanding;

            var thisRow = Amount(row.Cells["Paying"].Value);
            paying += thisRow;

            if (row.Cells["WriteOffRest"].Value is true)
                writingOff += Math.Max(0m, claim.Outstanding - thisRow);
        }

        lblTotals.Text = string.Format(CultureInfo.CurrentCulture,
            "Owed {0}     Paying {1}     Writing off {2}",
            Format.Money(owed), Format.Money(paying),
            Format.Money(writingOff));

        lblTotals.ForeColor = paying > 0m ? PhxTheme.Title : PhxTheme.Muted;
    }

    public IReadOnlyList<RemittanceLineDto> CollectRemittance()
    {
        var lines = new List<RemittanceLineDto>();

        foreach (DataGridViewRow row in grdClaims.Rows)
        {
            if (row.DataBoundItem is not ClaimRow claim) continue;

            var paying = Amount(row.Cells["Paying"].Value);
            var writeOff = row.Cells["WriteOffRest"].Value is true;

            // A row with nothing typed and nothing ticked was not on the
            // remittance at all, and must be left alone rather than
            // settled for zero.
            if (paying <= 0m && !writeOff) continue;

            lines.Add(new RemittanceLineDto(claim.Id, paying, writeOff));
        }

        return lines;
    }

    public void ShowAgeAnalysis(IReadOnlyList<SchemeAgeDto> analysis)
    {
        ArgumentNullException.ThrowIfNull(analysis);

        if (analysis.Count == 0)
        {
            ShowInfo("No scheme owes the pharmacy anything.");
            return;
        }

        var text = new StringBuilder();

        text.AppendLine(string.Format(CultureInfo.CurrentCulture,
            "{0,-28} {1,6} {2,12} {3,12} {4,12} {5,12} {6,12}",
            "Scheme", "Claims", "Current", "31-60", "61-90", "Over 90",
            "Total"));

        text.AppendLine(new string('-', 100));

        foreach (var scheme in analysis)
        {
            text.AppendLine(string.Format(CultureInfo.CurrentCulture,
                "{0,-28} {1,6} {2,12} {3,12} {4,12} {5,12} {6,12}",
                Trim(scheme.SchemeName, 28), scheme.Claims,
                Format.Amount(scheme.Current), Format.Amount(scheme.Days31),
                Format.Amount(scheme.Days61), Format.Amount(scheme.Over90),
                Format.Amount(scheme.Total)));
        }

        text.AppendLine(new string('-', 100));

        text.AppendLine(string.Format(CultureInfo.CurrentCulture,
            "{0,-28} {1,6} {2,12} {3,12} {4,12} {5,12} {6,12}",
            "", analysis.Sum(s => s.Claims),
            Format.Amount(analysis.Sum(s => s.Current)),
            Format.Amount(analysis.Sum(s => s.Days31)),
            Format.Amount(analysis.Sum(s => s.Days61)),
            Format.Amount(analysis.Sum(s => s.Over90)),
            Format.Amount(analysis.Sum(s => s.Total))));

        frmTextViewer.Show(this, "Medical aid age analysis",
                           text.ToString());
    }

    public string? PromptRejectReason() =>
        frmTextPrompt.Ask(this, "Reject claim",
            "Why is the scheme refusing this claim?",
            hint: "e.g. Rejected: member not on file");

    public void ClearRemittance()
    {
        txtReference.Clear();
        dtpReceived.Value = DateTime.Today;
    }

    private static string Trim(string text, int width) =>
        text.Length <= width ? text : text[..(width - 1)] + "…";

    /// <remarks>
    /// Silent on anything unparseable: the cell is being typed into, and
    /// half of "12.50" is "12." - a message box on the way through would
    /// make the screen impossible to use.
    /// </remarks>
    private static decimal Amount(object? value)
    {
        if (value is decimal already) return already;

        return decimal.TryParse(value?.ToString(), NumberStyles.Number,
                                CultureInfo.CurrentCulture, out var parsed)
                   && parsed > 0m
            ? parsed
            : 0m;
    }

    /// <summary>
    /// One claim as the grid shows it. Paying and WriteOffRest are what
    /// the operator fills in from the remittance advice.
    /// </summary>
    private sealed class ClaimRow
    {
        public Guid Id { get; init; }
        public string ClaimNumber { get; init; } = "";
        public string PatientName { get; init; } = "";
        public string MembershipNumber { get; init; } = "";
        public string ClaimedOnText { get; init; } = "";
        public string AgeText { get; init; } = "";
        public string OutstandingText { get; init; } = "";
        public decimal Outstanding { get; init; }
        public int AgeInDays { get; init; }

        // Settable, because the grid writes back into them.
        public string Paying { get; set; } = "";
        public bool WriteOffRest { get; set; }

        public static ClaimRow From(ClaimDto dto) => new()
        {
            Id = dto.Id,
            ClaimNumber = dto.ClaimNumber,
            PatientName = dto.PatientName,
            MembershipNumber = dto.MembershipNumber,
            ClaimedOnText = Format.Date(dto.ClaimedOn),
            AgeText = dto.AgeInDays.ToString(CultureInfo.CurrentCulture),
            OutstandingText = Format.Amount(dto.Outstanding),
            Outstanding = dto.Outstanding,
            AgeInDays = dto.AgeInDays
        };
    }

    private sealed record SchemeChoice(Guid? Id, string Text)
    {
        public override string ToString() => Text;
    }

    public event EventHandler? RefreshRequested;
    public event EventHandler? SchemeChanged;
    public event EventHandler? SettleRequested;
    public event EventHandler? RejectRequested;
    public event EventHandler? AgeAnalysisRequested;
    public event EventHandler? ClaimFormRequested;

    /// <summary>
    /// The grid as it stands, to a workbook the operator names.
    /// </summary>
    /// <remarks>
    /// What is on the screen, filters and all: this is read as a working
    /// document and the figures were previously only reachable by typing
    /// them again somewhere else.
    /// </remarks>
    private void Export()
    {
        if (_workbooks is null || _user is null) return;   // designer

        var saved = GridExport.Offer(this, _workbooks, grdClaims,
            "Medical Aid Claims", "Outstanding claims", _user.Username);

        if (saved is null)
        {
            if (grdClaims.Rows.Count == 0)
                ShowInfo("There is nothing on the screen to export.");

            return;
        }

        ShowSaved(saved);
    }
}
