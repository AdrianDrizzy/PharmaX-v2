using System.Text.Json;

namespace PharmaX.Application.Sales;

/// <summary>
/// Which parts of the till receipt are printed, set by an administrator
/// on the Receipt Template screen and kept pharmacy-wide, so every till
/// prints the same slip.
/// </summary>
/// <remarks>
/// Everything is on by default except the reprint mark. The receipt
/// number is not offered: without it a slip cannot be found again to
/// reprint or refund.
/// </remarks>
public sealed record ReceiptTemplate
{
    /// The pharmacy-wide setting it is kept under, as JSON.
    public const string SettingKey = "receipt.template";

    public static ReceiptTemplate Default { get; } = new();

    // The heading
    public bool TradingName { get; init; } = true;
    public bool Address { get; init; } = true;
    public bool VatNumber { get; init; } = true;
    public bool Telephone { get; init; } = true;
    public bool WhatsApp { get; init; } = true;
    public bool DocumentTitle { get; init; } = true;   // "Tax Invoice", "Cash Sale"

    // Whose sale
    public bool Operator { get; init; } = true;
    public bool TillNumber { get; init; } = true;
    public bool Date { get; init; } = true;
    public bool AccountAndPatient { get; init; } = true;

    // The items
    public bool ColumnHeadings { get; init; } = true;
    public bool ProductCodes { get; init; } = true;
    public bool ProductNames { get; init; } = true;
    public bool QuantityAndUnitPrice { get; init; } = true;
    public bool LineAmounts { get; init; } = true;

    // The money
    public bool SubTotal { get; init; } = true;
    public bool Vat { get; init; } = true;
    public bool Total { get; init; } = true;
    public bool Tenders { get; init; } = true;
    public bool Change { get; init; } = true;
    public bool MedicalAidShare { get; init; } = true;
    public bool AccountSignature { get; init; } = true;

    // The end
    public bool Footer { get; init; } = true;

    /// <summary>
    /// "*** COPY - REPRINTED ***" at the head and foot of a reprint. Off
    /// unless an administrator turns it on: the pharmacy asked for copies
    /// to look like the original. Every reprint is still recorded in the
    /// audit trail, with who printed it.
    /// </summary>
    public bool CopyMark { get; init; }
    public string FooterText { get; init; } = "Thank you";

    public string ToJson() => JsonSerializer.Serialize(this);

    /// <summary>
    /// The template saved as JSON, or the default where nothing has been
    /// saved or what was saved cannot be read - a slip must still print.
    /// </summary>
    public static ReceiptTemplate Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return Default;

        try
        {
            return JsonSerializer.Deserialize<ReceiptTemplate>(json) ?? Default;
        }
        catch (JsonException)
        {
            return Default;
        }
    }
}
