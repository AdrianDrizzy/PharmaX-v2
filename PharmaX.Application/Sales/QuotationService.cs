using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

public sealed record QuotationLineDto(int LineNo, Guid ProductId,
    string Description, decimal Quantity, decimal UnitPrice, string TaxCode,
    decimal LineTotal);

public sealed record QuotationDto(Guid Id, string QuotationNumber,
    string CustomerName, DateOnly QuotedOn, DateOnly ValidUntil,
    string Status, string? Notes, IReadOnlyList<QuotationLineDto> Lines,
    decimal Total)
{
    public static QuotationDto From(Quotation q)
    {
        ArgumentNullException.ThrowIfNull(q);

        return new QuotationDto(q.Id, q.QuotationNumber, q.CustomerName,
            q.QuotedOn, q.ValidUntil, q.Status.ToString(), q.Notes,
            q.Lines.OrderBy(l => l.LineNo).Select(l => new QuotationLineDto(l.LineNo, l.ProductId,
                l.Description, l.Quantity, l.UnitPrice.Amount, l.TaxCode,
                l.LineTotal.Amount)).ToList(),
            q.Total.Amount);
    }
}

public interface IQuotationRepository
{
    Task<Quotation?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<Quotation>> RecentAsync(int take = 100,
        CancellationToken ct = default);

    /// The number printed on the quotation the customer brings back.
    Task<Quotation?> ByNumberAsync(string quotationNumber,
                                   CancellationToken ct = default);

    Task AddAsync(Quotation quotation, CancellationToken ct = default);
}

public interface IQuotationService
{
    Task<Result<QuotationDto>> OpenAsync(string customerName,
        int validForDays = 30, CancellationToken ct = default);

    Task<Result<QuotationDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    Task<Result<IReadOnlyList<QuotationDto>>> RecentAsync(
        CancellationToken ct = default);

    Task<Result<QuotationDto>> AddLineAsync(Guid quotationId, string code,
        decimal quantity, CancellationToken ct = default);

    Task<Result<QuotationDto>> RemoveLineAsync(Guid quotationId, int lineNo,
        CancellationToken ct = default);

    Task<Result<QuotationDto>> IssueAsync(Guid quotationId, string? notes,
        CancellationToken ct = default);

    /// The quotation the customer has brought back, by the number on it.
    Task<Result<QuotationDto>> FindAsync(string quotationNumber,
        CancellationToken ct = default);
}

/// <summary>
/// A quotation as it is printed: what was quoted, for how much, and until
/// when.
/// </summary>
public static class QuotationDocument
{
    public static PrintableDocument For(QuotationDto quotation)
    {
        ArgumentNullException.ThrowIfNull(quotation);

        var culture = System.Globalization.CultureInfo.CurrentCulture;
        var lines = new List<string>
        {
            $"Quotation {quotation.QuotationNumber}",
            $"For {quotation.CustomerName}",
            string.Format(culture, "Quoted {0:dd/MM/yyyy}  valid to {1:dd/MM/yyyy}",
                quotation.QuotedOn, quotation.ValidUntil),
            new string('-', 42)
        };

        foreach (var line in quotation.Lines)
        {
            lines.Add(string.Format(culture, "{0,5:0.##} {1}", line.Quantity,
                line.Description.Length <= 36
                    ? line.Description
                    : line.Description[..36]));

            lines.Add(string.Format(culture, "{0,26:N2} {1,15:N2}",
                line.UnitPrice, line.LineTotal));
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

    public async Task<Result<QuotationDto>> OpenAsync(string customerName,
        int validForDays = 30, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        if (string.IsNullOrWhiteSpace(customerName))
            return Result<QuotationDto>.Fail("QUO-001",
                "A quotation needs a customer name to be addressed to.");

        var number = await _numbering.NextAsync(DocumentSeries.Quotation, ct);

        var quotation = Quotation.Open(number, customerName, _user.Id,
            Today, validForDays);

        await _quotations.AddAsync(quotation, ct);
        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationDto.From(quotation));
    }

    public async Task<Result<QuotationDto>> FindAsync(string quotationNumber,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var quotation = await _quotations.ByNumberAsync(quotationNumber, ct);

        return quotation is null
            ? Result<QuotationDto>.Fail("QUO-022",
                $"No quotation {quotationNumber}.")
            : Result<QuotationDto>.Ok(QuotationDto.From(quotation));
    }

    public async Task<Result<QuotationDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var quotation = await _quotations.GetAsync(id, ct);

        return quotation is null
            ? Result<QuotationDto>.Fail("QUO-000", "Quotation not found.")
            : Result<QuotationDto>.Ok(QuotationDto.From(quotation));
    }

    public async Task<Result<IReadOnlyList<QuotationDto>>> RecentAsync(
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var rows = await _quotations.RecentAsync(ct: ct);

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
        _permissions.Demand(PermissionCode.SaleCreate);

        var quotation = await _quotations.GetAsync(quotationId, ct);
        if (quotation is null)
            return Result<QuotationDto>.Fail("QUO-000", "Quotation not found.");

        var product = await _products.FindByCodeOrBarcodeAsync(code,
            _permissions.ProductScope(), ct);
        if (product is null)
            return Result<QuotationDto>.Fail("POS-001",
                $"No product for code {code}.");

        // Quoted at the shelf price of the day. A quotation makes no
        // promise about stock, so an item out of stock is still
        // quotable - the pharmacy is quoting a price, not reserving one.
        quotation.AddLine(product.Id, product.Description, quantity,
                          product.RetailPrice, product.TaxCode);

        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationDto.From(quotation));
    }

    public async Task<Result<QuotationDto>> RemoveLineAsync(Guid quotationId,
        int lineNo, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var quotation = await _quotations.GetAsync(quotationId, ct);
        if (quotation is null)
            return Result<QuotationDto>.Fail("QUO-000", "Quotation not found.");

        quotation.RemoveLine(lineNo);
        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationDto.From(quotation));
    }

    public async Task<Result<QuotationDto>> IssueAsync(Guid quotationId,
        string? notes, CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SaleCreate);

        var quotation = await _quotations.GetAsync(quotationId, ct);
        if (quotation is null)
            return Result<QuotationDto>.Fail("QUO-000", "Quotation not found.");

        quotation.Note(notes);
        quotation.Issue();

        _audit.Applied("pos.quotation.issue", "Quotation", quotation.Id,
            $"{quotation.QuotationNumber} to {quotation.CustomerName}, "
            + $"{quotation.Total}, valid to "
            + $"{quotation.ValidUntil:dd/MM/yyyy}");

        await _uow.SaveChangesAsync(ct);

        return Result<QuotationDto>.Ok(QuotationDto.From(quotation));
    }

    private DateOnly Today =>
        DateOnly.FromDateTime(_clock.UtcNow.ToLocalTime());
}
