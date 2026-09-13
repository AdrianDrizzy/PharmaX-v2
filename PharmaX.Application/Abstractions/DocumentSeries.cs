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
}
