namespace PharmaX.Domain.Legacy;

/// <summary>
/// One row of the history PharmaX took over from the system before it -
/// a prescription line, a till sale, a stock movement, a price change.
/// </summary>
/// <remarks>
/// Kept apart from PharmaX's own sales and dispensings on purpose. Loaded
/// as those, a year of history would have moved stock that had already
/// been counted, posted journals into books that start at go-live and
/// waited in the collection queue to be paid for again. Here it is read
/// and searched, and nothing else: append-only, like the audit log.
/// The original row is kept whole in Data, so nothing in the export is
/// lost even where no column below holds it.
/// </remarks>
public sealed class LegacyRecord
{
    public Guid Id { get; init; } = Guid.NewGuid();

    /// Where it came from: "RSI".
    public string Source { get; private set; } = "";

    /// Script, Sale, Receipt, Stock, Price, Override, Count, DayEnd, ZRead.
    public string Kind { get; private set; } = "";

    /// The old system's document number: script, receipt, GRN, Z-read.
    public string? Number { get; private set; }

    public DateTime OccurredAt { get; private set; }

    public string? ProductCode { get; private set; }

    /// The patient's file number in PharmaX, where it concerns a patient.
    public string? PatientFile { get; private set; }

    /// Who else it concerns: the patient, the supplier, the tender.
    public string? Party { get; private set; }

    public string Description { get; private set; } = "";

    public decimal? Quantity { get; private set; }

    public decimal? Amount { get; private set; }

    public string? Operator { get; private set; }

    /// The whole original row, as JSON.
    public string Data { get; private set; } = "";

    private LegacyRecord() { }

    public static LegacyRecord Create(string source, string kind,
        string? number, DateTime occurredAt, string description,
        string data, string? productCode = null, string? patientFile = null,
        string? party = null, decimal? quantity = null, decimal? amount = null,
        string? @operator = null) => new()
    {
        Source = source,
        Kind = kind,
        Number = Trim(number, 40),
        OccurredAt = occurredAt,
        Description = Trim(description, 300) ?? "",
        Data = data,
        ProductCode = Trim(productCode, 30),
        PatientFile = Trim(patientFile, 30),
        Party = Trim(party, 150),
        Quantity = quantity,
        Amount = amount,
        Operator = Trim(@operator, 60)
    };

    private static string? Trim(string? value, int length)
    {
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        return value is { Length: > 0 } && value.Length > length
            ? value[..length] : value;
    }
}
