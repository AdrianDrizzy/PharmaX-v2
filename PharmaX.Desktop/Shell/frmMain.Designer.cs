using PharmaX.Desktop.Mvp;

namespace PharmaX.Desktop.Shell;

partial class frmMain
{
    private System.ComponentModel.IContainer components = null!;
    private MenuStrip mnuMain = null!;
    private StatusStrip staMain = null!;
    private Panel pnlContent = null!;
    private Panel pnlHomeHeader = null!;
    private Panel pnlHomeText = null!;
    private Panel pnlHomeLogo = null!;
    private Label lblHomeTitle = null!;
    private Label lblHomeContext = null!;
    private Label lblHomeCaption = null!;
    private FlowLayoutPanel flpTiles = null!;
    private ToolStripStatusLabel lblUser = null!;
    private ToolStripStatusLabel lblTill = null!;
    private ToolStripStatusLabel lblSession = null!;
    private ToolStripStatusLabel lblConnectivity = null!;

    protected override void Dispose(bool disposing)
    {
        if (disposing) components?.Dispose();
        base.Dispose(disposing);
    }

    private void InitializeComponent()
    {
        components = new System.ComponentModel.Container();
        mnuMain = new MenuStrip();
        staMain = new StatusStrip();
        pnlContent = new Panel();
        lblUser = new ToolStripStatusLabel();
        lblTill = new ToolStripStatusLabel();
        lblSession = new ToolStripStatusLabel();
        lblConnectivity = new ToolStripStatusLabel();
        SuspendLayout();

        // The menu mirrors the incumbent structure, which is what the
        // staff already know (specification Section 2.3). Staff moving to
        // PharmaX should find things where their hands already expect them.
        mnuMain.Items.AddRange(new ToolStripItem[]
        {
            Menu("&File", null,
                Item("Printer Setup", "printer-setup"),
                Item("Till Session", "till", Keys.Control | Keys.T),
                Item("Log Off", "logoff"),
                Item("Change Password", "change-password"),
                Separator(),
                Item("E&xit", "exit", Keys.Control | Keys.X)),

            Menu("&Dispensary", "dispensary",
                Item("Dispensing", "dispensing", Keys.F2),
                Item("Patients", "patients", Keys.F4),
                Item("Re-Print Script", "reprint-script"),
                Item("Script Maintenance", "script-maintenance"),
                Item("Labels", "labels"),
                Item("Tariffs", "tariffs"),
                Item("Merge Patient Files", "merge-patients"),
                Item("Quotation", "quotation"),
                Item("Medical Aid Societies", "schemes"),
                Item("Drug Returns", "drug-returns")),

            Menu("&Point of Sale", "pos",
                Item("Sell", "pos", Keys.F3),
                Item("Returns and Credit Notes", "returns", Keys.F5)),

            Menu("&Inventory", "inventory",
                Item("Departments", "departments"),
                Item("Suppliers", "suppliers"),
                Item("Purchase Orders", "purchase-orders", Keys.F7),
                Item("Goods Receipt", "goods-receipt", Keys.F8),
                Item("Manufacturers", "manufacturers"),
                Item("Products", "products", Keys.F6),
                Item("Stock", "stock"),
                Item("Transfers", "transfers"),
                Item("Breakages", "breakages"),
                Item("Expired Stock", "expired-stock"),
                Item("Price Changes", "price-changes")),

            Menu("&Reports", "reports",
                Item("Reports", "reports", Keys.F9)),

            Menu("&Stocktake", "stocktake",
                Item("New Stocktake", "stocktake-new"),
                Item("Capture", "stocktake-capture"),
                Item("Variance", "stocktake-variance"),
                Item("Post", "stocktake-post")),

            Menu("&Administration", "admin",
                Item("VAT Codes", "vat-codes"),
                Item("Users", "users"),
                Item("Branches", "branches"),
                Item("Transaction Types", "transaction-types"),
                Item("Day-End Posting", "day-end"),
                Item("System Setup", "system-setup"),
                Item("System Console", "system-console"),
                Item("Control Variables", "control-variables")),

            Menu("&Help", null,
                Item("About", "about"))
        });

        mnuMain.Location = new Point(0, 0);
        mnuMain.Name = "mnuMain";
        mnuMain.Size = new Size(1200, 24);
        mnuMain.TabIndex = 0;

        // pnlContent
        pnlContent.Dock = DockStyle.Fill;
        pnlContent.Location = new Point(0, 24);
        pnlContent.Name = "pnlContent";
        pnlContent.Padding = new Padding(28, 22, 28, 22);
        pnlContent.Size = new Size(1200, 654);
        pnlContent.TabIndex = 1;

        BuildHome();

        // Status strip
        lblUser.Name = "lblUser";
        lblUser.Size = new Size(120, 19);
        lblUser.Text = "Not signed in";
        lblUser.BorderSides = ToolStripStatusLabelBorderSides.Right;

        lblTill.Name = "lblTill";
        lblTill.Size = new Size(120, 19);
        lblTill.BorderSides = ToolStripStatusLabelBorderSides.Right;

        lblSession.Name = "lblSession";
        lblSession.Size = new Size(140, 19);
        lblSession.BorderSides = ToolStripStatusLabelBorderSides.Right;

        lblConnectivity.Name = "lblConnectivity";
        lblConnectivity.Size = new Size(160, 19);
        lblConnectivity.Spring = true;
        lblConnectivity.TextAlign = ContentAlignment.MiddleRight;

        staMain.Items.AddRange(new ToolStripItem[]
            { lblUser, lblTill, lblSession, lblConnectivity });
        staMain.Location = new Point(0, 678);
        staMain.Name = "staMain";
        staMain.Size = new Size(1200, 22);
        staMain.TabIndex = 2;

        // frmMain
        AutoScaleDimensions = new SizeF(7F, 15F);
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(1200, 700);
        Controls.Add(pnlContent);
        Controls.Add(staMain);
        Controls.Add(mnuMain);

        // Child screens open as dialogs, not as MDI children: MDI is a
        // dead end for keyboard-driven work.
        IsMdiContainer = false;
        MainMenuStrip = mnuMain;
        Name = "frmMain";
        StartPosition = FormStartPosition.CenterScreen;
        Text = "PharmaX";
        WindowState = FormWindowState.Maximized;
        ResumeLayout(false);
        PerformLayout();
    }

    /// <summary>
    /// The shell's home screen. The menu keeps every function; these
    /// cards are the handful a person actually reaches for, sized for
    /// a mouse and a tired afternoon.
    /// </summary>
    private void BuildHome()
    {
        lblHomeTitle = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Font = PhxTheme.Display,
            Height = 42,
            Text = "PharmaX"
        };

        lblHomeContext = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            ForeColor = PhxTheme.Muted,
            Height = 26,
            Text = ""
        };

        // Width stays at zero until there is a logo to put in it, so a
        // pharmacy that has not set one gets no empty gap.
        pnlHomeLogo = new Panel
        {
            Dock = DockStyle.Left,
            Name = "pnlHomeLogo",
            Padding = new Padding(0, 0, 20, 0),
            Width = 0
        };

        pnlHomeLogo.Paint += (_, e) => PaintHomeLogo(e);

        pnlHomeText = new Panel { Dock = DockStyle.Fill };
        pnlHomeText.Controls.Add(lblHomeContext);
        pnlHomeText.Controls.Add(lblHomeTitle);

        // The logo is added last so it claims the left edge before the
        // text panel fills what is left.
        pnlHomeHeader = new Panel { Dock = DockStyle.Top, Height = 72 };
        pnlHomeHeader.Controls.Add(pnlHomeText);
        pnlHomeHeader.Controls.Add(pnlHomeLogo);

        lblHomeCaption = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Top,
            Font = PhxTheme.Subheading,
            Height = 42,
            Padding = new Padding(0, 16, 0, 0),
            Text = "What would you like to do?"
        };

        flpTiles = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            Name = "flpTiles",
            WrapContents = true
        };

        foreach (var (title, hint, key, group) in new[]
        {
            ("Sell", "F3   Point of sale", "pos", "pos"),
            ("Dispense", "F2   Script and label", "dispensing", "dispensary"),
            ("Patients", "F4   Find and edit", "patients", "dispensary"),
            ("Returns", "F5   And credit notes", "returns", "returns"),
            ("Products", "F6   Prices and stock", "products", "inventory"),
            ("Goods receipt", "F8   Book a delivery in",
             "goods-receipt", "goods-receipt"),
            ("Purchase orders", "F7   Order from a supplier",
             "purchase-orders", "inventory"),
            ("Stocktake", "Count and post", "stocktake-capture", "stocktake"),
            ("Reports", "F9   Sales, stock, VAT", "reports", "reports"),
            ("Till session", "Ctrl+T   Float and cash up", "till", "till")
        })
        {
            flpTiles.Controls.Add(new HomeTile(title, hint, key, group));
        }

        // Added bottom-up: docking is resolved from the highest child
        // index down, so the title added last is the one at the top.
        pnlContent.Controls.Add(flpTiles);
        pnlContent.Controls.Add(lblHomeCaption);
        pnlContent.Controls.Add(pnlHomeHeader);
    }

    private static ToolStripMenuItem Menu(string text, string? key,
        params ToolStripItem[] children)
    {
        var menu = new ToolStripMenuItem(text) { Tag = key };
        menu.DropDownItems.AddRange(children);
        return menu;
    }

    private static ToolStripMenuItem Item(string text, string key,
        Keys shortcut = Keys.None)
    {
        var item = new ToolStripMenuItem(text) { Tag = key };

        if (shortcut != Keys.None)
        {
            item.ShortcutKeys = shortcut;
            item.ShowShortcutKeys = true;
        }

        return item;
    }

    private static ToolStripSeparator Separator() => new();
}
