using FluentAssertions;
using NSubstitute;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Finance;
using PharmaX.Application.Reporting;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Domain.Tills;
using PharmaX.Reporting.Definitions;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Application;

/// <summary>
/// Money out of the drawer - drawings, lunch, other expenses - in the
/// cash-up's arithmetic, on the slip, on the X-read and the Z-read, in the
/// exports, and on screen before any of it prints (23 October 2026).
/// </summary>
public class TillPayoutTests
{
    private static readonly DateTime Noon = new(2026, 10, 23, 10, 0, 0, DateTimeKind.Utc);
    private static readonly Guid Cashier = Guid.NewGuid();

    /// <summary>
    /// A float of 100 and a 50.00 cash sale; 12.50 out for lunch (a note
    /// short enough to sit on one line beside its amount), 30.00
    /// drawn by the owner, and 4.00 for bread voided as entered twice.
    /// </summary>
    private static (TillSession Session, List<Sale> Sales, List<TillPayout> Payouts) Shift()
    {
        var session = TillSession.Open(Guid.NewGuid(), Guid.NewGuid(), "2", Cashier,
                                       Money.Of(100m), Noon);

        var sale = Sale.Open(session.BranchId, session.WorkstationId, session.Id, Cashier, Noon);
        sale.AddLine(SaleLine.Create(Guid.NewGuid(), "Panado 500mg 20s", 1m, Money.Of(50m), 15m,
            "A", PriceSource.StandardRetail));
        sale.TakePayments(new[] { SalePayment.Create(TenderType.Cash, Money.Of(50m)) });
        sale.Finalise("R1", Noon);

        var bread = TillPayout.Record(session.Id, PayoutKind.Expense, Money.Of(4m), "Bread",
                                      Cashier, Noon.AddMinutes(30));
        bread.Void(Cashier, Noon.AddMinutes(31), "Entered twice");

        return (session, new List<Sale> { sale }, new List<TillPayout>
        {
            TillPayout.Record(session.Id, PayoutKind.Lunch, Money.Of(12.50m),
                "Sandwiches", Cashier, Noon.AddHours(2)),
            TillPayout.Record(session.Id, PayoutKind.Drawings, Money.Of(30m), "Owner",
                Cashier, Noon.AddHours(3)),
            bread
        });
    }

    private static ShiftCashUpDto Tally()
    {
        var (session, sales, payouts) = Shift();

        return ShiftTally.Build(session, sales, Array.Empty<SaleReturn>(),
            Array.Empty<AccountTransaction>(),
            new Dictionary<Guid, string> { [Cashier] = "Thandi Cashier" },
            new Dictionary<Guid, string>(), payouts);
    }

    [Fact]
    public void Money_paid_out_comes_off_the_cash_in_the_drawer_and_not_the_takings()
    {
        var shift = Tally();

        shift.PaidOut.Should().Be(42.50m, "the voided bread counts for nothing");
        shift.ExpectedCash.Should().Be(107.50m, "100 float + 50 cash - 42.50 out");
        shift.ExpectedTotal.Should().Be(107.50m);
        shift.Tenders.Single(t => t.Tender == "Cash").Amount.Should().Be(50m,
            "the tenders are what was taken");
        shift.NetTakings.Should().Be(50m);
        shift.Payouts.Should().HaveCount(3);

        shift.Transactions.Where(t => t.Kind == "Paid out")
            .Select(t => (t.Number, t.Total, t.OperatorName))
            .Should().Equal(("Lunch: Sandwiches", -12.50m, "Thandi Cashier"),
                            ("Drawings: Owner", -30m, "Thandi Cashier"));
    }

    [Fact]
    public void The_slip_lists_what_came_out_and_still_fits_the_roll()
    {
        var slip = CashUpSlip.Document(Tally(), everyTransaction: false, Noon.ToLocalTime());

        slip.Lines.Should().OnlyContain(l => l.Length <= 32);
        slip.Lines.Should().Contain("Paid out of the drawer");
        slip.Lines.Should().Contain(l => l.Contains("Lunch: Sandwiches") && l.EndsWith("-12.50"));
        slip.Lines.Should().Contain(l => l.Contains("Drawings: Owner") && l.EndsWith("-30.00"));
        slip.Lines.Should().Contain(l => l.StartsWith("PAID OUT") && l.EndsWith("-42.50"));
        slip.Lines.Should().Contain(l => l.StartsWith("CASH IN DRAWER") && l.EndsWith("107.50"));
        slip.Lines.Should().NotContain(l => l.Contains("Bread"));
    }

    [Fact]
    public void A_shift_with_nothing_paid_out_prints_as_it_did()
    {
        var (session, sales, _) = Shift();
        var shift = ShiftTally.Build(session, sales, Array.Empty<SaleReturn>(),
            Array.Empty<AccountTransaction>(), new Dictionary<Guid, string>(),
            new Dictionary<Guid, string>());

        shift.PaidOut.Should().Be(0m);
        shift.ExpectedCash.Should().Be(150m);

        var slip = CashUpSlip.Document(shift, everyTransaction: true, Noon.ToLocalTime());
        slip.Lines.Should().NotContain(l => l.Contains("Paid out", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void The_z_read_and_the_x_read_say_where_the_money_went()
    {
        var payouts = Tally().Payouts!;
        var session = new TillSessionDto(Guid.NewGuid(), "2", "Thandi Cashier", Noon, 100m,
                                         "Open", 0);

        var z = TillReads.ZRead(session, new DeclarationDto(session.Id, 107.50m, 0m, new[]
        {
            new VarianceLineDto("Cash", 107.50m, 107.50m, 0m, false)
        }), null, Noon.ToLocalTime(), payouts);

        z.Lines.Should().OnlyContain(l => l.Length <= 42);
        z.Lines.Should().Contain("Paid out of the drawer");
        z.Lines.Should().Contain(l => l.Contains("Lunch: Sandwiches") && l.EndsWith("-12.50"));
        z.Lines.Should().Contain(l => l.StartsWith("Paid out") && l.EndsWith("-42.50"));
        z.Lines.Should().NotContain(l => l.Contains("Bread"));

        var x = TillReads.XRead(session, new XReadDto(session.Id, Noon, 1, new[]
        {
            new TenderExpectationDto("Cash", 107.50m, 1)
        }, 107.50m), payouts);

        x.Lines.Should().Contain(l => l.Contains("Drawings: Owner") && l.EndsWith("-30.00"));
        x.Lines.Should().Contain(l => l.StartsWith("SALES TOTAL") && l.EndsWith("50.00"));
    }

    /// <summary>
    /// Drawer 367, float 225, drawings 24, declared cash 354.
    /// Sales are the drawer less the float, with the drawings added back.
    /// </summary>
    [Fact]
    public void The_z_read_sales_tally_matches_the_day_s_sales()
    {
        var session = new TillSessionDto(Guid.NewGuid(), "3", "BUKHOSI MOYO",
            new DateTime(2026, 11, 21, 23, 24, 0, DateTimeKind.Utc), 225m, "Closed", 0);

        var declaration = new DeclarationDto(session.Id, 354m, -13m, new[]
        {
            new VarianceLineDto("Cash", 367m, 354m, -13m, true)
        });

        var closed = new DateTime(2026, 11, 21, 18, 23, 0);
        var payouts = new[]
        {
            new PayoutDto(Guid.NewGuid(), session.Id, PayoutKind.Drawings, 21m, "SIR",
                closed.AddMinutes(-6), "bk"),
            new PayoutDto(Guid.NewGuid(), session.Id, PayoutKind.Drawings, 3m, "MAAM",
                closed.AddMinutes(-5), "bk")
        };

        var z = TillReads.ZRead(session, declaration, "SHORTFALL", closed, payouts);

        z.Lines.Should().OnlyContain(l => l.Length <= 32);
        z.Lines.Should().Contain(l => l.StartsWith("SALES TOTAL") && l.EndsWith("166.00"),
            "367 in the drawer, less the 225 float, plus the 24 paid out");
        z.Lines.Should().Contain(l => l.StartsWith("COUNTED") && l.EndsWith("153.00"),
            "354 declared, less the 225 float, plus the 24 paid out");
        z.Lines.Should().Contain(l => l.StartsWith("DIFFERENCE") && l.EndsWith("-13.00"));
        z.Lines.Should().Contain("Sales tally");
        z.Lines.Should().Contain("Paid out of the drawer");
        z.Lines.Should().Contain("Variance reason: SHORTFALL");
        z.Lines.Should().Contain(l => l.Contains("Less float") && l.EndsWith("-225.00"));
        z.Lines.Should().Contain(l => l.Contains("Add paid out") && l.EndsWith("24.00"));
        z.Lines.Should().NotContain("EcoCash");
        z.Lines.Should().NotContain("Card");
    }

    [Fact]
    public void A_tender_with_an_amount_is_printed_and_a_zero_one_is_not()
    {
        var session = new TillSessionDto(Guid.NewGuid(), "3", "BUKHOSI MOYO",
            new DateTime(2026, 11, 21, 23, 24, 0, DateTimeKind.Utc), 225m, "Closed", 0);

        var z = TillReads.ZRead(session, new DeclarationDto(session.Id, 594m, -18.50m, new[]
        {
            new VarianceLineDto("Cash", 367m, 354m, -13m, true),
            new VarianceLineDto("EcoCash", 80m, 80m, 0m, false),
            new VarianceLineDto("Card", 0m, 0m, 0m, false),
            new VarianceLineDto("MedicalAid", 120m, 120m, 0m, false)
        }), null, new DateTime(2026, 11, 21, 20, 23, 0));

        var text = string.Join('\n', z.Lines);
        text.Should().Contain("EcoCash").And.Contain("Medical aid").And.Contain("80.00").And.Contain("120.00");
        text.Should().NotContain("Card");
        z.Lines.Should().Contain(l => l.StartsWith("SALES TOTAL") && l.EndsWith("342.00"));
    }

    [Fact]
    public async Task The_count_expects_the_cash_less_what_was_paid_out()
    {
        var (session, sales, payouts) = Shift();

        var repository = Substitute.For<ITillSessionRepository>();
        repository.CurrentAsync(Arg.Any<CancellationToken>()).Returns(session);
        repository.PayoutsAsync(session.Id, Arg.Any<CancellationToken>()).Returns(payouts);
        repository.DenominationsAsync(Arg.Any<string?>(), Arg.Any<CancellationToken>())
                  .Returns(Denomination.For(Currencies.Usd).ToList());

        var saleRepository = Substitute.For<ISaleRepository>();
        saleRepository.ForSessionAsync(session.Id, Arg.Any<CancellationToken>()).Returns(sales);
        var returns = Substitute.For<IReturnRepository>();
        returns.ForSessionAsync(session.Id, Arg.Any<CancellationToken>()).Returns(Array.Empty<SaleReturn>());
        var accounts = Substitute.For<IAccountRepository>();
        accounts.PaymentsForSessionAsync(session.Id, Arg.Any<CancellationToken>())
                .Returns(Array.Empty<AccountTransaction>());

        var cashUp = new CashUpService(repository, saleRepository, returns, accounts,
            Substitute.For<IJournalPoster>(), new FakeSessionContext(), new FakeAuditWriter(),
            Substitute.For<IOutbox>(), new FakeUnitOfWork(), new FakeCurrentUser { Id = Cashier },
            new FakeClock(Noon), Permissions(Role.Standard.Cashier));

        // 107.50 counted: a $100, a $5, two $1 and two 25c.
        var declared = await cashUp.DeclareAsync(new Dictionary<string, int>
        {
            ["$100"] = 1, ["$5"] = 1, ["$1"] = 2, ["25c"] = 2
        });

        var cash = declared.Variances.Single(v => v.TenderType == "Cash");
        cash.Expected.Should().Be(107.50m);
        cash.Variance.Should().Be(0m, "the drawer holds exactly what it should");
    }

    [Fact]
    public void The_exports_carry_the_money_paid_out()
    {
        var shift = Tally();
        var provenance = new ReportProvenance("admin", Noon.ToLocalTime(), "test", "test");

        var report = CashUpReports.Shift(shift, provenance, everyTransaction: false);
        report.Figures.Should().Contain(f => f.Label == "Paid out" && f.Value == (-42.50m).ToString("N2"));

        var section = report.Sections.Should().ContainSingle(s => s.Title == "Paid out of the drawer").Subject;
        section.Rows.Should().HaveCount(3);
        section.Rows.Select(r => r["Status"] as string).Should().Contain("Voided: Entered twice");

        var day = CashUpReports.EndOfDay(new EndOfDayDto(DateOnly.FromDateTime(Noon), new[] { shift }),
                                         provenance);
        day.Figures.Should().Contain(f => f.Label == "Paid out");
        day.Rows.Single()["PaidOut"].Should().Be(-42.50m);
        day.Sections.Select(s => s.Title).Should()
           .Equal("By tender", "By cashier", "Paid out of the drawer", "Every transaction");
    }

    /// <summary>
    /// What the preview shows is what the printer is sent: the slip under
    /// its centred header at thirty-two across, a read at forty-two.
    /// </summary>
    [Fact]
    public void The_preview_lays_the_slip_and_the_reads_out_as_they_print()
    {
        var header = new ReceiptHeaderDto("Bethrapha Pharmacy", "", "28 Woodstock Road", "10012345",
            "", "Branch 1 Till 2", "thandi", Noon);

        var (slipRows, slipColumns) = DocumentPrinter.Layout(
            CashUpSlip.Document(Tally(), everyTransaction: true, Noon.ToLocalTime()), header);

        slipColumns.Should().Be(32);
        slipRows[0].Text.Should().Be("Bethrapha Pharmacy");
        slipRows[0].Centred.Should().BeTrue();
        slipRows.Should().OnlyContain(r => r.Text.Length <= 32);
        slipRows.Should().Contain(r => r.Text.StartsWith("PAID OUT") && r.Style == ReceiptStyle.Bold);

        var session = new TillSessionDto(Guid.NewGuid(), "2", "Thandi", Noon, 100m, "Open", 0);
        var (readRows, readColumns) = DocumentPrinter.Layout(TillReads.XRead(session,
            new XReadDto(session.Id, Noon, 1, new[] { new TenderExpectationDto("Cash", 1m, 1) }, 1m),
            Tally().Payouts), header);

        readColumns.Should().Be(42);
        readRows.Should().OnlyContain(r => r.Text.Length <= 42);
        readRows.Should().Contain(r => r.Text == "X-read 1" && r.Style == ReceiptStyle.Title);
        readRows.Should().Contain(r => r.Text.Contains("Lunch: Sandwiches"));
    }

    [Fact]
    public void A_payout_needs_an_amount_and_a_note_and_is_voided_once()
    {
        var session = Guid.NewGuid();

        var noAmount = () => TillPayout.Record(session, PayoutKind.Lunch, Money.Of(0m), "Lunch",
                                               Cashier, Noon);
        var noNote = () => TillPayout.Record(session, PayoutKind.Lunch, Money.Of(5m), "  ",
                                             Cashier, Noon);

        noAmount.Should().Throw<PharmaX.Domain.Exceptions.DomainException>();
        noNote.Should().Throw<PharmaX.Domain.Exceptions.DomainException>();

        var payout = TillPayout.Record(session, PayoutKind.Lunch, Money.Of(5m), "Lunch",
                                       Cashier, Noon);
        payout.Void(Cashier, Noon, "Mistake");

        var again = () => payout.Void(Cashier, Noon, "Again");
        again.Should().Throw<PharmaX.Domain.Exceptions.DomainException>();
    }

    private static IPermissionService Permissions(string role) =>
        new PermissionService(new FakeCurrentUser(),
            new FakePermissionRepository(StandardRoleGrants.For(role)), new FakeAuditWriter());
}
