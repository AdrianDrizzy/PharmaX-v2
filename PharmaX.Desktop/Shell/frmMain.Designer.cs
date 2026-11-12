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
    private Label lblHomeParticulars = null!;
    private Label lblHomeContext = null!;
    private Label lblHomeCaption = null!;
    private FlowLayoutPanel flpTiles = null!;
    private Panel pnlCaption = null!;
    private TextBox txtGoTo = null!;
    private ToolStripStatusLabel lblUser = null!;
    private ToolStripStatusLabel lblTill = null!;
    private ToolStripStatusLabel lblSession = null!;
    private ToolStripStatusLabel lblConnectivity = null!;
    private ToolStripStatusLabel lblWaiting = null!;
    private ToolStripStatusLabel lblAlerts = null!;
    private ToolStripStatusLabel lblAssistant = null!;

    /// <summary>
    /// Re-reads the collection queue while the shell is up.
    /// </summary>
    /// <remarks>
    /// Fifteen seconds rather than the collection screen's five: this
    /// is the glance-at-the-corner count, not the queue somebody is
    /// working from, and it runs all day on every till in the pharmacy.
    /// </remarks>
    private System.Windows.Forms.Timer tmrWaiting = null!;

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
        lblWaiting = new ToolStripStatusLabel();
        lblAlerts = new ToolStripStatusLabel();
        lblAssistant = new ToolStripStatusLabel();
        tmrWaiting = new System.Windows.Forms.Timer(components)
        {
            Interval = 15000
        };

        tmrWaiting.Tick += (_, _) => WaitingTick?.Invoke(this, EventArgs.Empty);
        SuspendLayout();

        // The menu mirrors the incumbent structure, which is what the
        // staff already know (specification Section 2.3). Staff moving to
        // PharmaX should find things where their hands already expect them.
        mnuMain.Items.AddRange(new ToolStripItem[]
        {
            Menu("&File", null,
                Item("Printer Setup", "printer-setup"),
                Item("Till Session", "till", Keys.Control | Keys.T),
                Item("Cash Up", "cash-up", Keys.Control | Keys.U),
                Item("End-of-Day Cash-Up", "end-of-day"),
                Item("Log Off", "logoff"),
                Item("Change Password", "change-password"),
                Separator(),
                Item("E&xit", "exit", Keys.Control | Keys.X)),

            Menu("&Dispensary", "dispensary",
                Item("Dispensing", "dispensing", Keys.F2),
                Item("Patients", "patients", Keys.F4),
                Item("Re-Print Script", "reprint-script"),
                Item("Script Maintenance", "script-maintenance"),
                Item("Dispensary Quote", "dispensary-quote"),
                Item("Labels", "labels"),
                Item("Tariffs", "tariffs"),
                Item("Merge Patient Files", "merge-patients"),
                Item("Medical Aid Societies", "schemes"),
                Item("Prescribers", "prescribers"),
                Item("Dosage Codes", "dosage-codes"),
                Item("To Follow", "to-follow"),
                Item("Pre-packs", "prepack"),
                Item("Drug Returns", "drug-returns")),

            Menu("&Point of Sale", "pos",
                Item("Sell", "pos", Keys.F3),
                Item("Scripts to Collect", "collect", Keys.F11),
                Item("Returns and Credit Notes", "returns", Keys.F5),
                Item("Receipts (Reprint)", "receipts"),

                // A priced list for a customer at the counter is till
                // work, and under Dispensary it opened that whole heading
                // to a cashier for the sake of one entry.
                Item("Quotation", "quotation")),

            Menu("&Inventory", "inventory",
                Item("Stock Alerts", "alerts", Keys.F12),
                Item("Departments", "departments"),
                Item("Categories", "categories"),
                Item("Suppliers", "suppliers"),
                Item("Purchase Orders", "purchase-orders", Keys.F7),
                Item("Goods Receipt", "goods-receipt", Keys.F8),
                Item("Manufacturers", "manufacturers"),
                Item("Products", "products", Keys.F6),
                Item("Stock on hand", "stock"),
                Item("Stock Adjustments", "stock-adjust"),
                Item("Return to Supplier", "supplier-return"),
                Item("Move Stock and Pre-packs", "move-stock"),
                Item("Transfers report", "transfers"),
                Item("Breakages report", "breakages"),
                Item("Expired stock report", "expired-stock"),
                Item("Price changes report", "price-changes"),
                Item("Mass Price Change", "mass-price")),

            // The pharmacy's own books. Its own heading rather than a
            // corner of Administration: a manager doing the month end is
            // not administering the system, and the three screens here
            // are used together.
            // Day-End Posting lives here rather than under Administration:
            // turning the day's takings into a journal is the manager's
            // bookkeeping, and under Administration it was out of reach
            // of the manager who does it (DEF-0015).
            Menu("&Accounts", "accounts",
                Item("Trial Balance", "trial-balance"),
                Item("Journal Entry", "journal"),
                Item("Patient Accounts", "accounts-receivable"),
                Item("Medical Aid Claims", "claims"),
                Item("Chart of Accounts", "chart-of-accounts"),
                Item("Day-End Posting", "day-end")),

            Menu("&Reports", "reports",
                Item("Reports", "reports", Keys.F9)),

            Menu("&Stocktake", "stocktake",
                Item("New Stocktake", "stocktake-new"),
                Item("Capture", "stocktake-capture"),
                Item("Variance", "stocktake-variance"),
                Item("Post", "stocktake-post")),

            // Named for what they open. "VAT Codes" and "Transaction
            // Types" read as maintenance screens and opened reports
            // (DEF-0029): the VAT rate is set on each product, and the
            // movement types are fixed.
            Menu("&Administration", "admin",
                Item("VAT Summary", "vat-codes"),
                Item("Users", "users"),
                Item("Role permissions", "role-permissions"),
                Item("Branches", "branches"),
                Item("Stock Transactions", "transaction-types"),
                Item("System Setup", "system-setup"),
                Item("Receipt Template", "receipt-template"),
                Item("Import Data", "data-import"),
                Item("Back Up Now", "backup"),
                Item("System Console", "system-console"),
                Item("Control Variables", "control-variables")),

            // Also Ctrl+Shift+A on every screen; listed here so it can be found.
            Menu("&Help", null,
                Item("Ask PharmaX", "assistant", Mvp.PhxFormBase.AssistantKey),
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

        // Coloured and bold, because it is the one thing in the status
        // bar that means a person is waiting rather than a machine is
        // fine. Hidden entirely when the queue is empty: a permanent
        // "0 waiting" is something the eye stops reading by Tuesday.
        lblWaiting.Name = "lblWaiting";
        lblWaiting.Font = PhxTheme.Emphasis;
        lblWaiting.ForeColor = PhxTheme.Title;
        lblWaiting.Size = new Size(160, 19);
        lblWaiting.BorderSides = ToolStripStatusLabelBorderSides.Right;
        lblWaiting.Visible = false;

        // Red rather than the waiting queue's blue, and hidden when
        // there is nothing wrong. This one means stock is expiring or
        // gone, which is money, not a person at a counter.
        lblAlerts.Name = "lblAlerts";
        lblAlerts.Font = PhxTheme.Emphasis;
        lblAlerts.ForeColor = PhxTheme.ProblemText;
        lblAlerts.Size = new Size(150, 19);
        lblAlerts.BorderSides = ToolStripStatusLabelBorderSides.Right;
        lblAlerts.Visible = false;

        // The stock assistant: there only while draft orders it made are
        // waiting for somebody to look them over and send them.
        lblAssistant.Name = "lblAssistant";
        lblAssistant.Font = PhxTheme.Emphasis;
        lblAssistant.LinkColor = PhxTheme.Title;
        lblAssistant.IsLink = true;
        lblAssistant.LinkBehavior = LinkBehavior.HoverUnderline;
        lblAssistant.Size = new Size(220, 19);
        lblAssistant.BorderSides = ToolStripStatusLabelBorderSides.Right;
        lblAssistant.Visible = false;

        lblConnectivity.Name = "lblConnectivity";
        lblConnectivity.Size = new Size(160, 19);
        lblConnectivity.Spring = true;
        lblConnectivity.TextAlign = ContentAlignment.MiddleRight;

        staMain.Items.AddRange(new ToolStripItem[]
        {
            lblUser, lblTill, lblSession, lblWaiting, lblAlerts,
            lblAssistant, lblConnectivity
        });
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
        // The pharmacy's trading name, set by the presenter. Never the
        // product's: this is the pharmacy's till, not PharmaX's.
        lblHomeTitle = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Dock = DockStyle.Top,
            Font = PhxTheme.Display,
            ForeColor = PhxTheme.Title,
            Height = 42,
            Name = "lblHomeTitle",
            Text = ""
        };

        // Its address and VAT number, as the receipts print them.
        lblHomeParticulars = new Label
        {
            AutoEllipsis = true,
            AutoSize = false,
            Dock = DockStyle.Top,
            Height = 24,
            Name = "lblHomeParticulars",
            Text = "",
            Visible = false
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
        pnlHomeText.Controls.Add(lblHomeParticulars);
        pnlHomeText.Controls.Add(lblHomeTitle);

        // The logo is added last so it claims the left edge before the
        // text panel fills what is left. White, because a logo comes on a
        // white ground and on the tinted surface it showed as a box.
        pnlHomeHeader = new Panel
        {
            BackColor = PhxTheme.Card,
            Dock = DockStyle.Top,
            Height = 108,
            Padding = new Padding(12, 6, 12, 6)
        };
        pnlHomeHeader.Controls.Add(pnlHomeText);
        pnlHomeHeader.Controls.Add(pnlHomeLogo);

        lblHomeCaption = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = PhxTheme.Subheading,
            Padding = new Padding(0, 16, 0, 0),
            Text = "What would you like to do?"
        };

        // Any screen by name, for the menu entry nobody can find: typed
        // into, it offers what matches, and Enter opens the first.
        txtGoTo = new TextBox
        {
            AccessibleName = "Go to a screen",
            Dock = DockStyle.Top,
            Name = "txtGoTo",
            PlaceholderText = "Go to any screen, e.g. patients, cash up, receive   (Ctrl+K)"
        };

        var goToHolder = new Panel
        {
            Dock = DockStyle.Right,
            Padding = new Padding(0, 12, 0, 0),
            Width = 460
        };
        goToHolder.Controls.Add(txtGoTo);

        pnlCaption = new Panel { Dock = DockStyle.Top, Height = 42 };
        pnlCaption.Controls.Add(lblHomeCaption);
        pnlCaption.Controls.Add(goToHolder);

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
            ("Collect", "F11   Scripts to be paid for", "collect", "pos"),
            ("Dispense", "F2   Script and label", "dispensing", "dispensary"),
            ("Patients", "F4   Find and edit", "patients", "dispensary"),
            ("Returns", "F5   And credit notes", "returns", "returns"),
            ("Products", "F6   Prices and stock", "products", "inventory"),
            ("Stock alerts", "F12   Expiring and short", "alerts",
             "inventory"),
            ("Goods receipt", "F8   Book a delivery in",
             "goods-receipt", "goods-receipt"),
            ("Purchase orders", "F7   Order from a supplier",
             "purchase-orders", "inventory"),
            ("Stocktake", "Count and post", "stocktake-capture", "stocktake"),
            ("Reports", "F9   Sales, stock, VAT", "reports", "reports"),
            ("Trial balance", "The books, in balance", "trial-balance",
             "accounts"),
            ("Till session", "Ctrl+T   Float and cash up", "till", "till")
        })
        {
            flpTiles.Controls.Add(new HomeTile(title, hint, key, group,
                MenuIcons.GetValueOrDefault(key, PhxIcon.File)));
        }

        // Added bottom-up: docking is resolved from the highest child
        // index down, so the title added last is the one at the top. The
        // orange rule under the header is the logo's.
        pnlContent.Controls.Add(flpTiles);
        pnlContent.Controls.Add(pnlCaption);
        pnlContent.Controls.Add(new Panel
        {
            BackColor = PhxTheme.Highlight,
            Dock = DockStyle.Top,
            Height = 3
        });
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
