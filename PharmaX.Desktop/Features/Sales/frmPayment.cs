using System.Globalization;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Mvp;
using PharmaX.Domain.Sales;

namespace PharmaX.Desktop.Features.Sales;

/// <summary>
/// Takes the tender. Supports a split across several tenders, because a
/// customer paying part cash and part card is routine and a dialog that
/// only takes one forces the operator to abandon and start again.
/// </summary>
public partial class frmPayment : Form
{
    private readonly decimal _amountDue;
    private readonly List<PaymentDto> _payments = new();

    /// <summary>
    /// A tender as the cashier reads it against the name the sale
    /// stores.
    /// </summary>
    /// <remarks>
    /// The list used to be Enum.GetNames, so the busiest screen in the
    /// pharmacy offered "Eft" and "MedicalAid". The stored name is still
    /// the enum's, because that is what the domain parses.
    /// </remarks>
    private sealed class Tender
    {
        public Tender(TenderType type, string label)
        {
            Type = type;
            Label = label;
        }

        public TenderType Type { get; }
        public string Label { get; }
        public string Name => Type.ToString();

        public override string ToString() => Label;
    }

    private static readonly Tender[] Tenders =
    {
        new(TenderType.Cash, "Cash"),
        new(TenderType.Card, "Card"),
        new(TenderType.Eft, "EFT"),
        new(TenderType.Account, "Account"),
        new(TenderType.MedicalAid, "Medical aid"),
        new(TenderType.Voucher, "Voucher")
    };

    /// <summary>
    /// The tenders settled on a machine of their own, away from PharmaX.
    /// </summary>
    /// <remarks>
    /// There is no payment gateway. The card machine and the medical aid
    /// platform sit beside the till and the cashier works them directly,
    /// so PharmaX records what was taken and on which tender, and leaves
    /// the reference to the operator's discretion.
    /// </remarks>
    private static bool SettledElsewhere(TenderType tender) =>
        tender is TenderType.Card or TenderType.Eft
            or TenderType.MedicalAid;

    public IReadOnlyList<PaymentDto> Payments => _payments;

    public frmPayment(decimal amountDue)
    {
        InitializeComponent();

        // A plain Form rather than PhxFormBase, so branded here.
        Text = WindowTitles.Brand(Text);

        _amountDue = amountDue;

        cboTender.Items.AddRange(Tenders);
        cboTender.SelectedIndex = 0;

        cboTender.SelectedIndexChanged += (_, _) => ShowReferencePrompt();

        // Pre-filled with the whole amount: the overwhelmingly common
        // case is one tender for the full total, and Enter should finish
        // the sale without another keystroke.
        txtAmount.Amount = amountDue;

        btnAdd.Click += (_, _) => AddTender();
        btnRemove.Click += (_, _) => RemoveSelected();
        btnOk.Click += (_, _) => Accept();

        AcceptButton = btnAdd;
        CancelButton = btnCancel;

        Redraw();
        ShowReferencePrompt();
        Shown += (_, _) => { txtAmount.Focus(); txtAmount.SelectAll(); };
    }

    private Tender Selected =>
        cboTender.SelectedItem as Tender ?? Tenders[0];

    /// <summary>
    /// Says where the reference comes from, and only asks for one where
    /// there is something to read it off.
    /// </summary>
    private void ShowReferencePrompt()
    {
        lblReference.Text = "&Reference";

        txtReference.PlaceholderText = SettledElsewhere(Selected.Type)
            ? "Optional - slip or approval number"
            : "Optional";
    }

    private decimal Outstanding =>
        _amountDue - _payments.Sum(p => p.Amount);

    private decimal CashTendered => _payments
        .Where(p => string.Equals(p.TenderType, nameof(TenderType.Cash),
                                  StringComparison.Ordinal))
        .Sum(p => p.Amount);

    /// Turned round for a refund, where there is no change to give.
    private bool _isRefund;

    /// <summary>
    /// Why the tenders as they stand cannot finish the sale, or null if
    /// they can.
    /// </summary>
    /// <remarks>
    /// More than the amount due is fine when the excess can come back as
    /// change - which only cash can give. The dialog used to refuse any
    /// excess at all, so a 60.00 note for a 53.00 sale could not be
    /// entered and the till never worked out the change (DEF-0019).
    /// A static rule, so it can be tested without a window.
    /// </remarks>
    internal static string? RefuseToFinish(decimal outstanding,
                                           decimal cashTendered, bool refund)
    {
        if (outstanding > 0m)
            return $"Still {Format.Money(outstanding)} outstanding.";

        if (outstanding == 0m) return null;

        if (refund)
            return $"That refunds {Format.Money(-outstanding)} more than is "
                   + "owed. Reduce a line.";

        return -outstanding > cashTendered
            ? $"Over-tendered by {Format.Money(-outstanding)}, and only cash "
              + "can give change. Reduce the card, EFT or account amount."
            : null;
    }

    /// <summary>
    /// Why this tender cannot be added, or null if it can.
    /// </summary>
    /// <remarks>
    /// The amount is the only thing the till insists on. Card, EFT and
    /// medical aid are settled on their own machine before the cashier
    /// reaches this dialog, so a reference is theirs to add or not -
    /// demanding one only ever blocked a payment that had already gone
    /// through somewhere else.
    ///
    /// The rule rather than the click that runs it, so it can be checked
    /// without a window: a form that has never been shown cannot be
    /// clicked, and the rule is the part worth testing anyway.
    /// </remarks>
    internal static string? Refuse(TenderType tender, decimal? amount,
                                   string? reference)
    {
        _ = tender;
        _ = reference;

        return amount is not decimal value || value <= 0m
            ? "Enter an amount greater than zero."
            : null;
    }

    private void AddTender()
    {
        var tender = Selected;
        var reference = txtReference.Text.Trim();

        if (Refuse(tender.Type, txtAmount.Amount, reference) is string problem)
        {
            lblError.Text = problem;
            txtAmount.Focus();
            txtAmount.SelectAll();
            return;
        }

        _payments.Add(new PaymentDto(tender.Name, txtAmount.Amount ?? 0m,
            reference.Length == 0 ? null : reference));

        txtReference.Clear();
        Redraw();

        // Straight back to the amount for the next tender of a split,
        // which is where the cashier's hands already are.
        txtAmount.Focus();
        txtAmount.SelectAll();
    }

    private void RemoveSelected()
    {
        if (grdPayments.CurrentRow is null) return;

        var index = grdPayments.CurrentRow.Index;
        if (index >= 0 && index < _payments.Count) _payments.RemoveAt(index);

        Redraw();
    }

    private void Accept()
    {
        // BR-POS-01, checked here so the operator sees it before the
        // service refuses. The service checks it again regardless.
        if (RefuseToFinish(Outstanding, CashTendered, _isRefund)
            is string problem)
        {
            lblError.Text = problem;
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }

    /// <summary>
    /// Turns the dialog round for a refund. The tenders the customer
    /// originally paid with are offered first: refunding cash on a card
    /// sale is how a till ends up short with a matching card slip
    /// nobody can explain.
    /// </summary>
    public void UseAsRefund(IReadOnlyList<string> tendersUsed)
    {
        ArgumentNullException.ThrowIfNull(tendersUsed);

        Text = WindowTitles.Brand("PharmaX - Refund");
        _isRefund = true;
        lblDue.Text = "Amount to refund";
        btnOk.Text = "&Refund";

        if (tendersUsed.Count == 0) return;

        cboTender.Items.Clear();

        foreach (var tender in Tenders
                     .Where(t => tendersUsed.Contains(t.Name,
                                                      StringComparer.Ordinal)))
            cboTender.Items.Add(tender);

        // The others stay available, because a customer paying by card
        // and wanting cash back is a conversation, not an impossibility.
        foreach (var tender in Tenders
                     .Where(t => !tendersUsed.Contains(t.Name,
                                                       StringComparer.Ordinal)))
            cboTender.Items.Add(tender);

        cboTender.SelectedIndex = 0;
        ShowReferencePrompt();
    }

    private void Redraw()
    {
        var culture = CultureInfo.CurrentCulture;

        lblError.Text = "";
        grdPayments.Rows.Clear();

        foreach (var p in _payments)
        {
            // Read back the way it was offered, so a receipt query about
            // "the EFT" is not answered by a grid saying "Eft".
            var label = Tenders
                .FirstOrDefault(t => string.Equals(t.Name, p.TenderType,
                    StringComparison.Ordinal))?.Label ?? p.TenderType;

            grdPayments.Rows.Add(label, p.Amount, p.Reference ?? "");
        }

        lblDueValue.Text = Format.Money(_amountDue);

        // Over the amount due in cash is change to hand back, and the
        // dialog says so in those words rather than as a negative balance.
        var change = Outstanding < 0m && !_isRefund;
        lblOutstanding.Text = change ? "Change" : "Outstanding";
        lblOutstandingValue.Text = Format.Money(change ? -Outstanding
                                                       : Outstanding);

        btnOk.Enabled =
            RefuseToFinish(Outstanding, CashTendered, _isRefund) is null;

        // Once the tenders cover the sale, Enter pays.
        AcceptButton = btnOk.Enabled ? btnOk : btnAdd;
        txtAmount.Amount = Outstanding > 0m ? Outstanding : null;
    }
}
