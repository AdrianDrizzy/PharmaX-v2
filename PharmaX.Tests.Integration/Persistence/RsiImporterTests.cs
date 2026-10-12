using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PharmaX.Application.Legacy;
using PharmaX.Persistence;

namespace PharmaX.Tests.Integration.Persistence;

/// <summary>
/// RSI's export going into a real database: units become packs, history
/// stays history, and running it twice changes nothing.
/// </summary>
public sealed class RsiImporterTests : IDisposable
{
    private readonly PharmaXFixture _fixture = new();
    private readonly string _folder = Path.Combine(Path.GetTempPath(), $"rsi-export-{Guid.NewGuid():N}");

    public RsiImporterTests()
    {
        Directory.CreateDirectory(_folder);

        Sheet("DEPARTMENTS", "DEPTID,DEPTNAME", "1,DISPENSARY", "2,OTC");
        Sheet("SUPPLIERS", "SUPPLIERCODE,SUPPLIERNAME,TELEPHONE,EMAILADDRESS", "52,GRANITESIDE,0912853277,.");
        Sheet("PRODUCTS",
            "PRODUCTCODE,BARCODE,PRODUCTNAME,ALT_PRODUCTNAME,FORMCODE,STRENGTH,SUPPLIERCODE,DEPTID,DRUGSCHEDULE,UNITOFMEASURE,TAXCODE,DISCONTINUEDON",
            "9001,,NIFEDIPINE,ADALAT,TAB,20MG,52,1,PP,,1,",
            "9002,6001234567890,DETTOL 250ML,,,,52,2,PP,,2,");
        Sheet("STOCKMAST", "PRODUCTCODE,PACKSIZE,BATCHNO,BATCHEXPIRY,QUANTITY,COSTPRICE,SELLINGPRICE",
            "9001,100,B1,31/12/2027 00:00:00,250,2.50,10.00",
            "9002,1,,,-3,1.00,4.60");
        Sheet("Patients", "MEMBERCODE,DEPENDENTID,FIRSTNAME,SURNAME,IDNO,PHONE,STREET,AREA,CITY,DATEOFBIRTH",
            "1757,1,JOHN,MOYO,.,0771234567,12,MPOPOMA,BYO,");
        Sheet("Doctors", "DOCTORCODE,TITLE,INITIALS,FIRSTNAME,SURNAME,PRACTICENO,PHONE1,CITY",
            "5,DR,J K,,NCUBE,40525,0292,BYO");
        Sheet("Prescription_Lines",
            "DISPENSEDATE,DISPENSETIME,SCRIPTNO,MEMBERCODE,DEPENDENTID,PATIENT,DOCTOR,PHARMACIST,PRODUCTCODE,PRODUCT,STRENGTH,QUANTITY,DOSAGE,POS_AMOUNTPAID,DELETED",
            "01/10/2026 00:00:00,09:15:00,835,1757,1,JOHN MOYO,NCUBE,FABRICE,9001,NIFEDIPINE,20MG,30,T1 OD,3.00,False",
            "01/10/2026 00:00:00,09:16:00,836,1757,1,JOHN MOYO,NCUBE,FABRICE,9001,NIFEDIPINE,20MG,30,T1 OD,3.00,True");
        Sheet("Sales_Lines",
            "SALEDATE,SALETIME,TRANSNO,TYPE,TENDER,PRODUCTCODE,PRODUCTNAME,QUANTITY,LINETOTAL,OPERATOR,SCRIPTNO",
            "01/10/2026 00:00:00,10:00:00,100,Sales,Cash USD,9002,DETTOL 250ML,1,4.60,megan,0");
        Sheet("Sales_Receipts",
            "SALEDATE,SALETIME,TRANSNO,TENDER,TRANSTOTAL,AMOUNTTENDERED,CHANGEAMOUNT,TENDEREXCHANGE,RETURNREASON,OPERATOR",
            "01/10/2026 00:00:00,10:00:00,100,Cash USD,4.60,5.00,0.40,1,,megan",
            "14/01/2026 00:00:00,11:00:00,101,,33576.00,0,0,1,,test");
        Sheet("Stock_Movements",
            "MOVEDATE,TYPE,GRNO,DOCNO,INVOICENO,SUPPLIER,PRODUCTCODE,PRODUCTNAME,BATCHNO,QUANTITY,LINETOTAL,OPERATOR",
            "28/09/2026 00:00:00,Purchase Receipt,759,759,6959,GRANITESIDE,9001,NIFEDIPINE,B1,300,7.50,test");
        Sheet("Price_History",
            "CHANGEDATE,CHANGETIME,PRODUCTCODE,PRODUCTNAME,OLDCOST,NEWCOST,OLDSELLING,NEWSELLING,OPERATOR",
            "06/10/2025 00:00:00,15:18:20,9001,NIFEDIPINE,2.00,2.50,9.00,10.00,test");
        Sheet("Till_Price_Overrides",
            "OVERRIDEDATE,OVERRIDETIME,OVERIDEID,PRODUCTCODE,PRODUCTNAME,QUANTITY,SELLINGPRICE,CHANGEDTO,DIFFERENCE,OPERATOR,AUTHORIZEDBY",
            "23/07/2026 00:00:00,14:54:55,1,9002,DETTOL 250ML,1,0.00,4.60,4.60,test,");
        Sheet("Cycle_Counts",
            "COUNTDATE,CYCLECOUNTID,PRODUCTCODE,PRODUCTNAME,PRIORQTY,ACTUALQTY,VARIANCE_QTY,VARIANCE_VALUE_COST,OPERATOR",
            "18/08/2026 00:00:00,433,9001,NIFEDIPINE,260,250,-10,-0.25,test");
        Sheet("Day_End_Totals", "FORDAY,ZREADID,TENDER,SYSTEMTOTAL,ACTUALTOTAL,VARIANCE,OPERATOR",
            "01/10/2026 00:00:00,27,Cash USD,4.60,4.60,0,megan");
        Sheet("Z_Read_History", "ZREADID,TRANSDATE,TRANSFROM,TRANSTO,OPERATOR,AUTHORIZEDBY_NAME",
            "27,01/10/2026 00:00:00,100,101,megan,megan");

        File.WriteAllLines(Path.Combine(_folder, "Sales_Controls.csv"), new[]
        {
            "\"CONTROL\",\"DATABASE\",\"TAKEN\"",
            "\"Sales_Lines LINETOTAL\",\"4.6\",\"2026-10-06 12:26:53\"",
            "\"Sales_Receipts TRANSTOTAL\",\"33580.6\",\"2026-10-06 12:26:53\""
        });
    }

    public void Dispose()
    {
        _fixture.Dispose();
        try { Directory.Delete(_folder, recursive: true); } catch (IOException) { }
    }

    private void Sheet(string name, string header, params string[] rows) =>
        File.WriteAllLines(Path.Combine(_folder, name + ".csv"), rows.Prepend(header));

    private async Task<LegacyImportReport> ImportAsync(bool dryRun = false)
    {
        using var scope = _fixture.NewScope();
        return await PharmaXFixture.Resolve<ILegacyImporter>(scope).ImportAsync(_folder, dryRun);
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
        dettol.Schedule.Should().Be(2, "RSI sold it at the till, whatever its class said");
        dettol.VatRatePercent.Should().Be(15m);
        dettol.Barcodes.Should().Contain("6001234567890");

        var patient = await db.Patients.AsNoTracking().SingleAsync(p => p.FileNumber == "RSI-1757");
        patient.FirstNames.Should().Be("John");
        patient.Surname.Should().Be("Moyo");
        patient.IdNumber.Should().BeNull("'.' is not an ID number");
        patient.Address.Should().Contain("Bulawayo");

        (await db.Prescribers.AsNoTracking().SingleAsync(p => p.Code == "D5"))
            .Detail.Should().Contain("Practice 40525");

        var history = await db.LegacyRecords.AsNoTracking().ToListAsync();
        history.Count(h => h.Kind == "Script").Should().Be(1, "the deleted line is left out");
        history.Count(h => h.Kind == "Receipt").Should().Be(1, "the empty $33,576 receipt is left out");
        history.Select(h => h.Kind).Should().Contain(new[] { "Sale", "Stock", "Price", "Override", "Count", "DayEnd", "ZRead" });

        report.Text.Should().Contain("Receipt 101");
        report.Text.Should().Contain("DETTOL 250ML (RSI schedule PP)");
    }

    [Fact]
    public async Task Running_it_again_doubles_nothing()
    {
        (await ImportAsync()).Succeeded.Should().BeTrue();
        (await ImportAsync()).Succeeded.Should().BeTrue();

        using var scope = _fixture.NewScope();
        var db = PharmaXFixture.Resolve<PharmaXDbContext>(scope);

        (await db.Products.CountAsync(p => p.Code == "9001" || p.Code == "9002")).Should().Be(2);
        (await db.Batches.CountAsync()).Should().Be(1);
        (await db.StockMovements.CountAsync(m => m.ReasonCode == "RSI-OPENING")).Should().Be(1);
        (await db.Patients.CountAsync()).Should().Be(1);
        (await db.LegacyRecords.CountAsync()).Should().Be(9);
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
        (await db.LegacyRecords.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task An_incomplete_export_is_refused_before_anything_is_written()
    {
        File.Delete(Path.Combine(_folder, "Patients.csv"));

        var report = await ImportAsync();

        report.Succeeded.Should().BeFalse();
        report.Text.Should().Contain("Missing: Patients");
    }
}
