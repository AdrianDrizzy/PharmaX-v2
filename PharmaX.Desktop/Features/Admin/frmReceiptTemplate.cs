using PharmaX.Adapters.Printing;
using PharmaX.Application.Abstractions;
using PharmaX.Application.Inventory;
using PharmaX.Application.Inventory.Dtos;
using PharmaX.Application.Sales;
using PharmaX.Application.Sales.Dtos;
using PharmaX.Desktop.Controls;
using PharmaX.Desktop.Features.Dispensing;
using PharmaX.Desktop.Features.Inventory;
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
/// printer uses, so what is shown is what prints. Every made-up part of
/// it - items, script, patient, medical aid - can be taken off and real
/// products and scripts put on instead. Only the receipt number is not
/// offered: without it a slip cannot be found again.
/// </remarks>
public sealed class frmReceiptTemplate : PhxFormBase
{
    private readonly IPharmacySettings? _settings;
    private readonly IReceiptPrinter? _printer;
    private readonly ReceiptHeaderFactory? _headers;

    private readonly Dictionary<string, CheckBox> _boxes = new(StringComparer.Ordinal);
    private readonly TextBox _footer = new();
    private readonly Controls.ReceiptPreview _preview = new();

    private readonly Microsoft.Extensions.DependencyInjection.IServiceScopeFactory? _scopes;
    private readonly ViewFactory? _views;
    private readonly TextBox _search = new();
    private SearchSuggestions? _suggestions;
    private readonly List<SaleLineDto> _lines = DefaultLines();
    private string? _script = SampleScript;
    private (string Name, string? File)? _patient = SamplePatient;
    private bool _medicalAid = true;
    private readonly ListBox _items = new();

    protected override string? RequiredPermission =>
        Domain.Security.PermissionCode.ReceiptTemplate;

    /// Designer and tests only.
    public frmReceiptTemplate() => Build();

    public frmReceiptTemplate(IPharmacySettings settings, IReceiptPrinter printer,
                              ReceiptHeaderFactory headers,
                              Microsoft.Extensions.DependencyInjection.IServiceScopeFactory scopes,
                              ViewFactory views)
    {
        _settings = settings;
        _printer = printer;
        _headers = headers;
        _scopes = scopes;
        _views = views;
        Build();
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
        // The message the full width and the buttons under it: beside it,
        // on a narrow window, the buttons were drawn over the message.
        var bottom = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Bottom,
            Padding = new Padding(12, 6, 12, 10)
        };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var footerLabel = new Label { AutoSize = true, Margin = new Padding(3, 3, 3, 0), Text = "Closing &message" };
        _footer.Dock = DockStyle.Fill;
        _footer.MinimumSize = new Size(0, 40);
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

        var buttons = new FlowLayoutPanel
        {
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(0, 4, 0, 0),
            WrapContents = false
        };
        buttons.Controls.AddRange(new Control[] { test, save, cancel });

        bottom.Controls.Add(footerLabel);
        bottom.Controls.Add(_footer);
        bottom.Controls.Add(buttons);

        // ---- Left: the boxes, in sections that flow ------------------------
        // Under the sample, the two scrolling as one: on a short window the
        // boxes had a strip of their own a couple of lines high.
        var left = new Panel { AutoScroll = true, Dock = DockStyle.Fill };
        var boxes = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
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

        left.Controls.Add(boxes);
        left.Controls.Add(BuildSearch());

        Controls.Add(left);
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
    private static readonly (string Name, string? File) SamplePatient = ("Sample Patient", "F00001");
    private const string SampleScheme = "Sample Medical Aid";
    private const decimal SampleSchemeShare = 120.00m;

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
    internal static SaleDto SampleSale() =>
        SampleSale(DefaultLines(), SampleScript, SamplePatient, medicalAid: true);

    internal static SaleDto SampleSale(IReadOnlyList<SaleLineDto> lines, string? script,
                                       (string Name, string? File)? patient, bool medicalAid)
    {
        // A share of nothing still prints "Claimed from", so none at all
        // unless there is a script for the medical aid to pay towards.
        var claimed = medicalAid && lines.Any(l => l.IsScript);
        var total = lines.Sum(l => l.LineTotal);
        var vat = Math.Round(lines.Where(l => !l.IsScript)
            .Sum(l => l.LineTotal * l.VatRatePercent / (100m + l.VatRatePercent)), 2);
        var tendered = Math.Ceiling(total / 10m) * 10m;

        return new SaleDto(Guid.Empty, "SAMPLE", lines,
            new SaleTotalsDto(total, 0m, vat, total, tendered, tendered - total),
            new[] { new PaymentDto("Cash", total, null) },
            "Finalised", OccurredAtUtc: DateTime.UtcNow,
            Particulars: new ReceiptParticularsDto(null, null, patient?.Name, patient?.File,
                script, claimed ? SampleScheme : null, claimed ? SampleSchemeShare : null));
    }

    internal SaleDto Sample() => SampleSale(_lines, _script, _patient, _medicalAid);

    private void Redraw()
    {
        if (!IsHandleCreated) return;

        using var document = new ReceiptDocument(Sample(), SampleHeader());
        _preview.Show(document.Rows());
    }

    // ---- Adding real products and scripts to the sample ------------------

    /// Scripts first where a number is being typed, then products.
    private async Task<IReadOnlyList<Suggestion>> SuggestAsync(string term, CancellationToken ct)
    {
        var scripts = term.Any(char.IsDigit)
            ? await Suggest.Scripts(_scopes!)(term, ct)
            : Array.Empty<Suggestion>();
        var products = await Suggest.ProductsInFull(_scopes!)(term, ct);

        return scripts.Take(4).Concat(products).Take(8).ToList();
    }

    private void Take(Suggestion chosen)
    {
        if (chosen.Value is ProductListItemDto product) AddProduct(product);
        else if (chosen.Value is Application.Dispensing.Dtos.ScriptDto script)
            AddScript(script);
    }

    private void AddProduct(ProductListItemDto p)
    {
        _lines.Add(new SaleLineDto(NextLineNo(), p.Id, p.Description, 1m, p.RetailPrice,
            0m, "A", p.RetailPrice, null, null, p.Schedule > 0, false, 15m, p.Code));
        SampleChanged();
    }

    private int NextLineNo() => _lines.Count == 0 ? 1 : _lines.Max(l => l.LineNo) + 1;

    /// The product list as a picker, opened on what is typed.
    private async Task FindProductAsync()
    {
        if (_views is null || _scopes is null) return;

        if (frmProductList.Pick(_views, this, _search.Text) is not string code) return;
        if (await Suggest.FindProductAsync(_scopes, code) is ProductListItemDto product)
            AddProduct(product);
    }

    /// Every patient's scripts for a day, today first.
    private void FindScript()
    {
        if (_views is null) return;

        if (frmScriptPicker.Pick(_views, this, _search.Text) is { } script)
            AddScript(script);
    }

    /// A real script prints its own patient.
    private void AddScript(Application.Dispensing.Dtos.ScriptDto script)
    {
        if (!string.IsNullOrWhiteSpace(script.Patient)) _patient = (script.Patient.Trim(), null);
        AddScript(script.ScriptNumber);
    }

    /// What is typed or chosen becomes the script number the slip prints.
    private void AddScript(string typed)
    {
        var number = typed.Trim();
        if (number.Length == 0) return;

        _script = number;
        if (!_lines.Any(l => l.IsScript)) _lines.Add(ScriptLine(NextLineNo()));
        SampleChanged();
    }

    private void ResetSample()
    {
        _lines.Clear();
        _lines.AddRange(DefaultLines());
        _script = SampleScript;
        _patient = SamplePatient;
        _medicalAid = true;
        SampleChanged();
    }

    /// Nothing made up left on the slip, for the administrator to fill.
    internal void ClearSample()
    {
        _lines.Clear();
        _script = null;
        _patient = null;
        _medicalAid = false;
        SampleChanged();
    }

    /// One thing on the sample that can be taken off it.
    private sealed record SampleItem(string Text, Action Remove)
    {
        public override string ToString() => Text;
    }

    /// What is on the sample now, each with how to take it off.
    private IEnumerable<SampleItem> SampleItems()
    {
        foreach (var line in _lines.Where(l => !l.IsScript).ToList())
            yield return new SampleItem(
                $"{line.ProductCode} {line.Description}  {line.Quantity:0.###} @ {line.UnitPrice:N2}".Trim(),
                () => _lines.Remove(line));

        if (_lines.Any(l => l.IsScript))
            yield return new SampleItem($"Script charge {_script}".Trim(), () =>
            {
                _lines.RemoveAll(l => l.IsScript);
                _script = null;
            });

        if (_patient is { } patient)
            yield return new SampleItem(
                patient.File is null ? $"Patient: {patient.Name}" : $"Patient: {patient.Name} ({patient.File})",
                () => _patient = null);

        if (_medicalAid && _lines.Any(l => l.IsScript))
            yield return new SampleItem($"Medical aid: {SampleScheme} pays {SampleSchemeShare:N2}",
                () => _medicalAid = false);
    }

    internal IReadOnlyList<string> SampleItemTexts() => SampleItems().Select(i => i.Text).ToList();

    internal void RemoveSampleItem(int index)
    {
        var items = SampleItems().ToList();
        if (index < 0 || index >= items.Count) return;

        items[index].Remove();
        SampleChanged();
        if (_items.Items.Count > 0) _items.SelectedIndex = Math.Min(index, _items.Items.Count - 1);
    }

    private void SampleChanged()
    {
        _search.Clear();
        _items.BeginUpdate();
        _items.Items.Clear();
        _items.Items.AddRange(SampleItems().ToArray<object>());
        _items.EndUpdate();
        Redraw();
    }

    private Control BuildSearch()
    {
        // One column, the box and the list the full width and the buttons
        // wrapping under them: beside them, on a narrow window, the buttons
        // left the box and the list a few letters wide.
        var panel = new TableLayoutPanel
        {
            AutoSize = true,
            ColumnCount = 1,
            Dock = DockStyle.Top,
            Padding = new Padding(12, 4, 12, 4)
        };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        var label = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 3, 3, 2),
            Text = "Add to the sample - type a product or a script number and pick from the list, or find one"
        };

        _search.Dock = DockStyle.Fill;
        _search.Name = "txtSampleSearch";
        _search.PlaceholderText = "Product name or code, or script number";

        if (_scopes is not null)
        {
            _suggestions = new SearchSuggestions(_search, SuggestAsync);
            _suggestions.Chosen += (_, chosen) => Take(chosen);
        }

        // Enter with nothing picked: a name opens the product list on it,
        // digits are taken as the script number. Kept from Save, the
        // window's own Enter.
        _search.PreviewKeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter && _search.Text.Trim().Length > 0) e.IsInputKey = true;
        };
        _search.KeyDown += async (_, e) =>
        {
            if (e.Handled || e.KeyCode != Keys.Enter || _search.Text.Trim().Length == 0) return;
            e.SuppressKeyPress = true;

            if (Suggest.IsName(_search.Text)) await FindProductAsync();
            else AddScript(_search.Text);
        };

        var product = new Button { AutoSize = true, Name = "btnFindProduct", Text = "Find &product..." };
        product.Click += async (_, _) => await FindProductAsync();
        var script = new Button { AutoSize = true, Name = "btnFindScript", Text = "Find sc&ript..." };
        script.Click += (_, _) => FindScript();
        var typed = new Button { AutoSize = true, Name = "btnAddScript", Text = "&Use as script no." };
        typed.Click += (_, _) => AddScript(_search.Text);
        var reset = new Button { AutoSize = true, Name = "btnResetSample", Text = "Reset sa&mple" };
        reset.Click += (_, _) => ResetSample();

        var onSample = new Label
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(3, 8, 3, 2),
            Text = "On the sample - select one and press Delete to take it off, or clear it all and add your own"
        };

        _items.Dock = DockStyle.Fill;
        _items.HorizontalScrollbar = true;
        _items.IntegralHeight = false;
        _items.MinimumSize = new Size(0, 84);
        _items.Name = "lstSampleItems";
        _items.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Delete) return;
            e.Handled = true;
            RemoveSampleItem(_items.SelectedIndex);
        };

        var remove = new Button { AutoSize = true, Name = "btnRemoveSampleItem", Text = "Remo&ve from sample" };
        remove.Click += (_, _) => RemoveSampleItem(_items.SelectedIndex);
        var clear = new Button { AutoSize = true, Name = "btnClearSample", Text = "C&lear sample" };
        clear.Click += (_, _) => ClearSample();

        panel.Controls.Add(label);
        panel.Controls.Add(_search);
        panel.Controls.Add(Buttons(product, script, typed, reset));
        panel.Controls.Add(onSample);
        panel.Controls.Add(_items);
        panel.Controls.Add(Buttons(remove, clear));

        SampleChanged();
        return panel;

        static FlowLayoutPanel Buttons(params Button[] buttons)
        {
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 2, 0, 0),
                WrapContents = true
            };
            row.Controls.AddRange(buttons);
            return row;
        }
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
