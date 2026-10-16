using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

/// <param name="UnitPrice">The price of the pack.</param>
/// <param name="UnitsInPack">
/// How many of the quantity the pack holds: one at the till, the pack's
/// tablets or millilitres at the dispensary.
/// </param>
/// <param name="ProductCode">
/// The product's code today, which a quote taken up as a script is
/// captured by. Filled in where one quote is read, not for a list.
/// </param>
public sealed record QuotationLineDto(int LineNo, Guid ProductId,
    string Description, decimal Quantity, decimal UnitPrice, string TaxCode,
    decimal LineTotal, decimal UnitsInPack = 1m, string? ProductCode = null,
    decimal? OnHand = null);

/// <param name="Kind">"OverTheCounter" or "Dispensary".</param>
/// <param name="ScriptId">The script a dispensary quote became.</param>
public sealed record QuotationDto(Guid Id, string QuotationNumber,
    string CustomerName, DateOnly QuotedOn, DateOnly ValidUntil,
    string Status, string? Notes, IReadOnlyList<QuotationLineDto> Lines,
    decimal Total, string Kind = "OverTheCounter", Guid? ScriptId = null,
    Guid? PatientId = null)
{
    public static QuotationDto From(Quotation q)
    {
        ArgumentNullException.ThrowIfNull(q);

        return new QuotationDto(q.Id, q.QuotationNumber, q.CustomerName,
            q.QuotedOn, q.ValidUntil, q.Status.ToString(), q.Notes,
            q.Lines.OrderBy(l => l.LineNo).Select(l => new QuotationLineDto(
                l.LineNo, l.ProductId, l.Description, l.Quantity,
                l.UnitPrice.Amount, l.TaxCode, l.LineTotal.Amount,
                l.UnitsInPack)).ToList(),
            q.Total.Amount, q.Kind.ToString(), q.ScriptId, q.PatientId);
    }

    /// Whether it can still be changed: lines added or taken off.
    public bool IsOpen => Status == nameof(QuotationStatus.Open);

    /// Whether it can still be taken up as a script.
    public bool CanBecomeScript =>
        Kind == nameof(QuotationKind.Dispensary) && ScriptId is null
        && Status is nameof(QuotationStatus.Open)
                  or nameof(QuotationStatus.Issued);
}

public interface IQuotationRepository
{
    Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default);

    /// <param name="kind">One kind only, or every kind where null.</param>
    Task<IReadOnlyList<Quotation>> RecentAsync(int take = int.MaxValue,
        QuotationKind? kind = null, CancellationToken ct = default);

    /// The number printed on the quotation the customer brings back.
    Task<Quotation?> ByNumberAsync(string quotationNumber,
                                   CancellationToken ct = default);

    Task AddAsync(Quotation quotation, CancellationToken ct = default);
}

public interface IQuotationService
{
    /// <param name="customerName">
    /// Who it is for, where they have said: blank is allowed, so a price
    /// can be given before anybody's name is.
    /// </param>
    /// <param name="kind">
    /// A dispensary quote counts in tablets and becomes a script; the
    /// till's counts in packs and becomes a sale.
    /// </param>
    Task<Result<QuotationDto>> OpenAsync(string? customerName,
        int validForDays = 30,
        QuotationKind kind = QuotationKind.OverTheCounter,
        CancellationToken ct = default);

    Task<Result<QuotationDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<QuotationDto>>> RecentAsync(
        QuotationKind? kind = null, CancellationToken ct = default);

    Task<Result<QuotationDto>> AddLineAsync(Guid quotationId, string code,
        decimal quantity, CancellationToken ct = default);

    Task<Result<QuotationDto>> RemoveLineAsync(Guid quotationId, int lineNo,
        CancellationToken ct = default);

    /// <param name="customerName">
    /// Who it is for, where the customer has now said. Null leaves the
    /// name as it was.
    /// </param>
    Task<Result<QuotationDto>> IssueAsync(Guid quotationId, string? notes,
        string? customerName = null, CancellationToken ct = default);

    /// <summary>
    /// A product as a quote of this kind prices it, before the line goes
    /// on: what the counter asks the quantity of.
    /// </summary>
    Task<Result<QuotableProductDto>> FindProductAsync(string code,
        QuotationKind kind = QuotationKind.OverTheCounter,
        CancellationToken ct = default);

    /// The quotation the customer has brought back, by the number on it.
    Task<Result<QuotationDto>> FindAsync(string quotationNumber,
        CancellationToken ct = default);
}

/// <summary>
/// A quotation as it is printed: what was quoted, for how much, and until
/// when.
/// </summary>
/// <param name="PackPrice">The shelf price of one pack today.</param>
/// <param name="UnitsInPack">
/// What a quantity of one pack is: one at the till, the pack's tablets or
/// millilitres at the dispensary.
/// </param>
public sealed record QuotableProductDto(string Code, string Description,
    decimal PackPrice, decimal UnitsInPack, decimal OnHand, int Schedule);

public static class QuotationDocument
{
    public static PrintableDocument For(QuotationDto quotation)
    {
        ArgumentNullException.ThrowIfNull(quotation);

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var lines = new List<string>
        {
            $"Quotation {quotation.QuotationNumber}"
        };

        // A price given before the customer gave a name is still a quote.
        if (!string.IsNullOrWhiteSpace(quotation.CustomerName))
            lines.Add($"For {quotation.CustomerName}");

        lines.Add(string.Format(culture, "Quoted {0:dd/MM/yyyy}  valid to {1:dd/MM/yyyy}",
            quotation.QuotedOn, quotation.ValidUntil));
        lines.Add(new string('-', 42));

        foreach (var line in quotation.Lines)
        {
            lines.AddRange(SlipLayout.Described(
                string.Format(culture, "{0,5:0.##} ", line.Quantity),
                line.Description));

            // A dispensary line counts tablets out of a priced pack, and
            // says so: "21 x 120.00 = 42.00" reads as a mistake.
            var price = line.UnitsInPack == 1m
                ? line.UnitPrice.ToString("N2", culture)
                : string.Format(culture, "pack of {0:0.###} @ {1:N2}",
                    line.UnitsInPack, line.UnitPrice);

            lines.Add(string.Format(culture, "{0,26} {1,15:N2}",
                price, line.LineTotal));
        }

        lines.Add(new string('-', 42));
        lines.Add(string.Format(culture, "{0,-26}{1,16:N2}", "TOTAL (VAT incl.)",
            quotation.Total));
        lines.Add("");

        if (!string.IsNullOrWhiteSpace(quotation.Notes))
        {
            lines.Add(quotation.Notes);
            lines.Add("");
        }

        // Within the 42 characters of the roll.
        lines.Add("A quotation, not a tax invoice. Prices");
        lines.Add("are held until the date above; stock is");
        lines.Add("not reserved.");

        return new PrintableDocument($"Quotation {quotation.QuotationNumber}",
                                     lines);
    }
}

/// <summary>
/// Quotations: a priced list for a customer who has not bought anything.
/// </summary>
/// <remarks>
/// No stock moves and no receipt number is consumed. A quotation that
/// quietly took a document number would leave a gap in the sequence an
/// auditor reads, so quotations carry their own.
/// </remarks>
public sealed class QuotationService : IQuotationService
{
    private readonly IQuotationRepository _quotations;
    private readonly IProductRepository _products;
    private readonly IDocumentNumbering _numbering;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly ICurrentUser _user;
    private readonly IClock _clock;

    public QuotationService(IQuotationRepository quotations,
        IProductRepository products, IDocumentNumbering numbering,
        IPermissionService permissions, IAuditWriter audit, IUnitOfWork uow,
        ICurrentUser user, IClock clock)
    {
        _quotations = quotations;
        _products = products;
        _numbering = numbering;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
        _user = user;
        _clock = clock;
    }

    public async Task<Result<QuotationDto>> OpenAsync(string? customerName,
        int validForDays = 30,
        QuotationKind kind = QuotationKind.OverTheCounter,
        CancellationToken ct = default)
    {
        DemandQuoting(kind);

        // No name demanded: the customer asking the price has not given
        // one, and a quote is how the counter answers them.
        var number = await _numbering.NextAsync(DocumentSeries.Quotation, ct);

        var quotation = Quotation.Open(number, customerName, _user.Id,
            Today, validForDays, kind: kind);

        await _quotations.AddAsync(quotation, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationDto.From(quotation));
    }

    public async Task<Result<QuotationDto>> FindAsync(string quotationNumber,
        CancellationToken ct = default)
    {
        DemandQuoting();

        var quotation = await _quotations.ByNumberAsync(quotationNumber, ct);

        return quotation is null
            ? Result<QuotationDto>.Fail("QUO-022",
                $"No quotation {quotationNumber}.")
            : Result<QuotationDto>.Ok(await DescribeAsync(quotation, ct));
    }

    public async Task<Result<QuotationDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        DemandQuoting();

        var quotation = await _quotations.GetAsync(id, ct);

        return quotation is null
            ? Result<QuotationDto>.Fail("QUO-000", "Quotation not found.")
            : Result<QuotationDto>.Ok(await DescribeAsync(quotation, ct));
    }

    public async Task<Result<IReadOnlyList<QuotationDto>>> RecentAsync(
        QuotationKind? kind = null, CancellationToken ct = default)
    {
        DemandQuoting(kind);

        var rows = await _quotations.RecentAsync(kind: kind, ct: ct);

        // Lapsed the moment it is read rather than by a nightly job: a
        // workstation that was switched off has no nightly job.
        foreach (var q in rows)
        {
            if (q.Status == QuotationStatus.Issued && q.HasExpiredOn(Today))
                q.MarkExpired();
        }

        await _uow.SaveChangesAsync(ct);

        IReadOnlyList<QuotationDto> dtos =
            rows.Select(QuotationDto.From).ToList();

        return Result<IReadOnlyList<QuotationDto>>.Ok(dtos);
    }

    public async Task<Result<QuotationDto>> AddLineAsync(Guid quotationId,
        string code, decimal quantity, CancellationToken ct = default)
    {
        var quotation = await _quotations.GetAsync(quotationId, ct);
        if (quotation is null)
            return Result<QuotationDto>.Fail("QUO-000", "Quotation not found.");

        DemandQuoting(quotation.Kind);

        var product = await _products.FindByCodeOrBarcodeAsync(code,
            _permissions.ProductScope(), ct);
        if (product is null)
            return Result<QuotationDto>.Fail("POS-001",
                $"No product for code {code}.");

        // Quoted at the shelf price of the day. A quotation makes no
        // promise about stock, so an item out of stock is still
        // quotable - the pharmacy is quoting a price, not reserving one.
        // The dispensary counts tablets, and quotes the pack's share of
        // the price the way a dispensing charges it (DEF-0021).
        var unitsInPack = quotation.Kind == QuotationKind.Dispensary
            ? product.UnitsInPack
            : 1m;

        quotation.AddLine(product.Id, product.Description, quantity,
                          product.RetailPrice, product.TaxCode, unitsInPack);

        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(await DescribeAsync(quotation, ct));
    }

    public async Task<Result<QuotationDto>> RemoveLineAsync(Guid quotationId,
        int lineNo, CancellationToken ct = default)
    {
        var quotation = await _quotations.GetAsync(quotationId, ct);
        if (quotation is null)
            return Result<QuotationDto>.Fail("QUO-000", "Quotation not found.");

        DemandQuoting(quotation.Kind);

        quotation.RemoveLine(lineNo);
        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(await DescribeAsync(quotation, ct));
    }

    public async Task<Result<QuotationDto>> IssueAsync(Guid quotationId,
        string? notes, string? customerName = null,
        CancellationToken ct = default)
    {
        var quotation = await _quotations.GetAsync(quotationId, ct);
        if (quotation is null)
            return Result<QuotationDto>.Fail("QUO-000", "Quotation not found.");

        DemandQuoting(quotation.Kind);

        if (customerName is not null) quotation.AddressTo(customerName);

        quotation.Note(notes);
        quotation.Issue();

        _audit.Applied("pos.quotation.issue", "Quotation", quotation.Id,
            $"{quotation.QuotationNumber} to {quotation.CustomerName}, "
            + $"{quotation.Total}, valid to "
            + $"{quotation.ValidUntil:dd/MM/yyyy}");

        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(await DescribeAsync(quotation, ct));
    }

    public async Task<Result<QuotableProductDto>> FindProductAsync(
        string code, QuotationKind kind = QuotationKind.OverTheCounter,
        CancellationToken ct = default)
    {
        DemandQuoting(kind);

        var product = await _products.FindByCodeOrBarcodeAsync(code,
            _permissions.ProductScope(), ct);

        if (product is null)
            return Result<QuotableProductDto>.Fail("POS-001",
                $"No product for code {code}.");

        return Result<QuotableProductDto>.Ok(new QuotableProductDto(
            product.Code, product.Description, product.RetailPrice.Amount,
            kind == QuotationKind.Dispensary ? product.UnitsInPack : 1m,
            product.QuantityOnHand, product.Schedule));
    }

    /// <summary>
    /// Whoever sells or dispenses may quote: the till for what it sells,
    /// the dispensary for what it dispenses.
    /// </summary>
    private void DemandQuoting(QuotationKind? kind = null)
    {
        if (kind == QuotationKind.Dispensary
            ? _permissions.HasAny(PermissionCode.SaleCreate,
                                  PermissionCode.ScriptCapture,
                                  PermissionCode.ScriptDispense)
            : _permissions.HasAny(PermissionCode.SaleCreate,
                                  PermissionCode.ScriptCapture))
            return;

        _permissions.Demand(PermissionCode.SaleCreate);
    }

    /// <summary>
    /// One quote, with each line's product code and stock on hand today:
    /// what the counter tells the customer, and what a quote taken up as
    /// a script is captured by.
    /// </summary>
    private async Task<QuotationDto> DescribeAsync(Quotation quotation,
        CancellationToken ct)
    {
        var dto = QuotationDto.From(quotation);
        var lines = new List<QuotationLineDto>(dto.Lines.Count);

        foreach (var line in dto.Lines)
        {
            var product = await _products.GetAsync(line.ProductId, ct);

            lines.Add(line with
            {
                ProductCode = product?.Code,
                OnHand = product?.QuantityOnHand
            });
        }

        return dto with { Lines = lines };
    }

    private DateOnly Today =>
        DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime());
}
