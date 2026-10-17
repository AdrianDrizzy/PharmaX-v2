using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Patients;

namespace PharmaX.Application.Dispensing.Dtos;

public sealed record PatientSummaryDto(
    Guid Id, string FileNumber, string FullName, int? Age,
    string? Telephone, string? SchemeName, string? MembershipNumber,
    IReadOnlyList<string> Allergies, IReadOnlyList<string> ChronicConditions)
{
    public static PatientSummaryDto From(Patient p, ClinicalProfile? profile,
                                         DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(p);

        return new PatientSummaryDto(
            p.Id, p.FileNumber, p.FullName, p.AgeOn(today), p.Telephone,
            null, null,
            profile?.Allergies.Select(a => a.Substance).ToList()
                ?? new List<string>(),
            profile?.ChronicConditions.ToList() ?? new List<string>());
    }
}

/// <param name="Items">
/// What the script is for, one medicine and its quantity a line, so a
/// script can be told apart from the next - and chosen to use again -
/// without opening it.
/// </param>
public sealed record ScriptDto(
    Guid Id, string ScriptNumber, Guid PatientId, string PrescriberName,
    DateOnly PrescribedOn, DateOnly? ValidUntil, int RepeatsAuthorised,
    int RepeatsRemaining, string Status, string Items = "")
{
    public static ScriptDto From(Script s)
    {
        ArgumentNullException.ThrowIfNull(s);

        return new ScriptDto(
            s.Id, s.ScriptNumber, s.PatientId, s.PrescriberName,
            s.PrescribedOn, s.ValidUntil, s.RepeatsAuthorised,

            // Never below nothing on the screen: a script with no repeats,
            // once dispensed, has none left rather than minus one.
            Math.Max(0, s.RepeatsRemaining), s.Status.ToString(),
            ItemList(s.Items.OrderBy(i => i.LineNo)
                            .Select(i => (i.Description, i.Quantity))));
    }

    /// <summary>
    /// Medicines as a list for a screen: "IBUPROFEN 200MG  x40", one a
    /// line.
    /// </summary>
    public static string ItemList(
        IEnumerable<(string Description, decimal Quantity)> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        return string.Join(Environment.NewLine, items.Select(i =>
            string.Create(System.Globalization.CultureInfo.CurrentCulture,
                $"{i.Description}  x{i.Quantity:0.###}")));
    }
}

/// <param name="BatchNumber">
/// Every batch the line draws on, as the label prints them - "AMOX50-A
/// x4, AMOX50-B x17" - or the one batch where there is only one.
/// </param>
/// <param name="Expiry">The earliest expiry among those batches.</param>
public sealed record DispensingLineDto(
    int LineNo, Guid ProductId, string Description, decimal Quantity,
    string Directions, string? BatchNumber, DateOnly? Expiry,
    decimal Tariff, decimal Contribution, decimal CoPayment,
    bool IsSubstitution, decimal? Owed = null)
{
    public static DispensingLineDto From(DispensingLine l)
    {
        ArgumentNullException.ThrowIfNull(l);

        return new DispensingLineDto(
            l.LineNo, l.ProductId, l.Description, l.Quantity, l.Directions,
            l.BatchSummary, l.BatchExpiry, l.TariffAmount.Amount,
            l.SchemeContribution.Amount, l.CoPayment.Amount,
            l.IsSubstitution, l.Owed);
    }
}

/// <summary>
/// What the script says to hand over for a scanned product: the quantity
/// and directions the prescriber wrote, to pre-fill the prompts with.
/// </summary>
/// <param name="FromScript">
/// False where the product is not on the script - a substitution, or a
/// mistake - and the quantity and directions are only a starting point.
/// </param>
public sealed record ItemProposalDto(Guid ProductId, string ProductCode,
    string Description, decimal Quantity, string Directions,
    bool FromScript);

/// <summary>
/// Whether the person at the dispensing screen may issue it on their own
/// authority, and if not, why a pharmacist has to authorise it.
/// </summary>
public sealed record IssueAuthorityDto(bool OperatorMayIssue, string? Reason);

/// <summary>
/// What adding the script's items managed, item by item: the ones that
/// went on and why any did not.
/// </summary>
public sealed record ScriptItemsAddedDto(DispensingStateDto State,
    int Added, IReadOnlyList<string> Problems);

public sealed record ClinicalWarningDto(
    Guid Id, string Kind, string Severity, string Message, bool IsHardStop,
    bool IsAcknowledged)
{
    public static ClinicalWarningDto From(ClinicalWarning w)
    {
        ArgumentNullException.ThrowIfNull(w);

        return new ClinicalWarningDto(
            w.Id, w.Kind, w.Severity.ToString(), w.Message, w.IsHardStop,
            w.IsAcknowledged);
    }
}

/// <summary>
/// What the screen collected for one item. Directions are mandatory,
/// which is why they are a required member rather than a nullable string
/// the caller may forget.
/// </summary>
public sealed record DirectionsDto(decimal Quantity, string Directions,
                                   bool AllowSubstitution = true);

/// <summary>
/// The whole state of the dispensing after a change. One object rather
/// than four calls, because the screen redraws all of it at once anyway.
/// </summary>
public sealed record DispensingStateDto(
    Guid Id, IReadOnlyList<DispensingLineDto> Items,
    IReadOnlyList<ClinicalWarningDto> Warnings,
    decimal TariffTotal, decimal ContributionTotal, decimal CoPaymentTotal)
{
    public static DispensingStateDto From(Domain.Dispensing.Dispensing d)
    {
        ArgumentNullException.ThrowIfNull(d);

        return new DispensingStateDto(
            d.Id,
            d.Lines.OrderBy(l => l.LineNo).Select(DispensingLineDto.From).ToList(),
            d.Warnings.Select(ClinicalWarningDto.From).ToList(),
            d.TariffTotal.Amount, d.ContributionTotal.Amount,
            d.CoPaymentTotal.Amount);
    }
}

/// <summary>
/// An item corrected, and what that did to what the patient pays.
/// </summary>
/// <param name="WasCollected">
/// The co-payment was taken at the till before the correction, so the
/// difference is settled there: given back through returns, or taken as a
/// sale. One not yet collected is simply collected at the new amount.
/// </param>
public sealed record ItemAmendedDto(DispensingStateDto State, bool WasIssued,
    decimal CoPaymentBefore, decimal CoPaymentAfter, bool WasCollected)
{
    /// Still to settle at the till: more to take, or (negative) to give back.
    public decimal ToSettle =>
        WasCollected ? CoPaymentAfter - CoPaymentBefore : 0m;
}

/// <summary>
/// One label to print. A plain record so that the printer adapter needs
/// no reference to the domain.
/// </summary>
/// <param name="ItemNumber">
/// Which item of the script this label is for, and <paramref
/// name="ItemCount"/> how many the script has: a patient holding three
/// containers can see none is missing, and a query at the counter names
/// the item rather than describing it.
/// </param>
/// <param name="PrescriberName">
/// Who wrote the script, printed as "Doc." on the label. A container
/// out of its bag said nothing of who prescribed what is in it.
/// </param>
/// <param name="PrescriberNumber">
/// The prescriber's own practice number, printed as "Prof#". What a
/// medical aid asks for first when a claim is queried.
/// </param>
public sealed record LabelDto(
    string PatientName, string ProductDescription, decimal Quantity,
    string Directions, string? BatchNumber, DateOnly? Expiry,
    string PharmacistName, string ScriptNumber, DateOnly DispensedOn,
    IReadOnlyList<string> Cautions, int ItemNumber = 1, int ItemCount = 1,
    string? PrescriberName = null, string? PrescriberNumber = null);

/// <param name="ClaimNumber">
/// The medical aid claim the issue raised, if the scheme pays any of it,
/// so the dispensary can hand the member the claim form to sign.
/// </param>
/// <param name="Trailer">
/// The trailer label that follows the item labels: the whole script on
/// one label.
/// </param>
public sealed record IssueResultDto(
    string ScriptNumber, string DocumentNumber, decimal CoPayment,
    IReadOnlyList<LabelDto> Labels, string? ClaimNumber = null,
    TrailerLabelDto? Trailer = null);

/// Medicine owed to a patient because the shelf was short when it was issued.
public sealed record ToFollowDto(Guid DispensingId, int LineNo, string Patient,
    string Telephone, string Description, decimal Owed, DateTime Since,
    string DocumentNumber);

/// A script begun and kept to finish later.
public sealed record SuspendedDispensingDto(Guid PatientId, Guid ScriptId,
    string Patient, string ScriptNumber, int Items);
