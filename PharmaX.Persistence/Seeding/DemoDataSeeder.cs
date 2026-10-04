using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Patients;
using PharmaX.Application.Patients.Dtos;
using PharmaX.Application.Reference;
using PharmaX.Application.Security;
using PharmaX.Domain.Batches;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Security;

namespace PharmaX.Persistence.Seeding;

/// <summary>
/// The standing data every screen needs before it has anything to show:
/// the maintained lists, patients with clinical histories, accounts,
/// tariffs, and stock deliberately left in states the alert centre has
/// to report on.
/// </summary>
/// <remarks>
/// Gated on the same Development:SeedTestUsers flag as the test accounts
/// and the catalogue, for the same reason: none of this may exist at a
/// site. It runs after them, because it builds on the products,
/// suppliers and users they create.
///
/// Everything goes in through the application services rather than
/// straight into the tables. It is slower and it is the point: a patient
/// written directly has no audit entry, no outbox row and no document
/// number, so the screens that read those would show a hole that no real
/// patient ever has. The exception is the shelf state below, which is a
/// deliberate corruption of stock levels and has no service that would
/// allow it.
///
/// Every section is idempotent and independently guarded: a section that
/// throws is logged and skipped, because a half-seeded demonstration
/// database is worth more than a workstation that will not start.
/// </remarks>
public sealed class DemoDataSeeder
{
    private readonly PharmaXDbContext _db;
    private readonly PharmaXOptions _options;
    private readonly ISessionContext _context;
    private readonly IReferenceDataService _reference;
    private readonly ITariffService _tariffs;
    private readonly IPatientService _patients;
    private readonly IAccountService _accounts;
    private readonly DemoTradingSeeder _trading;
    private readonly IClock _clock;
    private readonly ILogger<DemoDataSeeder> _log;

    public DemoDataSeeder(PharmaXDbContext db, PharmaXOptions options,
        ISessionContext context, IReferenceDataService reference,
        ITariffService tariffs, IPatientService patients,
        IAccountService accounts, DemoTradingSeeder trading, IClock clock,
        ILogger<DemoDataSeeder> log)
    {
        _db = db;
        _options = options;
        _context = context;
        _reference = reference;
        _tariffs = tariffs;
        _patients = patients;
        _accounts = accounts;
        _trading = trading;
        _clock = clock;
        _log = log;
    }

    /// <summary>
    /// The maintained lists behind frmReferenceList. Two of each at
    /// least, because a list with one row never shows that the grid
    /// sorts, and one inactive entry so the "include inactive" filter
    /// has something to reveal.
    /// </summary>
    private static readonly (ReferenceKind Kind, string Code, string Name,
        string? Detail)[] ReferenceLists =
    {
        (ReferenceKind.Department, "DISP", "Dispensary", "Behind the counter"),
        (ReferenceKind.Department, "FRONT", "Front shop", "Self-selection"),
        (ReferenceKind.Department, "SURG", "Surgical", "Dressings and sundries"),
        (ReferenceKind.Department, "BABY", "Baby care", "Formula and nappies"),

        (ReferenceKind.Category, "ANALG", "Analgesics", "Pain and fever"),
        (ReferenceKind.Category, "ANTIB", "Antibiotics", "Prescription only"),
        (ReferenceKind.Category, "CHRON", "Chronic medication", "Repeat therapy"),
        (ReferenceKind.Category, "ALLRGY", "Antihistamines", "Allergy relief"),
        (ReferenceKind.Category, "COUGH", "Cough and cold", "Seasonal"),
        (ReferenceKind.Category, "VITMIN", "Vitamins and supplements", null),
        (ReferenceKind.Category, "FIRST", "First aid", "Dressings and plasters"),
        (ReferenceKind.Category, "PPE", "Protective equipment", "Masks and gloves"),

        (ReferenceKind.Manufacturer, "ADCOCK", "Adcock Ingram", "Adcock Ingram Ltd"),
        (ReferenceKind.Manufacturer, "ASPEN", "Aspen Pharmacare", "Aspen Holdings"),
        (ReferenceKind.Manufacturer, "CIPLA", "Cipla Medpro", "Cipla SA"),
        (ReferenceKind.Manufacturer, "SANOFI", "Sanofi", "Sanofi Aventis SA"),

        (ReferenceKind.Branch, "BR1", "Riverside Main", "1 Example Street"),
        (ReferenceKind.Branch, "BR2", "Riverside Mall", "Shop 14, The Mall"),

        (ReferenceKind.MedicalAidScheme, "ALPHA", "Alpha Medical Aid Society",
            "Alpha Administrators"),
        (ReferenceKind.MedicalAidScheme, "BONWELL", "Bonwell Health",
            "Bonwell Administrators"),
        (ReferenceKind.MedicalAidScheme, "CIVSRV", "Civil Service Medical",
            "State administered")
    };

    /// <summary>
    /// Patients with something on them. The mix is the point: an allergy
    /// that collides with the seeded amoxicillin so the dispensing screen
    /// has a hard stop to raise, a chronic patient on a scheme so a claim
    /// is raised without anyone configuring one, and a self-payer so the
    /// co-payment path is not the only path.
    /// </summary>
    private static readonly (string Surname, string Names, int BirthYear,
        string Phone, string? Allergy, string? Chronic, string? SchemeCode,
        string? Membership)[] Patients =
    {
        ("Dube", "Lindiwe", 1979, "082 555 0101", "Penicillin",
            "Hypertension", "ALPHA", "AM-4417290"),
        ("Mokoena", "Sipho", 1966, "083 555 0102", null, "Type 2 diabetes",
            "ALPHA", "AM-4417291"),
        ("Pillay", "Reshma", 1991, "084 555 0103", "Sulphonamides", null,
            "BONWELL", "BW-220041"),
        ("Botha", "Annelie", 1948, "072 555 0104", null, "Asthma",
            "CIVSRV", "CS-9930012"),
        ("Khumalo", "Bongani", 2001, "076 555 0105", null, null, null, null),
        ("Fourie", "Marius", 1985, "071 555 0106", "Aspirin", null, null, null)
    };

    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (!_options.Development.SeedTestUsers) return;

        // The seeded administrator is who all of this is done as. Without
        // it there are no permissions to act under, and every service
        // call below would be refused.
        var admin = await _db.Users
            .FirstOrDefaultAsync(u => u.Username == "test.admin", ct)
            .ConfigureAwait(false);

        if (admin is null)
        {
            _log.LogWarning(
                "Demonstration data was not seeded: the test.admin account "
                + "is missing, so there is nobody to seed it as.");

            return;
        }

        _context.SignIn(admin.Id, admin.Username,
            new[] { Role.Standard.Administrator },
            admin.DiscountLimitPercent);

        try
        {
            var added = await Section("reference lists",
                    SeedReferenceListsAsync, ct).ConfigureAwait(false)
                + await Section("patients", SeedPatientsAsync, ct)
                    .ConfigureAwait(false)
                + await Section("tariffs", SeedTariffsAsync, ct)
                    .ConfigureAwait(false)
                + await Section("patient accounts", SeedAccountsAsync, ct)
                    .ConfigureAwait(false)
                + await Section("product filing", SeedFilingAsync, ct)
                    .ConfigureAwait(false)
                + await Section("trading history", _trading.SeedAsync, ct)
                    .ConfigureAwait(false)

                // Before the shelf state, which then puts its handful of
                // products back below it: ten of everything is the floor
                // for testing, not an instruction to restock the line
                // that is deliberately out of stock.
                + await Section("stock for testing", SeedTestStockAsync, ct)
                    .ConfigureAwait(false)
                + await Section("test stock onto batches",
                        SeedTestBatchesAsync, ct)
                    .ConfigureAwait(false)

                // After the trading, whose delivery would otherwise put the
                // low and the out-of-stock lines back on the shelf. They
                // were only ever low at the second start, when this used to
                // run again and adjust that delivery away.
                + await Section("shelf state", SeedShelfStateAsync, ct)
                    .ConfigureAwait(false);

            if (added == 0) return;

            _log.LogWarning(
                "Development:SeedTestUsers is on: added {Count} "
                + "demonstration records so that every screen has "
                + "something on it. This must never be enabled at a site.",
                added);
        }
        finally
        {
            // The container hands the next screen the same scoped
            // context. Leaving the administrator signed in would let a
            // tester reach screens their own account cannot.
            _context.SignOut();
        }
    }

    /// <summary>
    /// Runs one section, and turns a failure into a log line rather than
    /// a workstation that will not start.
    /// </summary>
    private async Task<int> Section(string name,
        Func<CancellationToken, Task<int>> section, CancellationToken ct)
    {
        try
        {
            return await section(ct).ConfigureAwait(false);
        }
#pragma warning disable CA1031 // Deliberately broad: see the remarks.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.LogWarning(ex,
                "Demonstration {Section} could not be seeded; the rest of "
                + "the demonstration data was still written.", name);

            return 0;
        }
    }

    private async Task<int> SeedReferenceListsAsync(CancellationToken ct)
    {
        var added = 0;

        foreach (var kind in Enum.GetValues<ReferenceKind>())
        {
            var existing = await _reference.ListAsync(kind, ct: ct)
                .ConfigureAwait(false);

            var codes = existing.IsSuccess
                ? existing.Value.Select(i => i.Code).ToHashSet(
                    StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var entry in ReferenceLists.Where(r => r.Kind == kind))
            {
                if (!codes.Add(entry.Code)) continue;

                var created = await _reference.CreateAsync(kind,
                    new ReferenceEditDto(entry.Code, entry.Name, entry.Detail),
                    ct).ConfigureAwait(false);

                if (created.IsSuccess) added++;
            }
        }

        // One retired department, so that the inactive filter on
        // frmReferenceList has something to hide and then show.
        var departments = await _reference
            .ListAsync(ReferenceKind.Department, ct: ct).ConfigureAwait(false);

        if (departments.IsSuccess
            && departments.Value.FirstOrDefault(d =>
                   string.Equals(d.Code, "BABY", StringComparison.Ordinal))
               is { IsActive: true } retired)
        {
            await _reference.SetActiveAsync(ReferenceKind.Department,
                retired.Id, active: false, ct).ConfigureAwait(false);
        }

        return added;
    }

    private async Task<int> SeedPatientsAsync(CancellationToken ct)
    {
        var schemes = await _db.MedicalAidSchemes
            .ToDictionaryAsync(s => s.Code, s => s.Id, StringComparer.Ordinal,
                ct)
            .ConfigureAwait(false);

        var added = 0;

        foreach (var p in Patients)
        {
            var already = await _db.Patients.AnyAsync(x =>
                    x.Surname == p.Surname && x.FirstNames == p.Names, ct)
                .ConfigureAwait(false);

            if (already) continue;

            Guid? schemeId = p.SchemeCode is not null
                             && schemes.TryGetValue(p.SchemeCode, out var id)
                ? id
                : null;

            var allergies = p.Allergy is null
                ? Array.Empty<AllergyDto>()
                : new[]
                {
                    new AllergyDto(p.Allergy, "Severe",
                        "Recorded at registration")
                };

            var chronic = p.Chronic is null
                ? Array.Empty<string>()
                : new[] { p.Chronic };

            var created = await _patients.CreateAsync(new PatientEditDto(
                Surname: p.Surname,
                FirstNames: p.Names,
                IdNumber: null,
                DateOfBirth: new DateOnly(p.BirthYear, 6, 15),
                Telephone: p.Phone,
                Email: null,
                WeightKg: null,
                IsPregnant: false,
                IsBreastfeeding: false,
                ClinicalNotes: null,
                Allergies: allergies,
                ChronicConditions: chronic,

                // Null where the patient pays for themselves, which is
                // what makes the co-payment and the cash paths differ.
                MedicalAidSchemeId: schemeId,
                MembershipNumber: schemeId is null ? null : p.Membership,
                DependantCode: schemeId is null ? null : "00"), ct)
                .ConfigureAwait(false);

            if (created.IsSuccess) added++;
        }

        return added;
    }

    /// <summary>
    /// A scheme price for the dispensed lines, so that a script issued
    /// to a member splits into a contribution and a co-payment instead of
    /// falling back to full retail.
    /// </summary>
    private async Task<int> SeedTariffsAsync(CancellationToken ct)
    {
        if (await _db.Tariffs.AnyAsync(ct).ConfigureAwait(false)) return 0;

        var schemes = await _db.MedicalAidSchemes.ToListAsync(ct)
            .ConfigureAwait(false);

        var products = await _db.Products
            .Where(p => DemoCatalogue.TariffedCodes.Contains(p.Code))
            .ToListAsync(ct).ConfigureAwait(false);

        if (schemes.Count == 0 || products.Count == 0) return 0;

        var from = _clock.Today.AddMonths(-6);
        var added = 0;

        foreach (var scheme in schemes)
        {
            foreach (var product in products)
            {
                // Schemes pay less than the shelf price, and the gap is
                // the co-payment the patient is asked for at the till.
                var price = Math.Round(product.RetailPrice.Amount * 0.80m, 2,
                    MidpointRounding.AwayFromZero);

                var created = await _tariffs.CreateAsync(new TariffEditDto(
                    SchemeId: scheme.Id,
                    ProductId: product.Id,
                    Price: price,
                    DispensingFee: 26.50m,
                    EffectiveFrom: from,
                    EffectiveTo: null), ct).ConfigureAwait(false);

                if (created.IsSuccess) added++;
            }
        }

        return added;
    }

    /// <summary>
    /// Accounts for the patients who are meant to have one, in the three
    /// states frmAccounts has to draw differently: clear, owing, and on
    /// hold.
    /// </summary>
    private async Task<int> SeedAccountsAsync(CancellationToken ct)
    {
        var wanted = new (string Surname, decimal Limit, int Terms)[]
        {
            ("Dube", 2500m, 30),
            ("Mokoena", 5000m, 30),
            ("Botha", 1000m, 60)
        };

        var added = 0;

        foreach (var (surname, limit, terms) in wanted)
        {
            var patient = await _db.Patients
                .FirstOrDefaultAsync(p => p.Surname == surname, ct)
                .ConfigureAwait(false);

            if (patient is null) continue;

            var exists = await _db.Accounts
                .AnyAsync(a => a.PatientId == patient.Id, ct)
                .ConfigureAwait(false);

            if (exists) continue;

            var opened = await _accounts
                .OpenAsync(patient.Id, limit, terms, ct).ConfigureAwait(false);

            if (opened.IsFailure) continue;

            added++;

            // One account suspended, so the till has a refusal to show
            // that is not a bug.
            if (string.Equals(surname, "Botha", StringComparison.Ordinal))
            {
                await _accounts.HoldAsync(opened.Value.Id,
                    "Referred to the account holder for arrears.", ct)
                    .ConfigureAwait(false);
            }
        }

        return added;
    }

    /// <summary>
    /// Which department and manufacturer each seeded product belongs to.
    /// </summary>
    /// <remarks>
    /// Without this the reference lists are populated and nothing points
    /// at them, so the reports screen offers a Department to filter by
    /// and every choice returns nothing - which reads as a broken filter
    /// rather than as unfiled stock.
    /// </remarks>
    private static readonly (string Code, string Department, string Category,
        string Manufacturer)[] Filing =
    {
        ("PAN500", "FRONT", "ANALG", "ADCOCK"),
        ("DIS200", "FRONT", "ANALG", "ASPEN"),
        ("VITC1G", "FRONT", "VITMIN", "CIPLA"),
        ("PLAST1", "FRONT", "FIRST", "ADCOCK"),
        ("SURGM1", "SURG", "PPE", "ASPEN"),
        ("ALLERG", "DISP", "ALLRGY", "SANOFI"),
        ("COUGH1", "DISP", "COUGH", "ADCOCK"),
        ("AMOX50", "DISP", "ANTIB", "ASPEN"),
        ("METF85", "DISP", "CHRON", "CIPLA"),
        ("INSUL1", "DISP", "CHRON", "SANOFI")
    };

    private async Task<int> SeedFilingAsync(CancellationToken ct)
    {
        var departments = await _db.Departments
            .ToDictionaryAsync(d => d.Code, d => d.Id, StringComparer.Ordinal,
                ct)
            .ConfigureAwait(false);

        var categories = await _db.Categories
            .ToDictionaryAsync(c => c.Code, c => c.Id, StringComparer.Ordinal,
                ct)
            .ConfigureAwait(false);

        var manufacturers = await _db.Manufacturers
            .ToDictionaryAsync(m => m.Code, m => m.Id, StringComparer.Ordinal,
                ct)
            .ConfigureAwait(false);

        var codes = Filing.Select(f => f.Code).ToList();

        var products = await _db.Products
            .Where(p => codes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var filed = 0;

        foreach (var (code, department, category, manufacturer) in Filing)
        {
            if (!products.TryGetValue(code, out var product)) continue;

            // Only what nobody has filed already: a pharmacy that has
            // moved a product into its own department keeps that.
            if (product.DepartmentId is not null) continue;

            product.Classify(
                departments.TryGetValue(department, out var d) ? d : null,
                categories.TryGetValue(category, out var c) ? c : null,
                manufacturers.TryGetValue(manufacturer, out var m) ? m : null);

            filed++;
        }

        if (filed > 0) await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return filed;
    }

    /// <summary>
    /// Ten of everything on the price list, so that anything in the
    /// catalogue can be sold, dispensed, returned or counted without
    /// being received first.
    /// </summary>
    /// <remarks>
    /// The price list brings in the whole catalogue and no stock, which
    /// is right for a site - a price list says what things cost, not
    /// what is on the shelf - and useless for a test, where every second
    /// product scanned is refused for having none. Ten is enough to sell
    /// a few, dispense a few and still have some left to count.
    /// <para>
    /// Once per product, ever, and only up to ten: a shelf a tester has
    /// sold down, or counted, stays where they left it rather than being
    /// refilled behind them at the next start-up. Products the shelf
    /// state deliberately empties are left alone for the same reason -
    /// the out-of-stock alert has to have something to report.
    /// </para>
    /// </remarks>
    private async Task<int> SeedTestStockAsync(CancellationToken ct)
    {
        const decimal Level = 10m;

        var staged = await _db.StockMovements
            .Where(m => m.ReasonCode == DemoReason
                        && m.MovementType == MovementType.Adjustment)
            .Select(m => m.ProductId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var already = await _db.StockMovements
            .Where(m => m.ReasonCode == TestStockReason)
            .Select(m => m.ProductId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var leave = staged.Concat(already).ToHashSet();

        var bare = await _db.Products
            .Where(p => p.IsActive && p.QuantityOnHand < Level)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var expiry = _clock.Today.AddYears(2);
        var added = 0;

        foreach (var product in bare.Where(p => !leave.Contains(p.Id)))
        {
            // A batch-tracked product needs the batch as well as the
            // number: with no batch there is nothing to allocate, and
            // the till refuses it however much its on-hand claims.
            if (product.IsBatchTracked)
            {
                added += await AddBatchAsync(product,
                        $"{product.Code}-TEST", expiry,
                        Level - product.QuantityOnHand, ct,
                        TestStockReason)
                    .ConfigureAwait(false);

                continue;
            }

            Adjust(product, Level, TestStockReason);
            added++;
        }

        if (added > 0) await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return added;
    }

    /// <summary>
    /// The test stock on dispensary lines put onto a batch, where it was
    /// topped up before those lines were batch-tracked.
    /// </summary>
    /// <remarks>
    /// The price list loads dispensary lines batch-tracked now, and the
    /// top-up above receives their ten onto a batch. A database seeded
    /// before that has them untracked with the stock loose on the shelf,
    /// so the label printed from them carried no batch or expiry
    /// (OCT-DEF-02). Whatever is on the shelf now goes onto one test
    /// batch - the on-hand does not move, so there is no stock movement -
    /// and only on lines the top-up stocked: a real database never gets
    /// here, because none of this runs at a site.
    /// </remarks>
    private async Task<int> SeedTestBatchesAsync(CancellationToken ct)
    {
        var toppedUp = await _db.StockMovements
            .Where(m => m.ReasonCode == TestStockReason)
            .Select(m => m.ProductId)
            .Distinct()
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (toppedUp.Count == 0) return 0;

        var batched = (await _db.Batches
                .Select(b => b.ProductId)
                .Distinct()
                .ToListAsync(ct)
                .ConfigureAwait(false))
            .ToHashSet();

        var loose = await _db.Products
            .Where(p => toppedUp.Contains(p.Id) && p.Schedule >= 3
                        && !p.IsBatchTracked && p.QuantityOnHand > 0m)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var expiry = _clock.Today.AddYears(2);
        var added = 0;

        foreach (var product in loose.Where(p => !batched.Contains(p.Id)))
        {
            _db.Batches.Add(Batch.Receive(product.Id, $"{product.Code}-TEST",
                expiry, product.QuantityOnHand, product.CostPrice,
                _clock.UtcNow));

            product.SetBatchTracked(true);
            added++;
        }

        if (added > 0) await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return added;
    }

    /// <summary>
    /// Stock left in the states the alert centre exists to report: below
    /// its reorder level, out of stock entirely, expired, and expiring
    /// inside the warning window.
    /// </summary>
    /// <remarks>
    /// Written straight to the tables, and it is the one place here that
    /// is. There is no service that lets a stock level be set to an
    /// arbitrary number, because outside a demonstration database there
    /// should not be one: a quantity moves because something was sold,
    /// received or counted. A stocktake is posted in the trading seeder
    /// for the honest version of the same thing.
    /// </remarks>
    private async Task<int> SeedShelfStateAsync(CancellationToken ct)
    {
        var products = await _db.Products
            .Where(p => DemoCatalogue.AlertCodes.Contains(p.Code))
            .ToDictionaryAsync(p => p.Code, StringComparer.Ordinal, ct)
            .ConfigureAwait(false);

        var today = _clock.Today;
        var added = 0;

        // Once per product, ever. Start-up runs this on every launch, and it
        // used to set these levels whenever the shelf held more than them -
        // so stock genuinely received was adjusted away again at the next
        // start: a delivery of 113 Disprin read 8 on hand after a restart,
        // and 30 masks read none.

        // Low, but still sellable: the reorder report has a line and the
        // till still serves the customer.
        if (products.TryGetValue("DIS200", out var low)
            && low.QuantityOnHand > 10m
            && !await ShelvedBeforeAsync(low, ct).ConfigureAwait(false))
        {
            low.SetReorderLevel(60m);
            Adjust(low, 8m);
            added++;
        }

        // Out of stock, which is the alert a buyer actually acts on.
        if (products.TryGetValue("SURGM1", out var out_)
            && out_.QuantityOnHand > 0m
            && !await ShelvedBeforeAsync(out_, ct).ConfigureAwait(false))
        {
            out_.SetReorderLevel(25m);
            Adjust(out_, 0m);
            added++;
        }

        // An expired batch and one inside the critical window, on a
        // product that is batch tracked so the batch grid has rows.
        if (products.TryGetValue("COUGH1", out var batched))
        {
            added += await AddBatchAsync(batched, "COUGH1-EXPIRED",
                today.AddDays(-12), 6m, ct)
                .ConfigureAwait(false);

            added += await AddBatchAsync(batched, "COUGH1-CRITICAL",
                today.AddDays(18), 10m, ct)
                .ConfigureAwait(false);
        }

        if (products.TryGetValue("AMOX50", out var soon))
        {
            added += await AddBatchAsync(soon, "AMOX50-WARNING",
                today.AddDays(62), 12m, ct)
                .ConfigureAwait(false);
        }

        if (added > 0)
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return added;
    }

    /// <summary>
    /// Puts a batch on the shelf the way a delivery would: the batch, the
    /// product's own on-hand raised to match, and the receipt movement
    /// that explains it.
    /// </summary>
    /// <remarks>
    /// It used to add the batch alone. The product's quantity never moved,
    /// so every demonstration database started with the product and its
    /// batches disagreeing - Amoxicillin read 36 on hand against 48 in
    /// batches - and no stocktake could say which was right.
    /// </remarks>
    private async Task<int> AddBatchAsync(Product product, string number,
        DateOnly expiry, decimal quantity, CancellationToken ct,
        string reason = DemoReason)
    {
        if (quantity <= 0m) return 0;

        var exists = await _db.Batches
            .AnyAsync(b => b.ProductId == product.Id
                           && b.BatchNumber == number, ct)
            .ConfigureAwait(false);

        if (exists) return 0;

        var batch = Batch.Receive(product.Id, number, expiry, quantity,
            product.CostPrice, _clock.UtcNow);

        _db.Batches.Add(batch);

        var balance = product.QuantityOnHand + quantity;
        product.SetQuantityOnHand(balance);

        _db.StockMovements.Add(StockMovement.Record(product.Id, batch.Id,
            MovementType.Receipt, quantity, balance, _clock.UtcNow,
            reasonCode: reason, sourceDocumentType: "Demo"));

        return 1;
    }

    /// <summary>
    /// Whether the product has already been set to its demonstration level:
    /// the adjustment that did it is on its stock ledger.
    /// </summary>
    private Task<bool> ShelvedBeforeAsync(Product product,
                                          CancellationToken ct) =>
        _db.StockMovements.AnyAsync(m => m.ProductId == product.Id
                                         && m.MovementType
                                            == MovementType.Adjustment
                                         && m.ReasonCode == DemoReason, ct);

    private const string DemoReason = "DEMO";

    /// Why a product that had none is holding ten.
    internal const string TestStockReason = "TESTSTOCK";

    /// <summary>
    /// Sets a product that is not batch tracked to a demonstration level,
    /// with an adjustment movement carrying the difference.
    /// </summary>
    private void Adjust(Product product, decimal level,
                        string reason = DemoReason)
    {
        var delta = level - product.QuantityOnHand;
        product.SetQuantityOnHand(level);

        if (delta == 0m) return;

        _db.StockMovements.Add(StockMovement.Record(product.Id, null,
            MovementType.Adjustment, delta, level, _clock.UtcNow,
            reasonCode: reason, sourceDocumentType: "Demo"));
    }
}

/// <summary>
/// The catalogue codes the demonstration data is built around. Kept in
/// one place because two seeders reach for the same shelf, and a code
/// that only one of them knows about is a section that silently does
/// nothing.
/// </summary>
internal static class DemoCatalogue
{
    /// Over the counter, no schedule, no batch: the quickest sale.
    public const string Counter = "PAN500";

    /// Schedule 4 and batch tracked: forces the dispensing path.
    public const string Prescription = "AMOX50";

    /// Chronic, batch tracked, and the most expensive line on the shelf.
    public const string Chronic = "METF85";

    public static readonly string[] TariffedCodes =
        { Prescription, Chronic, "INSUL1" };

    public static readonly string[] AlertCodes =
        { "DIS200", "SURGM1", "COUGH1", Prescription };
}
