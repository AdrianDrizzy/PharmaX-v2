# Prompt: finish PharmaX's runtime theming

A prompt for an AI coding assistant (or a developer) picking up the theme
work. It replaces a generic "build a WinForms theme system" brief: PharmaX
already has the engine, so the job is to close the gaps, not to build a
second one.

---

**Role.** You are a senior C# WinForms engineer working in PharmaX, a
pharmacy POS and dispensing application on .NET 8 (`net8.0-windows`,
PerMonitorV2, C# 12, nullable on, analyzers enforced). Extend the existing
theme engine so every screen honours the chosen mode. Do not build a second
engine.

**What exists - reuse it, do not replace it.**

- `PharmaX.Desktop/Mvp/PhxTheme.cs` - the static engine: fonts, spacing and
  every shade; `Use(palette, mode)`; `Changed` (a map of old colour to new);
  `Recolour` (swaps colours by value); `Apply(form)`, `ApplyToGrid`,
  `Flag(row, colour)`, `MakePrimary(button)`; `TitleBar` (dark caption via
  DWM); `StripRenderer` for menus and status bars.
- `PharmaX.Desktop/Mvp/Palette.cs` - `Palette` (the brand primary and
  highlight, from the pharmacy's logo or a preset; accent colours come from
  here, not from the mode) and `ThemeMode` (Light, Dark, Midnight neon, Warm
  paper).
- `PhxFormBase` applies the theme on load and follows `Changed` while open;
  about 47 screens inherit it.
- The Theme menu on the shell (`frmMain.WireTheme`). The choice is saved per
  till as `WorkstationSettingKey.Theme` and restored at sign-in
  (`Program.LoadPharmacy`).
- Tests: xUnit and FluentAssertions in `PharmaX.Tests.Unit`
  (`Adapters/PaletteTests.cs`).

**Invariants.**

1. Light stays as it is today: system colours and native message boxes. The
   E2E scripts find message boxes by their window class, `#32770`. Light is
   the default when nothing is saved.
2. `Recolour` swaps colours by value, so every themed colour must be a
   `PhxTheme` member, and no other colour may equal one.
3. A colour that means something keeps its meaning in every mode: attention
   amber, problem red, inactive grey, negative figures, the test database's
   status bar. It changes shade, not role, and text on it reads at 4.5:1 or
   better.
4. No new packages and no framework upgrade. No change to business logic,
   event handlers, data binding or keyboard behaviour (function keys, Escape,
   Accept and Cancel buttons).
5. The designers are hand-written; editing them is fine.

**Work.**

1. Theme the 12 dialogs that inherit plain `Form`: the five in
   `Controls/Prompts.cs`, `frmJournalLinePrompt`, `frmStocktakeScope`,
   `frmChangePassword`, `frmSupervisorPrompt`, `frmAttachmentDetails`,
   `frmHeldSales` and `frmPayment`. Use one helper, `PhxTheme.Follow(form)`,
   not a change of base class: `PhxFormBase` would also bring function-key
   captions, title branding, the watermark and its Escape handling.
2. Make `Attention`, `Problem`, `ProblemText` and `Negative` shades of the
   mode, worked out in `Use` and listed in `Shades()`. Light keeps today's
   values; the dark modes tint the field with the mode's warning or error
   colour.
   - Replace the literal copies (`255,244,214`, `255,226,226`,
     `255,221,221`, `168,32,32`, `Color.Firebrick`) with the `PhxTheme`
     members.
   - Replace row-level `SystemColors.GrayText` with `Muted`.
   - Replace the `SystemColors` resets that run after `Apply` with `Ink`,
     `Field` or `Band`.
3. When switching to any mode but Light, put the designers' system colours
   (`FromSystem`) in the change map too.
4. In the dark modes, dark scrollbars and combo boxes through
   `SetWindowTheme` (`DarkMode_Explorer`, `DarkMode_CFD`), applied again when
   a handle is recreated and undone in Light. Give `NumericUpDown` and
   `TreeView` the field and ink colours.
5. Show keyboard focus: a `Focus` shade (the field tinted with the highlight
   - cyan in Midnight neon) on the focused text box, combo box or number box,
   put back to the field on leaving, never laid over a colour that means
   something.
6. Theme controls added after the screen has loaded (`ControlAdded`).
7. A per-control opt-out, `PhxTheme.Exclude(control)`, honoured by `Apply`
   and `Recolour`.
8. In the dark modes, message boxes as a themed `frmMessage` through
   `Messages.Show(...)`; Light keeps `MessageBox.Show`. Keep the icon, the
   sound, the default button, Enter and Escape, Ctrl+C to copy, and centring
   on the owner. Route `PhxFormBase.ShowError`, `ShowInfo` and `Confirm`
   through it. Dialogs before sign-in and on the crash path stay native.
9. Palettes:
   - **Dark:** paper #121418, field #20242C, ink #F1F3F5, warning #F4BB4F,
     error #FF626E.
   - **Midnight neon:** paper #080B14, field #111A2B, ink #E8F4FF, glow
     #00E5FF, warning #FFC857, error #FF477E; on Windows 11 the window
     border takes the glow.
   - Light and Warm paper unchanged.

**Out of scope** - not built unless asked: a library of themed controls
(buttons, text boxes, toggles, cards, badges, toasts), a navigation sidebar,
a custom title bar, custom scrollbars, a code editor, a theme per user,
third-party theming packages, a different default theme.

**Native limits - document them, do not fake them.** The DateTimePicker field
and calendar; the TabControl's tab strip (Patient edit); check-box and
radio-button glyphs; the ground of a disabled text box (verify); file
dialogs; message boxes in Light and before sign-in; the dark caption needs
Windows 10 2004 or later and the border colour Windows 11; the `DarkMode_*`
theme names are undocumented Windows names.

**Done when:**

- The solution builds clean with the analyzers on and the unit tests pass,
  including new ones that check: every mode's ink and problem text read at
  4.5:1 or better on their grounds; switching modes yields the semantic and
  system-colour swaps; an excluded control is left alone; an unknown saved id
  falls back to Light.
- Screenshots in every mode of the shell, a list with flagged rows, a prompt,
  a confirmation, Payment, Patient edit, Reports and a screen with a
  DateTimePicker show no unreadable text and no white islands beyond the
  documented limits.
- Light looks as before apart from the focus tint, and a Light `ShowError`
  still opens a `#32770` window.
- The final summary lists the changed files and the limits that remain.
