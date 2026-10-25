using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Desktop.Features.Sales;
using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Features.Admin;

/// <summary>
/// What the till receipt prints: a box for every part of the slip, for
/// the whole pharmacy, with the slip itself beside them as it will look.
/// Administrators only.
/// </summary>
/// <remarks>
/// The preview is drawn from a made-up sale - two counter items, a
/// collected script, cash with change - through the same layout the
/// printer uses, so what is shown is what prints. Only the receipt
/// number is not offered: without it a slip cannot be found again.
/// </remarks>
public sealed class frmReceiptTemplate : PhxFormBase
{
    private readonly IPharmacySettings? _settings;
    private readonly IReceiptPrinter? _printer;
    private readonly ReceiptHeaderFactory? _headers;

    private readonly Dictionary<string, CheckBox> _boxes = new(StringComparer.Ordinal);
    private readonly TextBox _footer = new();
    private readonly Controls.ReceiptPreview _preview = new();

    private readonly IProductService? _products;
    private readonly TextBox _search = new();
    private readonly ListBox _matches = new();
    private readonly System.Windows.Forms.Timer _searchTimer = new();
    private readonly List<SaleLineDto> _lines = DefaultLines();
    private string? _script = SampleScript;

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.SettingsChange;

    /// Designer and tests only.
    public frmReceiptTemplate() => Build();

    public frmReceiptTemplate(IPharmacySettings settings, IReceiptPrinter printer,
                              ReceiptHeaderFactory headers, IProductService products)
    {
        _settings = settings;
        _printer = printer;
        _headers = headers;
        _products = products;
        Build();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _searchTimer.Dispose();
        base.Dispose(disposing);
    }

    /// The boxes, in the order the slip prints them, under their headings.
    private static readonly (string Heading, (string Key, string Text)[] Items)[] Sections =
    {
        ("Heading", new[]
        {
            ("TradingName", "Pharmacy name"), ("Address", "Address"),
            ("VatNumber", "VAT number"), ("Telephone", "Telephone"),
            ("WhatsApp", "WhatsApp"), ("DocumentTitle", "\"Tax Invoice\" and sale type")
        }),
        ("The sale", new[]
        {
            ("Operator", "Cashier"), ("TillNumber", "Till number"),
            ("Date", "Date and time"), ("AccountAndPatient", "Account, patient and script")
        }),
        ("Items", new[]
        {
            ("ColumnHeadings", "Column headings"), ("ProductCodes", "Product codes"),
            ("ProductNames", "Product names"), ("QuantityAndUnitPrice", "Quantity and unit price"),
            ("LineAmounts", "Line amounts")
        }),
        ("Money", new[]
        {
            ("SubTotal", "Sub total"), ("Vat", "VAT"), ("Total", "Total"),
            ("Tenders", "How it was paid"), ("Change", "Change"),
            ("MedicalAidShare", "Medical aid share"), ("AccountSignature", "Signature line (account sales)")
        }),
        ("End", new[]
        {
            ("Footer", "Closing message"),
            ("CopyMark", "\"COPY - REPRINTED\" on reprints")
        })
    };

    private void Build()
    {
        Text = "PharmaX - Receipt Template";
        Name = "frmReceiptTemplate";
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1120, 680);
        MinimumSize = new Size(900, 560);
        StartPosition = FormStartPosition.CenterParent;
        KeyPreview = true;

        // ---- Right: the slip as it will print -----------------------------
        var right = new Panel { Dock = DockStyle.Right, Width = 450, Padding = new Padding(8) };
        var previewTitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Font = new Font(Font, FontStyle.Bold),
            Height = 26,
            Text = "Preview - sample sale, not a real receipt"
        };
        var scroller = new Panel { AutoScroll = true, BackColor = SystemColors.ControlDark, Dock = DockStyle.Fill, Padding = new Padding(12) };
        _preview.Location = new Point(12, 12);
        _preview.Name = "pnlPreview";
        scroller.Controls.Add(_preview);
        right.Controls.Add(scroller);
        right.Controls.Add(previewTitle);

        // ---- Bottom: closing message and the buttons ----------------------
        var bottom = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Bottom,
            Padding = new Padding(12, 6, 12, 10)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var footerLabel = new Label { AutoSize = true, Margin = new Padding(3, 3, 3, 0), Text = "Closing &message" };
        _footer.Dock = DockStyle.Fill;
        _footer.Height = 48;
        _footer.MinimumSize = new Size(280, 48);
        _footer.Multiline = true;
        _footer.Name = "txtFooter";
        _footer.Text = ReceiptTemplate.Default.FooterText;
        _footer.TextChanged += (_, _) => Redraw();

        var test = new Button { AutoSize = true, Name = "btnTest", Padding = new Padding(8, 2, 8, 2), Text = "Print &sample" };
        test.Click += async (_, _) => await PrintSampleAsync();
        var save = new Button { AutoSize = true, Name = "btnSave", Padding = new Padding(12, 2, 12, 2), Text = "&Save" };
        save.Click += async (_, _) => await SaveAsync();
        var cancel = new Button { AutoSize = true, DialogResult = DialogResult.Cancel, Name = "btnCancel", Padding = new Padding(8, 2, 8, 2), Text = "Cancel" };
        cancel.Click += (_, _) => Close();

        bottom.Controls.Add(footerLabel, 0, 0);
        bottom.SetColumnSpan(footerLabel, 4);
        bottom.Controls.Add(_footer, 0, 1);
        bottom.Controls.Add(test, 1, 1);
        bottom.Controls.Add(save, 2, 1);
        bottom.Controls.Add(cancel, 3, 1);

        // ---- Left: the boxes, in sections that flow ------------------------
        var boxes = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(8, 4, 8, 4),
            WrapContents = true
        };

        foreach (var (heading, items) in Sections)
        {
            var section = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                Margin = new Padding(4, 4, 24, 12),
                WrapContents = false
            };

            section.Controls.Add(new Label
            {
                AutoSize = true,
                Font = new Font(Font, FontStyle.Bold),
                Margin = new Padding(0, 0, 0, 4),
                Text = heading
            });

            foreach (var (key, text) in items)
            {
                var box = new CheckBox
                {
                    AutoSize = true,
                    Checked = (bool)typeof(ReceiptTemplate).GetProperty(key)!.GetValue(ReceiptTemplate.Default)!,
                    Margin = new Padding(2, 1, 2, 1),
                    Name = "chk" + key,
                    Text = text
                };
                box.CheckedChanged += (_, _) => Redraw();
                _boxes[key] = box;
                section.Controls.Add(box);
            }

            boxes.Controls.Add(section);
        }

        _boxes["Footer"].CheckedChanged += (_, _) => _footer.Enabled = _boxes["Footer"].Checked;

        var intro = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 34,
            Padding = new Padding(12, 8, 12, 0),
            Text = "Tick what every till's receipt prints. The receipt number always prints."
        };

        Controls.Add(boxes);
        Controls.Add(BuildSearch());
        Controls.Add(intro);
        Controls.Add(bottom);
        Controls.Add(right);

        AcceptButton = save;
        CancelButton = cancel;
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        if (_settings is not null)
        {
            try
            {
                Show(ReceiptTemplate.Parse(await _settings.GetAsync(ReceiptTemplate.SettingKey)));
            }
#pragma warning disable CA1031 // Reported and logged; the defaults stay ticked.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ErrorHandler.Log(ex);
                ShowError(ErrorHandler.Describe(ex));
            }
        }

        Redraw();
    }

    /// The template's settings as boxes, by property name.
    private void Show(ReceiptTemplate template)
    {
        foreach (var (key, box) in _boxes)
            box.Checked = (bool)typeof(ReceiptTemplate).GetProperty(key)!.GetValue(template)!;

        _footer.Text = template.FooterText;
        _footer.Enabled = template.Footer;
    }

    /// The boxes as a template.
    internal ReceiptTemplate Current()
    {
        var template = new ReceiptTemplate { FooterText = _footer.Text.Trim() };

        // Each box sets the property of its own name, on a template not
        // yet handed to anyone.
        foreach (var (key, box) in _boxes)
            typeof(ReceiptTemplate).GetProperty(key)!.SetValue(template, box.Checked);

        return template;
    }

    private ReceiptHeaderDto SampleHeader()
    {
        ReceiptHeaderDto header;

        try
        {
            header = _headers?.Build() ?? Fallback();
        }
#pragma warning disable CA1031 // A preview with made-up details is better than none.
        catch (Exception)
#pragma warning restore CA1031
        {
            header = Fallback();
        }

        // A detail the pharmacy has not entered yet would print nothing,
        // and a ticked box that changes nothing looks broken.
        return header with
        {
            Address = Blank(header.Address) ? "Address not set\n(System Setup)" : header.Address,
            VatNumber = Blank(header.VatNumber) ? "not set" : header.VatNumber,
            Telephone = Blank(header.Telephone) ? "not set" : header.Telephone,
            WhatsApp = Blank(header.WhatsApp) ? "not set" : header.WhatsApp,
            Template = Current(),
            IsCopy = false,
            PrintedAtLocal = DateTime.Now
        };

        static bool Blank(string? text) => string.IsNullOrWhiteSpace(text);

        static ReceiptHeaderDto Fallback() => new("Your Pharmacy", "", "1 Main Street\nTown",
            "1234567890", "", "Branch 1 Till 1", "cashier", DateTime.Now,
            TillNumber: "1", CurrencyCode: "USD", Telephone: "000 000 0000");
    }

    private const string SampleScript = "12345";

    /// What the sample starts with: two counter items and a collected script.
    private static List<SaleLineDto> DefaultLines() => new()
    {
        new SaleLineDto(1, Guid.NewGuid(), "Panado 500mg 20s", 2m, 34.95m, 0m, "A",
            69.90m, null, null, false, false, 15m, "PAN500"),
        new SaleLineDto(2, Guid.NewGuid(), "Vitamin C 1000mg 30s", 1m, 89.00m, 0m, "A",
            89.00m, null, null, false, false, 15m, "VITC1000"),
        ScriptLine(3)
    };

    private static SaleLineDto ScriptLine(int lineNo) =>
        new(lineNo, Guid.NewGuid(), "Sample script item", 1m, 45.00m, 0m, "B",
            45.00m, null, null, true, false, 0m, IsScript: true);

    /// A made-up sale with something for every box to switch off.
    internal static SaleDto SampleSale() => SampleSale(DefaultLines(), SampleScript);

    private static SaleDto SampleSale(IReadOnlyList<SaleLineDto> lines, string? script)
    {
        var total = lines.Sum(l => l.LineTotal);
        var vat = Math.Round(lines.Where(l => !l.IsScript)
            .Sum(l => l.LineTotal * l.VatRatePercent / (100m + l.VatRatePercent)), 2);
        var tendered = Math.Ceiling(total / 10m) * 10m;

        return new SaleDto(Guid.Empty, "SAMPLE", lines,
            new SaleTotalsDto(total, 0m, vat, total, tendered, tendered - total),
            new[] { new PaymentDto("Cash", total, null) },
            "Finalised", OccurredAtUtc: DateTime.UtcNow,
            Particulars: new ReceiptParticularsDto(null, null, "Sample Patient", "F00001",
                script, "Sample Medical Aid", lines.Any(l => l.IsScript) ? 120.00m : 0m));
    }

    private SaleDto Sample() => SampleSale(_lines, _script);

    private void Redraw()
    {
        if (!IsHandleCreated) return;

        using var document = new ReceiptDocument(Sample(), SampleHeader());
        _preview.Show(document.Rows());
    }

    // ---- Adding real products and scripts to the sample ------------------

    private async Task SearchAsync()
    {
        _searchTimer.Stop();
        var term = _search.Text.Trim();

        if (_products is null || term.Length < 2)
        {
            _matches.DataSource = null;
            _matches.Visible = false;
            return;
        }

        try
        {
            var found = await _products.SearchAsync(term, take: 20);
            if (term != _search.Text.Trim()) return; // typed on since

            var items = found.IsSuccess ? found.Value.Items.Where(p => p.IsActive).ToList() : new();
            _matches.DisplayMember = nameof(ProductListItemDto.Description);
            _matches.DataSource = items;
            _matches.Visible = items.Count > 0;
        }
#pragma warning disable CA1031 // A failed lookup leaves the sample as it was.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
        }
    }

    private void AddProduct()
    {
        if (_matches.SelectedItem is not ProductListItemDto p) return;

        _lines.Add(new SaleLineDto(_lines.Count + 1, p.Id, p.Description, 1m, p.RetailPrice,
            0m, "A", p.RetailPrice, null, null, p.Schedule > 0, false, 15m, p.Code));
        _search.Clear();
        Redraw();
    }

    /// What is typed becomes the script number the slip prints.
    private void AddScript()
    {
        var number = _search.Text.Trim();
        if (number.Length == 0) return;

        _script = number;
        if (!_lines.Any(l => l.IsScript)) _lines.Add(ScriptLine(_lines.Count + 1));
        _search.Clear();
        Redraw();
    }

    private void ResetSample()
    {
        _lines.Clear();
        _lines.AddRange(DefaultLines());
        _script = SampleScript;
        _search.Clear();
        Redraw();
    }

    private Control BuildSearch()
    {
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 4,
            Dock = DockStyle.Top,
            Padding = new Padding(12, 4, 12, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        var label = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 2),
            Text = "Add to the sample - type a product name or code, or a script number"
        };

        _search.Dock = DockStyle.Fill;
        _search.Name = "txtSampleSearch";
        _search.PlaceholderText = "Search products, or type a script number";
        _search.TextChanged += (_, _) => { _searchTimer.Stop(); _searchTimer.Start(); };
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Down && _matches.Visible) { _matches.Focus(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter && _matches.Visible) { AddProduct(); e.SuppressKeyPress = true; }
        };
        _searchTimer.Interval = 300;
        _searchTimer.Tick += async (_, _) => await SearchAsync();

        var product = new Button { AutoSize = true, Name = "btnAddProduct", Text = "Add &product" };
        product.Click += (_, _) => AddProduct();
        var script = new Button { AutoSize = true, Name = "btnAddScript", Text = "Add as sc&ript no." };
        script.Click += (_, _) => AddScript();
        var reset = new Button { AutoSize = true, Name = "btnResetSample", Text = "Reset sa&mple" };
        reset.Click += (_, _) => ResetSample();

        _matches.Dock = DockStyle.Fill;
        _matches.Height = 110;
        _matches.IntegralHeight = false;
        _matches.Name = "lstSampleMatches";
        _matches.Visible = false;
        _matches.DoubleClick += (_, _) => AddProduct();
        _matches.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { AddProduct(); e.SuppressKeyPress = true; }
        };

        panel.Controls.Add(label, 0, 0);
        panel.SetColumnSpan(label, 4);
        panel.Controls.Add(_search, 0, 1);
        panel.Controls.Add(product, 1, 1);
        panel.Controls.Add(script, 2, 1);
        panel.Controls.Add(reset, 3, 1);
        panel.Controls.Add(_matches, 0, 2);
        panel.SetColumnSpan(_matches, 4);
        return panel;
    }

    private async Task SaveAsync()
    {
        if (_settings is null) return;

        try
        {
            SetBusy(true);
            await _settings.SetAsync(ReceiptTemplate.SettingKey, Current().ToJson());
            ShowInfo("Saved. Every till's receipts print this way from the next sale.");
            CloseView(true);
        }
#pragma warning disable CA1031 // Reported to the operator and logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            ShowError(ErrorHandler.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

    /// The sample slip on the receipt printer, as the preview shows it.
    private async Task PrintSampleAsync()
    {
        if (_printer is null) return;

        try
        {
            SetBusy(true);
            var result = await _printer.PrintAsync(Sample(), SampleHeader());
            const string what = "Sample receipt";
            if (result.Printed) ShowInfo(result.Describe(what));
            else ShowError(result.Describe(what));
        }
#pragma warning disable CA1031 // Reported to the operator and logged.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ErrorHandler.Log(ex);
            ShowError(ErrorHandler.Describe(ex));
        }
        finally
        {
            SetBusy(false);
        }
    }

}
