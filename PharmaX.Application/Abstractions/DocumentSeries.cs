namespace PharmaX.Application.Abstractions;

/// <summary>
/// Every series of document numbers PharmaX allocates.
/// </summary>
/// <remarks>
/// One list, because a series is useless to a workstation until the
/// seeder has given it a block, and the two lists had already drifted
/// three times. A journal series was added without a block and the first
/// sale of the day failed; a claims series and an accounts series went
/// the same way; and quotations had been asking for a QUO block that
/// nobody had ever seeded, which means that feature could never have
/// worked at a site.
///
/// The failure is always the same and always misleading: the transaction
/// that needed the number dies with "no document numbers left", naming a
/// series the operator has never heard of, at the moment they were doing
/// something else entirely.
///
/// So the constants and the seeder now read from the same array, and a
/// test fails if a series is used that is not in it.
/// </remarks>
public static class DocumentSeries
{
    public const string Sale = "SALE";
    public const string Dispensing = "DISP";
    public const string Return = "RET";
    public const string Patient = "PAT";
    public const string Script = "SCR";
    public const string GoodsReceipt = "GRN";
    public const string PurchaseOrder = "PO";
    public const string Stocktake = "STK";
    public const string Quotation = "QUO";
    public const string Journal = "JNL";
    public const string Claim = "CLM";
    public const string Account = "ACC";

    /// <summary>
    /// What the seeder gives a starting block to on every start-up.
    /// </summary>
    /// <remarks>
    /// Adding a series means adding it here, and the seeder picks it up
    /// on the next start of every workstation - including ones already
    /// installed, because seeding adds only what is missing.
    /// </remarks>
    public static IReadOnlyList<string> All { get; } = new[]
    {
        Sale, Dispensing, Return, Patient, Script, GoodsReceipt,
        PurchaseOrder, Stocktake, Quotation, Journal, Claim, Account
    };

    /// <summary>
    /// What a number in each series starts with: one prefix per series,
    /// and no two alike.
    /// </summary>
    /// <remarks>
    /// Numbers were prefixed with the series code's first letter, so a
    /// sale, a script and a stocktake all printed as S-001-000003 and a
    /// patient file and a purchase order both as P-001-000002 (DEF-0010).
    /// A number on a label, a receipt or a phone call has to say what it
    /// is. Sales, dispensings, returns and patient files keep the letter
    /// they have always had, so every receipt and file card already in a
    /// drawer still reads the same; the series that collided with them
    /// take a second letter.
    /// </remarks>
    public static string PrefixOf(string series) => series switch
    {
        Sale => "S",
        Dispensing => "D",
        Return => "R",
        Patient => "P",
        Script => "SC",
        GoodsReceipt => "G",
        PurchaseOrder => "PO",
        Stocktake => "ST",
        Quotation => "Q",
        Journal => "J",
        Claim => "C",
        Account => "A",
        _ => throw new ArgumentOutOfRangeException(nameof(series), series,
            "Every series needs its own prefix: add it to PrefixOf.")
    };
}
