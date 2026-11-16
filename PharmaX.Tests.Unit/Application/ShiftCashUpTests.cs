using ClosedXML.Excel;
using FluentAssertions;
using NSubstitute;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing;
using PharmaX.Application.Finance;
using PharmaX.Application.Reporting;
using PharmaX.Application.Repositories;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Tills;
using PharmaX.Reporting.Definitions;
using PharmaX.Reporting.Exporters;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// The cashier's cash-up and the end of the day: the arithmetic, the
/// receipt's privacy rule for scripts, the 32-column slip, who may cash up
/// whose till, and the exports.
/// </summary>
public class ShiftCashUpTests
{
    private static readonly DateTime Noon = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Cashier = Guid.NewGuid();
    private static readonly Guid Supervisor = Guid.NewGuid();

    private const string Medicine = "Amoxicillin 500mg caps";

    /// <summary>
    /// A float of 100; a 20.00 cash sale paid with 50 (30 change); a 15.00
    /// card sale; a 45.00 script collected in cash; the 20.00 refunded in cash
    /// off the first sale; 20.00 paid off an account in cash.
    /// </summary>
    private static (TillSession Session, List<Sale> Sales, List<SaleReturn> Returns,
        List<AccountTransaction> Payments, Dictionary<Guid, string> Scripts) Shift()
    {
        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "2", Cashier,
                                       Money.Of(100m), Noon);

        Sale Sell(string description, decimal price, TenderType tender, decimal handed,
                  bool script = false, string number = "")
        {
            var sale = Sale.Open(session.BranchId, session.WorkstationId, session.Id, Cashier, Noon);
            if (script) sale.CollectFor(Guid.NewGuid());
            sale.AddLine(SaleLine.Create(Guid.NewGuid(), description, 1m, Money.Of(price), 15m,
                "A", PriceSource.StandardRetail, isScript: script));
            sale.TakePayments(new[] { SalePayment.Create(tender, Money.Of(handed)) });
            sale.Finalise(number, Noon);
            return sale;
        }

        var cash = Sell("Panado 500mg 20s", 20m, TenderType.Cash, 50m, number: "R1");
        var card = Sell("Vitamin C 1000mg", 15m, TenderType.Card, 15m, number: "R2");
        var collection = Sell(Medicine, 45m, TenderType.Cash, 45m, script: true, number: "R3");

        // Left out: never finalised.
        var open = Sale.Open(session.BranchId, session.WorkstationId, session.Id, Cashier, Noon);
        open.AddLine(SaleLine.Create(Guid.NewGuid(), "Basket left open", 1m, Money.Of(99m), 15m,
            "A", PriceSource.StandardRetail));

        var refund = SaleReturn.Against(cash, ReturnKind.GoodsReturn, session.Id, Cashier,
            Supervisor, "Damaged box", Noon.AddHours(1));
        refund.AddLine(cash.Lines[0], 1m, 0m, false, DateOnly.FromDateTime(Noon));
        refund.AddRefund(SalePayment.Create(TenderType.Cash, Money.Of(20m)));
        refund.Finalise("CN1", Noon.AddHours(1));

        var account = AccountTransaction.RecordPayment(Guid.NewGuid(), Money.Of(20m),
            DateOnly.FromDateTime(Noon), TenderType.Cash, session.Id, "AP1");
        account.CreatedBy = Cashier;
        account.CreatedAtUtc = Noon.AddHours(2);

        return (session, new List<Sale> { cash, card, collection, open },
            new List<SaleReturn> { refund }, new List<AccountTransaction> { account },
            new Dictionary<Guid, string> { [collection.Id] = "12345" });
    }

    private static ShiftCashUpDto Tally()
    {
        var (session, sales, returns, payments, scripts) = Shift();
        return ShiftTally.Build(session, sales, returns, payments,
            new Dictionary<Guid, string> { [Cashier] = "Thandi Cashier" }, scripts);
    }

    [Fact]
    public void The_shift_adds_up_by_tender_and_expects_the_float_plus_the_cash()
    {
        var shift = Tally();

        shift.SalesCount.Should().Be(3, "the open basket was never sold");
        shift.SalesTotal.Should().Be(80m);
        shift.RefundsTotal.Should().Be(20m);
        shift.AccountPayments.Should().Be(20m);
        shift.NetTakings.Should().Be(80m);
        shift.ChangeGiven.Should().Be(30m);
        shift.ScriptsCollected.Should().Be(1);

        // 20 kept from the 50, 45 for the script, 20 refunded, 20 off an account.
        shift.Tenders.Should().ContainSingle(t => t.Tender == "Cash")
             .Which.Amount.Should().Be(65m);
        shift.Tenders.Should().ContainSingle(t => t.Tender == "Card")
             .Which.Amount.Should().Be(15m);
        shift.ExpectedCash.Should().Be(165m);
        shift.CashierName.Should().Be("Thandi Cashier");
        shift.Transactions.Should().HaveCount(5);
    }

    [Fact]
    public void A_collected_script_shows_its_number_and_never_the_medicine()
    {
        var shift = Tally();

        var collection = shift.Transactions.Single(t => t.Kind == "Script collection");
        collection.Items.Should().ContainSingle().Which.Text.Should().Be("Script 12345");

        shift.Transactions.SelectMany(t => t.Items).Select(i => i.Text)
             .Should().NotContain(text => text.Contains("Amoxicillin"));
    }

    [Fact]
    public void A_refunded_script_names_the_script_not_what_came_back()
    {
        var (session, sales, _, _, scripts) = Shift();
        var collection = sales[2];

        var back = SaleReturn.Against(collection, ReturnKind.GoodsReturn, session.Id, Cashier,
            Supervisor, "Not needed", Noon);
        back.AddLine(collection.Lines[0], 1m, 0m, false, DateOnly.FromDateTime(Noon));
        back.AddRefund(SalePayment.Create(TenderType.Cash, Money.Of(45m)));
        back.Finalise("CN2", Noon);

        var shift = ShiftTally.Build(session, sales, new[] { back },
            Array.Empty<AccountTransaction>(), new Dictionary<Guid, string>(), scripts);

        shift.Transactions.Single(t => t.Kind == "Refund").Items
             .Should().ContainSingle().Which.Text.Should().Be("Script 12345");
    }

    [Fact]
    public void The_slip_is_thirty_two_across_and_keeps_scripts_private()
    {
        var slip = CashUpSlip.Document(Tally(), everyTransaction: true, Noon.ToLocalTime());

        slip.Columns.Should().Be(32);
        slip.Lines.Should().OnlyContain(line => line.Length <= 32);
        slip.Lines.Should().Contain(l => l.StartsWith("CASH IN DRAWER") && l.EndsWith("165.00"));
        slip.Lines.Should().Contain(l => l.Contains("Script 12345"));
        slip.Lines.Should().NotContain(l => l.Contains("Amoxicillin"));
        slip.Lines.Should().Contain(l => l.Contains("Change") && l.EndsWith("30.00"));
    }

    [Fact]
    public void The_slip_prints_under_the_receipts_own_centred_header()
    {
        var slip = CashUpSlip.Document(Tally(), everyTransaction: true, Noon.ToLocalTime());
        var header = new PharmaX.Application.Sales.ReceiptHeaderDto("Bethrapha Pharmacy", "",
            "28 Woodstock Road", "10012345", "", "Branch 1 Till 2", "thandi", Noon,
            Telephone: "029 222 0000");

        using var document = new PharmaX.Adapters.Printing.SlipDocument(slip, header);
        var rows = document.Rows();

        rows[0].Text.Should().Be("Bethrapha Pharmacy");
        rows[0].Centred.Should().BeTrue();
        rows.Should().Contain(r => r.Text == "VAT No: 10012345" && r.Centred);
        rows.Should().Contain(r => r.Text == "Cash-up" && r.Centred);
        rows.Should().OnlyContain(r => r.Text.Length <= 32);
        rows.Should().Contain(r => r.Text.StartsWith("CASH IN DRAWER")
                                   && r.Style == PharmaX.Adapters.Printing.ReceiptStyle.Bold);
    }

    [Fact]
    public void The_short_slip_leaves_the_transactions_off()
    {
        var slip = CashUpSlip.Document(Tally(), everyTransaction: false, Noon.ToLocalTime());

        slip.Lines.Should().NotContain("Transactions");
        slip.Lines.Should().NotContain(l => l.Contains("Panado"));
    }

    [Fact]
    public void The_day_adds_every_till_and_cashier_together()
    {
        var shift = Tally();
        var other = shift with
        {
            TillNumber = "1",
            Tenders = new[] { new TenderTotalDto("Card", 2, 40m) },
            SalesTotal = 40m,
            SalesCount = 2,
            RefundsTotal = 0m,
            AccountPayments = 0m,
            Transactions = Array.Empty<ShiftTransactionDto>()
        };

        var day = new EndOfDayDto(DateOnly.FromDateTime(Noon), new[] { shift, other });

        day.NetTakings.Should().Be(120m);
        day.Tenders.Single(t => t.Tender == "Card").Amount.Should().Be(55m);
        day.Cashiers.Should().ContainSingle(c => c.Name == "Thandi Cashier")
           .Which.Net.Should().Be(80m);
    }

    [Fact]
    public void The_end_of_day_report_has_the_tills_tenders_and_cashiers_and_no_graph()
    {
        var day = new EndOfDayDto(DateOnly.FromDateTime(Noon), new[] { Tally() });

        var report = CashUpReports.EndOfDay(day, Provenance());

        report.Rows.Should().ContainSingle();
        report.Sections.Select(s => s.Title).Should()
              .Equal("By tender", "By cashier", "Every transaction");
        report.Figures.Should().Contain(f => f.Label == "Net takings" && f.Value == 80m.ToString("N2"));
        report.Chart.Should().BeNull("graphs belong to the Reports screen, not the cash-ups");

        report.Sections.Single(s => s.Title == "Every transaction").Rows
              .Select(r => r["Items"] as string)
              .Should().NotContain(text => text!.Contains("Amoxicillin"));
    }

    [Fact]
    public void Exports_carry_the_letterhead_and_the_figures()
    {
        var day = new EndOfDayDto(DateOnly.FromDateTime(Noon), new[] { Tally() });
        var report = CashUpReports.EndOfDay(day, Provenance());
        var branding = new Letterhead();
        var folder = Directory.CreateTempSubdirectory("pharmax-cashup-").FullName;

        try
        {
            var xlsx = Path.Combine(folder, "day.xlsx");
            new ExcelExporter(branding).WriteTo(report, xlsx);

            using (var book = new XLWorkbook(xlsx))
            {
                var sheet = book.Worksheet(1);
                var text = sheet.CellsUsed().Select(c => c.GetString()).ToList();

                text.Should().Contain("Bethrapha Pharmacy");
                text.Should().Contain("28 Woodstock Road");
                text.Should().Contain(t => t.Contains("VAT No: 10012345") && t.Contains("Tel: 029 222"));
                text.Should().Contain("End-of-Day Cash-Up");
                text.Should().Contain("By cashier");
                book.Worksheets.Should().NotContain(w => w.Name == "Graph");
            }

            var pdf = Path.Combine(folder, "day.pdf");
            new PdfExporter(branding).WriteTo(report, pdf);

            File.ReadAllBytes(pdf).Take(4).Should().Equal((byte)'%', (byte)'P', (byte)'D', (byte)'F');
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_shift_exports_to_pdf_with_a_logo_and_every_transaction()
    {
        var folder = Directory.CreateTempSubdirectory("pharmax-shift-").FullName;

        try
        {
            var logo = Path.Combine(folder, "logo.png");
            File.WriteAllBytes(logo, ReportCharts.Picture(
                new ReportChart("Logo", ChartKind.Bar, "x",
                    new[] { new ChartPoint("a", 1m), new ChartPoint("b", 2m) }), 600, 200));

            var report = CashUpReports.Shift(Tally() with { Tenders = new[] { new TenderTotalDto("Cash", 2, 130.12m) } }, Provenance(), everyTransaction: true);
            var pdf = Path.Combine(folder, "shift.pdf");

            
            new PdfExporter(new Letterhead { Logo = logo }).WriteTo(report, pdf);

            new FileInfo(pdf).Length.Should().BeGreaterThan(1000);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    [Fact]
    public void A_summary_gets_a_graph_and_a_list_of_sales_does_not()
    {
        var summary = new ReportResult("Sales summary",
            new[] { new ReportColumn("Tender", "Tender"), new ReportColumn("Net", "Net", "N2", true) },
            new[]
            {
                Row(("Tender", "Cash"), ("Net", 120m)),
                Row(("Tender", "Card & EFT"), ("Net", 80m))
            },
            new Dictionary<string, object?>(), Provenance());

        var chart = ReportCharts.Suggest(summary);
        chart.Should().NotBeNull();
        chart!.Points.Select(p => p.Label).Should().Equal("Cash", "Card & EFT");

        var svg = ReportCharts.Svg(chart, 600, 300);
        svg.Should().StartWith("<svg").And.Contain("Card &amp; EFT");
        ReportCharts.Picture(chart, 300, 150).Take(4).Should()
                    .Equal((byte)0x89, (byte)'P', (byte)'N', (byte)'G');

        var list = summary with
        {
            Columns = new[] { new ReportColumn("DocumentNumber", "Document"),
                              new ReportColumn("Total", "Total", "N2", true) }
        };

        ReportCharts.Suggest(list).Should().BeNull();
    }

    [Fact]
    public void A_profit_report_graphs_the_profit_and_the_week_keeps_its_order()
    {
        var byDay = new ReportResult("Sales by Day of the Week",
            new[]
            {
                new ReportColumn("Group", "Day of week"),
                new ReportColumn("Sales", "Sales", "N2", true),
                new ReportColumn("Margin", "Profit", "N2", true)
            },
            new[]
            {
                Row(("Group", "Monday"), ("Sales", 50m), ("Margin", 10m)),
                Row(("Group", "Tuesday"), ("Sales", 90m), ("Margin", 30m)),
                Row(("Group", "Wednesday"), ("Sales", 70m), ("Margin", 20m))
            },
            new Dictionary<string, object?>(), Provenance());

        var sales = ReportCharts.Suggest(byDay)!;
        sales.Points.Select(p => p.Label).Should().Equal("Monday", "Tuesday", "Wednesday");
        sales.ValueLabel.Should().Be("Sales");

        var profit = ReportCharts.Suggest(byDay, "Margin")!;
        profit.ValueLabel.Should().Be("Profit");
        profit.Points.Select(p => p.Value).Should().Equal(10m, 30m, 20m);
    }

    [Fact]
    public async Task A_cashier_sees_every_shift_of_the_day_their_own_first()
    {
        var (mine, theirs, service) = Service(PermissionsOf(Role.Standard.Cashier));

        // A till opened for them under another login is still in the list.
        var shifts = await service.ShiftsAsync();
        shifts.Select(s => s.SessionId).Should().Equal(mine.Id, theirs.Id);
        shifts[0].IsOwn.Should().BeTrue();
        service.MayActForOthers.Should().BeFalse(
            "closing or paying out of someone else's till stays with a supervisor");

        (await service.ShiftAsync(theirs.Id)).SessionId.Should().Be(theirs.Id);

        var day = () => service.EndOfDayAsync(DateOnly.FromDateTime(Noon));
        await day.Should().ThrowAsync<PermissionDeniedException>();
    }

    /// One till, opened once, two cashiers taking turns at it: each one's
    /// own takings, and their cash, add up to the shift's.
    [Fact]
    public void Each_cashier_on_a_shared_till_has_their_own_takings()
    {
        static ShiftTransactionDto T(string kind, string who, decimal total, string tender) =>
            new(Noon, kind, "", who, total, 0m,
                new[] { new TenderTotalDto(tender, 1, total) }, Array.Empty<ShiftItemDto>());

        var byCashier = ShiftTally.ByCashier(new[]
        {
            T("Sale", "Mary", 20m, "Cash"),
            T("Script collection", "Mary", 45m, "Card"),
            T("Sale", "John", 15m, "Cash"),
            T("Refund", "John", -5m, "Cash"),
            T("Account payment", "John", 10m, "Cash")
        });

        byCashier.Select(c => (c.Name, c.Sales, c.Net, c.Cash)).Should().Equal(
            ("John", 1, 20m, 20m),
            ("Mary", 2, 65m, 20m));
    }

    /// A till cashed up today after opening yesterday must match the
    /// cash-up slip on today's end of day: the whole shift, not only
    /// today's slice.
    [Fact]
    public async Task The_end_of_day_matches_the_cash_up_slip_for_a_shift_closed_today()
    {
        var yesterday = Noon.AddDays(-1);
        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "3", Cashier,
                                       Money.Of(50m), yesterday);

        Sale Sell(decimal price, DateTime at)
        {
            var sale = Sale.Open(session.BranchId, session.WorkstationId, session.Id, Cashier, at);
            sale.AddLine(SaleLine.Create(Guid.NewGuid(), "Panado 500mg 20s", 1m, Money.Of(price), 15m,
                "A", PriceSource.StandardRetail));
            sale.TakePayments(new[] { SalePayment.Create(TenderType.Cash, Money.Of(price)) });
            sale.Finalise("R" + price, at);
            return sale;
        }

        var sales = new[] { Sell(30m, yesterday), Sell(12m, Noon) };
        session.Close(Cashier, Money.Of(92m), Noon.AddHours(2), "Balanced");

        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.ActiveBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(new[] { session });
        sessions.ReadAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        sessions.PayoutsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<TillPayout>());
        var saleRepo = Substitute.For<ISaleRepository>();
        saleRepo.ForSessionAsync(session.Id, Arg.Any<CancellationToken>()).Returns(sales);
        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(Array.Empty<SaleReturn>());
        var accounts = Substitute.For<IAccountRepository>();
        accounts.PaymentsForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AccountTransaction>());

        var service = new ShiftCashUpService(sessions, saleRepo, returns, accounts,
            new InMemoryUserRepository(), Substitute.For<IDispensingRepository>(),
            Substitute.For<IScriptRepository>(), new FakeCurrentUser { Id = Supervisor },
            PermissionsOf(Role.Standard.Administrator), new FakeClock(Noon), new PharmaXOptions());

        var slip = await service.ShiftAsync(session.Id);
        var today = await service.EndOfDayAsync(DateOnly.FromDateTime(Noon.ToLocalTime()));
        var before = await service.EndOfDayAsync(DateOnly.FromDateTime(yesterday.ToLocalTime()));

        today.Shifts.Should().ContainSingle();
        // Today's sales only - yesterday's stay on yesterday's End-of-Day.
        today.SalesTotal.Should().Be(12m);
        before.SalesTotal.Should().Be(30m);
        (today.SalesTotal + before.SalesTotal).Should().Be(slip.SalesTotal);

        // Counted / Expected match the cash-up slip (whole shift).
        today.Shifts[0].Declared.Should().Be(slip.Declared);
        today.Shifts[0].ExpectedTotal.Should().Be(slip.ExpectedTotal);
        today.Shifts[0].Variance.Should().Be(slip.Variance);
        before.Shifts[0].Declared.Should().BeNull();
    }

    /// A dispensary till left open from yesterday brought yesterday's
    /// takings into today's end of day too, so the day never added up.
    [Fact]
    public async Task The_end_of_day_counts_only_that_days_sales_from_a_till_left_open()
    {
        var yesterday = Noon.AddDays(-1);
        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "3", Cashier,
                                       Money.Of(50m), yesterday);

        Sale Sell(decimal price, DateTime at)
        {
            var sale = Sale.Open(session.BranchId, session.WorkstationId, session.Id, Cashier, at);
            sale.AddLine(SaleLine.Create(Guid.NewGuid(), "Panado 500mg 20s", 1m, Money.Of(price), 15m,
                "A", PriceSource.StandardRetail));
            sale.TakePayments(new[] { SalePayment.Create(TenderType.Cash, Money.Of(price)) });
            sale.Finalise("R" + price, at);
            return sale;
        }

        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.ActiveBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(new[] { session });
        sessions.ReadAsync(session.Id, Arg.Any<CancellationToken>()).Returns(session);
        sessions.PayoutsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<TillPayout>());
        var sales = Substitute.For<ISaleRepository>();
        sales.ForSessionAsync(session.Id, Arg.Any<CancellationToken>())
             .Returns(new[] { Sell(30m, yesterday), Sell(12m, Noon) });
        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(Array.Empty<SaleReturn>());
        var accounts = Substitute.For<IAccountRepository>();
        accounts.PaymentsForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AccountTransaction>());

        var service = new ShiftCashUpService(sessions, sales, returns, accounts,
            new InMemoryUserRepository(), Substitute.For<IDispensingRepository>(),
            Substitute.For<IScriptRepository>(), new FakeCurrentUser { Id = Supervisor },
            PermissionsOf(Role.Standard.Administrator), new FakeClock(Noon), new PharmaXOptions());

        var today = await service.EndOfDayAsync(DateOnly.FromDateTime(Noon.ToLocalTime()));
        today.SalesTotal.Should().Be(12m);
        today.SalesCount.Should().Be(1);

        var before = await service.EndOfDayAsync(DateOnly.FromDateTime(yesterday.ToLocalTime()));
        before.SalesTotal.Should().Be(30m);

        // The shift itself still counts the whole drawer.
        (await service.ShiftAsync(session.Id)).SalesTotal.Should().Be(42m);
    }

    /// Today by default; another day reads that day's shifts, midnight to
    /// midnight where the till is.
    [Fact]
    public async Task Cash_up_lists_the_shifts_of_the_day_chosen()
    {
        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.ActiveBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<TillSession>());

        var service = new ShiftCashUpService(sessions, Substitute.For<ISaleRepository>(),
            Substitute.For<IReturnRepository>(), Substitute.For<IAccountRepository>(),
            new InMemoryUserRepository(), Substitute.For<IDispensingRepository>(),
            Substitute.For<IScriptRepository>(), new FakeCurrentUser { Id = Cashier },
            PermissionsOf(Role.Standard.Cashier), new FakeClock(Noon), new PharmaXOptions());

        var earlier = new DateOnly(2026, 10, 20);
        await service.ShiftsAsync(earlier);
        await service.ShiftsAsync();

        var from = earlier.ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        await sessions.Received(1).ActiveBetweenAsync(from, from.AddDays(1), Arg.Any<CancellationToken>());

        var today = DateOnly.FromDateTime(Noon.ToLocalTime())
            .ToDateTime(TimeOnly.MinValue, DateTimeKind.Local).ToUniversalTime();
        await sessions.Received(1).ActiveBetweenAsync(today, today.AddDays(1), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(Role.Standard.Pharmacist)]
    [InlineData(Role.Standard.PharmacistAssistant)]
    [InlineData(Role.Standard.Administrator)]
    public async Task A_supervisor_cashes_up_any_cashiers_shift_and_the_day(string role)
    {
        var (mine, theirs, service) = Service(PermissionsOf(role));

        service.MayActForOthers.Should().BeTrue();
        (await service.ShiftsAsync()).Should().HaveCount(2);
        (await service.ShiftAsync(theirs.Id)).SessionId.Should().Be(theirs.Id);
        (await service.EndOfDayAsync(DateOnly.FromDateTime(Noon))).Shifts.Should().HaveCount(2);
        mine.Should().NotBeNull();
    }

    [Fact]
    public async Task A_cashier_cannot_close_another_cashiers_till_by_its_id()
    {
        var theirs = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "3", Guid.NewGuid(),
                                      Money.Of(50m), Noon);
        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.GetAsync(theirs.Id, Arg.Any<CancellationToken>()).Returns(theirs);

        var cashUp = new CashUpService(sessions, Substitute.For<ISaleRepository>(),
            Substitute.For<IReturnRepository>(), Substitute.For<IAccountRepository>(),
            Substitute.For<IJournalPoster>(), new FakeSessionContext(), new FakeAuditWriter(),
            Substitute.For<IOutbox>(), new FakeUnitOfWork(), new FakeCurrentUser { Id = Cashier },
            new FakeClock(Noon), PermissionsOf(Role.Standard.Cashier));

        var result = await cashUp.CloseAsync(null, theirs.Id);

        result.IsFailure.Should().BeTrue();
        result.Error.Code.Should().Be("POS-027");
        theirs.Status.Should().Be(TillSessionStatus.Open);
    }

    [Fact]
    public async Task A_pharmacist_assistant_closes_a_cashiers_till_on_their_behalf()
    {
        var theirs = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "3", Cashier,
                                      Money.Of(50m), Noon);
        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.GetAsync(theirs.Id, Arg.Any<CancellationToken>()).Returns(theirs);

        var sales = Substitute.For<ISaleRepository>();
        sales.ForSessionAsync(theirs.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<Sale>());
        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(theirs.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        var accounts = Substitute.For<IAccountRepository>();
        accounts.PaymentsForSessionAsync(theirs.Id, Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AccountTransaction>());

        var cashUp = new CashUpService(sessions, sales, returns, accounts,
            Substitute.For<IJournalPoster>(), new FakeSessionContext(), new FakeAuditWriter(),
            Substitute.For<IOutbox>(), new FakeUnitOfWork(), new FakeCurrentUser { Id = Supervisor },
            new FakeClock(Noon), PermissionsOf(Role.Standard.PharmacistAssistant));

        var result = await cashUp.CloseAsync("Cashier went home ill", theirs.Id);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        theirs.Status.Should().Be(TillSessionStatus.Closed);
        theirs.ClosedBy.Should().Be(Supervisor);
    }

    /// The one till is opened in the morning and the cashiers take turns:
    /// whoever is signed in at it at the end cashes it up from Cash Up.
    [Fact]
    public async Task The_cashier_signed_in_at_the_till_closes_it_whoever_opened_it()
    {
        var till = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1", Guid.NewGuid(),
                                    Money.Of(50m), Noon);
        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.GetAsync(till.Id, Arg.Any<CancellationToken>()).Returns(till);
        sessions.PayoutsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<TillPayout>());

        var sales = Substitute.For<ISaleRepository>();
        sales.ForSessionAsync(till.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<Sale>());
        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(till.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        var accounts = Substitute.For<IAccountRepository>();
        accounts.PaymentsForSessionAsync(till.Id, Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AccountTransaction>());

        var cashier = PharmaX.Domain.Security.User.Create("john", "John", "x", mustChangePassword: false);
        var atTheTill = new FakeCurrentUser { Id = cashier.Id, TillSessionId = till.Id };
        var shifts = new ShiftCashUpService(sessions, sales, returns, accounts,
            new InMemoryUserRepository(cashier), Substitute.For<IDispensingRepository>(),
            Substitute.For<IScriptRepository>(), atTheTill,
            PermissionsOf(Role.Standard.Cashier), new FakeClock(Noon), new PharmaXOptions());
        sessions.ActiveBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(new[] { till });

        var listed = (await shifts.ShiftsAsync()).Should().ContainSingle().Subject;
        listed.IsOwn.Should().BeTrue("they are on the till now");
        listed.CurrentCashier.Should().Be("John");

        var cashUp = new CashUpService(sessions, sales, returns, accounts,
            Substitute.For<IJournalPoster>(), new FakeSessionContext(), new FakeAuditWriter(),
            Substitute.For<IOutbox>(), new FakeUnitOfWork(), atTheTill,
            new FakeClock(Noon), PermissionsOf(Role.Standard.Cashier));

        var result = await cashUp.CloseAsync("Counted at the end of the day", till.Id);

        result.IsSuccess.Should().BeTrue(result.IsFailure ? result.Error.Message : "");
        till.Status.Should().Be(TillSessionStatus.Closed);
    }

    /// The picker starts on this PC's till, open before closed, named for
    /// whoever is signed in here; every other till of the day follows.
    [Fact]
    public async Task Cash_up_starts_on_this_PCs_till_named_for_whoever_is_signed_in()
    {
        var options = new PharmaXOptions();
        var thisPc = Guid.Parse(options.Workstation.WorkstationId);
        var mary = PharmaX.Domain.Security.User.Create("mary", "Mary", "x", mustChangePassword: false);
        var john = PharmaX.Domain.Security.User.Create("john", "John", "x", mustChangePassword: false);

        var morning = TillSession.Open(Guid.NewGuid(), thisPc, "1", mary.Id, Money.Of(50m), Noon.AddHours(-3));
        morning.Close(mary.Id, Money.Of(50m), Noon.AddHours(-1), null);
        var here = TillSession.Open(Guid.NewGuid(), thisPc, "1", mary.Id, Money.Of(50m), Noon);
        var johns = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "2", john.Id, Money.Of(50m), Noon);
        var other = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "3", Guid.NewGuid(), Money.Of(50m), Noon);

        var service = Over(new[] { other, johns, morning, here }, options,
            new FakeCurrentUser { Id = john.Id, TillSessionId = null }, mary, john);

        var shifts = await service.ShiftsAsync();

        shifts.Select(s => s.SessionId).Should().Equal(here.Id, morning.Id, johns.Id, other.Id);
        shifts[0].AtThisPc.Should().BeTrue();
        shifts[0].ToString().Should().StartWith("Till 1 (this PC) - John (opened by Mary)");
        shifts[2].AtThisPc.Should().BeFalse();
        shifts[2].IsOwn.Should().BeTrue();
    }

    /// Every till closed in the range, whoever's, latest first, with what
    /// was counted and why it was out; open ones and earlier ones left out.
    [Fact]
    public async Task The_history_lists_the_tills_closed_between_the_days_latest_first()
    {
        var mary = PharmaX.Domain.Security.User.Create("mary", "Mary", "x", mustChangePassword: false);

        var before = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1", Cashier, Money.Of(100m), Noon.AddDays(-5));
        before.Close(Cashier, Money.Of(100m), Noon.AddDays(-3), null);
        var yesterday = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "2", Cashier, Money.Of(100m), Noon.AddDays(-1));
        yesterday.Close(mary.Id, Money.Of(95m), Noon.AddDays(-1).AddHours(6), "Change short");
        var today = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1", Cashier, Money.Of(100m), Noon.AddHours(-2));
        today.Close(Cashier, Money.Of(100m), Noon, null);
        var open = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "3", Cashier, Money.Of(100m), Noon);

        var service = Over(new[] { before, yesterday, today, open }, new PharmaXOptions(),
            new FakeCurrentUser { Id = Cashier }, mary);

        var to = DateOnly.FromDateTime(Noon.ToLocalTime());
        var history = await service.HistoryAsync(to.AddDays(-1), to);

        history.Select(h => h.SessionId).Should().Equal(today.Id, yesterday.Id);
        history[1].ClosedByName.Should().Be("Mary");
        history[1].Declared.Should().Be(95m);
        history[1].Variance.Should().Be(-5m);
        history[1].VarianceReason.Should().Be("Change short");
    }

    /// A till opened last week and never cashed up is flagged at sign-in,
    /// by the local date: one opened at 00:30 today is today's, though
    /// its UTC time may fall on yesterday.
    [Theory]
    [InlineData(-7, 20, false, true)]   // opened 31/10 20:00, signed in on 07/11
    [InlineData(-1, 23, false, true)]   // last night's till, the next morning
    [InlineData(0, 0, true, false)]     // just after midnight, still today
    [InlineData(0, 8, false, false)]
    public void A_till_open_from_an_earlier_day_is_told_apart(int days, int hour,
        bool halfPast, bool earlier)
    {
        var today = new DateOnly(2026, 11, 7);
        var opened = today.AddDays(days).ToDateTime(new TimeOnly(hour, halfPast ? 30 : 0),
            DateTimeKind.Local).ToUniversalTime();
        var session = new PharmaX.Application.Tills.Dtos.TillSessionDto(
            Guid.NewGuid(), "1", "Thandi", opened, 100m, "Open", 0);

        session.OpenedBefore(today).Should().Be(earlier);
    }

    [Theory]
    [InlineData(Role.Standard.Cashier, true, false)]
    [InlineData(Role.Standard.Pharmacist, true, true)]
    [InlineData(Role.Standard.PharmacistAssistant, true, true)]
    [InlineData(Role.Standard.Administrator, true, true)]
    [InlineData(Role.Standard.StockController, false, false)]
    public void The_menu_offers_cash_up_and_the_end_of_day_to_the_right_people(
        string role, bool cashUp, bool endOfDay)
    {
        var permissions = PermissionsOf(role);

        ScreenAccess.CanOpen("cash-up", permissions).Should().Be(cashUp);
        ScreenAccess.CanOpen("end-of-day", permissions).Should().Be(endOfDay);
    }

    // ---- Helpers -----------------------------------------------------

    private static IPermissionService PermissionsOf(string role) =>
        new PermissionService(new FakeCurrentUser(),
            new FakePermissionRepository(StandardRoleGrants.For(role)), new FakeAuditWriter());

    private static (TillSession Mine, TillSession Theirs, ShiftCashUpService Service) Service(
        IPermissionService permissions)
    {
        var mine = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "1", Cashier, Money.Of(100m), Noon);
        var theirs = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "2", Guid.NewGuid(),
                                      Money.Of(100m), Noon);

        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.ActiveBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(new[] { mine, theirs });
        sessions.ReadAsync(mine.Id, Arg.Any<CancellationToken>()).Returns(mine);
        sessions.ReadAsync(theirs.Id, Arg.Any<CancellationToken>()).Returns(theirs);

        var sales = Substitute.For<ISaleRepository>();
        sales.ForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Sale>());
        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(Array.Empty<SaleReturn>());
        var accounts = Substitute.For<IAccountRepository>();
        accounts.PaymentsForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AccountTransaction>());

        var service = new ShiftCashUpService(sessions, sales, returns, accounts,
            new InMemoryUserRepository(), Substitute.For<IDispensingRepository>(),
            Substitute.For<IScriptRepository>(), new FakeCurrentUser { Id = Cashier },
            permissions, new FakeClock(Noon), new PharmaXOptions());

        return (mine, theirs, service);
    }

    /// Over these sessions, none with a sale, refund, payment or payout.
    private static ShiftCashUpService Over(IReadOnlyList<TillSession> tills, PharmaXOptions options,
        ICurrentUser user, params PharmaX.Domain.Security.User[] users)
    {
        var sessions = Substitute.For<ITillSessionRepository>();
        sessions.ActiveBetweenAsync(Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
                .Returns(tills);
        sessions.PayoutsAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<TillPayout>());
        var sales = Substitute.For<ISaleRepository>();
        sales.ForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<Sale>());
        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
               .Returns(Array.Empty<SaleReturn>());
        var accounts = Substitute.For<IAccountRepository>();
        accounts.PaymentsForSessionAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AccountTransaction>());

        return new ShiftCashUpService(sessions, sales, returns, accounts,
            new InMemoryUserRepository(users), Substitute.For<IDispensingRepository>(),
            Substitute.For<IScriptRepository>(), user, PermissionsOf(Role.Standard.Cashier),
            new FakeClock(Noon), options);
    }

    private static ReportProvenance Provenance() =>
        new("admin", Noon.ToLocalTime(), "End-of-day cash-up | test", "Every till is closed");

    private static IReadOnlyDictionary<string, object?> Row(params (string Key, object? Value)[] cells) =>
        cells.ToDictionary(c => c.Key, c => c.Value, StringComparer.Ordinal);

    private sealed class Letterhead : IReportBranding
    {
        public string? Logo { get; init; }
        public string? LogoPath => Logo;
        public string TradingName => "Bethrapha Pharmacy";
        public string Address => "28 Woodstock Road\nThorngrove, Bulawayo";
        public string VatNumber => "10012345";
        public string Telephone => "029 222 0000";
    }
}
