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

    public IReadOnlyList<PaymentDto> Payments => _payments;

    public frmPayment(decimal amountDue)
    {
        InitializeComponent();

        _amountDue = amountDue;

        cboTender.Items.AddRange(Enum.GetNames<TenderType>());
        cboTender.SelectedIndex = 0;

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
        Shown += (_, _) => { txtAmount.Focus(); txtAmount.SelectAll(); };
    }

    private decimal Outstanding =>
        _amountDue - _payments.Sum(p => p.Amount);

    private void AddTender()
    {
        if (txtAmount.Amount is not decimal amount || amount <= 0m)
        {
            lblError.Text = "Enter an amount greater than zero.";
            return;
        }

        var tender = cboTender.SelectedItem?.ToString() ?? nameof(TenderType.Cash);
        var reference = txtReference.Text.Trim();

        // Card and EFT need the authorisation code off the slip, which is
        // what reconciles the merchant statement to the day's takings.
        if (tender is nameof(TenderType.Card) or nameof(TenderType.Eft)
            && reference.Length == 0)
        {
            lblError.Text = "A card or EFT payment needs its authorisation "
                            + "reference from the terminal slip.";
            txtReference.Focus();
            return;
        }

        _payments.Add(new PaymentDto(tender, amount,
            reference.Length == 0 ? null : reference));

        txtReference.Clear();
        Redraw();
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
        if (Outstanding != 0m)
        {
            lblError.Text = Outstanding > 0m
                ? $"Still {Format.Money(Outstanding)} outstanding."
                : $"Over-tendered by {Format.Money(-Outstanding)}. Reduce "
                  + "a line, or give change from the cash tender.";
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

        Text = "PharmaX - Refund";
        lblDue.Text = "Amount to refund";
        btnOk.Text = "&Refund";

        if (tendersUsed.Count == 0) return;

        cboTender.Items.Clear();

        foreach (var tender in tendersUsed) cboTender.Items.Add(tender);

        // The others stay available, because a customer paying by card
        // and wanting cash back is a conversation, not an impossibility.
        foreach (var tender in Enum.GetNames<TenderType>())
        {
            if (!tendersUsed.Contains(tender, StringComparer.Ordinal))
                cboTender.Items.Add(tender);
        }

        cboTender.SelectedIndex = 0;
    }

    private void Redraw()
    {
        var culture = CultureInfo.CurrentCulture;

        lblError.Text = "";
        grdPayments.Rows.Clear();

        foreach (var p in _payments)
            grdPayments.Rows.Add(p.TenderType, p.Amount, p.Reference ?? "");

        lblDueValue.Text = Format.Money(_amountDue);
        lblOutstandingValue.Text = Format.Money(Outstanding);

        btnOk.Enabled = Outstanding == 0m;
        txtAmount.Amount = Outstanding > 0m ? Outstanding : null;
    }
}
