using System.Globalization;
using System.Text;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Finance;

/// <summary>
/// Day-end posting: a day's takings turned into a balanced journal an
/// accountant can use.
/// </summary>
/// <remarks>
/// Prepare and post are separate on purpose. A prepared journal is saved
/// and can be read line by line; posting it is the moment it becomes
/// immutable, and nobody should be asked to commit to a figure they have
/// not been shown.
/// </remarks>
public partial class frmDayEnd : PhxFormBase
{
    private readonly IDayEndPostingService? _posting;
    private readonly PharmaXOptions? _options;

    private IReadOnlyList<JournalBatchDto> _batches = Array.Empty<JournalBatchDto>();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ReportFinancial;

    /// Designer only. Never called at runtime.
    public frmDayEnd()
    {
        InitializeComponent();

        dtpDay.Value = DateTime.Today.AddDays(-1);   // yesterday: the usual day to post

        btnSummarise.Click += (_, _) => _ = SummariseAsync();
        btnPrepare.Click += (_, _) => _ = PrepareAsync();
        btnPost.Click += (_, _) => _ = PostAsync();
        btnExport.Click += (_, _) => Export();
        btnClose.Click += (_, _) => CloseView(true);

        grdBatches.SelectionChanged += (_, _) => ShowSelectedLines();
    }

    /// Runtime. Resolved by the container.
    public frmDayEnd(IDayEndPostingService posting, PharmaXOptions options)
        : this()
    {
        _posting = posting;
        _options = options;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        await LoadBatchesAsync();
        await SummariseAsync();
    }

    private DateOnly Day => DateOnly.FromDateTime(dtpDay.Value.Date);

    private Task SummariseAsync() => GuardAsync(async () =>
    {
        if (_posting is null) return;

        var result = await _posting.SummariseAsync(Day);

        if (result.IsFailure)
        {
            lblSummary.Text = result.Error.Message;
            return;
        }

        var s = result.Value;
        var text = new StringBuilder();

        text.Append(CultureInfo.CurrentCulture,
            $"{s.Day:dd/MM/yyyy}: {s.Sales:N0} sale(s), ");
        text.Append(CultureInfo.CurrentCulture,
            $"{Format.Money(s.Gross)} taken - {Format.Amount(s.Vat)} VAT ");
        text.Append(CultureInfo.CurrentCulture,
            $"= {Format.Amount(s.Net)} income.");

        if (s.ByTender.Count > 0)
        {
            text.AppendLine();
            text.Append(string.Join("   ", s.ByTender
                .OrderBy(t => t.Key, StringComparer.Ordinal)
                .Select(t => $"{t.Key} {Format.Amount(t.Value)}")));
        }

        if (s.AlreadyPosted)
        {
            text.AppendLine();
            text.Append("This day already has a journal.");
        }

        lblSummary.Text = text.ToString();

        btnPrepare.Enabled = s.Sales > 0 && !s.AlreadyPosted;
    });

    private Task PrepareAsync() => GuardAsync(async () =>
    {
        if (_posting is null) return;

        var result = await _posting.PrepareAsync(Day);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        await LoadBatchesAsync();
        Select(result.Value.Id);
        await SummariseAsync();

        ShowInfo($"Journal {result.Value.BatchNumber} prepared. Read it, "
               + "then post it.");
    });

    private Task PostAsync() => GuardAsync(async () =>
    {
        if (_posting is null || Selected() is not JournalBatchDto batch) return;

        if (batch.IsPosted)
        {
            ShowInfo($"Journal {batch.BatchNumber} is already posted.");
            return;
        }

        if (!Confirm($"Post journal {batch.BatchNumber} for "
                   + $"{batch.PostingDate:dd/MM/yyyy}? "
                   + "A posted journal cannot be changed."))
            return;

        var result = await _posting.PostAsync(batch.Id);

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        await LoadBatchesAsync();
        Select(result.Value.Id);

        ShowInfo($"Journal {result.Value.BatchNumber} posted.");
    });

    /// <summary>
    /// Writes the journal out in the shape every accounting package will
    /// take: one row per line, date, account, debit, credit.
    /// </summary>
    private void Export()
    {
        if (Selected() is not JournalBatchDto batch) return;

        using var save = new SaveFileDialog
        {
            FileName = $"{batch.BatchNumber}.csv",
            Filter = "CSV file (*.csv)|*.csv",
            Title = "Export journal"
        };

        if (save.ShowDialog(this) != DialogResult.OK) return;

        var csv = new StringBuilder();
        csv.AppendLine("Date,Batch,Account,AccountName,Debit,Credit,Narration");

        foreach (var line in batch.Lines)
        {
            csv.AppendLine(string.Join(",",
                batch.PostingDate.ToString("yyyy-MM-dd",
                    CultureInfo.InvariantCulture),
                Quote(batch.BatchNumber),
                Quote(line.AccountCode),
                Quote(line.AccountName),
                line.Debit.ToString("F2", CultureInfo.InvariantCulture),
                line.Credit.ToString("F2", CultureInfo.InvariantCulture),
                Quote(line.Narration ?? "")));
        }

        File.WriteAllText(save.FileName, csv.ToString(), Encoding.UTF8);

        ShowInfo($"Exported to {save.FileName}");
    }

    /// A field containing a comma or a quote would otherwise split a row.
    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";

    private async Task LoadBatchesAsync()
    {
        if (_posting is null) return;

        var result = await _posting.RecentAsync();

        if (result.IsFailure)
        {
            ShowError(result.Error.Message);
            return;
        }

        _batches = result.Value;
        grdBatches.DataSource = _batches.ToList();
        ShowSelectedLines();
    }

    private void Select(Guid id)
    {
        foreach (DataGridViewRow row in grdBatches.Rows)
        {
            if (row.DataBoundItem is not JournalBatchDto b || b.Id != id)
                continue;

            row.Selected = true;
            grdBatches.CurrentCell = row.Cells["BatchNumber"];
            return;
        }
    }

    private JournalBatchDto? Selected() =>
        grdBatches.CurrentRow?.DataBoundItem as JournalBatchDto;

    private void ShowSelectedLines()
    {
        if (Selected() is not JournalBatchDto batch)
        {
            grdLines.DataSource = null;
            lblBalance.Text = "";
            btnPost.Enabled = false;
            btnExport.Enabled = false;
            return;
        }

        grdLines.DataSource = batch.Lines.ToList();

        lblBalance.Text = batch.IsBalanced
            ? $"Debits {Format.Amount(batch.TotalDebits)} = credits "
              + $"{Format.Amount(batch.TotalCredits)}."
              + (batch.IsPosted ? "  Posted." : "  Not yet posted.")
            : $"Out of balance: debits {Format.Amount(batch.TotalDebits)}, "
              + $"credits {Format.Amount(batch.TotalCredits)}.";

        btnPost.Enabled = !batch.IsPosted && batch.IsBalanced;
        btnExport.Enabled = true;
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
