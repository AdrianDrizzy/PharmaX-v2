using PharmaX.Domain.Batches;
using PharmaX.Domain.Claims;
using PharmaX.Domain.Finance;
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

    /// <summary>
    /// The unfinished sale already opened against a dispensing, if there
    /// is one.
    /// </summary>
    /// <remarks>
    /// A cashier who opens a script and is called away leaves a draft
    /// sale holding that dispensing, and only one sale may hold it. The
    /// next attempt has to find that draft and carry on with it rather
    /// than start a second one, which the database would refuse.
    /// </remarks>
    Task<Sale?> OpenCollectionForAsync(Guid dispensingId,
                                       CancellationToken ct = default);

    /// The number printed on the customer's receipt, which is the only
    /// identifier they can hand back across a counter.
    Task<Sale?> ByDocumentNumberAsync(string documentNumber,
                                      CancellationToken ct = default);

    /// <summary>
    /// Baskets parked at this branch's tills, oldest first, so a held sale
    /// can be recalled from any till - the customer comes back to
    /// whichever one is free.
    /// </summary>
    Task<IReadOnlyList<Sale>> HeldAsync(CancellationToken ct = default);

    /// <summary>
    /// An unfinished sale already converting this quotation, so recalling
    /// it twice carries on with the same basket.
    /// </summary>
    Task<Sale?> OpenForQuotationAsync(Guid quotationId,
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

/// <summary>
/// How much of the catalogue a query may return.
/// </summary>
/// <remarks>
/// Applied in the query, not by the screen afterwards: a cashier who types
/// an exact dispensary code must get "no product", not a product the till
/// then declines to show.
/// </remarks>
public enum ProductScope
{
    /// Everything, scheduled medicines included.
    All = 0,

    /// Only what sells over the counter: Schedule 0 to 2
    /// (Product.RequiresPharmacist is false).
    OverTheCounter = 1
}

public interface IProductRepository
{
    Task<Product?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Product?> FindByCodeOrBarcodeAsync(string code,
        ProductScope scope = ProductScope.All,
        CancellationToken ct = default);

    Task<IReadOnlyList<Product>> SearchAsync(string term, int take = 200,
        ProductScope scope = ProductScope.All,
        CancellationToken ct = default);

    /// How many the same term matches altogether, so a list screen can
    /// say how much it is not showing.
    Task<int> CountAsync(string term, ProductScope scope = ProductScope.All,
                         CancellationToken ct = default);

    /// <summary>
    /// Active products at or under their reorder level.
    /// </summary>
    /// <remarks>
    /// Only where a reorder level has actually been set. Product.
    /// ReorderLevel defaults to zero, and on zero the level rule reads
    /// "warn me when there are none left" for every line in the
    /// catalogue - which for a price list loaded without stock is all of
    /// them. A product with no reorder level has no reorder policy, so
    /// there is nothing here to breach.
    /// </remarks>
    Task<IReadOnlyList<Product>> BelowReorderLevelAsync(
        CancellationToken ct = default);

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

    /// <summary>
    /// Every batch of every one of these products, in one round trip.
    /// </summary>
    /// <remarks>
    /// A list screen showing two hundred products would otherwise ask
    /// two hundred times, which is the shape of query that is quick on a
    /// developer's machine and unusable over a branch's network.
    /// </remarks>
    Task<IReadOnlyList<Batch>> ForProductsAsync(
        IReadOnlyCollection<Guid> productIds,
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
    /// <summary>
    /// Active patients matching every word typed: surname, first names,
    /// file number, ID number, membership number or telephone.
    /// </summary>
    Task<IReadOnlyList<Patient>> SearchAsync(string term, int take = 100,
                                             CancellationToken ct = default);

    /// <summary>
    /// Files that may be the same person: the same ID number, or the
    /// same names and date of birth.
    /// </summary>
    /// <param name="telephone">
    /// A number to find on other files as well, compared on its digits.
    /// </param>
    Task<IReadOnlyList<Patient>> LikelySameAsync(string surname,
        string firstNames, DateOnly? dateOfBirth, string? idNumber,
        Guid? except, string? telephone = null,
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

    /// <summary>
    /// The last declaration captured for a session - what the operator
    /// actually counted, as opposed to what the sales say they should
    /// have.
    /// </summary>
    Task<TillDeclaration?> LatestDeclarationAsync(Guid tillSessionId,
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

    /// <summary>
    /// How much of each product is on an order not yet delivered - drafts
    /// included, because a draft is an order somebody has already
    /// decided to place.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, decimal>> OnOrderAsync(
        CancellationToken ct = default);

    Task AddAsync(PurchaseOrder order, CancellationToken ct = default);
}

public interface IGoodsReceiptRepository
{
    Task<GoodsReceipt?> GetAsync(Guid id, CancellationToken ct = default);

    Task<IReadOnlyList<GoodsReceipt>> RecentAsync(int take = 100,
        CancellationToken ct = default);

    Task AddAsync(GoodsReceipt receipt, CancellationToken ct = default);
}

public interface ILedgerRepository
{
    Task<IReadOnlyList<LedgerAccount>> AccountsAsync(
        bool includeInactive = false, CancellationToken ct = default);

    Task<LedgerAccount?> AccountAsync(Guid id,
                                      CancellationToken ct = default);

    Task<LedgerAccount?> AccountByCodeAsync(string code,
                                            CancellationToken ct = default);

    Task AddAccountAsync(LedgerAccount account,
                         CancellationToken ct = default);

    Task<JournalBatch?> BatchAsync(Guid id, CancellationToken ct = default);

    /// Posted batches in a date range, newest first, for the journal list.
    Task<IReadOnlyList<JournalBatch>> BatchesAsync(DateOnly from,
        DateOnly until, CancellationToken ct = default);

    Task AddBatchAsync(JournalBatch batch, CancellationToken ct = default);

    /// <summary>
    /// The debit and credit totals per account for everything posted up
    /// to and including a date - the trial balance, summed in the
    /// database rather than by pulling every journal line across.
    /// </summary>
    Task<IReadOnlyList<AccountTotals>> TotalsAsync(DateOnly asAt,
        CancellationToken ct = default);

    /// Every posted line touching one account, for the detail behind a
    /// trial balance figure.
    Task<IReadOnlyList<LedgerEntry>> EntriesAsync(string accountCode,
        DateOnly from, DateOnly until, CancellationToken ct = default);
}

/// What one account has been debited and credited altogether.
public sealed record AccountTotals(string AccountCode, decimal Debits,
                                   decimal Credits);

/// One posted line, with enough of its batch to read on a screen.
public sealed record LedgerEntry(DateOnly PostingDate, string BatchNumber,
    string Source, string AccountCode, decimal Debit, decimal Credit,
    string? Narration);

public interface IAccountRepository
{
    Task<Account?> GetAsync(Guid id, CancellationToken ct = default);

    Task<Account?> ByNumberAsync(string accountNumber,
                                 CancellationToken ct = default);

    /// Accounts by name or number, for the picker at the till.
    Task<IReadOnlyList<Account>> SearchAsync(string term, int take = 100,
                                             CancellationToken ct = default);

    /// <summary>
    /// Everything owing, largest first - the list somebody works through
    /// when chasing money.
    /// </summary>
    Task<IReadOnlyList<Account>> OwingAsync(CancellationToken ct = default);

    Task AddAsync(Account account, CancellationToken ct = default);

    /// The movements on one account, oldest first, so a statement reads
    /// down the page with a running balance.
    Task<IReadOnlyList<AccountTransaction>> TransactionsAsync(Guid accountId,
        DateOnly from, DateOnly until, CancellationToken ct = default);

    Task AddTransactionAsync(AccountTransaction transaction,
                             CancellationToken ct = default);

    /// Payments taken off accounts at one till session, for its cash-up.
    Task<IReadOnlyList<AccountTransaction>> PaymentsForSessionAsync(
        Guid tillSessionId, CancellationToken ct = default);

    /// <summary>
    /// What the movements add up to, for checking the stored balance
    /// against its own history.
    /// </summary>
    Task<decimal> MovementTotalAsync(Guid accountId,
                                     CancellationToken ct = default);
}

public interface IClaimRepository
{
    Task<MedicalAidClaim?> GetAsync(Guid id, CancellationToken ct = default);

    Task<MedicalAidClaim?> ByNumberAsync(string claimNumber,
                                         CancellationToken ct = default);

    /// <summary>
    /// The claim already raised for a dispensing, if there is one.
    /// </summary>
    /// <remarks>
    /// Issuing is idempotent and runs in a transaction, but a retry that
    /// got as far as saving would otherwise bill the scheme twice for
    /// one script.
    /// </remarks>
    Task<MedicalAidClaim?> ForDispensingAsync(Guid dispensingId,
        CancellationToken ct = default);

    /// Claims still owed, oldest first. One scheme, or every scheme.
    Task<IReadOnlyList<MedicalAidClaim>> OutstandingAsync(
        Guid? schemeId = null, CancellationToken ct = default);

    Task AddAsync(MedicalAidClaim claim, CancellationToken ct = default);
}

public interface IPermissionRepository
{
    bool UserHas(Guid userId, string permissionCode);

    /// True where a supervisor may authorise the action on the spot rather
    /// than the system simply refusing it.
    bool IsSupervisorEscalatable(string permissionCode);

    IReadOnlyList<string> ForUser(Guid userId);
}
