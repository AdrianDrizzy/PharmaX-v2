namespace PharmaX.Desktop.Mvp;

/// <summary>
/// The look of PharmaX in one place: fonts, colours and the spacing
/// every screen measures from.
/// </summary>
/// <remarks>
/// A theme rather than per-form choices, because forty screens each
/// picking their own font size is how a product ends up looking like
/// forty products. Applied by PhxFormBase, so a new screen inherits it
/// without doing anything.
/// </remarks>
public static class PhxTheme
{
    // ---- Spacing ---------------------------------------------------------

    /// The gutter every screen leaves round its edge.
    public const int Gutter = 12;

    /// The height of a header or footer band.
    public const int BandHeight = 52;

    /// One row of a labelled form.
    public const int RowHeight = 34;

    // ---- Type ------------------------------------------------------------

    public static Font Body { get; } = new("Segoe UI", 9.75f);

    public static Font Emphasis { get; } =
        new("Segoe UI", 9.75f, FontStyle.Bold);

    /// The name of the pharmacy on the shell's home screen.
    public static Font Display { get; } =
        new("Segoe UI", 19f, FontStyle.Bold);

    /// The title of a screen or a section of one.
    public static Font Heading { get; } =
        new("Segoe UI", 13f, FontStyle.Bold);

    public static Font Subheading { get; } =
        new("Segoe UI", 10.5f, FontStyle.Bold);

    /// <summary>
    /// Figures. Still Segoe UI rather than a monospaced face: its digits
    /// are already the same width as each other, so a right-aligned
    /// column of money lines up on the decimal point without the screen
    /// looking like a terminal.
    /// </summary>
    public static Font Figure { get; } = new("Segoe UI", 11f);

    public static Font FigureLarge { get; } =
        new("Segoe UI", 18f, FontStyle.Bold);

    // ---- Colour ----------------------------------------------------------

    /// The page behind a screen's content.
    public static Color Surface { get; } = Color.FromArgb(250, 250, 252);

    /// A header or footer band, one step darker than the surface.
    public static Color Band { get; } = Color.FromArgb(240, 242, 246);

    public static Color Line { get; } = Color.FromArgb(222, 226, 232);

    public static Color Muted { get; } = Color.FromArgb(104, 112, 124);

    /// Informative, not alarming: offline is a normal working state.
    public static Color Information { get; } = Color.FromArgb(226, 239, 252);

    /// Something needs a decision before it can proceed.
    public static Color Attention { get; } = Color.FromArgb(255, 244, 214);

    /// Something is wrong and a person has to look at it.
    public static Color Problem { get; } = Color.FromArgb(255, 226, 226);

    public static Color ProblemText { get; } = Color.FromArgb(168, 32, 32);

    /// A figure that is short rather than over.
    public static Color Negative { get; } = Color.FromArgb(168, 32, 32);

    /// The one colour the eye is meant to be drawn to on a screen.
    public static Color Accent { get; } = Color.FromArgb(0, 95, 184);

    public static Color AccentDisabled { get; } =
        Color.FromArgb(226, 229, 234);

    // ---- Building blocks -------------------------------------------------

    /// <summary>
    /// A header band across the top of a screen. Returns the panel so
    /// the caller can put its own labels in it.
    /// </summary>
    public static Panel HeaderBand(int height = BandHeight) => new()
    {
        BackColor = Band,
        Dock = DockStyle.Top,
        Height = height,
        Padding = new Padding(Gutter, 8, Gutter, 8)
    };

    public static Panel FooterBand(int height = BandHeight) => new()
    {
        BackColor = Band,
        Dock = DockStyle.Bottom,
        Height = height,
        Padding = new Padding(Gutter, 8, Gutter, 8)
    };

    /// A one-pixel rule, for separating a band from the content.
    public static Panel Rule(DockStyle edge = DockStyle.Top) => new()
    {
        BackColor = Line,
        Dock = edge,
        Height = 1
    };

    public static Label Caption(string text, Point at, int width = 120) =>
        new()
        {
            AutoSize = false,
            Font = Body,
            ForeColor = Muted,
            Location = at,
            Size = new Size(width, 20),
            Text = text,
            TextAlign = ContentAlignment.MiddleLeft
        };

    /// <summary>
    /// A right-aligned figure. Right-aligned because that is how a
    /// column of money is read, and money is most of what PharmaX shows.
    /// </summary>
    public static Label Figures(Point at, int width = 140,
                                bool large = false) => new()
    {
        AutoSize = false,
        Font = large ? FigureLarge : Figure,
        Location = at,
        Size = new Size(width, large ? 30 : 22),
        Text = Format.Amount(0m),
        TextAlign = ContentAlignment.MiddleRight
    };

    /// <summary>
    /// The one button on a screen that does the thing the screen exists
    /// for: post the receipt, take the payment, close the till.
    /// </summary>
    /// <remarks>
    /// Filled rather than merely bolder. On a till the operator is not
    /// reading the screen, they are hitting the button they hit a
    /// thousand times before, and a block of colour is what the hand
    /// aims at. Flat style is also the marker Apply uses to leave these
    /// buttons alone, so the theme does not paint over them afterwards.
    /// </remarks>
    public static void MakePrimary(Button button)
    {
        ArgumentNullException.ThrowIfNull(button);

        button.Font = Emphasis;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.UseVisualStyleBackColor = false;

        // A flat-styled button keeps its colour when disabled, which
        // would leave a dead control looking like the live one.
        button.EnabledChanged += (sender, _) => PaintPrimary((Button)sender!);
        PaintPrimary(button);
    }

    private static void PaintPrimary(Button button)
    {
        button.BackColor = button.Enabled ? Accent : AccentDisabled;
        button.ForeColor = button.Enabled ? Color.White : Muted;

        button.FlatAppearance.MouseOverBackColor = button.Enabled
            ? Color.FromArgb(0, 82, 160)
            : AccentDisabled;
    }

    /// <summary>
    /// Applies the theme to a form and everything already on it. Called
    /// by PhxFormBase after InitializeComponent, so a screen gets this
    /// without asking.
    /// </summary>
    public static void Apply(Form form)
    {
        ArgumentNullException.ThrowIfNull(form);

        form.BackColor = Surface;
        form.Font = Body;

        ApplyTo(form.Controls);
        SeatRightAnchoredControls(form);
    }

    /// <summary>
    /// Brings back any control that is anchored to the right-hand edge
    /// and has ended up off the side of the window.
    /// </summary>
    /// <remarks>
    /// A right-anchored control remembers how far its right edge sits
    /// from its parent's right edge, and it remembers that distance as
    /// it was the last time the control's own bounds changed. In a
    /// hand-written designer that moment is while the band it sits in
    /// is still the default 200 pixels wide, so the remembered distance
    /// comes out several hundred pixels negative and Windows Forms
    /// dutifully lays the control out that far past the edge. Close
    /// buttons, record counts and the whole totals block on two screens
    /// were invisible for this reason, on every screen, from the first
    /// build.
    ///
    /// Re-seating them here - once, after the form has its real size -
    /// teaches each anchor the distance it should have had, and the
    /// anchor behaves correctly from then on. Only controls that are
    /// actually off the edge are touched, so a screen that was laid out
    /// properly is left exactly as its designer wrote it.
    /// </remarks>
    private static void SeatRightAnchoredControls(Control parent)
    {
        var available = parent.ClientSize.Width - parent.Padding.Right;

        foreach (Control child in parent.Controls)
        {
            if (child.HasChildren) SeatRightAnchoredControls(child);

            var anchor = child.Anchor;

            if (!anchor.HasFlag(AnchorStyles.Right)
                || anchor.HasFlag(AnchorStyles.Left)) continue;

            if (child.Right <= available) continue;

            child.Left = available - Gutter - child.Width;
        }
    }

    private static void ApplyTo(Control.ControlCollection controls)
    {
        foreach (Control control in controls)
        {
            switch (control)
            {
                case DataGridView grid:
                    ApplyToGrid(grid);
                    break;

                // Flat is MakePrimary's marker. Repainting it here is
                // what would quietly undo the accent on every screen.
                case Button button when button.FlatStyle != FlatStyle.Flat:
                    button.FlatStyle = FlatStyle.System;
                    break;

                case TextBox or ComboBox or CheckedListBox or ListBox:
                    control.BackColor = SystemColors.Window;
                    break;
            }

            if (control.HasChildren) ApplyTo(control.Controls);
        }
    }

    /// <summary>
    /// The grid settings every list in PharmaX wants. PhxGrid sets them
    /// itself; this catches the plain DataGridViews used where a grid has
    /// to be editable, which is where the default grey background and
    /// cramped rows were showing through.
    /// </summary>
    public static void ApplyToGrid(DataGridView grid)
    {
        ArgumentNullException.ThrowIfNull(grid);

        grid.BackgroundColor = SystemColors.Window;
        grid.BorderStyle = BorderStyle.None;
        grid.GridColor = Line;
        grid.EnableHeadersVisualStyles = false;
        grid.RowHeadersVisible = false;
        grid.AllowUserToResizeRows = false;

        if (grid.RowTemplate.Height < 26) grid.RowTemplate.Height = 26;

        grid.ColumnHeadersHeight = 32;
        grid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.EnableResizing;

        grid.ColumnHeadersDefaultCellStyle.BackColor = Band;
        grid.ColumnHeadersDefaultCellStyle.ForeColor = SystemColors.ControlText;
        grid.ColumnHeadersDefaultCellStyle.Font = Emphasis;
        grid.ColumnHeadersDefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;

        grid.DefaultCellStyle.Padding = new Padding(6, 0, 6, 0);
        grid.DefaultCellStyle.SelectionBackColor = Color.FromArgb(210, 226, 246);
        grid.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;

        grid.AlternatingRowsDefaultCellStyle.BackColor =
            Color.FromArgb(248, 249, 251);

        // Detached first so that applying the theme twice to the same
        // grid does not stack handlers.
        grid.CellFormatting -= ReadableCell;
        grid.CellFormatting += ReadableCell;

        grid.DataError -= QuietDataError;
        grid.DataError += QuietDataError;
    }

    /// <summary>
    /// A value a cell cannot show or take is marked on the cell and
    /// logged, rather than raised as the framework's own modal dialog.
    /// </summary>
    /// <remarks>
    /// Nothing in the application handled DataError, so any value a grid
    /// could not format put up "DataGridView Default Error Dialog" - 47 of
    /// them in one test run across Returns, Claims and Capture Script -
    /// each one stopping the screen until it was dismissed (DEF-0017).
    /// The cause of those was ReadableCell below, and is fixed there; this
    /// is so that the next one is a red mark on a cell and a line in the
    /// log, not a wall of dialogs.
    /// </remarks>
    private static void QuietDataError(object? sender,
        DataGridViewDataErrorEventArgs e)
    {
        if (e is null) return;

        e.ThrowException = false;
        e.Cancel = e.Context.HasFlag(DataGridViewDataErrorContexts.Commit);

        if (e.Exception is not null) ErrorHandler.Log(e.Exception);

        if (sender is DataGridView grid && e.RowIndex >= 0
            && e.RowIndex < grid.Rows.Count && e.ColumnIndex >= 0)
            grid.Rows[e.RowIndex].Cells[e.ColumnIndex].ErrorText =
                "This value could not be shown or saved: "
                + (e.Exception?.Message ?? "unknown problem");
    }

    /// <summary>
    /// Colours a grid row so that the colour survives selection.
    /// </summary>
    /// <remarks>
    /// A background set on the row alone disappears the moment the row
    /// is selected, and a grid selects its first row on its own. That
    /// is how a till that is eighty rand short came to look exactly
    /// like one that balanced. A row colour here is a warning, so it
    /// has to outlast a mouse click.
    /// </remarks>
    public static void Flag(DataGridViewRow row, Color colour)
    {
        ArgumentNullException.ThrowIfNull(row);

        row.DefaultCellStyle.BackColor = colour;
        row.DefaultCellStyle.SelectionBackColor = Darken(colour);
        row.DefaultCellStyle.SelectionForeColor = SystemColors.ControlText;
    }

    private static Color Darken(Color colour) => Color.FromArgb(
        (int)(colour.R * 0.90), (int)(colour.G * 0.90),
        (int)(colour.B * 0.90));

    /// <summary>
    /// A column bound to a bool renders True and False by default,
    /// which is programmer output on a pharmacist's screen.
    /// </summary>
    /// <remarks>
    /// Only where the cell shows text. A tick box's formatted value has to
    /// stay a bool, and turning it into "Yes" was what raised the
    /// FormatException dialogs - and why Capture Script's "Generic ok" box
    /// showed unticked while its value was yes (DEF-0017).
    /// </remarks>
    private static void ReadableCell(object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (e is null) return;

        // A time stored in UTC is shown in the till's own time. The
        // stocktake list and the users list bound their UTC columns
        // straight to the grid, so a count opened at 05:59 was listed at
        // 03:59 (DEF-0028). Every stored time carries the suffix, so the
        // rule follows the name rather than being repeated per screen.
        if (e.Value is DateTime stamp && sender is DataGridView grid
            && e.ColumnIndex >= 0
            && grid.Columns[e.ColumnIndex].DataPropertyName
                   .EndsWith("Utc", StringComparison.Ordinal))
        {
            var local = DateTime.SpecifyKind(stamp, DateTimeKind.Utc)
                                .ToLocalTime();

            e.Value = local.ToString(
                string.IsNullOrEmpty(e.CellStyle?.Format)
                    ? "dd/MM/yyyy HH:mm"
                    : e.CellStyle.Format,
                System.Globalization.CultureInfo.CurrentCulture);
            e.FormattingApplied = true;
            return;
        }

        if (e.Value is not bool flag) return;
        if (e.DesiredType != typeof(string)) return;

        e.Value = flag ? "Yes" : "No";
        e.FormattingApplied = true;
    }
}
