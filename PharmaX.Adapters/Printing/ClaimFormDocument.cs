using System.Drawing.Drawing2D;
using System.Drawing.Printing;
using System.Globalization;
using Microsoft.Extensions.Logging;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Claims;
using PharmaX.Application.Sales;

namespace PharmaX.Adapters.Printing;

/// <summary>
/// The certified copy of the doctor's prescription and medical aid drug
/// claim form, on A4: the paper the member signs and the scheme pays
/// against.
/// </summary>
/// <remarks>
/// Laid out after the pre-printed claim forms pharmacies already post to
/// the schemes, so a claims clerk finds every box where they expect it.
/// The front carries the claim; the sections the member completes are
/// shaded blue and the scheme's own columns grey, as on the printed
/// stationery. The back carries the returns notice and the allergy and
/// condition checklist the patient ticks. Headed with the pharmacy's name,
/// address and numbers in words and no logo, as the receipt is.
///
/// Drawn with GDI+ in hundredths of an inch, like the receipt and the
/// label, rather than through the PDF exporter: it goes to a printer, and
/// a PDF would have to be opened in something else before it could be.
/// </remarks>
public sealed class ClaimFormDocument : PrintDocument
{
    private readonly ClaimFormDto _form;
    private int _page;

    private readonly Font _name = new("Arial", 13f, FontStyle.Bold);
    private readonly Font _title = new("Arial", 11.5f, FontStyle.Bold);
    private readonly Font _label = new("Arial", 6.5f);
    private readonly Font _labelBold = new("Arial", 6.5f, FontStyle.Bold);
    private readonly Font _value = new("Arial", 9f);
    private readonly Font _valueBold = new("Arial", 9f, FontStyle.Bold);
    private readonly Font _cell = new("Arial", 8f);
    private readonly Font _small = new("Arial", 6f);
    private readonly Font _counselTitle = new("Arial", 14f, FontStyle.Bold);
    private readonly Font _counsel = new("Arial", 9.5f);
    private readonly Font _heading = new("Arial", 15f, FontStyle.Bold);

    private readonly Pen _line = new(Color.Black, 1f);
    private readonly Pen _cut = new(Color.Black, 1f) { DashStyle = DashStyle.Dash };

    /// The member's sections, as the stationery shades them.
    private readonly Brush _member = new SolidBrush(Color.FromArgb(221, 231, 243));

    /// The scheme's sections, for official use only.
    private readonly Brush _official = new SolidBrush(Color.FromArgb(224, 224, 224));

    public ClaimFormDocument(ClaimFormDto form)
    {
        _form = form;
        ApplyClaimPage(DefaultPageSettings);
    }

    /// <summary>
    /// The chosen claim-form page size. Defaults to A4.
    /// </summary>
    internal static void ApplyClaimPage(PageSettings page)
    {
        ArgumentNullException.ThrowIfNull(page);

        var layout = PrintLayouts.Claim;
        var width = layout.WidthHundredths > 0 ? layout.WidthHundredths : 827;
        var height = layout.HeightHundredths > 0 ? layout.HeightHundredths : 1169;

        page.PaperSize = new PaperSize(
            $"Claim {PrintLayouts.Describe(layout)}", width, height);
        page.Landscape = false;
        page.Margins = layout.HasCustomPadding
            ? layout.ToMargins(40)
            : new Margins(40, 40, 35, 35);
    }

    public const int Pages = 2;

    protected override void OnBeginPrint(PrintEventArgs e)
    {
        base.OnBeginPrint(e);
        _page = 0;
        ApplyClaimPage(DefaultPageSettings);

        // Prefer the driver's named A4 / Letter when the layout matches
        // those stock sizes, so drivers that refuse a custom paper still
        // print. Custom sizes keep the layout's own paper.
        try
        {
            var layout = PrintLayouts.Claim;
            PaperKind? kind = layout.PresetId switch
            {
                "claim-a4" => PaperKind.A4,
                "claim-letter" => PaperKind.Letter,
                _ => null
            };

            if (kind is null) return;

            var match = PrinterSettings.PaperSizes.Cast<PaperSize>()
                .FirstOrDefault(p => p.Kind == kind);

            if (match is not null) DefaultPageSettings.PaperSize = match;
        }
        catch (InvalidPrinterException)
        {
            // Printed on the size ApplyClaimPage set.
        }
    }

    protected override void OnPrintPage(PrintPageEventArgs e)
    {
        base.OnPrintPage(e);
        ArgumentNullException.ThrowIfNull(e);

        e.HasMorePages = RenderPage(e.Graphics!, e.MarginBounds, _page++);
    }

    /// <summary>
    /// Draws one page and says whether there is another. Separate from
    /// OnPrintPage so a test can render it to a bitmap and look at it.
    /// </summary>
    internal bool RenderPage(Graphics g, Rectangle bounds, int page)
    {
        ArgumentNullException.ThrowIfNull(g);

        g.SmoothingMode = SmoothingMode.None;
        g.TextRenderingHint =
            System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        if (page == 0)
        {
            Front(g, bounds);
            return true;
        }

        Back(g, bounds);
        return false;
    }

    // ---- The front: the claim -------------------------------------------

    private void Front(Graphics g, Rectangle bounds)
    {
        var culture = CultureInfo.CurrentCulture;
        float left = bounds.Left;
        float width = bounds.Width;
        float right = bounds.Right;

        var y = Header(g, bounds);

        Centre(g, "CERTIFIED COPY OF DOCTOR'S PRESCRIPTION / MEDICAL AID "
                  + "DRUG CLAIM FORM", _title, left, y, width);
        y += 26;

        // The patient and member, on the left; the scheme and the claim
        // on the right.
        const float leftWidth = 455f;
        var column = left + leftWidth + 15;
        var columnWidth = right - column;
        var top = y;

        Captioned(g, "PATIENT'S\nNAME", left, y, 85, _form.PatientName,
                  leftWidth - 90);
        y += 32;

        Captioned(g, "POSTAL\nADDRESS", left, y, 85,
                  string.IsNullOrWhiteSpace(_form.Telephone)
                      ? ""
                      : "Tel " + _form.Telephone, leftWidth - 90,
                  fill: _member);
        y += 32;

        Label(g, "MEMBER'S SURNAME", left, y + 6);
        Underlined(g, _form.MemberSurname, left + 100, y, 200);
        Label(g, "MEMBER'S INITS.", left + 320, y + 6);
        Underlined(g, _form.MemberInitials, left + 395, y, leftWidth - 395);
        y += 30;

        Shaded(g, "MEMBER'S NUMBER", left, y, 170, 24);
        Boxed(g, _form.MembershipNumber, left + 170, y, 150, 24,
              _valueBold);
        y += 32;

        Shaded(g, "GROSS AMOUNT CLAIMED   " + _form.CurrencySymbol, left, y,
               200, 24);
        Boxed(g, _form.Gross.ToString("N2", culture), left + 200, y, 100, 24,
              _valueBold, StringAlignment.Far);

        // The patient within the membership: which of the family it is,
        // and their date of birth, which the scheme matches the suffix on.
        var details = left + 320;
        Centre(g, "PATIENT DETAILS", _labelBold, details, y - 12, 135);
        Cell(g, "SUFFIX", _form.DependantCode ?? "", details, y, 40);

        var born = _form.DateOfBirth;
        Cell(g, "DAY", born?.ToString("dd", culture) ?? "", details + 45, y, 28);
        Cell(g, "MTH", born?.ToString("MM", culture) ?? "", details + 73, y, 28);
        Cell(g, "YEAR", born?.ToString("yyyy", culture) ?? "", details + 101,
             y, 34);
        y += 34;

        // The right-hand column, from the top of the section.
        var ry = top;
        Captioned(g, "MEDICAL\nSCHEME", column, ry, 55, _form.SchemeName,
                  columnWidth - 60);
        ry += 32;
        Captioned(g, "DATE", column, ry, 55,
                  _form.ClaimedOn.ToString("dd/MM/yyyy", culture),
                  columnWidth - 60);
        ry += 32;

        var claimBox = new RectangleF(column, ry, columnWidth, y - ry - 6);
        g.DrawRectangle(_line, claimBox.X, claimBox.Y, claimBox.Width,
                        claimBox.Height);
        Centre(g, "CLAIM NUMBER", _labelBold, column, ry + 3, columnWidth);
        Centre(g, _form.ClaimNumber, _valueBold, column, ry + 15, columnWidth);

        var stampTop = ry + 34;
        var stampHeight = claimBox.Bottom - stampTop;
        var half = columnWidth / 2;

        foreach (var x in new[] { column, column + half })
        {
            var stamp = new RectangleF(x, stampTop, half, stampHeight);
            g.FillRectangle(_official, stamp);
            g.DrawRectangle(_line, stamp.X, stamp.Y, stamp.Width, stamp.Height);
            Centre(g, "DATE STAMP", _labelBold, x, stampTop + 3, half);
        }

        // The member's declaration and signature.
        const string declaration =
            "I confirm that the details given above are correct and that "
            + "this claim is lodged against my medical aid society in the "
            + "utmost good faith. I confirm that the amount claimed herein is "
            + "not claimable from another source, and I understand that no "
            + "awards are payable in respect of this claim until my medical "
            + "aid society has received contributions for the stated period "
            + "of treatment.";

        var said = Wrapped(g, declaration, _small, left, y, leftWidth - 120);

        OfficialBoxes(g, new[] { "DELAY", "P.M.", "B/P O/R", "STAFF" },
                      column + columnWidth - 200, y, 200, 34);
        y = Math.Max(y + said, y + 34) + 14;

        Label(g, "MEMBER'S SIGNATURE", left, y);
        g.DrawLine(_line, left + 100, y + 11, left + 330, y + 11);
        Label(g, "DATE", left + 345, y);
        g.DrawLine(_line, left + 375, y + 11, left + leftWidth, y + 11);
        y += 24;

        // The prescription: who wrote it, who dispensed it, which script.
        Numbered(g, "DOCTOR'S No.", _form.DoctorNumber ?? "", left, y, 130);
        Numbered(g, "PHARMACY No.", _form.PharmacyNumber, left + 160, y, 130);
        Numbered(g, "PRESCRIPTION No.", _form.ScriptNumber, left + 320, y, 135);
        OfficialBoxes(g, new[] { "B/P O/R", "P.M.", "STAFF" },
                      column + columnWidth - 150, y, 150, 38);
        y += 46;

        Captioned(g, "NAME OF\nPHARMACY", left, y, 70, _form.PharmacyName,
                  260, outline: false);
        Captioned(g, "NAME OF\nDOCTOR", left + 345, y, 60, _form.DoctorName,
                  width - 345 - 65, outline: false);
        y += 32;

        y = DrugTable(g, left, y, width, culture);

        // Under the table: the gross under the charges, and what is left
        // on the script.
        var chargeX = left + ColumnsBefore("CHARGE");
        Label(g, "GROSS  " + _form.CurrencySymbol, chargeX - 62, y + 7);
        Boxed(g, _form.Gross.ToString("N2", culture), chargeX, y,
              Widths["CHARGE"] + Widths["R.P"], 22, _valueBold,
              StringAlignment.Far);

        Label(g, "REPEATS LEFT ON PRESCRIPTION", left + 560, y + 7);
        Boxed(g, _form.RepeatsLeft.ToString(culture), right - 30, y, 30, 22,
              _valueBold, StringAlignment.Center);
        y += 26;

        g.DrawString(string.Format(culture,
                "Dispensing {0} on {1:dd/MM/yyyy}.   Claimed from {2}: {3}{4:N2}."
                + "   Paid by the patient: {3}{5:N2}.",
                _form.DispensingNumber, _form.DispensedOn,
                string.IsNullOrWhiteSpace(_form.SchemeName)
                    ? "the scheme"
                    : _form.SchemeName,
                _form.CurrencySymbol, _form.Claimed, _form.CoPayment),
            _label, Brushes.Black, left, y);
        y += 16;

        y += Notes(g, left, y, width) + 10;

        Counselling(g, bounds, y, culture);

        Footer(g, bounds, 1, culture);
    }

    /// <summary>
    /// The pharmacy at the head of the page: its name, address and
    /// numbers. Returns where the page continues.
    /// </summary>
    private float Header(Graphics g, Rectangle bounds)
    {
        float top = bounds.Top;
        float x = bounds.Left;
        float width = bounds.Width;
        var y = top;

        g.DrawString(_form.PharmacyName, _name, Brushes.Black,
                     new RectangleF(x, y, width, 24));
        y += 24;

        foreach (var part in (_form.PharmacyAddress ?? "").Split('\n')
                     .Select(p => p.Trim()).Where(p => p.Length > 0))
        {
            g.DrawString(part, _value, Brushes.Black, x, y);
            y += 15;
        }

        var numbers = new List<string>();

        if (!string.IsNullOrWhiteSpace(_form.PharmacyNumber))
            numbers.Add($"Pharmacy No. {_form.PharmacyNumber.Trim()}");

        if (!string.IsNullOrWhiteSpace(_form.VatNumber))
            numbers.Add($"VAT No. {_form.VatNumber.Trim()}");

        if (numbers.Count > 0)
        {
            g.DrawString(string.Join("     ", numbers), _value, Brushes.Black,
                         x, y);
            y += 15;
        }

        var bottom = y + 6;
        g.DrawLine(_line, bounds.Left, bottom, bounds.Right, bottom);

        return bottom + 8;
    }

    // ---- The medicines ---------------------------------------------------

    /// The table's columns, left to right, in hundredths of an inch.
    private static readonly (string Key, string Header, float Width,
        bool Official)[] Columns =
    {
        ("NAME", "NAME OF DRUG", 190, false),
        ("LINE", "LINE", 28, false),
        ("P/R", "P/R", 26, false),
        ("PRICE", "PRICE CODE", 62, false),
        ("QUANT", "QUANT", 44, false),
        ("DAY", "DAY", 24, false),
        ("MTH", "MTH", 26, false),
        ("YR", "YR", 30, false),
        ("CHARGE", "CHARGE", 62, false),
        ("R.P", "R.P", 24, false),
        ("AWARD", "AWARD", 58, true),
        ("SHORT", "SHORTFALL", 60, true),
        ("REASON", "REASON", 46, true),
        ("BPOR", "B/P\nO/R", 32, true),
        ("STAFF", "STAFF", 35, true)
    };

    private static readonly Dictionary<string, float> Widths =
        Columns.ToDictionary(c => c.Key, c => c.Width);

    private static float ColumnsBefore(string key) =>
        Columns.TakeWhile(c => c.Key != key).Sum(c => c.Width);

    /// Room for five medicines at least, as the stationery has, and for
    /// every medicine on the script.
    public const int MinimumRows = 5;

    private float DrugTable(Graphics g, float left, float top, float width,
                            CultureInfo culture)
    {
        const float headerHeight = 26f;
        const float rowHeight = 23f;

        var rows = Math.Max(MinimumRows, _form.Lines.Count);
        var scale = width / Columns.Sum(c => c.Width);
        var y = top;

        // The header.
        var x = left;

        foreach (var column in Columns)
        {
            var cell = new RectangleF(x, y, column.Width * scale, headerHeight);

            g.FillRectangle(column.Official ? _official : _member, cell);
            g.DrawRectangle(_line, cell.X, cell.Y, cell.Width, cell.Height);
            Write(g, column.Header, _labelBold, cell, StringAlignment.Center,
                  wrap: true);

            x += cell.Width;
        }

        y += headerHeight;

        for (var r = 0; r < rows; r++)
        {
            var line = r < _form.Lines.Count ? _form.Lines[r] : null;
            x = left;

            foreach (var column in Columns)
            {
                var cell = new RectangleF(x, y, column.Width * scale, rowHeight);

                if (column.Official) g.FillRectangle(_official, cell);
                g.DrawRectangle(_line, cell.X, cell.Y, cell.Width, cell.Height);

                if (line is not null)
                {
                    var (text, align) = CellText(column.Key, line, r, culture);

                    if (text.Length > 0)
                        Write(g, text, _cell, Inset(cell), align);
                }
                else if (column.Key == "LINE")
                {
                    // The stationery numbers every line, filled or not.
                    Write(g, (r + 1).ToString("00", culture), _label,
                          Inset(cell), StringAlignment.Center);
                }

                x += cell.Width;
            }

            y += rowHeight;
        }

        return y + 6;
    }

    private (string Text, StringAlignment Align) CellText(string key,
        ClaimFormLineDto line, int index, CultureInfo culture) => key switch
    {
        "NAME" => (line.Description, StringAlignment.Near),
        "LINE" => ((index + 1).ToString("00", culture), StringAlignment.Center),
        "P/R" => (_form.IsRepeat ? "R" : "P", StringAlignment.Center),
        "PRICE" => (line.PriceCode, StringAlignment.Center),
        "QUANT" => (line.Quantity.ToString("0.##", culture),
                    StringAlignment.Far),
        "DAY" => (_form.DispensedOn.ToString("dd", culture),
                  StringAlignment.Center),
        "MTH" => (_form.DispensedOn.ToString("MM", culture),
                  StringAlignment.Center),
        "YR" => (_form.DispensedOn.ToString("yy", culture),
                 StringAlignment.Center),
        "CHARGE" => (line.Charge.ToString("N2", culture), StringAlignment.Far),
        _ => ("", StringAlignment.Near)
    };

    private float Notes(Graphics g, float left, float top, float width)
    {
        string[] notes =
        {
            "NOTES",
            "1) All sections in blue not completed by the computer must be "
            + "completed by the member/patient. All other sections are for "
            + "official use only.",
            "2) If another claim form is attached to this form, ensure that "
            + "it is submitted together with this form.",
            "3) Please adhere to any other special conditions of your "
            + "medical aid society.",
            "4) Certain sections of the form may not be required by your "
            + "medical aid society and will remain blank.",
            "5) Retain this form for submission to the tax authorities if "
            + "you are not covered by a drug benefit scheme.",
            "6) Do not lose this form, as a fee may be charged for a copy.",
            "7) Please check all details carefully. If any information "
            + "printed by the computer is incorrect or has been omitted, "
            + "please notify us."
        };

        var y = top;

        foreach (var note in notes)
            y += Wrapped(g, note, note == "NOTES" ? _labelBold : _small,
                         left, y, width);

        return y - top;
    }

    /// <summary>
    /// The counselling slip at the foot: how to take each medicine, in
    /// the pharmacist's words, under the stationery's own caution.
    /// </summary>
    private void Counselling(Graphics g, Rectangle bounds, float top,
                             CultureInfo culture)
    {
        float left = bounds.Left;
        float width = bounds.Width;
        var footer = bounds.Bottom - 16;

        if (top > footer - 120) return;   // a script too long to leave room

        g.DrawLine(_cut, left, top, bounds.Right, top);
        var y = top + 8;

        Centre(g, "PATIENT COUNSELLING", _counselTitle, left, y, width);
        y += 24;

        foreach (var sentence in new[]
                 {
                     "THE INFORMATION GIVEN BELOW IS ONLY A GUIDE. IF IT DOES NOT",
                     "AGREE WITH INSTRUCTIONS GIVEN BY YOUR DOCTOR, PLEASE",
                     "FOLLOW THE DOCTOR'S INSTRUCTIONS."
                 })
        {
            Centre(g, sentence, _counsel, left, y, width);
            y += 16;
        }

        y += 8;

        Label(g, "NAME", left + 30, y + 3);
        g.DrawString(_form.PatientName, _value, Brushes.Black, left + 70, y);
        Label(g, "DATE", left + 470, y + 3);
        g.DrawString(_form.DispensedOn.ToString("dd/MM/yyyy", culture), _value,
                     Brushes.Black, left + 505, y);
        y += 16;
        g.DrawLine(_line, left + 30, y, bounds.Right - 30, y);
        y += 8;

        foreach (var line in _form.Lines)
        {
            var text = string.IsNullOrWhiteSpace(line.Directions)
                ? line.Description
                : $"{line.Description}:  {line.Directions}";

            var height = g.MeasureString(text, _value, (int)(width - 60)).Height;

            if (y + height > footer) break;

            g.DrawString(text, _value, Brushes.Black,
                new RectangleF(left + 30, y, width - 60, height));
            y += height + 4;
        }
    }

    private void Footer(Graphics g, Rectangle bounds, int page,
                        CultureInfo culture)
    {
        var text = string.Format(culture, "{0}  -  Claim {1}  -  Dispensing {2}",
            _form.PharmacyName, _form.ClaimNumber, _form.DispensingNumber);

        g.DrawString(text, _small, Brushes.Black, bounds.Left,
                     bounds.Bottom - 10);

        var pageText = string.Format(culture, "Page {0} of {1}", page, Pages);
        var size = g.MeasureString(pageText, _small);

        g.DrawString(pageText, _small, Brushes.Black,
                     bounds.Right - size.Width, bounds.Bottom - 10);
    }

    // ---- The back: returns, allergies and conditions ---------------------

    private static readonly string[] Allergies =
    {
        "Alcohol", "Aspirin", "Barbiturates", "Cephalosporins",
        "Chloramphenicols", "Codeine", "Erythromycins", "Iodine", "Mercury",
        "Neomycins", "Penicillins", "Phenothiazines", "Pethidine", "Steroids",
        "Sulphonamides"
    };

    private static readonly string[] Conditions =
    {
        "Adrenal disease", "Alcohol (chronic use)", "Asthma",
        "Cardiovascular disease", "Depression", "Diabetes mellitus",
        "Dyspepsia", "Epilepsy", "Gall bladder disease", "Glaucoma", "Gout",
        "Gastric ulceration", "Haemophilia / bleeding disorders",
        "Hepatic insufficiency / jaundice", "Hyperkalaemia", "Hypertension",
        "Joint replacements", "Migraine", "Peptic ulceration", "Porphyria",
        "Pregnancy", "Renal insufficiency", "Smoking", "Thrombophlebitis",
        "Thyroid disease"
    };

    private void Back(Graphics g, Rectangle bounds)
    {
        var culture = CultureInfo.CurrentCulture;
        float left = bounds.Left;
        float width = bounds.Width;

        var y = Header(g, bounds);

        g.DrawString(string.Format(culture, "{0}   Claim {1}   Script {2}",
                _form.PatientName, _form.ClaimNumber, _form.ScriptNumber),
            _value, Brushes.Black, left, y);
        y += 30;

        // The returns notice, boxed as the stationery has it.
        var box = new RectangleF(left + 130, y, width - 260, 150);
        g.DrawRectangle(_line, box.X, box.Y, box.Width, box.Height);

        var inner = box.Y + 14;

        foreach (var paragraph in new[]
                 {
                     "In the interests of public health, pharmacists do not "
                     + "accept the return of prescribed medicines from their "
                     + "customers.",
                     "Once medicines have left the pharmacy, the pharmacist can "
                     + "no longer vouch for the conditions under which they "
                     + "have been kept, nor for their efficacy.",
                     "We therefore regret that under no circumstances will "
                     + "dispensed medicines be accepted for credit."
                 })
        {
            inner += CentredWrapped(g, paragraph, _value, box.X + 20, inner,
                                    box.Width - 40) + 10;
        }

        y = box.Bottom + 36;

        Centre(g, "ALLERGIES AND MEDICAL CONDITIONS", _heading, left, y, width);
        y += 30;

        y += CentredWrapped(g,
            "Please tell the pharmacist if any of the following allergies or "
            + "medical conditions apply to you. We record them in your patient "
            + "profile, as they may affect future prescriptions.",
            _value, left + 40, y, width - 80) + 18;

        var half = width / 2;

        g.DrawString("ALLERGIES", _valueBold, Brushes.Black, left + 40, y);
        g.DrawString("MEDICAL CONDITIONS", _valueBold, Brushes.Black,
                     left + half + 20, y);
        y += 22;

        const float rowHeight = 19f;

        for (var i = 0; i < Math.Max(Allergies.Length, Conditions.Length); i++)
        {
            if (i < Allergies.Length)
                Tick(g, Allergies[i], left + 40, y + i * rowHeight);

            if (i < Conditions.Length)
                Tick(g, Conditions[i], left + half + 20, y + i * rowHeight);
        }

        y += Math.Max(Allergies.Length, Conditions.Length) * rowHeight + 20;

        Label(g, "OTHER", left + 40, y + 4);
        g.DrawLine(_line, left + 85, y + 15, bounds.Right - 40, y + 15);
        y += 30;
        g.DrawLine(_line, left + 85, y + 15, bounds.Right - 40, y + 15);
        y += 46;

        Label(g, "PATIENT / MEMBER SIGNATURE", left + 40, y + 4);
        g.DrawLine(_line, left + 185, y + 15, left + 460, y + 15);
        Label(g, "DATE", left + 480, y + 4);
        g.DrawLine(_line, left + 510, y + 15, bounds.Right - 40, y + 15);

        Footer(g, bounds, 2, culture);
    }

    private void Tick(Graphics g, string text, float x, float y)
    {
        g.DrawRectangle(_line, x, y + 2, 11, 11);
        g.DrawString(text, _value, Brushes.Black, x + 18, y);
    }

    // ---- Drawing ---------------------------------------------------------

    private void Label(Graphics g, string text, float x, float y) =>
        g.DrawString(text, _label, Brushes.Black, x, y);

    /// A caption on the left and the value in a box beside it.
    private void Captioned(Graphics g, string caption, float x, float y,
        float captionWidth, string value, float boxWidth,
        Brush? fill = null, bool outline = true)
    {
        Write(g, caption, _label, new RectangleF(x, y, captionWidth, 24),
              StringAlignment.Near, wrap: true);

        var box = new RectangleF(x + captionWidth + 5, y, boxWidth, 24);

        if (fill is not null) g.FillRectangle(fill, box);

        if (outline)
            g.DrawRectangle(_line, box.X, box.Y, box.Width, box.Height);
        else
            g.DrawLine(_line, box.Left, box.Bottom, box.Right, box.Bottom);

        Write(g, value, _value, Inset(box), StringAlignment.Near);
    }

    private void Underlined(Graphics g, string value, float x, float y,
                            float width)
    {
        g.DrawLine(_line, x, y + 20, x + width, y + 20);
        Write(g, value, _value, new RectangleF(x + 2, y, width - 4, 20),
              StringAlignment.Near);
    }

    /// A caption cell shaded as the member's to complete.
    private void Shaded(Graphics g, string caption, float x, float y,
                        float width, float height)
    {
        var box = new RectangleF(x, y, width, height);

        g.FillRectangle(_member, box);
        g.DrawRectangle(_line, box.X, box.Y, box.Width, box.Height);
        Write(g, caption, _labelBold, Inset(box), StringAlignment.Near);
    }

    private void Boxed(Graphics g, string value, float x, float y,
        float width, float height, Font font,
        StringAlignment align = StringAlignment.Near)
    {
        var box = new RectangleF(x, y, width, height);

        g.DrawRectangle(_line, box.X, box.Y, box.Width, box.Height);
        Write(g, value, font, Inset(box), align);
    }

    /// A small labelled cell: the caption at the top, the value under it.
    private void Cell(Graphics g, string caption, string value, float x,
                      float y, float width)
    {
        var box = new RectangleF(x, y, width, 28);

        g.DrawRectangle(_line, box.X, box.Y, box.Width, box.Height);
        Centre(g, caption, _small, x, y + 1, width);
        Write(g, value, _value, new RectangleF(x, y + 9, width, 19),
              StringAlignment.Center);
    }

    /// A caption over a box, as the stationery numbers the prescription.
    private void Numbered(Graphics g, string caption, string value, float x,
                          float y, float width)
    {
        Centre(g, caption, _labelBold, x, y, width);
        Boxed(g, value, x, y + 12, width, 24, _value, StringAlignment.Center);
    }

    /// A row of the scheme's own little boxes, shaded and left empty.
    private void OfficialBoxes(Graphics g, IReadOnlyList<string> captions,
                               float x, float y, float width, float height)
    {
        var each = width / captions.Count;

        for (var i = 0; i < captions.Count; i++)
        {
            var box = new RectangleF(x + i * each, y, each, height);

            g.FillRectangle(_official, box);
            g.DrawRectangle(_line, box.X, box.Y, box.Width, box.Height);
            Centre(g, captions[i], _small, box.X, y + 2, each);
        }
    }

    private void Centre(Graphics g, string text, Font font, float x, float y,
                        float width)
    {
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };

        g.DrawString(text, font, Brushes.Black,
            new RectangleF(x, y, width, font.GetHeight(g) + 2), format);
    }

    /// Wraps text within a width and returns the height it took.
    private static float Wrapped(Graphics g, string text, Font font, float x,
                                 float y, float width)
    {
        var height = g.MeasureString(text, font, (int)width).Height;

        g.DrawString(text, font, Brushes.Black,
                     new RectangleF(x, y, width, height));

        return height;
    }

    private static float CentredWrapped(Graphics g, string text, Font font,
                                        float x, float y, float width)
    {
        var height = g.MeasureString(text, font, (int)width).Height;

        using var format = new StringFormat { Alignment = StringAlignment.Center };

        g.DrawString(text, font, Brushes.Black,
                     new RectangleF(x, y, width, height), format);

        return height;
    }

    private static void Write(Graphics g, string text, Font font,
        RectangleF area, StringAlignment align, bool wrap = false)
    {
        using var format = new StringFormat
        {
            Alignment = align,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter
        };

        if (!wrap) format.FormatFlags = StringFormatFlags.NoWrap;

        g.DrawString(text, font, Brushes.Black, area, format);
    }

    private static RectangleF Inset(RectangleF box) =>
        RectangleF.Inflate(box, -3, -1);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            foreach (var font in new[]
                     {
                         _name, _title, _label, _labelBold, _value,
                         _valueBold, _cell, _small, _counselTitle, _counsel,
                         _heading
                     })
                font.Dispose();

            _line.Dispose();
            _cut.Dispose();
            _member.Dispose();
            _official.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>
/// Sends a claim form to the workstation's A4 document printer.
/// </summary>
/// <remarks>
/// Its own printer rather than the receipt printer: an A4 form sent to an
/// 80 millimetre roll prints a strip of its left-hand edge.
/// </remarks>
public sealed class ClaimFormPrinter : IClaimFormPrinter
{
    private readonly ILogger<ClaimFormPrinter> _log;
    private readonly IWorkstationSettings _settings;

    public ClaimFormPrinter(ILogger<ClaimFormPrinter> log,
                            IWorkstationSettings settings)
    {
        _log = log;
        _settings = settings;
    }

    /// <remarks>
    /// Never throws, like every print path: the claim is already raised,
    /// and the form can be printed again from the Claims screen or from
    /// Dispensary > Labels.
    /// </remarks>
    public async Task<PrintResult> PrintAsync(ClaimFormDto form,
                                              CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(form);

        using var doc = new ClaimFormDocument(form);

        return await Spooler.PrintAsync(doc, _settings,
            WorkstationSettingKey.DocumentPrinter, "claim form",
            $"Claim form {form.ClaimNumber}", _log, ct).ConfigureAwait(false);
    }
}
