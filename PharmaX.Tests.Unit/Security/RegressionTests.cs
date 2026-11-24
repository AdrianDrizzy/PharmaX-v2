using FluentAssertions;
using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Dispensing.Dtos;
using PharmaX.Application.Repositories;
using PharmaX.Application.Sales;
using PharmaX.Application.Security;
using PharmaX.Application.Tills;
using PharmaX.Application.Tills.Dtos;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Domain.Exceptions;
using PharmaX.Domain.Patients;
using PharmaX.Domain.Primitives;
using PharmaX.Domain.Sales;
using PharmaX.Domain.Security;
using PharmaX.Tests.Unit.Fakes;

namespace PharmaX.Tests.Unit.Security;

/// <summary>
/// DEF-0006: the allergy screen understands drug classes and brands, and
/// does not stop medicines it has no reason to.
/// </summary>
public class AllergyScreenTests
{
    private static IReadOnlyList<AllergyMatch> Check(string allergy,
        string product, string? generic = null)
    {
        var profile = ClinicalProfile.For(Guid.NewGuid());
        profile.RecordAllergy(allergy, "Moderate");
        return profile.AllergiesAgainst(product, generic);
    }

    [Fact]
    public void A_penicillin_allergy_stops_amoxicillin() =>
        Check("Penicillin", "Amoxicillin 500mg capsules 21s")
            .Should().ContainSingle(m => m.IsHardStop
                                         && m.Kind == AllergyMatchKind.SameClass);

    [Theory]
    [InlineData("Penicillins", "Flucloxacillin 250mg capsules")]
    [InlineData("PCN", "Ampicillin 500mg")]
    [InlineData("amoxicillin", "Augmentin 625mg tablets")]
    [InlineData("Amoxil", "Amoxicillin 250mg suspension")]
    [InlineData("Sulfa drugs", "Co-trimoxazole 480mg tablets")]
    [InlineData("Aspirin", "Ibuprofen 400mg tablets")]
    [InlineData("Codeine", "Tramadol 50mg capsules")]
    [InlineData("Penicillin", "Penicillin V 250mg")]
    public void A_class_or_brand_allergy_is_a_hard_stop(string allergy,
                                                        string product) =>
        Check(allergy, product).Should().Contain(m => m.IsHardStop);

    [Fact]
    public void The_generic_name_is_matched_as_well_as_the_description() =>
        Check("Penicillin", "Moxypen 500 capsules", generic: null)
            .Should().Contain(m => m.IsHardStop, "Moxypen is amoxicillin");

    [Fact]
    public void A_penicillin_allergy_cautions_against_a_cephalosporin() =>
        Check("Penicillin", "Cephalexin 500mg capsules")
            .Should().ContainSingle(m =>
                m.Kind == AllergyMatchKind.CrossSensitivity && !m.IsHardStop);

    [Theory]
    [InlineData("Penicillin", "Metformin 850mg tablets 60s")]
    [InlineData("Penicillin tablets", "Paracetamol 500mg tablets")]
    [InlineData("Sulfa", "Panado 500mg tablets")]
    [InlineData("Latex", "Vitamin C 1000mg effervescent")]
    public void Unrelated_medicines_are_not_stopped(string allergy,
                                                    string product) =>
        Check(allergy, product).Should().BeEmpty();

    [Fact]
    public void IsAllergicTo_now_follows_the_class() =>
        new Func<bool>(() =>
        {
            var profile = ClinicalProfile.For(Guid.NewGuid());
            profile.RecordAllergy("Penicillin", "Severe");
            return profile.IsAllergicTo("Amoxicillin 500mg capsules 21s");
        })().Should().BeTrue();
}

/// <summary>
/// DEF-0007: a supervisor-authorised action is checked against the
/// supervisor's authority, and never against the operator's alone.
/// </summary>
public class AuthorisedByTests
{
    private readonly Guid _cashier = Guid.NewGuid();
    private readonly Guid _manager = Guid.NewGuid();
    private readonly Guid _otherCashier = Guid.NewGuid();
    private readonly FakeAuditWriter _audit = new();

    private IPermissionService Service(Guid signedIn) =>
        new PermissionService(new FakeCurrentUser { Id = signedIn },
            new PerUserPermissions(new Dictionary<Guid, string[]>
            {
                [_cashier] = StandardRoleGrants.For(Role.Standard.Cashier).ToArray(),
                [_otherCashier] = StandardRoleGrants.For(Role.Standard.Cashier).ToArray(),
                [_manager] = StandardRoleGrants.For(Role.Standard.Manager).ToArray()
            }),
            _audit);

    [Fact]
    public void A_manager_authorising_a_cashiers_refund_is_accepted()
    {
        var act = () => Service(_cashier)
            .DemandOrAuthorised(PermissionCode.ReturnAuthorise, _manager);

        act.Should().NotThrow();
    }

    /// A refund and a discount over the limit are authorised by a
    /// pharmacist, an assistant, a manager or the administrator alike.
    [Theory]
    [InlineData(Role.Standard.Pharmacist, PermissionCode.ReturnAuthorise)]
    [InlineData(Role.Standard.PharmacistAssistant, PermissionCode.ReturnAuthorise)]
    [InlineData(Role.Standard.Manager, PermissionCode.ReturnAuthorise)]
    [InlineData(Role.Standard.Administrator, PermissionCode.ReturnAuthorise)]
    [InlineData(Role.Standard.Pharmacist, PermissionCode.SaleDiscountAboveLimit)]
    [InlineData(Role.Standard.PharmacistAssistant, PermissionCode.SaleDiscountAboveLimit)]
    [InlineData(Role.Standard.Manager, PermissionCode.SaleDiscountAboveLimit)]
    [InlineData(Role.Standard.Administrator, PermissionCode.SaleDiscountAboveLimit)]
    public void The_senior_roles_authorise_a_cashiers_refund_and_discount(
        string role, string permission)
    {
        var authoriser = Guid.NewGuid();
        var service = new PermissionService(new FakeCurrentUser { Id = _cashier },
            new PerUserPermissions(new Dictionary<Guid, string[]>
            {
                [_cashier] = StandardRoleGrants.For(Role.Standard.Cashier).ToArray(),
                [authoriser] = StandardRoleGrants.For(role).ToArray()
            }),
            _audit);

        var act = () => service.DemandOrAuthorised(permission, authoriser);

        act.Should().NotThrow($"a {role} authorises {permission}");
    }

    [Fact]
    public void An_authoriser_without_the_permission_is_refused_and_recorded()
    {
        var act = () => Service(_cashier)
            .DemandOrAuthorised(PermissionCode.ReturnAuthorise, _otherCashier);

        act.Should().Throw<PermissionDeniedException>();
        _audit.Recorded(PermissionCode.ReturnAuthorise, "Refused")
            .Should().BeTrue();
    }

    [Fact]
    public void Nobody_authorises_their_own_action()
    {
        var act = () => Service(_cashier)
            .DemandOrAuthorised(PermissionCode.ReturnAuthorise, _cashier);

        act.Should().Throw<PermissionDeniedException>()
            .WithMessage("*own action*");
    }

    [Fact]
    public void No_authoriser_at_all_is_refused()
    {
        var act = () => Service(_cashier)
            .DemandOrAuthorised(PermissionCode.ReturnAuthorise, null);

        act.Should().Throw<PermissionDeniedException>();
    }

    [Fact]
    public void An_operator_holding_the_permission_needs_nobody()
    {
        var act = () => Service(_manager)
            .DemandOrAuthorised(PermissionCode.ReturnAuthorise, null);

        act.Should().NotThrow();
    }

    [Fact]
    public void A_flat_no_cannot_be_authorised_away()
    {
        // Stocktake capture is not a supervisor-escalatable permission.
        var act = () => Service(_cashier)
            .DemandOrAuthorised(PermissionCode.StockTakeCapture, _manager);

        act.Should().Throw<PermissionDeniedException>();
    }

    private sealed class PerUserPermissions : IPermissionRepository
    {
        private readonly Dictionary<Guid, string[]> _grants;

        public PerUserPermissions(Dictionary<Guid, string[]> grants) =>
            _grants = grants;

        public bool UserHas(Guid userId, string permissionCode) =>
            _grants.TryGetValue(userId, out var codes)
            && codes.Contains(permissionCode, StringComparer.Ordinal);

        public bool IsSupervisorEscalatable(string permissionCode) =>
            permissionCode is PermissionCode.ReturnAuthorise
                or PermissionCode.SaleDiscountAboveLimit;

        public IReadOnlyList<string> ForUser(Guid userId) =>
            _grants.GetValueOrDefault(userId) ?? Array.Empty<string>();
    }
}

/// <summary>
/// Printing: the printer is chosen honestly, and a failure is reported as
/// one.
/// </summary>
public class PrintingTests
{
    private static string? Healthy(string printer) => null;

    [Fact]
    public void No_printer_installed_is_a_plain_message()
    {
        var choice = PrinterResolver.Resolve(null, "receipt",
            Array.Empty<string>(), null, Healthy);

        choice.CanPrint.Should().BeFalse();
        choice.Printer.Should().BeNull();
        choice.Problem.Should().Contain("No printer is installed");
    }

    [Fact]
    public void The_printer_chosen_for_the_till_is_used()
    {
        var choice = PrinterResolver.Resolve("Till Roll", "receipt",
            new[] { "Office Laser", "Till Roll" }, "Office Laser", Healthy);

        choice.CanPrint.Should().BeTrue();
        choice.Printer.Should().Be("Till Roll");
        choice.Note.Should().BeNull();
    }

    [Fact]
    public void With_nothing_chosen_the_windows_default_is_used()
    {
        var choice = PrinterResolver.Resolve(null, "receipt",
            new[] { "Till Roll", "Office Laser" }, "Till Roll", Healthy);

        choice.Printer.Should().Be("Till Roll");
    }

    /// <remarks>
    /// At a till the Windows default is the receipt printer, and labels
    /// sent to it came out on the receipt roll. A label goes only to the
    /// printer chosen for labels, or nowhere, and says why.
    /// </remarks>
    [Fact]
    public void A_label_never_goes_to_the_windows_default()
    {
        var none = PrinterResolver.Resolve(null, "label",
            new[] { "Till Roll", "LX-350" }, "Till Roll", Healthy, orDefault: false);

        none.CanPrint.Should().BeFalse();
        none.Problem.Should().Be("No label printer is chosen for this computer, "
            + "so the label was not printed. Choose the label printer in "
            + "File > Printer Setup.");

        var gone = PrinterResolver.Resolve("LX-350", "label",
            new[] { "Till Roll" }, "Till Roll", Healthy, orDefault: false);

        gone.CanPrint.Should().BeFalse();
        gone.Printer.Should().BeNull();
        gone.Problem.Should().Contain("'LX-350', is not installed any more");

        PrinterResolver.Resolve("LX-350", "label", new[] { "Till Roll", "LX-350" },
            "Till Roll", Healthy, orDefault: false).Printer.Should().Be("LX-350");
    }

    [Fact]
    public void A_chosen_printer_that_has_gone_falls_back_and_says_so()
    {
        var choice = PrinterResolver.Resolve("Old Printer", "receipt",
            new[] { "Office Laser" }, "Office Laser", Healthy);

        choice.CanPrint.Should().BeTrue();
        choice.Printer.Should().Be("Office Laser");
        choice.Note.Should().Contain("Old Printer").And.Contain("not installed");
    }

    [Fact]
    public void An_offline_printer_is_not_printed_to()
    {
        var choice = PrinterResolver.Resolve("Till Roll", "receipt",
            new[] { "Till Roll" }, "Till Roll", _ => "is offline");

        choice.CanPrint.Should().BeFalse();
        choice.Problem.Should().Be("The receipt printer 'Till Roll' is offline.");
    }

    [Fact]
    public void A_failed_print_never_reads_as_printed()
    {
        var failed = PrintResult.Failed("Till Roll", "It jammed.");

        failed.Printed.Should().BeFalse();
        failed.Describe("The Z-read").Should()
            .Be("The Z-read did not print. It jammed.");

        PrintResult.Ok("Till Roll").Describe("The receipt")
            .Should().Be("The receipt printed on Till Roll.");
    }

    [Fact]
    public void A_label_carries_the_product_quantity_batches_and_expiry()
    {
        using var label = new LabelDocument(new LabelDto("Priya Naidoo",
            "Amoxicillin 500mg capsules 21s", 21m,
            "One capsule three times a day", "AMOX50-A",
            new DateOnly(2027, 1, 31), "Test Pharmacist", "SC-001-000003",
            new DateOnly(2026, 9, 23), new[] { "Keep in the fridge." }));

        var lines = label.Lines();

        lines[0].Should().StartWith("AMOXICILLIN 500MG CAPSULES 21S").And.EndWith("(21)");
        lines.Should().Contain(line => line.StartsWith("PRIYA NAIDOO", StringComparison.Ordinal));
        string.Join(" ", lines).Should()
            .Contain("ONE CAPSULE THREE TIMES A DAY KEEP IN THE FRIDGE.");
        string.Join(" ", lines).Should().Contain("EXP:31/01/2027");
        string.Join(" ", lines).Should().Contain("BN:AMOX50-A");
    }

    [Fact]
    public void A_long_receipt_description_wraps_rather_than_losing_its_end()
    {
        var pieces = ReceiptDocument.Wrap("Metformin 850mg tablets 60s x 30",
                                          24, 38);

        pieces.Should().Equal("Metformin 850mg tablets", "60s x 30");
        string.Join(" ", pieces).Should()
            .Be("Metformin 850mg tablets 60s x 30");

        ReceiptDocument.Wrap("Panado 500mg", 24, 38).Should()
            .Equal("Panado 500mg");
        ReceiptDocument.Wrap(new string('X', 30), 24, 38).Should()
            .Equal(new string('X', 24), new string('X', 6));
    }

    /// <summary>
    /// A line wider than the roll wraps instead of running off it, and a
    /// page too short for a line still moves on, so printing always ends.
    /// </summary>
    [Fact]
    public void A_plain_document_wraps_long_lines_and_always_finishes()
    {
        var header = new ReceiptHeaderDto("E2E Test Pharmacy", "", "", "",
            "", "Branch 1 Till 1", "test.cashier", new DateTime(2026, 9, 24));
        var document = new PrintableDocument("Quotation Q-001-000002",
            new[]
            {
                "A note far longer than the roll is wide, which used to be "
                + "drawn straight off the edge of the paper and lost.",
                "Next line"
            });

        using var bitmap = new System.Drawing.Bitmap(315, 1200);
        using var graphics = System.Drawing.Graphics.FromImage(bitmap);

        using (var roll = new PlainDocument(document, header))
            roll.RenderPage(graphics, new System.Drawing.Rectangle(10, 10, 295, 1180))
                .Should().BeFalse("it all fits one roll-length page");

        using var tiny = new PlainDocument(document, header);
        var pages = 1;
        while (tiny.RenderPage(graphics, new System.Drawing.Rectangle(10, 10, 295, 40))
               && pages < 20)
            pages++;

        pages.Should().BeLessThan(20, "every page prints at least one line");
    }

    /// <summary>
    /// On a till printer on Windows' Generic / Text Only driver a till-roll
    /// document goes as text, like the receipt: its columns kept as they
    /// were laid out, its title once, a long line wrapped, and every line
    /// ended so the last one prints.
    /// </summary>
    [Fact]
    public void A_plain_document_as_text_keeps_its_columns_and_wraps_long_lines()
    {
        var header = new ReceiptHeaderDto("E2E Test Pharmacy", "", "", "",
            "", "Branch 1 Till 1", "test.cashier", new DateTime(2026, 9, 24));
        var document = new PrintableDocument("Z-Read", new[]
        {
            "Z-Read",
            "Cash".PadRight(34) + "1,480.00",
            "A note far longer than the roll is wide, which used to be "
            + "drawn straight off the edge of the paper and lost."
        });

        using var roll = new PlainDocument(document, header);
        var bytes = roll.AsText();

        bytes.TakeLast(6).Should().Equal(new byte[] { 0x1B, 0x64, 0x04, 0x1D, 0x56, 0x01 },
            "the document is fed past the tear bar and cut");

        var lines = System.Text.Encoding.ASCII.GetString(bytes[5..^6])
            .Replace("\u001BE\u0001", "", StringComparison.Ordinal)
            .Replace("\u001BE\u0000", "", StringComparison.Ordinal)
            .Split('\n');

        lines[^1].Should().BeEmpty("every line ends in a line feed");
        lines[..^1].Should().OnlyContain(line => line.Length <= SlipLayout.Width);

        var texts = lines[..^1].Select(line => line.TrimEnd()).ToList();

        texts.Count(line => line == "Z-Read").Should().Be(1);
        texts.Should().ContainInOrder(
            "Cash".PadRight(34) + "1,480.00",
            "A note far longer than the roll is wide,");
        texts[^1].Should().EndWith("paper and lost.");
    }

    [Theory]
    [InlineData(@"C:\out\quote.csv", @"C:\out\quote.xlsx")]
    [InlineData(@"C:\out\quote", @"C:\out\quote.xlsx")]
    [InlineData(@"C:\out\Quote.XLSX", @"C:\out\Quote.XLSX")]
    public void An_export_is_always_saved_as_the_workbook_it_is(string chosen,
                                                                 string saved) =>
        PharmaX.Desktop.Mvp.GridExport.WorkbookPath(chosen).Should().Be(saved);

    [Fact]
    public void The_z_read_prints_expected_declared_and_variance()
    {
        var session = new TillSessionDto(Guid.NewGuid(), "1", "Cashier One",
            new DateTime(2026, 9, 23, 6, 0, 0, DateTimeKind.Utc), 500m,
            "Open", 1);

        var declaration = new DeclarationDto(session.Id, 1480m, -20m,
            new[] { new VarianceLineDto("Cash", 1500m, 1480m, -20m, true) });

        var z = TillReads.ZRead(session, declaration, "Change short",
                                new DateTime(2026, 9, 23, 18, 0, 0));

        z.Title.Should().Be("Z-read");
        z.Columns.Should().Be(32, "a 42-column line loses its right side on the till roll");
        z.Lines.Should().OnlyContain(l => l.Length <= 32);
        var text = string.Join('\n', z.Lines);
        text.Should().Contain("1,500.00").And.Contain("1,480.00").And.Contain("-20.00");
        text.Should().NotContain("EcoCash").And.NotContain("Card");
        z.Lines.Should().Contain("Variance reason: Change short");
    }
}

/// <summary>
/// The till's own rules: voided lines, change from cash, holding a basket.
/// </summary>
public class TillRuleTests
{
    private static Sale OpenSale() =>
        Sale.Open(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                  DateTime.UtcNow);

    private static SaleLine Line(decimal price, decimal quantity) =>
        SaleLine.Create(Guid.NewGuid(), "Item", quantity, Money.Of(price), 15m,
                        "A", PriceSource.StandardRetail);

    /// <summary>DEF-0016.</summary>
    [Fact]
    public void A_voided_line_counts_in_neither_the_subtotal_nor_the_discount()
    {
        var sale = OpenSale();
        var first = sale.AddLine(Line(34.95m, 2m));
        sale.AddLine(Line(39.90m, 1m));
        sale.AddLine(Line(49.99m, 1m));

        first.ApplyDiscount(Money.Of(20m), 100m, null);
        sale.VoidLine(1, "Changed mind", Guid.NewGuid());

        sale.Subtotal.Amount.Should().Be(89.89m);
        sale.DiscountTotal.Amount.Should().Be(0m);
        (sale.Subtotal - sale.DiscountTotal).Should().Be(sale.Total);
    }

    /// <summary>DEF-0019: 100 for 80 is 20 change, and 80 stays in the drawer.</summary>
    [Fact]
    public void Cash_over_the_total_is_change()
    {
        var sale = OpenSale();
        sale.AddLine(Line(80m, 1m));

        sale.TakePayments(new[] { SalePayment.Create(TenderType.Cash,
                                                     Money.Of(100m)) });
        sale.Finalise("S-001-000001", DateTime.UtcNow);

        sale.Change.Amount.Should().Be(20m);
        sale.Tendered.Amount.Should().Be(100m);
        sale.AmountPaid.Amount.Should().Be(80m);
    }

    [Fact]
    public void Split_tender_takes_the_change_from_the_cash()
    {
        var sale = OpenSale();
        sale.AddLine(Line(80m, 1m));

        sale.TakePayments(new[]
        {
            SalePayment.Create(TenderType.Card, Money.Of(50m)),
            SalePayment.Create(TenderType.Cash, Money.Of(50m))
        });

        sale.Change.Amount.Should().Be(20m);
        sale.Payments.Single(p => p.Tender == TenderType.Card).Amount.Amount
            .Should().Be(50m);
        sale.Payments.Single(p => p.Tender == TenderType.Cash).Amount.Amount
            .Should().Be(30m);
    }

    [Fact]
    public void A_card_cannot_be_over_tendered()
    {
        var sale = OpenSale();
        sale.AddLine(Line(80m, 1m));

        var act = () => sale.TakePayments(new[]
            { SalePayment.Create(TenderType.Card, Money.Of(100m)) });

        act.Should().Throw<DomainException>().WithMessage("*only cash*");
    }

    /// <summary>DEF-0014.</summary>
    [Fact]
    public void A_held_sale_resumes_on_another_session_with_everything_on_it()
    {
        var sale = OpenSale();
        var line = sale.AddLine(Line(50m, 2m));
        line.ApplyDiscount(Money.Of(5m), 100m, null);

        sale.Hold();
        sale.Status.Should().Be(SaleStatus.Held);

        var act = () => sale.AddLine(Line(1m, 1m));
        act.Should().Throw<DomainException>("a held sale is parked");

        var session = Guid.NewGuid();
        var till = Guid.NewGuid();
        var cashier = Guid.NewGuid();
        sale.Resume(session, till, cashier);

        sale.Status.Should().Be(SaleStatus.Open);
        sale.TillSessionId.Should().Be(session);
        sale.WorkstationId.Should().Be(till);
        sale.OperatorId.Should().Be(cashier);
        sale.Total.Amount.Should().Be(95m);
    }

    /// <remarks>
    /// The assistant at the dispensary computer makes the basket up and
    /// holds it. It is in no till session, and no money can be taken on
    /// it, until a cashier recalls it at the till with the drawer.
    /// </remarks>
    [Fact]
    public void A_basket_made_up_for_the_cashier_takes_no_money_until_a_till_recalls_it()
    {
        var sale = Sale.Prepare(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
                                DateTime.UtcNow);
        sale.AddLine(Line(10m, 1m));

        sale.TillSessionId.Should().Be(Guid.Empty, "it is in neither till's session");

        var pay = () => sale.TakePayments(new[]
            { SalePayment.Create(TenderType.Cash, Money.Of(10m)) });
        pay.Should().Throw<DomainException>().WithMessage("*cashier*");

        sale.Hold();

        var session = Guid.NewGuid();
        sale.Resume(session, Guid.NewGuid(), Guid.NewGuid());

        sale.TillSessionId.Should().Be(session);
        pay.Should().NotThrow();
    }

    [Fact]
    public void An_empty_sale_cannot_be_held()
    {
        var act = () => OpenSale().Hold();
        act.Should().Throw<DomainException>();
    }

    [Fact]
    public void A_quotation_is_sold_only_once()
    {
        var quotation = Quotation.Open("Q-001-000001", "Walk-in",
            Guid.NewGuid(), new DateOnly(2026, 9, 1), 30);
        quotation.AddLine(Guid.NewGuid(), "Item", 1m, Money.Of(10m), "A");
        quotation.Issue();

        quotation.AcceptInto(Guid.NewGuid(), new DateOnly(2026, 9, 2));

        var again = () => quotation.AcceptInto(Guid.NewGuid(),
                                               new DateOnly(2026, 9, 3));
        again.Should().Throw<DomainException>();
    }

    [Theory]
    [InlineData(53, 60, 60, false, null)]
    [InlineData(53, 53, 53, false, null)]
    [InlineData(53, 40, 40, false, "outstanding")]
    [InlineData(53, 60, 0, false, "only cash")]
    [InlineData(53, 60, 60, true, "more than is owed")]
    public void The_payment_screen_accepts_change_only_from_cash(
        decimal due, decimal tendered, decimal cash, bool refund,
        string? problem)
    {
        var refused = frmPayment.RefuseToFinish(due - tendered, cash, refund);

        if (problem is null) refused.Should().BeNull();
        else refused.Should().Contain(problem);
    }
}

/// <summary>DEF-0010.</summary>
public class DocumentPrefixTests
{
    [Fact]
    public void Every_series_has_its_own_prefix() =>
        DocumentSeries.All.Select(DocumentSeries.PrefixOf)
            .Should().OnlyHaveUniqueItems();

    [Fact]
    public void Sales_and_patient_files_keep_the_prefix_already_in_the_drawer()
    {
        DocumentSeries.PrefixOf(DocumentSeries.Sale).Should().Be("S");
        DocumentSeries.PrefixOf(DocumentSeries.Patient).Should().Be("P");
        DocumentSeries.PrefixOf(DocumentSeries.Script).Should().Be("SC");
        DocumentSeries.PrefixOf(DocumentSeries.PurchaseOrder).Should().Be("PO");
        DocumentSeries.PrefixOf(DocumentSeries.Stocktake).Should().Be("ST");
    }
}

/// <summary>DEF-0021: the price follows the quantity dispensed.</summary>
public class DispensingPriceTests
{
    [Fact]
    public void A_pack_of_21_dispensed_as_7_costs_a_third()
    {
        var product = PharmaX.Domain.Inventory.Product.Create("AMOX50",
            "Amoxicillin 500mg capsules 21s", Money.Of(126m), schedule: 4);
        product.SetUnitsPerPack(21m);

        product.PriceForUnits(product.RetailPrice, 7m).Amount.Should().Be(42m);
        product.PriceForUnits(product.RetailPrice, 21m).Amount.Should().Be(126m);
    }

    [Fact]
    public void Without_a_pack_size_each_unit_is_a_pack()
    {
        var product = PharmaX.Domain.Inventory.Product.Create("INSUL1",
            "Insulin pen refill 3ml", Money.Of(412.30m), schedule: 4);

        product.PriceForUnits(product.RetailPrice, 2m).Amount
            .Should().Be(824.60m);
    }
}
