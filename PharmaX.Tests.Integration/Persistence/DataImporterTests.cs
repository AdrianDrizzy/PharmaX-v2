using ClosedXML.Excel;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Import;
using PharmaX.Domain.Dispensing;
using PharmaX.Domain.Inventory;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Tills;
using PharmaX.Persistence;
using PharmaX.Persistence.Import;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// Another system's export going into a real database: units become
/// packs, the history lands in PharmaX's own tables, and running it twice
/// changes nothing.
/// </summary>
public sealed class DataImporterTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"pharmax-export-{Guid.NewGuid():N}");

    public DataImporterTests()
    {
        Directory.CreateDirectory(_folder);
        WriteExport(_folder);
    }

    public void Dispose()
    {
        _fixture.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    /// <summary>
    /// A pharmacy's export, one table to a CSV file, as the old system's
    /// export scripts write it: a script collected at the till, a sale
    /// paid in cash with change, a refund, a delivery, a count, a price
    /// change, a till override and a cash-up.
    /// </summary>
    internal static void WriteExport(string folder)
    {
        void Sheet(string name, string header, params string[] rows) =>
            File.WriteAllLines(Path.Combine(folder, name + ".csv"), rows.Prepend(header));

        Sheet("DEPARTMENTS", "DEPTID,DEPTNAME", "1,DISPENSARY", "2,OTC");
        Sheet("SUPPLIERS", "SUPPLIERCODE,SUPPLIERNAME,TELEPHONE,EMAILADDRESS", "52,GRANITESIDE,0912853277,.");
        Sheet("PRODUCTS",
            "PRODUCTCODE,BARCODE,PRODUCTNAME,ALT_PRODUCTNAME,FORMCODE,STRENGTH,SUPPLIERCODE,DEPTID,DRUGSCHEDULE,UNITOFMEASURE,TAXCODE,DISCONTINUEDON",
            "9001,,NIFEDIPINE,ADALAT,TAB,20MG,52,1,PP,,1,",
            "9002,6001234567890,DETTOL 250ML,,,,52,2,PP,,2,");
        Sheet("STOCKMAST", "PRODUCTCODE,PACKSIZE,BATCHNO,BATCHEXPIRY,QUANTITY,COSTPRICE,SELLINGPRICE",
            "9001,100,B1,31/12/2027 00:00:00,250,2.50,10.00",
            "9002,1,,,-3,1.00,4.60");
        Sheet("Patients", "MEMBERCODE,DEPENDENTID,FIRSTNAME,SURNAME,IDNO,PHONE,STREET,AREA,CITY,DATEOFBIRTH,MEDICALAID",
            "1757,1,JOHN,MOYO,.,0771234567,12,MPOPOMA,BYO,,_");
        Sheet("Doctors", "DOCTORCODE,TITLE,INITIALS,FIRSTNAME,SURNAME,PRACTICENO,PHONE1,CITY",
            "5,DR,J K,,NCUBE,40525,0292,BYO");
        Sheet("Prescription_Lines",
            "DISPENSEDATE,DISPENSETIME,SCRIPTNO,MEMBERCODE,DEPENDENTID,PATIENT,DOCTOR,PRACTICENO,PHARMACIST,PRODUCTCODE,PRODUCT,STRENGTH,QUANTITY,DOSAGE,SELLINGPRICE,DISPENSINGFEE,POS_MEDAIDPAID,POS_AMOUNTPAID,DELETED",
            "01/10/2026 00:00:00,09:15:00,835,1757,1,JOHN MOYO,NCUBE,40525,FABRICE,9001,NIFEDIPINE,20MG,30,T1 OD,3.00,0,0,3.00,False",
            "01/10/2026 00:00:00,09:16:00,836,1757,1,JOHN MOYO,NCUBE,40525,FABRICE,9001,NIFEDIPINE,20MG,30,T1 OD,3.00,0,0,3.00,True");
        Sheet("Sales_Lines",
            "SALEDATE,SALETIME,TRANSNO,TILLNO,TYPE,TENDER,PRODUCTCODE,PRODUCTNAME,QUANTITY,PACKSIZE,SELLINGPRICE,LINETOTAL,OPERATOR,SCRIPTNO",
            "01/10/2026 00:00:00,10:00:00,100,1,Sales,Cash USD,9002,DETTOL 250ML,1,1,4.60,4.60,megan,0",
            "01/10/2026 00:00:00,10:05:00,101,1,Sales,Cash USD,9001,NIFEDIPINE,30,30,3.00,3.00,megan,835",
            "01/10/2026 00:00:00,11:00:00,102,1,Sales Return,Sales Return,9002,DETTOL 250ML,1,1,4.60,0,megan,0");
        Sheet("Sales_Receipts",
            "SALEDATE,SALETIME,TRANSNO,TENDER,TRANSTOTAL,AMOUNTTENDERED,CHANGEAMOUNT,TENDEREXCHANGE,RETURNREASON,OPERATOR",
            "01/10/2026 00:00:00,10:00:00,100,Cash USD,4.60,5.00,0.40,1,,megan",
            "01/10/2026 00:00:00,10:05:00,101,Cash USD,3.00,3.00,0,1,,megan",
            "01/10/2026 00:00:00,11:00:00,102,Cash USD,4.60,4.60,0,1,DAMAGED,megan",
            "14/01/2026 00:00:00,11:00:00,103,,33576.00,0,0,1,,test");
        Sheet("Stock_Movements",
            "MOVEDATE,CATEGORY,TYPE,GRNO,DOCNO,INVOICENO,SUPPLIER,PRODUCTCODE,PRODUCTNAME,PACKSIZE,BATCHNO,BATCHEXPIRY,QUANTITY,COSTPRICE,LINETOTAL,OPERATOR",
            "28/09/2026 00:00:00,Purchase,Purchase Receipt,759,759,6959,GRANITESIDE,9001,NIFEDIPINE,100,B1,31/12/2027 00:00:00,300,2.50,7.50,test");
        Sheet("Price_History",
            "CHANGEDATE,CHANGETIME,PRODUCTCODE,PRODUCTNAME,PACKSIZE,OLDCOST,NEWCOST,OLDSELLING,NEWSELLING,OPERATOR",
            "06/10/2025 00:00:00,15:18:20,9001,NIFEDIPINE,100,2.00,2.50,9.00,10.00,test");
        Sheet("Till_Price_Overrides",
            "OVERRIDEDATE,OVERRIDETIME,OVERIDEID,PRODUCTCODE,PRODUCTNAME,PACKSIZE,QUANTITY,SELLINGPRICE,CHANGEDTO,DIFFERENCE,OPERATOR,AUTHORIZEDBY",
            "01/10/2026 00:00:00,10:00:30,1,9002,DETTOL 250ML,1,1,5.00,4.60,-0.40,megan,");
        Sheet("Cycle_Counts",
            "COUNTDATE,CYCLECOUNTID,PRODUCTCODE,PRODUCTNAME,PACKSIZE,PRIORQTY,ACTUALQTY,VARIANCE_QTY,COSTPRICE,OPERATOR",
            "18/08/2026 00:00:00,433,9001,NIFEDIPINE,100,260,250,-10,2.50,test");
        Sheet("Day_End_Totals", "FORDAY,ZREADID,TENDER,SYSTEMTOTAL,ACTUALTOTAL,VARIANCE,OPERATOR",
            "01/10/2026 00:00:00,27,Cash USD,3.00,3.00,0,megan");
        Sheet("Z_Read_History", "ZREADID,TRANSDATE,TRANSFROM,TRANSTO,OPERATOR,AUTHORIZEDBY_NAME",
            "27,01/10/2026 00:00:00,100,103,megan,megan");
        Sheet("Monthly_By_Department", "MONTH,DEPARTMENT,ITEMS,TOTAL", "2026-10,OTC,1,4.60");

        File.WriteAllLines(Path.Combine(folder, "Sales_Controls.csv"), new[]
        {
            "\"CONTROL\",\"DATABASE\",\"TAKEN\"",
            "\"Sales_Lines LINETOTAL\",\"7.6\",\"2026-10-06 12:26:53\"",
            "\"Sales_Receipts TRANSTOTAL\",\"33588.2\",\"2026-10-06 12:26:53\""
        });
    }

    private async Task<ImportResult> ImportAsync(bool dryRun = false, params string[] paths)
    {
        using var scope = _fixture.NewScope();
        return await PharmaXFixture.Resolve<IDataImporter>(scope)
            .ImportAsync(paths.Length == 0 ? new[] { _folder } : paths, dryRun);
    }

    [Fact]
    public async Task Stock_comes_in_as_packs_on_its_batch_and_every_total_agrees()
    {
        var report = await ImportAsync();

        report.Succeeded.Should().BeTrue(report.Text);

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var nifedipine = await db.Products.AsNoTracking().SingleAsync(p => p.Code == "9001");
        nifedipine.QuantityOnHand.Should().Be(2.5m, "250 tablets in packs of 100");
        nifedipine.UnitsPerPack.Should().Be(100m);
        nifedipine.Schedule.Should().Be(3, "the dispensary's own department");
        nifedipine.IsBatchTracked.Should().BeTrue();
        nifedipine.VatRatePercent.Should().Be(0m);
        nifedipine.GenericName.Should().Be("ADALAT");

        var batch = await db.Batches.AsNoTracking().SingleAsync(b => b.ProductId == nifedipine.Id);
        batch.BatchNumber.Should().Be("B1");
        batch.QuantityOnHand.Should().Be(2.5m);

        var dettol = await db.Products.AsNoTracking().SingleAsync(p => p.Code == "9002");
        dettol.QuantityOnHand.Should().Be(0m, "negative stock is set to nothing and listed");
        dettol.Schedule.Should().Be(2, "it was sold at the till, whatever its class said");
        dettol.VatRatePercent.Should().Be(15m);
        dettol.Barcodes.Should().Contain("6001234567890");

        var patient = await db.Patients.AsNoTracking().SingleAsync(p => p.FileNumber == "1757");
        patient.FirstNames.Should().Be("John");
        patient.Surname.Should().Be("Moyo");
        patient.IdNumber.Should().BeNull("'.' is not an ID number");
        patient.Address.Should().Contain("Bulawayo");
        patient.MedicalAidSchemeId.Should().BeNull("'_' is no medical aid");

        (await db.Prescribers.AsNoTracking().SingleAsync(p => p.Code == "5"))
            .Detail.Should().Contain("Practice 40525");

        report.Text.Should().Contain("Receipt 103");
        report.Text.Should().Contain("DETTOL 250ML (scheduled PP in the files)");
        report.Text.Should().Contain("Monthly_By_Department: totals worked out from other tables");
        report.Text.Should().NotContain("RSI");
    }

    /// <remarks>
    /// The files carry no pharmacy's own address, so the import records
    /// the one it was run with - the installer build's - for every till to
    /// print on its receipts and labels. One already set is the pharmacy's
    /// and is kept.
    /// </remarks>
    [Fact]
    public async Task The_pharmacy_it_was_run_for_goes_into_the_database()
    {
        using var fixture = PharmaXFixture.ForPharmacy(new PharmacyOptions
        {
            TradingName = "Bethrapha Pharmacy",
            Address = "28 Woodstock Road\nThorngrove\nBulawayo"
        });

        using (var scope = fixture.NewScope())
        {
            await PharmaXFixture.Resolve<IPharmacySettings>(scope)
                .SetAsync(PharmacySettingKey.TradingName, "BethRapha");

            (await PharmaXFixture.Resolve<IDataImporter>(scope)
                    .ImportAsync(new[] { _folder }, dryRun: false))
                .Succeeded.Should().BeTrue();
        }

        using var check = fixture.NewScope();
        var settings = PharmaXFixture.Resolve<IPharmacySettings>(check);

        (await settings.GetAsync(PharmacySettingKey.Address))
            .Should().Be("28 Woodstock Road\nThorngrove\nBulawayo");
        (await settings.GetAsync(PharmacySettingKey.TradingName))
            .Should().Be("BethRapha", "the name was already set");
    }

    /// <remarks>
    /// What keeps the development seeders' test stock off an imported
    /// shelf: the old system's receipt and script numbers give it away.
    /// </remarks>
    [Fact]
    public async Task Imported_trading_is_recognised_as_such()
    {
        using (var scope = _fixture.NewScope())
            (await ImportedHistory.ExistsAsync(PharmaXFixture.Resolve<PharmaXDbContext>(scope)))
                .Should().BeFalse("nothing has been imported yet");

        (await ImportAsync()).Succeeded.Should().BeTrue();

        using var check = _fixture.NewScope();
        (await ImportedHistory.ExistsAsync(PharmaXFixture.Resolve<PharmaXDbContext>(check)))
            .Should().BeTrue();
    }

    [Fact]
    public async Task The_history_lands_in_PharmaX_own_tables_linked_up()
    {
        (await ImportAsync()).Succeeded.Should().BeTrue();

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        // The prescription: a script, its dispensing, collected at the till.
        var script = await db.Scripts.AsNoTracking().Include(s => s.Items).SingleAsync();
        script.ScriptNumber.Should().Be("835", "the deleted line's script is left out");
        script.PrescriberName.Should().Be("NCUBE");
        script.Items.Should().ContainSingle().Which.Directions.Should().Be("T1 OD");

        var dispensing = await db.Dispensings.AsNoTracking().Include(d => d.Lines).SingleAsync();
        dispensing.Status.Should().Be(DispensingStatus.Issued);
        dispensing.SaleId.Should().NotBeNull("receipt 101 collected it");
        dispensing.AwaitsCollection.Should().BeFalse();
        dispensing.CoPaymentTotal.Amount.Should().Be(3.00m);

        // The till: two sales, the refund against the first, one session.
        var sales = await db.Sales.AsNoTracking().Include(s => s.Lines).Include(s => s.Payments)
            .OrderBy(s => s.DocumentNumber).ToListAsync();
        sales.Select(s => s.DocumentNumber).Should().Equal("100", "101");
        sales.Should().OnlyContain(s => s.Status == SaleStatus.Finalised);

        sales[0].Total.Amount.Should().Be(4.60m);
        sales[0].ChangeGiven.Amount.Should().Be(0.40m, "5.00 was tendered");
        sales[0].Payments.Should().ContainSingle().Which.Tender.Should().Be(TenderType.Cash);

        sales[1].DispensingId.Should().Be(dispensing.Id);
        sales[1].Lines.Should().ContainSingle().Which.IsScript.Should().BeTrue();

        var refund = await db.SaleReturns.AsNoTracking().Include(r => r.Lines).SingleAsync();
        refund.OriginalSaleId.Should().Be(sales[0].Id);
        refund.Reason.Should().Be("DAMAGED");
        refund.Total.Amount.Should().Be(4.60m);

        var session = await db.TillSessions.AsNoTracking().SingleAsync();
        session.Status.Should().Be(TillSessionStatus.Closed);
        sales.Should().OnlyContain(s => s.TillSessionId == session.Id);

        var declaration = await db.TillDeclarations.AsNoTracking().SingleAsync();
        declaration.TillSessionId.Should().Be(session.Id);
        declaration.Lines.Should().ContainSingle().Which.Declared.Amount.Should().Be(3.00m);

        // The shelf: a delivery, a count and a ledger that ends where the shelf is.
        var receipt = await db.GoodsReceipts.AsNoTracking().Include(g => g.Lines).SingleAsync();
        receipt.ReceiptNumber.Should().Be("759");
        receipt.IsPosted.Should().BeTrue();
        receipt.Lines.Should().ContainSingle().Which.Quantity.Should().Be(3m, "300 tablets in packs of 100");

        var count = await db.Stocktakes.AsNoTracking().Include(s => s.Lines).SingleAsync();
        count.Status.Should().Be(StocktakeStatus.Posted);
        count.Lines.Should().ContainSingle().Which.Variance.Should().Be(-0.1m);

        var nifedipine = await db.Products.AsNoTracking().SingleAsync(p => p.Code == "9001");
        var ledger = await db.StockMovements.AsNoTracking().Where(m => m.ProductId == nifedipine.Id).ToListAsync();
        ledger.OrderBy(m => m.OccurredAtUtc).Last().BalanceAfter.Should().Be(2.5m);
        ledger.Select(m => m.MovementType).Should().BeEquivalentTo(new[]
            { MovementType.StockTake, MovementType.Receipt, MovementType.Issue });

        // The audit trail: the price change and the till override.
        (await db.AuditEntries.CountAsync(a => a.Action == "inv.price.change" && a.EntityId == nifedipine.Id))
            .Should().Be(1);
        (await db.AuditEntries.SingleAsync(a => a.Action == "pos.sale.discount"))
            .EntityType.Should().Be("SaleLine", "the override is matched to the line it was rung up on");

        // The staff, as users nobody can sign in as yet.
        var staff = await db.Users.AsNoTracking().Where(u => u.Username != "admin").ToListAsync();
        staff.Select(u => u.Username).Should().Contain(new[] { "megan", "test", "fabrice" });
        staff.Should().OnlyContain(u => !u.IsActive);
    }

    /// <remarks>
    /// A till that takes rands and Zimbabwe gold writes their cash-up in
    /// rands and gold, with how many of each make a dollar. PharmaX holds
    /// the drawer in dollars; the files' own total of the column still
    /// reads as the files wrote it.
    /// </remarks>
    [Fact]
    public async Task A_cash_up_in_another_currency_comes_in_as_dollars()
    {
        File.WriteAllLines(Path.Combine(_folder, "Day_End_Totals.csv"), new[]
        {
            "FORDAY,ZREADID,TENDER,SYSTEMTOTAL,ACTUALTOTAL,VARIANCE,OPERATOR,TENDEREXCHANGE",
            "01/10/2026 00:00:00,27,Cash USD,3.00,3.00,0,megan,1",
            "01/10/2026 00:00:00,27,Rands,60.00,80.00,20,megan,20",
            "01/10/2026 00:00:00,27,EcoCash ZWG,68822.00,0,-68822,megan,36"
        });
        File.AppendAllLines(Path.Combine(_folder, "Sales_Controls.csv"),
            new[] { "\"Day_End_Totals ACTUALTOTAL\",\"83\",\"2026-10-06 12:26:53\"" });

        var report = await ImportAsync();

        report.Succeeded.Should().BeTrue(report.Text);
        report.Text.Should().Contain("Day_End_Totals: ACTUALTOTAL read");
        report.Text.Should().MatchRegex(@"Cash-ups: expected by the till\s+1,917\.72");
        report.Text.Should().MatchRegex(@"Cash-ups: counted\s+7\.00");

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var declaration = await db.TillDeclarations.AsNoTracking().SingleAsync();

        var cash = declaration.Lines.Single(l => l.Tender == TenderType.Cash);
        cash.Expected.Amount.Should().Be(6.00m, "3 dollars and 60 rands at 20 to the dollar");
        cash.Declared.Amount.Should().Be(7.00m);

        var ecoCash = declaration.Lines.Single(l => l.Tender == TenderType.EcoCash);
        ecoCash.Expected.Amount.Should().Be(1911.72m, "68,822 at 36 to the dollar");
        ecoCash.Declared.Amount.Should().Be(0m);

        (await db.TillSessions.AsNoTracking().SingleAsync()).ClosingDeclared!.Value.Amount.Should().Be(7.00m);
    }

    [Fact]
    public async Task Running_it_again_doubles_nothing()
    {
        (await ImportAsync()).Succeeded.Should().BeTrue();
        var again = await ImportAsync();

        again.Succeeded.Should().BeTrue(again.Text);
        again.Text.Should().Contain("stock and history were left as they are");

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Products.CountAsync(p => p.Code == "9001" || p.Code == "9002")).Should().Be(2);
        (await db.Batches.CountAsync()).Should().Be(1);
        (await db.Patients.CountAsync()).Should().Be(1);
        (await db.Sales.CountAsync()).Should().Be(2);
        (await db.Scripts.CountAsync()).Should().Be(1);
        (await db.StockMovements.CountAsync()).Should().Be(5);
    }

    [Fact]
    public async Task A_dry_run_reports_and_keeps_nothing()
    {
        var report = await ImportAsync(dryRun: true);

        report.Succeeded.Should().BeTrue(report.Text);
        report.Text.Should().Contain("DRY RUN");

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Patients.CountAsync()).Should().Be(0);
        (await db.Sales.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Any_layout_of_text_workbook_or_JSON_is_recognised_by_its_columns()
    {
        var folder = Path.Combine(_folder, "generic");
        Directory.CreateDirectory(folder);

        File.WriteAllLines(Path.Combine(folder, "stock list.txt"), new[]
        {
            "Item Code;Description;Retail Price;Cost;Qty On Hand;Barcode",
            "A1;Panado 24s;12.50;8.00;10;6001234000017"
        });

        File.WriteAllText(Path.Combine(folder, "clients.json"),
            """[{ "File No": "C1", "First Name": "jane", "Last Name": "doe", "Mobile": "0771000001" }]""");

        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Vendors");
            sheet.Cell(1, 1).Value = "Code";
            sheet.Cell(1, 2).Value = "Name";
            sheet.Cell(1, 3).Value = "Phone";
            sheet.Cell(2, 1).Value = "MED1";
            sheet.Cell(2, 2).Value = "Medico Wholesale";
            sheet.Cell(2, 3).Value = "0242000000";
            book.SaveAs(Path.Combine(folder, "lists.xlsx"));
        }

        var report = await ImportAsync(false, folder);

        report.Succeeded.Should().BeTrue(report.Text);

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        var panado = await db.Products.AsNoTracking().SingleAsync(p => p.Code == "A1");
        panado.Description.Should().Be("Panado 24s");
        panado.RetailPrice.Amount.Should().Be(12.50m);
        panado.QuantityOnHand.Should().Be(10m);
        panado.Barcodes.Should().Contain("6001234000017");

        var jane = await db.Patients.AsNoTracking().SingleAsync(p => p.FileNumber == "C1");
        jane.FullName.Should().Be("Jane Doe");
        jane.Telephone.Should().Be("0771000001");

        (await db.Suppliers.AsNoTracking().SingleAsync(s => s.Code == "MED1"))
            .Telephone.Should().Be("0242000000");

        (await db.StockMovements.AsNoTracking().SingleAsync(m => m.ProductId == panado.Id))
            .ReasonCode.Should().Be("OPENING", "stock with no history says where it came from");
    }

    /// <remarks>
    /// A supplier's price list, brought in after the change-over, changes
    /// prices. It does not take the pharmacist off a prescription medicine,
    /// put VAT on a zero-rated one or empty the shelf.
    /// </remarks>
    [Fact]
    public async Task A_partial_list_changes_only_what_it_carries()
    {
        (await ImportAsync()).Succeeded.Should().BeTrue();

        var folder = Path.Combine(_folder, "price list");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "products.csv"), new[] { "Code,Name,Price", "9001,NIFEDIPINE,12.00" });

        var report = await ImportAsync(false, folder);
        report.Succeeded.Should().BeTrue(report.Text);

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);
        var nifedipine = await db.Products.AsNoTracking().SingleAsync(p => p.Code == "9001");

        nifedipine.RetailPrice.Amount.Should().Be(12.00m, "the list carries the price");
        nifedipine.Schedule.Should().Be(3, "it says nothing about the schedule");
        nifedipine.VatRatePercent.Should().Be(0m);
        nifedipine.CostPrice.Amount.Should().Be(2.50m);
        nifedipine.UnitsPerPack.Should().Be(100m);
        nifedipine.GenericName.Should().Be("ADALAT");
        nifedipine.DepartmentId.Should().NotBeNull();
        nifedipine.DefaultSupplierId.Should().NotBeNull();
        nifedipine.QuantityOnHand.Should().Be(2.5m);
        nifedipine.IsActive.Should().BeTrue();
        (await db.Batches.CountAsync(b => b.ProductId == nifedipine.Id)).Should().Be(1);
    }

    /// <remarks>
    /// Before the pharmacy trades, a list with quantities sets the shelf.
    /// One without them, read later, leaves it alone - and a semicolon file
    /// from a South African Excel writes "1 250,50" for one thousand two
    /// hundred and fifty rand fifty.
    /// </remarks>
    [Fact]
    public async Task A_list_with_no_quantities_leaves_the_shelf_and_reads_decimal_commas()
    {
        var first = Path.Combine(_folder, "stocked");
        var later = Path.Combine(_folder, "prices");
        Directory.CreateDirectory(first);
        Directory.CreateDirectory(later);

        File.WriteAllLines(Path.Combine(first, "products.csv"), new[] { "Code,Name,Price,Qty", "A1,Panado,10.00,10" });
        File.WriteAllLines(Path.Combine(later, "products.csv"), new[] { "Code;Name;Price", "A1;Panado 24s;1 250,50" });

        (await ImportAsync(false, first)).Succeeded.Should().BeTrue();
        var report = await ImportAsync(false, later);

        report.Succeeded.Should().BeTrue(report.Text);
        report.Text.Should().Contain("stock on hand was left as it is");

        using var scope = _fixture.NewScope();
        var panado = await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Products.AsNoTracking()
            .SingleAsync(p => p.Code == "A1");

        panado.Description.Should().Be("Panado 24s");
        panado.RetailPrice.Amount.Should().Be(1250.50m);
        panado.QuantityOnHand.Should().Be(10m, "the price list says nothing about the shelf");
    }

    /// <remarks>
    /// An export saved as CSV and again in yesterday's workbook, with a
    /// column fewer, is one table: read once, from the CSV, and said so.
    /// </remarks>
    [Fact]
    public async Task An_older_copy_of_a_table_beside_it_is_read_once()
    {
        using (var book = new XLWorkbook())
        {
            var sheet = book.AddWorksheet("Patients");
            sheet.Cell(1, 1).Value = "MEMBERCODE";
            sheet.Cell(1, 2).Value = "FIRSTNAME";
            sheet.Cell(1, 3).Value = "SURNAME";
            sheet.Cell(2, 1).Value = "9999";
            sheet.Cell(2, 2).Value = "OLD";
            sheet.Cell(2, 3).Value = "COPY";
            book.SaveAs(Path.Combine(_folder, "Prescriptions_2026-10-05.xlsx"));
        }

        var report = await ImportAsync();

        report.Succeeded.Should().BeTrue(report.Text);
        report.Text.Should().Contain("Prescriptions_2026-10-05.xlsx [Patients]: the same table as Patients.csv");

        using var scope = _fixture.NewScope();
        (await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Patients.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Same_named_lists_from_two_folders_are_both_read()
    {
        var north = Path.Combine(_folder, "north");
        var south = Path.Combine(_folder, "south");
        Directory.CreateDirectory(north);
        Directory.CreateDirectory(south);

        File.WriteAllLines(Path.Combine(north, "patients.csv"), new[] { "File No,First Name,Surname", "N1,Ann,Dube" });
        File.WriteAllLines(Path.Combine(south, "patients.csv"), new[] { "File No,First Name,Surname", "S1,Ben,Dube" });

        (await ImportAsync(false, north, south)).Succeeded.Should().BeTrue();

        using var scope = _fixture.NewScope();
        (await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Patients
            .Select(p => p.FileNumber).ToListAsync())
            .Should().Contain(new[] { "N1", "S1" });
    }

    /// <remarks>
    /// A name alone is not a person: two John Moyos with no file number and
    /// no birth date are two patients until somebody merges them.
    /// </remarks>
    [Fact]
    public async Task Two_people_of_one_name_and_no_birth_date_stay_two_patients()
    {
        var folder = Path.Combine(_folder, "names");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "patients.csv"),
            new[] { "First Name,Surname,Phone", "John,Moyo,0771000001", "John,Moyo,0771000002" });

        (await ImportAsync(false, folder)).Succeeded.Should().BeTrue();

        using var scope = _fixture.NewScope();
        (await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Patients
            .CountAsync(p => p.Surname == "Moyo" && p.FirstNames == "John")).Should().Be(2);
    }

    /// <remarks>
    /// A till that starts again at receipt 1 every morning: each day's
    /// receipt 1 is a sale of its own, the second taking its day after its
    /// number so the two can be told apart.
    /// </remarks>
    [Fact]
    public async Task Receipt_numbers_used_on_two_days_are_two_sales()
    {
        var folder = Path.Combine(_folder, "daily numbers");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "products.csv"), new[] { "Code,Name,Price", "A1,Panado,5.00" });
        File.WriteAllLines(Path.Combine(folder, "sale lines.csv"), new[]
        {
            "TransNo,SaleDate,ProductCode,ProductName,Quantity,LineTotal",
            "1,01/10/2026,A1,Panado,1,5.00",
            "1,02/10/2026,A1,Panado,2,10.00"
        });

        var report = await ImportAsync(false, folder);
        report.Succeeded.Should().BeTrue(report.Text);

        using var scope = _fixture.NewScope();
        var sales = await PharmaXFixture.Resolve<PharmaXDbContext>(scope).Sales.AsNoTracking()
            .Include(s => s.Lines).OrderBy(s => s.OccurredAtUtc).ToListAsync();

        sales.Select(s => s.DocumentNumber).Should().Equal("1", "1-20261002");
        sales.Select(s => s.Total.Amount).Should().Equal(5.00m, 10.00m);
    }

    [Fact]
    public async Task Files_with_nothing_PharmaX_keeps_are_refused_before_anything_is_written()
    {
        var folder = Path.Combine(_folder, "nothing");
        Directory.CreateDirectory(folder);
        File.WriteAllLines(Path.Combine(folder, "notes.csv"), new[] { "Colour,Flavour", "Red,Sweet" });

        var report = await ImportAsync(false, folder);

        report.Succeeded.Should().BeFalse();
        report.Text.Should().Contain("Nothing in what was chosen is a table PharmaX can import");
    }
}
