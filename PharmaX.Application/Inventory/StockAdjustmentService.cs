using System.Globalization;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Finance;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Inventory;

/// <summary>What happened to the stock.</summary>
public enum StockAdjustmentKind
{
    /// Broken, spilt or damaged, and thrown away.
    Breakage = 1,

    /// Past its expiry date and taken off the shelf.
    ExpiredWriteOff = 2,

    /// Sent to another branch.
    TransferOut = 3,

    /// Received from another branch.
    TransferIn = 4,

    /// Sent back to the supplier, for credit.
    ReturnToSupplier = 5,

    /// Packs of one item opened into another: the dispensary's bulk pack
    /// into the shop's, or tablets counted into pre-packs.
    MoveToItem = 6
}

/// <param name="BatchId">
/// The batch it comes out of, for stock going out of a batch-tracked
/// product.
/// </param>
/// <param name="OtherBranch">The branch a transfer goes to or comes from.</param>
/// <param name="BatchNumber">
/// The batch a transfer in arrives as, for a batch-tracked product.
/// </param>
/// <param name="Expiry">That batch's expiry date.</param>
public sealed record StockAdjustmentEditDto(StockAdjustmentKind Kind,
    string ProductCode, decimal Quantity, string Reason,
    Guid? BatchId = null, string? OtherBranch = null,
    string? BatchNumber = null, DateOnly? Expiry = null);

public sealed record AdjustableBatchDto(Guid BatchId, string BatchNumber,
    DateOnly Expiry, decimal QuantityOnHand, bool IsExpired);

public sealed record AdjustableProductDto(Guid ProductId, string Code,
    string Description, decimal QuantityOnHand, bool IsBatchTracked,
    IReadOnlyList<AdjustableBatchDto> Batches);

/// <param name="PrePack">
/// For stock moved into another item: the label each of its packs
/// carries, and how many packs there now are to label.
/// </param>
public sealed record StockAdjustmentResultDto(string Description,
    decimal QuantityOnHand, string Summary, LabelDto? PrePack = null,
    int PrePacks = 0);

public interface IStockAdjustmentService
{
    /// The product to adjust, with the batches stock can come out of.
    Task<Result<AdjustableProductDto>> FindAsync(string code,
        CancellationToken ct = default);

    Task<Result<StockAdjustmentResultDto>> RecordAsync(
        StockAdjustmentEditDto edit, CancellationToken ct = default);
}

/// <summary>
/// Stock that leaves or arrives other than by a sale, a dispensing or a
/// delivery: breakages, expired stock written off, and transfers between
/// branches.
/// </summary>
/// <remarks>
/// The menu offered Transfers, Breakages and Expired Stock, and each
/// opened a report: nothing in the application could record one, so the
/// shelf and the system drifted apart until the next stocktake
/// (ADM-DEF-05). The authority is the stocktake poster's, because a
/// write-off is the same act as posting a count that came up short.
/// </remarks>
public sealed class StockAdjustmentService : IStockAdjustmentService
{
    private readonly IProductRepository _products;
    private readonly IBatchRepository _batches;
    private readonly IStockMovementWriter _movements;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _uow;
    private readonly IClock _clock;
    private readonly ICurrentUser _user;
    private readonly IJournalPoster _journals;

    public StockAdjustmentService(IProductRepository products,
        IBatchRepository batches, IStockMovementWriter movements,
        IPermissionService permissions, IAuditWriter audit, IOutbox outbox,
        IUnitOfWork uow, IClock clock, ICurrentUser user,
        IJournalPoster journals)
    {
        _user = user;
        _journals = journals;
        _products = products;
        _batches = batches;
        _movements = movements;
        _permissions = permissions;
        _audit = audit;
        _outbox = outbox;
        _uow = uow;
        _clock = clock;
    }

    public async Task<Result<AdjustableProductDto>> FindAsync(string code,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.StockTakePost);

        var product = await _products.FindByCodeOrBarcodeAsync(
            (code ?? "").Trim(), _permissions.ProductScope(), ct);

        if (product is null)
            return Result<AdjustableProductDto>.Fail("INV-020",
                $"No product for code {code}.");

        var batches = product.IsBatchTracked
            ? (await _batches.ForProductAsync(product.Id, ct))
                .Where(b => b.QuantityOnHand > 0m)
                .OrderBy(b => b.ExpiryDate)
                .Select(b => new AdjustableBatchDto(b.Id, b.BatchNumber,
                    b.ExpiryDate, b.QuantityOnHand, b.IsExpiredOn(_clock.Today)))
                .ToList()
            : new List<AdjustableBatchDto>();

        return Result<AdjustableProductDto>.Ok(new AdjustableProductDto(
            product.Id, product.Code, product.Description,
            product.QuantityOnHand, product.IsBatchTracked, batches));
    }

    public async Task<Result<StockAdjustmentResultDto>> RecordAsync(
        StockAdjustmentEditDto edit, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.StockTakePost);

        if (!Enum.IsDefined(edit.Kind))
            return Fail("INV-021", "Choose what happened to the stock.");

        if (edit.Quantity <= 0m)
            return Fail("INV-022", "The quantity must be more than zero.");

        if (string.IsNullOrWhiteSpace(edit.Reason))
            return Fail("INV-023",
                "Say why. A write-off with no reason is a loss nobody can "
                + "explain at the stocktake.");

        var transfer = edit.Kind is StockAdjustmentKind.TransferOut
                                 or StockAdjustmentKind.TransferIn;

        if ((transfer || edit.Kind is StockAdjustmentKind.ReturnToSupplier
                                   or StockAdjustmentKind.MoveToItem)
            && string.IsNullOrWhiteSpace(edit.OtherBranch))
            return Fail("INV-024", edit.Kind switch
            {
                StockAdjustmentKind.TransferOut => "Which branch is it going to?",
                StockAdjustmentKind.TransferIn => "Which branch is it coming from?",
                StockAdjustmentKind.ReturnToSupplier => "Which supplier is it going back to?",
                _ => "Which item is it going into? Give its code."
            });

        var product = await _products.FindByCodeOrBarcodeAsync(
            edit.ProductCode.Trim(), _permissions.ProductScope(), ct);

        if (product is null)
            return Fail("INV-020", $"No product for code {edit.ProductCode}.");

        Product? into = null;

        if (edit.Kind == StockAdjustmentKind.MoveToItem)
        {
            into = await _products.FindByCodeOrBarcodeAsync(
                edit.OtherBranch!.Trim(), _permissions.ProductScope(), ct);

            if (into is null || into.Id == product.Id)
                return Fail("INV-020", $"No other product for code {edit.OtherBranch}.");
        }

        await using var tx = await _uow.BeginTransactionAsync(ct);

        var problem = edit.Kind switch
        {
            StockAdjustmentKind.TransferIn => await BringInAsync(product, edit, ct),
            StockAdjustmentKind.MoveToItem => await MoveAsync(product, into!, edit, ct),
            _ => await TakeOutAsync(product, edit, ct)
        };

        if (problem is Error error) return Fail(error.Code, error.Message);

        var summary = Summarise(product, edit);

        _audit.Applied("inv.stock.adjust", "Product", product.Id, summary);

        // The supplier owes the pharmacy what went back, at what it cost.
        if (edit.Kind == StockAdjustmentKind.ReturnToSupplier)
            await _journals.PostSupplierReturnAsync(edit.OtherBranch!.Trim(),
                product.CostPrice * edit.Quantity, ct);

        if (transfer)
            _outbox.Append("StockTransfer", product.Id,
                string.Format(CultureInfo.InvariantCulture,
                    "{{\"productId\":\"{0}\",\"direction\":\"{1}\","
                    + "\"branch\":\"{2}\",\"quantity\":{3}}}",
                    product.Id,
                    edit.Kind == StockAdjustmentKind.TransferOut ? "out" : "in",
                    edit.OtherBranch!.Trim().Replace("\"", "", StringComparison.Ordinal),
                    edit.Quantity));

        await _uow.SaveChangesAsync(ct);
        await tx.DisposeAsync();

        if (into is null)
            return Result<StockAdjustmentResultDto>.Ok(new StockAdjustmentResultDto(
                product.Description, product.QuantityOnHand, summary));

        // Each new pack labelled as a pre-pack: what it is, how many are
        // in it, and the batch it was counted out of.
        var batch = edit.BatchId is Guid id ? await _batches.GetAsync(id, ct) : null;

        return Result<StockAdjustmentResultDto>.Ok(new StockAdjustmentResultDto(
            product.Description, product.QuantityOnHand, summary,
            new LabelDto("", into.Description, into.UnitsInPack, "",
                batch?.BatchNumber, batch?.ExpiryDate, _user.Username, "Pre-pack",
                _clock.Today, Array.Empty<string>()),
            (int)Math.Floor(Moved(product, into, edit.Quantity))));
    }

    /// <summary>
    /// Out of one item and into another, the same tablets in different
    /// packs: a pack of a thousand becomes fifty of twenty.
    /// </summary>
    /// <remarks>
    /// Checked before anything moves, so a refusal leaves both as they were.
    /// </remarks>
    private async Task<Error?> MoveAsync(Product from, Product into,
        StockAdjustmentEditDto edit, CancellationToken ct)
    {
        var batch = edit.BatchId is Guid id ? await _batches.GetAsync(id, ct) : null;

        if (into.IsBatchTracked && (batch is null || batch.ExpiryDate <= _clock.Today))
            return new Error("INV-032",
                $"{into.Description} is batch tracked: move an in-date batch of "
                + $"{from.Description} into it.");

        return await TakeOutAsync(from, edit, ct)
               ?? await BringInAsync(into, edit with
               {
                   Kind = StockAdjustmentKind.TransferIn,
                   OtherBranch = from.Code,
                   Quantity = Moved(from, into, edit.Quantity),
                   BatchNumber = batch?.BatchNumber,
                   Expiry = batch?.ExpiryDate
               }, ct);
    }

    private static decimal Moved(Product from, Product into, decimal packs) =>
        Math.Round(packs * from.UnitsInPack / into.UnitsInPack, 3);

    /// Breakage, expired write-off or transfer out.
    private async Task<Error?> TakeOutAsync(Product product,
        StockAdjustmentEditDto edit, CancellationToken ct)
    {
        Batch? batch = null;

        if (product.IsBatchTracked)
        {
            if (edit.BatchId is not Guid batchId)
                return new Error("INV-025",
                    $"{product.Description} is batch tracked: choose the "
                    + "batch it comes out of.");

            batch = await _batches.GetAsync(batchId, ct);

            if (batch is null || batch.ProductId != product.Id)
                return new Error("INV-025",
                    $"That batch is not one of {product.Description}'s.");

            if (edit.Quantity > batch.QuantityOnHand)
                return new Error("INV-026",
                    $"Batch {batch.BatchNumber} holds "
                    + $"{batch.QuantityOnHand:0.###}, so {edit.Quantity:0.###} "
                    + "cannot come out of it.");

            // Written off as expired only when it is: stock still in date
            // that is being thrown away is a breakage, and the reports
            // treat the two differently.
            if (edit.Kind == StockAdjustmentKind.ExpiredWriteOff
                && !batch.IsExpiredOn(_clock.Today.AddDays(1)))
                return new Error("INV-027",
                    $"Batch {batch.BatchNumber} is in date until "
                    + $"{batch.ExpiryDate:dd/MM/yyyy}. Record it as a "
                    + "breakage if it is being thrown away.");

            if (edit.Kind == StockAdjustmentKind.ExpiredWriteOff
                && edit.Quantity == batch.QuantityOnHand)
                batch.Dispose(edit.Reason.Trim());
            else
                batch.Issue(edit.Quantity);
        }
        else if (edit.Quantity > product.QuantityOnHand)
        {
            return new Error("INV-026",
                $"{product.Description}: {Math.Max(product.QuantityOnHand, 0m):0.###} "
                + $"on hand, so {edit.Quantity:0.###} cannot come out.");
        }

        var balance = product.QuantityOnHand - edit.Quantity;
        product.SetQuantityOnHand(balance);

        _movements.Add(StockMovement.Record(
            productId: product.Id,
            batchId: batch?.Id,
            movementType: edit.Kind switch
            {
                StockAdjustmentKind.TransferOut or StockAdjustmentKind.MoveToItem
                    => MovementType.TransferOut,
                StockAdjustmentKind.ReturnToSupplier => MovementType.ReturnToSupplier,
                _ => MovementType.Disposal
            },
            quantity: edit.Quantity,
            balanceAfter: balance,
            occurredAtUtc: _clock.UtcNow,
            reasonCode: ReasonCode(edit),
            sourceDocumentType: "StockAdjustment"));

        return null;
    }

    /// A transfer in from another branch.
    private async Task<Error?> BringInAsync(Product product,
        StockAdjustmentEditDto edit, CancellationToken ct)
    {
        Batch? batch = null;

        if (product.IsBatchTracked)
        {
            if (string.IsNullOrWhiteSpace(edit.BatchNumber)
                || edit.Expiry is not DateOnly expiry)
                return new Error("INV-028",
                    $"{product.Description} is batch tracked: give the batch "
                    + "number and expiry printed on what arrived.");

            if (expiry <= _clock.Today)
                return new Error("INV-029",
                    $"Batch {edit.BatchNumber.Trim()} expired on "
                    + $"{expiry:dd/MM/yyyy}. Do not take expired stock onto "
                    + "the shelf.");

            var number = edit.BatchNumber.Trim();

            batch = (await _batches.ForProductAsync(product.Id, ct))
                .FirstOrDefault(b => string.Equals(b.BatchNumber, number,
                                         StringComparison.OrdinalIgnoreCase)
                                     && b.ExpiryDate == expiry);

            if (batch is null)
            {
                batch = Batch.Receive(product.Id, number, expiry,
                    edit.Quantity, product.CostPrice, _clock.UtcNow);

                await _batches.AddAsync(batch, ct);
            }
            else
            {
                batch.Return(edit.Quantity);
            }
        }

        var balance = product.QuantityOnHand + edit.Quantity;
        product.SetQuantityOnHand(balance);

        _movements.Add(StockMovement.Record(
            productId: product.Id,
            batchId: batch?.Id,
            movementType: MovementType.TransferIn,
            quantity: edit.Quantity,
            balanceAfter: balance,
            occurredAtUtc: _clock.UtcNow,
            reasonCode: ReasonCode(edit),
            sourceDocumentType: "StockAdjustment"));

        return null;
    }

    /// What the movement reports print in their Reason column.
    private static string ReasonCode(StockAdjustmentEditDto edit)
    {
        var why = edit.Reason.Trim();

        var code = edit.Kind switch
        {
            StockAdjustmentKind.Breakage => $"BREAKAGE: {why}",
            StockAdjustmentKind.ExpiredWriteOff => $"EXPIRED: {why}",
            StockAdjustmentKind.TransferOut =>
                $"TO {edit.OtherBranch!.Trim()}: {why}",
            StockAdjustmentKind.ReturnToSupplier =>
                $"RETURN {edit.OtherBranch!.Trim()}: {why}",
            StockAdjustmentKind.MoveToItem =>
                $"INTO {edit.OtherBranch!.Trim()}: {why}",
            _ => $"FROM {edit.OtherBranch!.Trim()}: {why}"
        };

        // The column holds thirty; the whole reason is on the audit entry.
        return code.Length <= 30 ? code : code[..30];
    }

    private static string Summarise(Product product, StockAdjustmentEditDto edit) =>
        string.Format(CultureInfo.CurrentCulture, "{0} {1:0.###} x {2}{3}. {4}",
            edit.Kind switch
            {
                StockAdjustmentKind.Breakage => "Breakage:",
                StockAdjustmentKind.ExpiredWriteOff => "Expired, written off:",
                StockAdjustmentKind.TransferOut => "Transferred out:",
                StockAdjustmentKind.ReturnToSupplier => "Returned to the supplier:",
                StockAdjustmentKind.MoveToItem => "Moved:",
                _ => "Transferred in:"
            },
            edit.Quantity, product.Description,
            (edit.OtherBranch is null ? ""
                : edit.Kind switch
                {
                    StockAdjustmentKind.TransferIn => " from ",
                    StockAdjustmentKind.MoveToItem => " into ",
                    _ => " to "
                } + edit.OtherBranch.Trim())
            + (edit.Kind == StockAdjustmentKind.ReturnToSupplier
                ? string.Format(CultureInfo.CurrentCulture, ", {0:N2} at cost",
                    product.CostPrice.Amount * edit.Quantity)
                : ""),
            edit.Reason.Trim());

    private static Result<StockAdjustmentResultDto> Fail(string code,
        string message) =>
        Result<StockAdjustmentResultDto>.Fail(code, message);
}
