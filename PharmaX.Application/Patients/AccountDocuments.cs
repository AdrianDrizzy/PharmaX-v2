using System.Globalization;
using PharmaX.Application.Reporting;
using PharmaX.Application.Sales;

namespace PharmaX.Application.Patients;

/// <summary>
/// What a patient account puts on paper: the statement, and the receipt
/// for money paid off it.
/// </summary>
/// <remarks>
/// A statement could be read on screen and nowhere else, and a payment
/// off an account printed nothing at all, so the customer who settled in
/// cash walked out with no proof of it (ADM-OBS-17). Both go through the
/// document printer, under the pharmacy's own header.
/// </remarks>
public static class AccountDocuments
{
    public static PrintableDocument Statement(StatementDto statement)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var culture = CultureInfo.CurrentCulture;
        var account = statement.Account;

        var lines = new List<string>
        {
            $"Account {account.AccountNumber}",
            account.Name,
            string.Format(culture, "{0:dd/MM/yyyy} to {1:dd/MM/yyyy}",
                statement.From, statement.Until),
            new string('-', SlipLayout.Width),
            string.Format(culture, "{0,-10} {1,-9} {2,10} {3,10}",
                "Date", "What", "Amount", "Balance"),
            string.Format(culture, "{0,-20} {1,21:N2}", "Brought forward",
                statement.Opening)
        };

        foreach (var line in statement.Lines)
        {
            var amount = line.Charged != 0m ? line.Charged : -line.Paid;

            lines.Add(string.Format(culture, "{0:dd/MM/yyyy} {1,-9} {2,10:N2} {3,10:N2}",
                line.OccurredOn, Trim(line.KindText, 9), amount, line.Balance));

            if (!string.IsNullOrWhiteSpace(line.Reference))
                lines.Add("           " + Trim(line.Reference, 31));
        }

        lines.Add(new string('-', SlipLayout.Width));
        lines.Add(string.Format(culture, "{0,-20} {1,21:N2}", "Now due",
            statement.Closing));

        if (statement.Lines.Count == 0)
            lines.Add("Nothing moved in this period.");

        return new PrintableDocument($"Statement {account.AccountNumber}",
                                     lines);
    }

    /// <summary>
    /// The same statement as a workbook, for sending on.
    /// </summary>
    public static ReportResult StatementWorkbook(StatementDto statement,
        string runBy, DateTime nowLocal)
    {
        ArgumentNullException.ThrowIfNull(statement);

        var columns = new[]
        {
            new ReportColumn("Date", "Date", "dd/MM/yyyy"),
            new ReportColumn("What", "What"),
            new ReportColumn("Reference", "Reference"),
            new ReportColumn("Charged", "Charged", "#,##0.00", true),
            new ReportColumn("Paid", "Paid", "#,##0.00", true),
            new ReportColumn("Balance", "Balance", "#,##0.00", true)
        };

        var rows = new List<IReadOnlyDictionary<string, object?>>
        {
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["What"] = "Brought forward",
                ["Balance"] = statement.Opening
            }
        };

        rows.AddRange(statement.Lines.Select(l =>
            (IReadOnlyDictionary<string, object?>)
            new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["Date"] = l.OccurredOn.ToDateTime(TimeOnly.MinValue),
                ["What"] = l.KindText,
                ["Reference"] = l.Reference,
                ["Charged"] = l.Charged == 0m ? null : l.Charged,
                ["Paid"] = l.Paid == 0m ? null : l.Paid,
                ["Balance"] = l.Balance
            }));

        rows.Add(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["What"] = "Now due",
            ["Balance"] = statement.Closing
        });

        return new ReportResult(
            $"Statement {statement.Account.AccountNumber} "
            + statement.Account.Name,
            columns, rows,
            new Dictionary<string, object?>(StringComparer.Ordinal),
            new ReportProvenance(runBy, nowLocal,
                string.Format(CultureInfo.CurrentCulture,
                    "{0:dd/MM/yyyy} to {1:dd/MM/yyyy}",
                    statement.From, statement.Until),
                "As at " + nowLocal.ToString("dd/MM/yyyy HH:mm",
                    CultureInfo.CurrentCulture)));
    }

    /// <summary>
    /// The customer's receipt for a payment off their account.
    /// </summary>
    public static PrintableDocument PaymentReceipt(AccountDto account,
        decimal amount, string tender, string? reference, DateOnly receivedOn)
    {
        ArgumentNullException.ThrowIfNull(account);

        var culture = CultureInfo.CurrentCulture;

        var lines = new List<string>
        {
            "Payment on account",
            $"Account {account.AccountNumber}",
            account.Name,
            receivedOn.ToString("dd/MM/yyyy", culture),
            new string('-', SlipLayout.Width),
            string.Format(culture, "{0,-30}{1,12:N2}", $"Paid by {tender}",
                amount)
        };

        if (!string.IsNullOrWhiteSpace(reference))
            lines.Add($"Reference {Trim(reference, 32)}");

        lines.Add(new string('-', SlipLayout.Width));
        lines.Add(string.Format(culture, "{0,-30}{1,12:N2}",
            account.Balance < 0m ? "In credit" : "Still owing",
            Math.Abs(account.Balance)));

        return new PrintableDocument("Payment on account", lines);
    }

    private static string Trim(string? text, int width)
    {
        var value = text ?? "";
        return value.Length <= width ? value : value[..width];
    }
}
