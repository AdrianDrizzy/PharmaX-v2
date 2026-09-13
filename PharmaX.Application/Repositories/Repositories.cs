using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Suppliers;
using PharmaX.Domain.Tills;

namespace PharmaX.Application.Repositories;

// Repositories return domain entities and take no LINQ expressions from
// callers, because a repository that accepts arbitrary predicates is a
// DbContext with extra steps (Section 13.1).

public interface ISaleRepository
{
    Guid CurrentBranchId { get; }
    Guid CurrentWorkstationId { get; }

    Task<Sale?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Sale sale, CancellationToken ct = default);

    Task<IReadOnlyList<Sale>> ForSessionAsync(Guid tillSessionId,
                                              CancellationToken ct = default);

    /// The number printed on the customer's receipt, which is the only
    /// identifier they can hand back across a counter.
    Task<Sale?> ByDocumentNumberAsync(string documentNumber,
                                      CancellationToken ct = default);
}

public interface IReturnRepository
{
    Task<SaleReturn?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<SaleReturn>> RecentAsync(int take = 100,
        CancellationToken ct = default);

    /// <summary>
    /// How much has already come back against each line of a sale.
    /// Previous returns are separate documents, so without this a line
    /// could be refunded twice over.
    /// </summary>
    Task<IReadOnlyDictionary<int, decimal>> ReturnedQuantitiesAsync(
        Guid saleId, CancellationToken ct = default);

    Task<IReadOnlyList<SaleReturn>> ForSessionAsync(Guid tillSessionId,
        CancellationToken ct = default);

    Task AddAsync(SaleReturn saleReturn, CancellationToken ct = default);
}

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Product?> FindByCodeOrBarcodeAsync(string code,
                                            CancellationToken ct = default);

    Task<IReadOnlyList<Product>> SearchAsync(string term, int take = 200,
                                             CancellationToken ct = default);

    /// How many the same term matches altogether, so a list screen can
    /// say how much it is not showing.
    Task<int> CountAsync(string term, CancellationToken ct = default);

    Task AddAsync(Product product, CancellationToken ct = default);
}

public interface IBatchRepository
{
    Task<Batch?> GetAsync(Guid id, CancellationToken ct = default);

    /// Every batch for the product, in no particular order. FEFO ordering
    /// is the allocator's job, not the database's, so that the rule is
    /// testable without one.
    Task<IReadOnlyList<Batch>> ForProductAsync(Guid productId,
                                               CancellationToken ct = default);

    Task<IReadOnlyList<Batch>> ExpiringBeforeAsync(DateOnly expiringBefore,
                                                   CancellationToken ct = default);

    Task AddAsync(Batch batch, CancellationToken ct = default);
}

public interface IPatientRepository
{
    Task<Patient?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Patient?> ByFileNumberAsync(string fileNumber,
                                     CancellationToken ct = default);
    Task<IReadOnlyList<Patient>> SearchAsync(string term, int take = 100,
                                             CancellationToken ct = default);
    Task<ClinicalProfile?> ClinicalProfileAsync(Guid patientId,
                                                CancellationToken ct = default);
    Task AddAsync(Patient patient, CancellationToken ct = default);

    Task AddClinicalProfileAsync(ClinicalProfile profile,
                                 CancellationToken ct = default);

    /// True where the patient has any recorded allergy, for the list
    /// screen's warning flag. A count rather than the profile itself,
    /// because a list of names must not pull medical histories.
    Task<IReadOnlySet<Guid>> WithAllergiesAsync(IEnumerable<Guid> patientIds,
                                                CancellationToken ct = default);

    /// Scans filed against the patient, newest first.
    Task<IReadOnlyList<PatientAttachment>> AttachmentsAsync(Guid patientId,
        CancellationToken ct = default);

    Task<PatientAttachment?> AttachmentAsync(Guid attachmentId,
        CancellationToken ct = default);

    Task AddAttachmentAsync(PatientAttachment attachment,
                            CancellationToken ct = default);

    void RemoveAttachment(PatientAttachment attachment);
}

public interface IUserRepository
{
    Task<User?> ByUsernameAsync(string username, CancellationToken ct = default);
    Task<User?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(User user, CancellationToken ct = default);

    /// Active accounts by default. An inactive one is never deleted, so
    /// the administration screen has to be able to ask for them.
    Task<IReadOnlyList<User>> ListAsync(bool includeInactive = false,
                                        CancellationToken ct = default);
}

public interface ITillSessionRepository
{
    /// The open session for this workstation, or null. Null is the normal
    /// state first thing in the morning, not an error.
    Task<TillSession?> CurrentAsync(CancellationToken ct = default);

    Task<TillSession?> GetAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(TillSession session, CancellationToken ct = default);

    /// Null means the currency this installation trades in.
    Task<IReadOnlyList<Denomination>> DenominationsAsync(
        string? currency = null, CancellationToken ct = default);

    Task AddDeclarationAsync(TillDeclaration declaration,
                             CancellationToken ct = default);
}

public interface IStocktakeRepository
{
    Task<Stocktake?> GetAsync(Guid id, CancellationToken ct = default);

    /// The one count still being taken, or null. Two counts of the same
    /// shelf against two snapshots produce two contradictory variances.
    Task<Stocktake?> OpenCountAsync(CancellationToken ct = default);

    Task<IReadOnlyList<Stocktake>> RecentAsync(int take = 50,
        CancellationToken ct = default);

    Task AddAsync(Stocktake stocktake, CancellationToken ct = default);
}

public interface ISupplierRepository
{
    Task<Supplier?> GetAsync(Guid id, CancellationToken ct = default);
    Task<Supplier?> ByCodeAsync(string code, CancellationToken ct = default);

    Task<IReadOnlyList<Supplier>> ListAsync(bool includeInactive = false,
                                            CancellationToken ct = default);

    Task AddAsync(Supplier supplier, CancellationToken ct = default);
}

public interface IPurchaseOrderRepository
{
    Task<PurchaseOrder?> GetAsync(Guid id, CancellationToken ct = default);

    /// Orders still expecting stock, which is what a receiving screen
    /// offers when the delivery note quotes an order number.
    Task<IReadOnlyList<PurchaseOrder>> OutstandingAsync(
        Guid? supplierId = null, CancellationToken ct = default);

    Task AddAsync(PurchaseOrder order, CancellationToken ct = default);
}

public interface IGoodsReceiptRepository
{
    Task<GoodsReceipt?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<GoodsReceipt>> RecentAsync(int take = 100,
        CancellationToken ct = default);

    Task AddAsync(GoodsReceipt receipt, CancellationToken ct = default);
}

public interface IPermissionRepository
{
    bool UserHas(Guid userId, string permissionCode);

    /// True where a supervisor may authorise the action on the spot rather
    /// than the system simply refusing it.
    bool IsSupervisorEscalatable(string permissionCode);

    IReadOnlyList<string> ForUser(Guid userId);
}
