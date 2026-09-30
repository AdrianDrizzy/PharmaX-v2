# PharmaX — Quotes before a patient, field-by-field search, and a function key on every button

You are working in the **PharmaX** repository: a Windows desktop pharmacy
system (.NET 8, WinForms) used at BethRapha Pharmacy. Deliver the three
features below, in the order given, each one finished — code, migrations,
tests, and a run-through in the real app — before the next is started.

Read this whole brief first. Where it says "already built", build on what is
there rather than writing it again.

---

## 0. How the codebase works (read before changing anything)

- **Layers.** `PharmaX.Domain` (entities, rules) → `PharmaX.Application`
  (services returning `Result` / `Result<T>`, DTOs, permission checks) →
  `PharmaX.Persistence` (EF Core) and `PharmaX.Adapters` (printers) →
  `PharmaX.Desktop` (WinForms, MVP). The Desktop never talks to EF directly.
- **MVP.** Screens derive from `PhxFormBase` and implement an `IxxxView`;
  presenters derive from `PresenterBase<TView>` and serialise work with
  `Enqueue(...)` / `GuardAsync(...)`. `ViewFactory.Create<TForm>()` gives each
  screen its **own DI scope and DbContext** — never share a context between two
  concurrent queries (see `Suggest.Products`, which opens its own scope).
- **Databases.** Two EF contexts: `PharmaXDbContext` (SQLite, a till on its
  own) and `HostDbContext` (MySQL/MariaDB, the shared pharmacy database). Every
  model change needs **two migrations**, generated with:
  ```
  dotnet ef migrations add <Name> --project PharmaX.Persistence --startup-project PharmaX.Persistence --context PharmaXDbContext --output-dir Migrations --configuration Release
  dotnet ef migrations add Host<Name> --project PharmaX.Persistence --startup-project PharmaX.Persistence --context HostDbContext --output-dir Migrations/Host --configuration Release --no-build
  ```
  Check both only contain your change.
- **Permissions** are `PermissionCode` constants checked in the service
  (`_permissions.Demand(...)`) *and* reflected on screen (hide or disable what
  the user may not do). Roles are in `StandardRoleGrants`.
- **Audit and numbering.** Anything that changes a record writes
  `_audit.Applied(...)`; document numbers come from `IDocumentNumbering`
  (`DocumentSeries`).
- **Style.** Match the surrounding code: comments explain *why*, in plain
  sentences, and cite defect IDs where one is being fixed. CRLF line endings.
  Mnemonics (`&Save`) and `AccessibleName` on every input.
- **Tests.** `PharmaX.Tests.Unit` (xUnit, FluentAssertions, forms constructed
  directly; Desktop internals are visible to it) and
  `PharmaX.Tests.Integration` (`PharmaXFixture`, SQLite, real services). Every
  feature below needs both.
- **Building while the app is open.** The developer usually has the Debug
  build running from `PharmaX.Desktop\bin\Debug`, which locks it. Build and test
  with `-c Release`.
- **Running it for real.** Drive the Release build with `docs/uia-drive.ps1`
  and `docs/e2e-helpers.ps1` against a throwaway MySQL database (set
  `Database__SharedConnection=...Database=PharmaX_E2E_<name>...`). **Never** run
  against the working `PharmaX` database. Printers can be pointed at
  "Microsoft Print to PDF" in File > Printer Setup.

### Already built (do not rebuild)

- `SearchTerms` (Application/Abstractions): searches are read **word by word**
  — every word must match somewhere, in any order; telephone numbers are
  compared on their last nine digits.
- Patient search: surname, first names, file, ID, membership, **telephone and
  address**; results **ranked surname-first** (surname starting with the first
  word, then first name starting with the second). `Patient.Address` exists.
  A new patient whose phone number is already on file is questioned
  (`PossibleDuplicatesAsync`).
- Product search: word by word over code, description and generic name; exact
  code first, then descriptions starting with the first word.
- `SearchSuggestions` (Desktop/Controls): a drop-down of matches under a text
  box — push (`Offer(...)`, for list screens) or pull (`Source`, for boxes
  without a list). Down/Up move, Enter takes the highlighted match, Escape
  closes; mouse hover never decides what Enter takes. `Suggest` formats
  patients (`Surname, First names · file · phone · address`), products and
  accounts. Wired into: Patients, Products, Accounts, Account picker,
  reference lists, Tariffs, Scripts to Collect, Stock Adjustment, and the till
  and dispensing scan bars (`ScanBar.SuggestFrom`).
- Function keys exist only on the till (`PosKeyMap`: F2 Find item, F3
  Customer, F4 Quantity, F5 Discount, F6 Pay, F7 Close, F8 Hold, F9 Recall, F10
  Quotation, F12 Reprint, Esc Void line), on the main menu (F2 Dispensing, F3
  Sell, F4 Patients, F5 Returns, F6 Products, F7 Purchase Orders, F8 Goods
  Receipt, F11 Scripts to Collect, F12 Stock Alerts) and on Reports.
- OTC quotations: `Quotation` (Domain/Sales) with an optional `PatientId`, a
  `SaleId` once sold, `ValidUntil`, and `IQuotationService` (`OpenAsync(customerName)`,
  `AddLineAsync`, `RemoveLineAsync`, `IssueAsync`, `FindAsync`, `RecentAsync`);
  the till opens it with F10.

---

## 1. A function key on every button, on every screen

**Goal.** Everything a mouse can press, a keyboard can press with F1–F12 — on
all ~47 screens, the same key meaning the same thing wherever possible.

### Requirements

1. **One mechanism, in `PhxFormBase`.** Add something like
   `protected void Shortcut(Keys key, Button button)` that:
   - puts the key in front of the caption, as the till does (`F6 Save`);
   - handles the key in `ProcessCmdKey`, clicking the button only when it is
     **visible and enabled** (a hidden button must not fire from the keyboard);
   - sets the button's `AccessibleDescription` to the key, so screen readers
     and UI automation announce it;
   - refuses (throws in Debug) two buttons on one key on the same form.
2. **A standard map**, applied everywhere, with any exception written down in
   the screen's own comments:

   | Key | Meaning |
   |-----|---------|
   | F1 | Help: a small panel listing this screen's shortcuts and what it is for |
   | F2 | Find / search (focus the first search box, or open the picker) |
   | F3 | Add / New |
   | F4 | Edit / Open the selected record |
   | F5 | Refresh |
   | F6 | The screen's main action: Save, Pay, Issue, Record remittance, Post |
   | F7 | Remove / Deactivate / Void the selected line |
   | F8 | Hold / Park / Save as quote |
   | F9 | Recall / Resume / Choose (in a picker) |
   | F10 | Print |
   | F11 | Export / the screen's second action |
   | F12 | Reprint / Documents / the screen's third action |
   | Esc | Cancel / Close (keep `CancelButton` behaviour) |

   Keep the till's existing map exactly as it is (cashiers already know it).
   The main menu's F-keys only work on the shell; modal screens have their own.
   `F10` normally opens a window's menu bar in Windows — take it in
   `ProcessCmdKey` so it prints instead.
3. **Every screen.** Go through every `frm*.cs` in `PharmaX.Desktop/Features`
   and `Shell`, and give each button a key. Dialogs with only OK/Cancel use
   F6 for OK and Esc for Cancel. Where a screen has more than twelve actions,
   use Shift+F-keys for the rarer ones and show them the same way.
4. **Discoverable.** F1 on any screen shows its keys (generated from the
   registrations, so it can never be out of date).

### Tests

- A unit test that creates **every form** (the way `ContainerTests` resolves
  them) and asserts: each visible `Button` has a shortcut; no key is used twice
  on one form; each caption starts with its key.
- A unit test that pressing a key clicks its button, and that a hidden or
  disabled button does not fire.
- E2E: complete a sale, a dispensing and a goods receipt **using only the
  keyboard**.

---

## 2. Search by field, from the first few letters, on every text input

**Goal.** The counter finds a patient by typing a few letters of the
**surname first**, then the name, without typing either in full — and can
narrow by address or phone number in their own boxes. Products the same way,
by code or description. Every box in the app that names a record suggests
matches as it is typed into.

### 2a. Patient search with separate fields

Replace the single search box on the patient list / picker (`frmPatientList`)
with a row of boxes, in this tab order:

`Surname` → `First name` → `Phone` → `Address` → `File / ID / membership`

- **Surname** has the focus when the screen opens.
- Each box matches **from the start of a word**: `pil` finds *Pillay*;
  `moy` finds *Moyo* and *Moyana*, not *Chimoyo*. (SQL: `LIKE 'pil%' OR LIKE
  '% pil%'`.) Phone matches on digits (last nine); address and file/ID/
  membership match anywhere.
- Filled boxes combine with **AND**; empty ones are ignored.
- Results are ranked surname-first, as now; the suggestion list opens under
  whichever box is being typed into and shows
  `Surname, First names · file · phone · address`.
- Add `PatientSearchDto(Surname, FirstName, Phone, Address, Other)` and an
  overload `IPatientService.SearchAsync(PatientSearchDto)`; keep the existing
  single-term search (other screens and tests use it).
- The **same field set** is used wherever a patient is picked: Dispensing
  (Find script), Script Maintenance, Labels, Merge Patient Files, the till's
  F3 Customer, and Scripts to Collect.

### 2b. Product search

- Product list and product picker: a **Code** box (exact first, then
  prefix) and a **Description** box (word prefixes, generic name included).
  One box filled is enough.
- Keep the scan bars as one box (a scanner types into one place), with the
  existing suggestions.

### 2c. Type-ahead on every record-naming input

Attach `SearchSuggestions` to every input that names a record. At least:

- **Script capture** (`frmScriptEdit`): the **patient** is chosen right here
  by typing the surname's first letters (no separate picker needed), and the
  **product** cell of each item row suggests products as its description is
  typed — attach to the grid's editing control in `EditingControlShowing`;
  choosing fills the code and moves to Quantity.
- **Prescriber** (script capture): suggest prescribers already used on
  earlier scripts, with their practice numbers.
- Purchase order and goods receipt lines (products), stocktake capture
  (products), tariff edit (product), quotation lines (products), labels and
  patient merge (patients), supplier fields (suppliers), medical aid scheme
  fields (schemes).
- Any other `TextBox` that holds a code or a name of a record: find them all.

### Tests

- Integration: prefix matching per field (`pil` finds Pillay, not
  Chipillay), AND between fields, phone with and without spaces, ranking.
- Unit: the field boxes build the right `PatientSearchDto`; suggestions open
  under the box being typed into; choosing a product in a script row fills
  the code and moves to Quantity.
- E2E: capture a script by typing `pil` → pick Pillay, Reshma → type `amo` in
  the first row → pick Amoxicillin → quantity → save, without the mouse.

---

## 3. Quotes: prices first, the patient later

**Goal.** A customer asks "what does amoxicillin cost?" before anything else.
The pharmacist (or the till) builds a list of products and quantities, reads
out the prices — and only if the customer goes ahead is the list saved and
the patient's details filled in. The feature is called **Quote**. It works in
the dispensary and at the till.

### 3a. One Quote, both sides

Extend the existing `Quotation` rather than adding a second model:

- `Kind`: `OverTheCounter` | `Dispensary`.
- `CustomerName` and `PatientId` become optional **until** the quote is
  converted. `OpenAsync` no longer requires a name.
- Optional `SchemeId`: price the lines at a medical aid's **tariff** and show
  the scheme's share and the co-payment, without a patient ("with Cimas you
  would pay $X").
- Lines carry quantity, unit price, line total, the stock on hand when quoted
  (for the conversation, not reserved), and — for dispensary quotes — optional
  directions.
- `ConvertedToScriptId` alongside the existing `SaleId`; statuses `Open`,
  `Saved`, `Converted`, `Expired`, `Cancelled`. Quotes post **nothing** to the
  ledger and move **no** stock.
- Migrations for both contexts.

### 3b. Dispensary Quote screen

- **Dispensary > Quote** (and a main-menu function key), open to anyone with
  `ScriptCapture`, `ScriptDispense` or `SaleCreate`.
- Opens straight onto a product box with suggestions. Type → pick → quantity →
  the line shows price and stock; the total updates. Remove lines (F7).
  Optionally pick a scheme to see tariff prices and co-payment.
- **F6 Save quote**: numbered from a new `DocumentSeries.Quote`, valid for a
  set number of days (default 30, from settings), a customer name optional.
  Print it on the receipt printer as *QUOTE — not a tax invoice*, with the
  validity date.
- **F9 Resume**: find a saved quote by number, name, date or product, and
  carry on with it.
- **Convert to script**: when the customer wants the medicines —
  1. choose the patient with the new surname-first search, or add a new one
     (the patient form opens, then returns);
  2. enter the prescriber and repeats, as on script capture;
  3. PharmaX creates the script **from the quote's lines** (quantities and
     directions kept), marks the quote `Converted`, and opens Dispensing on
     it, items already on.
- Prices are **re-read at conversion**; where a price or tariff has changed
  since the quote, say so and show both, rather than silently charging the
  new one.
- Scheduled medicines can be quoted (it is only a price); the dispensing rules
  apply when it becomes a script.

### 3c. Till: price check and OTC quotes

- On the till, **price check** without starting a sale: scan or type
  products, see prices and stock, then either **Sell these** (the lines move
  into a new sale) or **F8 Save as quote**. Keep F10 for opening quotations.
- A saved OTC quote is recalled into a sale by its number (existing
  `FindAsync` path), and marked converted with the sale.

### Tests

- Integration: open a quote with no customer; add lines; save; resume;
  convert to a script for a patient (script items equal quote lines); convert
  an OTC quote to a sale; price changed between quote and conversion is
  reported; nothing is posted and no stock moves until conversion.
- Unit: quote screen totals, scheme co-payment display, conversion prompts.
- E2E: the whole story — "How much is amoxicillin and metformin?" → quote →
  save → customer returns → resume → convert → choose Pillay, Reshma →
  prescriber → dispense → collect → pay → receipt, labels and trailer label
  print.

---

## Done means

- All three features working in the Release build against a throwaway MySQL
  database, with screenshots of each.
- `dotnet test` green for both test projects.
- Both migrations present for every model change, and nothing else in them.
- No existing behaviour broken: the till's keys, receipts, labels, trailer
  labels, claim forms and the Products grid editing all still work.
- A short summary for the pharmacy of what changed and which keys do what.
