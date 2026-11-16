using PharmaX.Application.Abstractions;
using PharmaX.Application.Repositories;
using PharmaX.Application.Results;
using PharmaX.Application.Security;
using PharmaX.Application.Suppliers.Dtos;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;

namespace PharmaX.Application.Suppliers;

public interface ISupplierService
{
    Task<Result<IReadOnlyList<SupplierListItemDto>>> ListAsync(
        bool includeInactive = false, ListFilter? filter = null,
        CancellationToken ct = default);

    Task<Result<SupplierDto>> GetAsync(Guid id,
        CancellationToken ct = default);

    Task<Result<Guid>> CreateAsync(SupplierEditDto edit,
        CancellationToken ct = default);

    Task<Result> UpdateAsync(Guid id, SupplierEditDto edit,
        CancellationToken ct = default);

    Task<Result> DeactivateAsync(Guid id, CancellationToken ct = default);

    /// Brings a deactivated supplier back to the receiving screen.
    Task<Result> ReactivateAsync(Guid id, CancellationToken ct = default);
}

public sealed class SupplierService : ISupplierService
{
    private readonly ISupplierRepository _suppliers;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;

    public SupplierService(ISupplierRepository suppliers,
        IPermissionService permissions, IAuditWriter audit, IUnitOfWork uow)
    {
        _suppliers = suppliers;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
    }

    public async Task<Result<IReadOnlyList<SupplierListItemDto>>> ListAsync(
        bool includeInactive = false, ListFilter? filter = null,
        CancellationToken ct = default)
    {
        // Reading the supplier list is part of ordering stock, so it
        // rides on the product-read right rather than a separate one.
        _permissions.Demand(PermissionCode.ProductRead);

        var suppliers = await _suppliers.ListAsync(includeInactive, filter, ct);

        IReadOnlyList<SupplierListItemDto> items =
            suppliers.Select(SupplierListItemDto.From).ToList();

        return Result<IReadOnlyList<SupplierListItemDto>>.Ok(items);
    }

    public async Task<Result<SupplierDto>> GetAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.ProductRead);

        var supplier = await _suppliers.GetAsync(id, ct);

        return supplier is null
            ? Result<SupplierDto>.Fail("SUP-000", "Supplier not found.")
            : Result<SupplierDto>.Ok(SupplierDto.From(supplier));
    }

    public async Task<Result<Guid>> CreateAsync(SupplierEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.SupplierMaintain);

        if (Validate(edit) is Error problem)
            return Result<Guid>.Fail(problem.Code, problem.Message);

        if (await _suppliers.ByCodeAsync(edit.Code, ct) is not null)
            return Result<Guid>.Fail("SUP-002",
                $"Supplier code {edit.Code} is already in use.");

        var supplier = Supplier.Create(edit.Code, edit.Name,
            edit.TermsDays, edit.LeadTimeDays);

        supplier.UpdateContact(edit.ContactName, edit.Telephone, edit.Email);
        supplier.SetAccountNumber(edit.AccountNumber);

        await _suppliers.AddAsync(supplier, ct);

        _audit.Applied("inv.supplier.create", "Supplier", supplier.Id,
            edit.Name);

        await _uow.SaveChangesAsync(ct);
        return Result<Guid>.Ok(supplier.Id);
    }

    public async Task<Result> UpdateAsync(Guid id, SupplierEditDto edit,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(edit);

        _permissions.Demand(PermissionCode.SupplierMaintain);

        if (Validate(edit) is Error problem)
            return Result.Fail(problem.Code, problem.Message);

        var supplier = await _suppliers.GetAsync(id, ct);
        if (supplier is null)
            return Result.Fail("SUP-000", "Supplier not found.");

        supplier.Rename(edit.Name);
        supplier.UpdateContact(edit.ContactName, edit.Telephone, edit.Email);
        supplier.SetAccountNumber(edit.AccountNumber);
        supplier.SetTerms(edit.TermsDays, edit.LeadTimeDays);

        _audit.Applied("inv.supplier.update", "Supplier", supplier.Id);

        await _uow.SaveChangesAsync(ct);
        return Result.Ok();
    }

    public async Task<Result> DeactivateAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SupplierMaintain);

        var supplier = await _suppliers.GetAsync(id, ct);
        if (supplier is null)
            return Result.Fail("SUP-000", "Supplier not found.");

        // Never deleted: every goods receipt and every batch ever taken
        // in points at a supplier, and those records have to resolve.
        supplier.Deactivate();

        _audit.Applied("inv.supplier.deactivate", "Supplier", supplier.Id);
        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    /// <remarks>
    /// Deactivating was a one-way door: the list showed a deactivated
    /// supplier but offered only to deactivate it again, so a supplier
    /// switched off by mistake - or one the pharmacy starts buying from
    /// again - could not be received from (ADM-DEF-03).
    /// </remarks>
    public async Task<Result> ReactivateAsync(Guid id,
        CancellationToken ct = default)
    {
        _permissions.Demand(PermissionCode.SupplierMaintain);

        var supplier = await _suppliers.GetAsync(id, ct);
        if (supplier is null)
            return Result.Fail("SUP-000", "Supplier not found.");

        if (supplier.IsActive) return Result.Ok();

        supplier.Reactivate();

        _audit.Applied("inv.supplier.reactivate", "Supplier", supplier.Id);
        await _uow.SaveChangesAsync(ct);

        return Result.Ok();
    }

    private static Error? Validate(SupplierEditDto edit)
    {
        if (string.IsNullOrWhiteSpace(edit.Code))
            return new Error("SUP-003", "A supplier needs a code.");

        if (string.IsNullOrWhiteSpace(edit.Name))
            return new Error("SUP-004", "A supplier needs a name.");

        if (edit.TermsDays < 0)
            return new Error("SUP-005",
                "Payment terms cannot be a negative number of days.");

        // A lead time is what the reorder calculation adds to today to
        // decide when to order, so a nonsense value orders nonsensically.
        if (edit.LeadTimeDays is < 0 or > 365)
            return new Error("SUP-006",
                "A lead time must be between 0 and 365 days.");

        // Where orders are sent. "not-an-email" was saved as typed and
        // would have been found out the day an order went nowhere
        // (ADM-OBS-04).
        if (!string.IsNullOrWhiteSpace(edit.Email)
            && !IsEmailAddress(edit.Email.Trim()))
            return new Error("SUP-007",
                $"\"{edit.Email.Trim()}\" is not an email address.");

        return null;
    }

    /// <summary>
    /// One mailbox, written the way a mail client would accept it.
    /// </summary>
    /// <remarks>
    /// MailAddress alone accepts a display name and angle brackets, so
    /// the parsed address has to be all that was typed.
    /// </remarks>
    internal static bool IsEmailAddress(string value) =>
        value.Contains('@', StringComparison.Ordinal)
        && !value.Contains(' ', StringComparison.Ordinal)
        && System.Net.Mail.MailAddress.TryCreate(value, out var parsed)
        && string.Equals(parsed.Address, value, StringComparison.OrdinalIgnoreCase)
        && parsed.Host.Contains('.', StringComparison.Ordinal);
}
