using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Reference;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Application.Security;
using PharmaX.Domain.Security;

namespace PharmaX.Application.Sales;

/// <summary>
/// Who a receipt is for, where the sale was for somebody in particular.
/// </summary>
/// <param name="AccountNumber">The account a sale was charged to.</param>
/// <param name="AccountName">Whose account it is.</param>
/// <param name="PatientName">The patient a script was collected for.</param>
/// <param name="FileNumber">The patient's file.</param>
/// <param name="ScriptNumber">The script the collection paid for.</param>
/// <param name="SchemeName">The medical aid that pays the rest.</param>
/// <param name="SchemeShare">What that scheme is being claimed for.</param>
public sealed record ReceiptParticularsDto(string? AccountNumber,
    string? AccountName, string? PatientName, string? FileNumber,
    string? ScriptNumber, string? SchemeName, decimal? SchemeShare);

public interface IReceiptParticularsService
{
    /// <summary>
    /// The sale with its particulars filled in, ready to print.
    /// </summary>
    Task<SaleDto> ForReceiptAsync(SaleDto sale, CancellationToken ct = default);

    /// <summary>
    /// Records that a receipt was printed again, and by whom.
    /// </summary>
    Task RecordReprintAsync(SaleDto sale, CancellationToken ct = default);
}

/// <summary>
/// Fills in the particulars a receipt needs beyond the sale itself.
/// </summary>
/// <remarks>
/// A receipt for a sale on account named nobody, so the slip the
/// customer signs for did not say whose account it went on (ADM-DEF-09);
/// a collected script's receipt named neither the patient nor the
/// script, nor what the medical aid was paying (ADM-OBS-15). Kept apart
/// from the sale service, which should not need to know about patients,
/// scripts and schemes to ring up a bar of soap.
/// </remarks>
public sealed class ReceiptParticularsService : IReceiptParticularsService
{
    private readonly ISaleRepository _sales;
    private readonly IAccountRepository _accounts;
    private readonly IPatientRepository _patients;
    private readonly IDispensingRepository _dispensings;
    private readonly IScriptRepository _scripts;
    private readonly IReferenceRepository _references;
    private readonly IPermissionService _permissions;
    private readonly IAuditWriter _audit;
    private readonly IUnitOfWork _uow;
    private readonly IProductRepository _products;

    public ReceiptParticularsService(ISaleRepository sales,
        IAccountRepository accounts, IPatientRepository patients,
        IDispensingRepository dispensings, IScriptRepository scripts,
        IReferenceRepository references, IPermissionService permissions,
        IAuditWriter audit, IUnitOfWork uow, IProductRepository products)
    {
        _products = products;
        _sales = sales;
        _accounts = accounts;
        _patients = patients;
        _dispensings = dispensings;
        _scripts = scripts;
        _references = references;
        _permissions = permissions;
        _audit = audit;
        _uow = uow;
    }

    public async Task<SaleDto> ForReceiptAsync(SaleDto sale,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        sale = await WithCodesAsync(sale, ct);

        var stored = await _sales.GetAsync(sale.Id, ct);
        if (stored is null) return sale;

        string? accountNumber = null, accountName = null;

        if (stored.AccountId is Guid accountId
            && await _accounts.GetAsync(accountId, ct) is { } account)
        {
            accountNumber = account.AccountNumber;
            accountName = account.Name;
        }

        string? patientName = null, fileNumber = null, scriptNumber = null,
                schemeName = null;
        decimal? schemeShare = null;

        var patientId = stored.PatientId;

        if (stored.DispensingId is Guid dispensingId
            && await _dispensings.GetAsync(dispensingId, ct) is { } dispensing)
        {
            patientId ??= dispensing.PatientId;

            if (await _scripts.GetAsync(dispensing.ScriptId, ct) is { } script)
                scriptNumber = script.ScriptNumber;

            if (!dispensing.ContributionTotal.IsZero)
                schemeShare = dispensing.ContributionTotal.Amount;
        }

        if (patientId is Guid id && await _patients.GetAsync(id, ct) is { } patient)
        {
            patientName = patient.FullName;
            fileNumber = patient.FileNumber;

            if (schemeShare is not null
                && patient.MedicalAidSchemeId is Guid schemeId
                && await _references.GetAsync(ReferenceKind.MedicalAidScheme,
                       schemeId, ct) is { } scheme)
                schemeName = scheme.Name;
        }

        if (accountNumber is null && patientName is null) return sale;

        return sale with
        {
            Particulars = new ReceiptParticularsDto(accountNumber,
                accountName, patientName, fileNumber, scriptNumber,
                schemeName, schemeShare)
        };
    }

    /// <summary>
    /// Each line with its product's code, which the receipt heads the
    /// line with.
    /// </summary>
    /// <remarks>
    /// The code is what a customer reads back at the counter and what
    /// the pharmacy looks the item up by. A product since deleted or out
    /// of reach simply prints without one.
    /// </remarks>
    private async Task<SaleDto> WithCodesAsync(SaleDto sale,
        CancellationToken ct)
    {
        if (sale.Lines.Count == 0) return sale;

        var codes = new Dictionary<Guid, string?>();

        foreach (var id in sale.Lines.Select(l => l.ProductId).Distinct())
            codes[id] = (await _products.GetAsync(id, ct))?.Code;

        return sale with
        {
            Lines = sale.Lines
                .Select(l => l with { ProductCode = codes[l.ProductId] })
                .ToList()
        };
    }

    /// <remarks>
    /// A reprint is a second copy of a document that has already been
    /// handed over, which is how a refund gets claimed twice. It was
    /// neither marked nor recorded (ADM-DEF-08); the slip now says COPY
    /// and this leaves the trail.
    /// </remarks>
    public async Task RecordReprintAsync(SaleDto sale,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(sale);

        _permissions.Demand(PermissionCode.SaleCreate);

        _audit.Applied("pos.receipt.reprint", "Sale", sale.Id,
            $"Receipt {sale.DocumentNumber} printed again");

        await _uow.SaveChangesAsync(ct);
    }
}
