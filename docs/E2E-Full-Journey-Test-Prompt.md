# PharmaX – Full End-to-End User Journey Test (Prompt)

> Hand this whole document to the testing agent as its instructions.

---

## 0. Your mission

You are the end-to-end test lead for **PharmaX**, a WinForms pharmacy system
(dispensary, point of sale, inventory, purchasing, accounts, reports). A
previous E2E run (`docs/PharmaX-E2E-Test-Report.pdf`,
`evidence/e2e-retest/access-first-build/`) only proved that **screens open**
for each role. It did not complete business transactions, did not follow
data from one screen to the next and did not check any outputs. This run
must do all three.

You must:

1. **Finish every feature for real.** A sale is only tested once it is paid,
   a receipt is produced, stock has gone down, the till expects the money and
   the sale appears in the reports. Opening `frmPOS` and taking a screenshot
   does not count.
2. **Carry out full CRUD on every maintained entity.** That means create,
   read (search, filter, open), update and delete or deactivate, plus the
   validation failures (duplicates, missing required fields, bad values).
3. **Connect everything.** Each journey passes its output to the next role.
   A script captured by an assistant must be dispensed, show up at a
   cashier's Scripts to Collect queue, be paid for, appear on the receipt, in
   the drawer count, in the day-end journal, in the trial balance and in the
   reports. Where money is owed, it must also reach the patient account or
   the medical aid claim.
4. **Record every click and its output.** For each step, write down what you
   clicked or typed, what you expected, what actually happened (message text,
   grid contents, document numbers, totals), a screenshot, and a PDF for
   anything printed.
5. **Reconcile.** Keep a running ledger of what stock, cash, patient account
   balances and claims should be, and check the application against it after
   every journey.

Do **not** change application code during the run. Log defects (section 9)
and keep going. If a defect blocks a step, mark that step `BLOCKED`, apply
the smallest documented workaround (for example seeding the missing record)
so the later journeys can still run, and record the workaround.

---

## 1. Environment and seeding

### 1.1 Fresh database
- MySQL on `127.0.0.1:3306`, user `root` / `root`.
- Drop and recreate the schema **`PharmaX_E2E_FULL`**. Never touch the
  developer's working `PharmaX` database.
- Environment for the app process:
  ```powershell
  $env:Database__SharedConnection = 'Server=127.0.0.1;Port=3306;Database=PharmaX_E2E_FULL;User ID=root;Password=root'
  $env:Development__SeedTestUsers = 'true'
  ```
- Build Release: `dotnet build PharmaX.sln -c Release`. Run the unit and
  integration tests first (`dotnet test`) and record the pass/fail counts in
  the report header. A red suite is a finding, but it does not stop the UI
  run.
- Start `PharmaX.Desktop.exe`. On start-up the app migrates the schema and
  then runs these seeders in order. Each one is idempotent.

| Seeder | What it gives you |
|---|---|
| `ReferenceDataSeeder` | Chart of accounts, permissions, the six roles, the default administrator (must change password on first login), cash denominations, first document-number block |
| `TestUserSeeder` | `test.admin`, `test.manager`, `test.pharmacist` (reg. P00000), `test.assistant`, `test.cashier`, `test.stock`, all with password `Test#1234`. Discount limits: 100 / 50 / 10 / 5 / 5 / 0 % |
| `TestCatalogueSeeder` | 10 products (PAN500, DIS200, VITC1G, ALLERG sched 2, COUGH1 sched 2 batched, AMOX50 sched 4 batched, METF85 sched 4, INSUL1 sched 4, PLAST1, SURGM1), suppliers UPD and ALPHA, patients P000001 Ndlovu and P000002 Van Wyk |
| `PriceListSeeder` | Pharmacy price list |
| `DemoDataSeeder` | Departments, categories, manufacturers, branches BR1/BR2, schemes ALPHA/BONWELL/CIVSRV, 6 clinical patients (Dube: **penicillin allergy** on ALPHA; Mokoena: diabetic on ALPHA; Pillay: sulphonamide allergy on BONWELL; Botha: asthma on CIVSRV; Khumalo: self-pay; Fourie: aspirin allergy), tariffs, patient accounts, low/expired shelf states for alerts, one day of trading history |

### 1.2 Seeding checks (before any journey)
Sign in as `test.admin` and **prove** the seed landed. Take screenshots and
record the row counts: users list (6 + administrator), product list (10+),
patients (8), suppliers (2), each reference list, stock alerts (at least one
low-stock and one expiry alert), and the chart of accounts. Record the
**opening stock on hand** for every product and the **opening balance** of
every patient account. These are row 0 of the reconciliation ledger
(section 8).

### 1.3 Data you must create yourself (through the UI, as part of the journeys)
The seed has gaps the full story needs. Create these **through the screens,
by the role that would do it in real life**. They are test steps, not setup:
- A **Schedule 6 (controlled)** product (`DispensingService.ControlledSchedule = 6`),
  batched, with an expiry date. Created by the stock controller, priced by the
  manager, received on a goods receipt.
- A **new OTC product with a barcode**, so the till can be tested with a
  scanner (`Chars` helper with `-Enter` imitates a keyboard-wedge scanner).
- A **new patient** created by the assistant, with an allergy, a chronic
  condition, scheme membership and an attachment.
- A **new supplier**, a **new cashier user**, a **new department/category/manufacturer**.

Only seed directly in SQL when no screen exists for the data (for example
forcing a batch to expire). Record every direct SQL change in
`evidence/e2e-full/seed-log.md` with the statement and the reason.

### 1.4 Tooling
- Dot-source the existing helpers:
  ```powershell
  $E2E = "C:\Users\mlilo\source\repos\PharmaX\evidence\e2e-full"; . docs\e2e-helpers.ps1
  ```
  They provide `SignIn`, `PMenu`, `Win`, `Ids`, `El`, `SetV`, `Btn`,
  `BtnName`, `Chars`, `Key`, `TypeCell`, `PickRow`, `GridRows`, `Combo`,
  `Prompt`, `MsgBox`, `Dialogs`, `Answer`, `SavePrint`, `SaveAs`,
  `RunReport`, `Shot`, `ShotH` and `Rec`. Override the connection string the
  helper sets so that it points at `PharmaX_E2E_FULL`.
- **Never guess control names.** Before you drive a new form, run
  `Ids '<window title>'` and use the AutomationIds it reports.
- Set the receipt and label printers (File → Printer Setup) to **Microsoft
  Print to PDF**, so every receipt, label, credit note, quotation and PO ends
  up as a PDF under `evidence/e2e-full/receipts/`.
- Every assertion also gets a **database cross-check**: a `SELECT` against
  `PharmaX_E2E_FULL` that proves the row, status, quantity or amount. Take
  table and column names from
  `PharmaX.Persistence/Migrations/PharmaXDbContextModelSnapshot.cs`. Save the
  query and its output with the step.

---

## 2. Users and what each may do

| User | Role | Key grants (see `StandardRoleGrants.cs`) |
|---|---|---|
| test.admin | Administrator | Everything |
| test.manager | Manager | Sell, discount above limit, authorise returns, close another user's till, patients, product/price/reference/supplier maintenance, stocktake capture+post, PO, GRN, all non-clinical reports, ledger read+post, assign roles (no wider than own) |
| test.pharmacist | Pharmacist | Sell, patients, script capture/dispense/**authorise**/**reverse**, product read incl. dispensary, stocktake capture, operational + clinical reports |
| test.assistant | Pharmacist Assistant | Sell, patients, script capture/dispense, dispensary products, stocktake capture, goods receipt, operational reports. **No** clinical override, **no** controlled release, **no** reverse |
| test.cashier | Cashier | Sell and read the OTC catalogue **only** |
| test.stock | Stock Controller | Products (not price change), stocktake capture+post, suppliers, reference lists, PO, GRN, operational reports |

Screen access is decided per menu key in
`PharmaX.Application/Security/ScreenAccess.cs`. The menu lives in
`PharmaX.Desktop/Shell/frmMain.Designer.cs`.

---

## 3. Rules of evidence

For **every step** call
`Rec <case> <role> <step> <expected> <actual> <PASS|FAIL|BLOCKED> <shot>`,
so that `results.csv` holds one row per click or entry. The `actual` column
must contain the **observed output**: message box text, status bar text,
document number, grid totals, printed PDF name. A generic "OK" is not
enough.

For every transaction, also record:
- Document number issued (sale, script, dispensing, PO, GRN, return, quotation, journal).
- Totals: excl. VAT, VAT, incl. VAT, discount, change due.
- Stock on hand before and after, per product and per batch.
- DB cross-check query and its result.

Screenshots: `<journey>_<step>_<role>_<what>.png`. Receipts/labels:
`<journey>_<docno>.pdf`.

---

## 4. Journey chain

Run the journeys **in this order, on one database, on one trading day**. Each
one uses what the previous ones created. Where a step says "as X", sign out
and sign in as X (or use a second app instance for the multi-till steps).

### J1 – Administrator sets the pharmacy up
As **default administrator**: first login, forced password change (including
a weak password that must be rejected and a mismatched confirmation). Then
as **test.admin**:
1. **System Setup**: pharmacy profile (name, address, VAT number, practice
   number, logo). Save, reopen, confirm it stuck. Later journeys check that
   it prints on receipts, labels and report headers.
2. **Control Variables / System Console / Printer Setup**: record every
   option; change one, save, restart the app, confirm it persisted
   per workstation.
3. **Branches CRUD**: create BR3, edit its name, deactivate it, show it with
   "include inactive", try a duplicate code (must fail).
4. **Users CRUD**: create `e2e.cashier` (Cashier, discount 5 %), edit display
   name, reset password (first login must force a change), disable, confirm
   the disabled user cannot sign in, re-enable. As **test.manager**, try to
   give a user the Administrator role; it must be refused (a manager cannot
   grant a role wider than their own).
5. **Change Password / Log Off** for a normal user.

### J2 – Stock controller maintains the catalogue
As **test.stock**:
1. **Departments, Categories, Manufacturers CRUD**. For each: create, search,
   edit, deactivate, include-inactive filter, duplicate code, blank name.
2. **Products CRUD**. Create `E2EOTC` (OTC, barcode `6001234500017`, VAT
   standard, reorder level 10) and `E2ECD6` (Schedule 6, batched, dispensary
   department). Edit description, filing and reorder level. Try to change the
   **selling price** of an existing product; it must be refused, because that
   needs `inv.price.change`. Try to deactivate a product that has stock and
   record what happens.
3. **Suppliers CRUD**: create `E2ESUP`, edit terms, deactivate/reactivate.
As **test.manager**:
4. Set the prices for `E2EOTC` and `E2ECD6`, and change PAN500's price.
   Inventory → **Price Changes** must list the change with old and new price,
   user and time.
5. **Tariffs CRUD** and **Medical Aid Societies CRUD** (add a scheme, edit, deactivate).

### J3 – Purchasing and receiving (stock arrives)
1. As **test.stock**: Purchase Orders → raise a PO to `E2ESUP` for `E2EOTC`
   ×50, `E2ECD6` ×20 and AMOX50 ×30. Save as Draft, edit a quantity, remove a
   line, send it. Print it to PDF. Status must go Draft → Sent.
2. As **test.assistant**: Goods Receipt → choose the supplier, then choose
   the order. Receive **part** of it: `E2EOTC` ×30, `E2ECD6` ×20 with batch
   number and expiry, AMOX50 ×30 with batch and expiry. Check the
   "Resolved – what will be posted" preview, then Post. PO status must be
   **PartiallyReceived**. Stock on hand must go up by exactly those
   quantities, and the batches must be visible.
3. Receive the rest (`E2EOTC` ×20). PO must be **Received** and must leave
   "Orders still expecting stock".
4. Negative tests: over-receive beyond the ordered quantity, a batched item
   without batch or expiry, an expiry date in the past, receiving against a
   cancelled PO. Also cancel a second PO.
5. Stock Alerts: any low-stock alert for a product you just received must
   clear. Expired and low alerts for other products must remain.

### J4 – Opening the tills
1. As **test.cashier** on till 1: File → Till Session → open with a float
   counted by denomination. Record the float.
2. As **test.assistant** on a second instance (till 2): open a session.
3. Negative tests: try to sell with no open session (must be refused), and
   try to open a second session on the same till.

### J5 – Over-the-counter sale, every till option (as test.cashier)
Point of Sale → Sell (F3). Exercise **every** function key from `PosKeyMap`
and record the output of each:

| Key | Action to test | Expected output to record |
|---|---|---|
| F2 Find item | search "pan", pick PAN500 | line added, price, VAT |
| (scan) | `Chars` barcode `6001234500017` with Enter | `E2EOTC` line added |
| F4 Quantity | set PAN500 qty to 3, then 0 and -1 (must be rejected) | line total recalculated |
| F5 Discount | 5 % on a line (within limit) | discount shown, total down |
| F5 Discount | 20 % on a line | **supervisor prompt**. Wrong password refused; `test.pharmacist` (limit 10 %) refused; `test.manager` accepted; audit row written |
| Esc Void line | void one line | line gone, totals recalculated |
| F8 Hold | hold the basket | basket cleared, held |
| F9 Recall | recall it **at till 2** as test.assistant, then return it to the cashier's till | same lines and prices come back |
| F10 Quotation | bring in the quotation from J8 step 1 | quotation lines become sale lines; quotation becomes **Accepted** |
| F3 Customer | attach patient Khumalo | customer shown on sale and receipt |
| F6 Pay | see payment table below | receipt PDF, change due, document number |
| F12 Reprint | reprint last receipt | identical PDF marked as a copy |
| F7 Close | close the till screen | returns to shell |

Complete **one sale per payment method** (`TenderType`: Cash, Card, Account,
MedicalAid, Voucher, EFT) and add these payment edge cases:
- Cash overpayment → correct change.
- Card/EFT without a reference, and card over the amount due → must be refused.
- **Split tender** (cash + card) via "Add tender" / "Remove".
- **Account** tender for a patient with no account, or over their limit → refused. Otherwise the patient account balance goes up.
- Cancel out of payment → sale stays open, no document number used.

After each sale, check: receipt PDF contents (pharmacy profile header, lines,
VAT breakdown, tender, change, document number, cashier name), stock
decremented, document numbers sequential with no gaps.

Negative tests as cashier: search `AMOX50` or `INSUL1` → **"no product"**
(dispensary stock is hidden from a cashier); sell more than the stock on
hand; open Dispensary, Inventory maintenance, Reports or Accounts through the
menu **and** through the shortcut keys (F2, F4, F6, F7, F8, F9, F12). All of
them must be unavailable.

### J6 – Script → Dispense → Collect → Pay (the core connected story)
Run **every variant**. Each one must end with the money in the drawer (or on
account or claim) and the medicine out of stock.

**J6a – Assistant, new self-pay patient, ordinary script**
1. As **test.assistant**: Dispensary → Patients (F4) → **create** a patient
   (name, ID/DOB, phone, allergy "Sulphonamides", chronic condition). Add an
   **attachment** (upload, view details, rename, delete one). Search by
   surname and phone. Edit the phone number.
2. Script Maintenance → **capture** a script for this patient: prescriber,
   date, "Prescriber set a validity date", items METF85 ×60 with 2 repeats and
   PAN500 ×20. Save. Record the **script number**; status must be **Captured**.
   Then edit the script (change a quantity, remove an item, add it back) and
   save again.
3. Dispensing (F2) → Find script → the lines appear. Check the clinical
   warnings panel. Remove an item and re-add it. **Issue**. Labels print to
   PDF: check patient, drug, directions, pharmacist, pharmacy profile, date.
   Dispensing status must be **Issued**. Script status must be
   **PartiallyDispensed** (repeats remain).
4. As **test.cashier** on till 1: Point of Sale → Scripts to Collect (F11).
   Within the poll interval the script must appear with patient, script
   number, document number, co-payment and waiting time. The summary line
   must read "N waiting – X owed – longest …". Filter by patient name, then
   by script number, then by document number.
5. **Take Payment** → pay the co-payment in cash. You must see "<patient>
   paid. Next patient." The row must leave the queue, a receipt PDF must
   print, and the script number must be on it.
6. Check stock: METF85 and PAN500 go down **exactly once**. Find out and
   record whether stock moves at issue or at payment, and confirm it is not
   double-counted.
7. Later in the day, as the assistant, dispense **repeat 1**, and the
   cashier collects it. After the last repeat the script must be **Completed**
   and must refuse a further dispense.

**J6b – Pharmacist and a hard-stop allergy**
1. As **test.assistant**: capture a script for **Dube** (penicillin allergy)
   with AMOX50.
2. Dispense it. A **hard-stop clinical warning** must appear. When the
   assistant tries to Issue, a **supervisor prompt** must appear. Enter the
   assistant's own credentials: refused. Enter `test.cashier`: refused. Enter
   `test.pharmacist` with an override reason: accepted, and the override is
   audited with the pharmacist's name.
3. The cashier collects and pays by **card**.

**J6c – Admin captures, medical-aid patient, claim raised**
1. As **test.admin**: capture a script for **Mokoena** (ALPHA member) with
   INSUL1 and METF85.
2. As **test.pharmacist**: dispense and Issue. Record how the total splits
   between the scheme portion and the patient co-payment.
3. As **test.cashier**: collect, pay the **co-payment only** by cash, and
   check that the receipt shows the split.
4. As **test.manager**: Accounts → **Medical Aid Claims** must show an
   **Outstanding** claim for the scheme portion. Take one claim to
   **PartPaid**, then **Settled**. Raise another and **Reject** it. Check
   the effect on accounts receivable and the trial balance.

**J6d – Controlled (Schedule 6) medicine**
1. As **test.assistant**: capture and dispense `E2ECD6` for Botha. At Issue
   the app must demand a pharmacist's authority ("controlled"). Show that the
   assistant alone cannot release it, then release it with
   `test.pharmacist`.
2. Batch and expiry must come from the J3 receipt, and the batch stock must
   go down.
3. The cashier collects and pays by **EFT**.

**J6e – Patient on account**
Capture, dispense and collect for a patient with an account; pay with
**Account** tender. Accounts → Patient Accounts must show the new balance
and the transaction. Then take a payment on that account and check the
balance goes down.

**J6f – Reversal before and after collection**
1. As **test.pharmacist**: dispense a script, then **reverse** it before
   collection. It must leave the Collect queue, stock must come back, and the
   status must be **Reversed**. Show that `test.assistant` cannot reverse.
2. For a script that has already been collected, use Dispensary → **Drug
   Returns** (needs dispense permission plus return authority, so a
   supervisor prompt is expected for the assistant). Check the refund and
   the stock returned to the right batch.

**J6g – Two tills, one script (concurrency)**
Have the same script open in Scripts to Collect on till 1 (cashier) and
till 2 (assistant). Pay on till 1, then try to pay on till 2. It must be
refused or already gone. It must never be paid twice.

**J6h – Cashier backs out**
Take Payment → Cancel. The script must stay in the queue, no receipt must
print, no document number must be used.

**J6 extras**: Re-Print Script, Labels (reprint and edit), **Merge Patient
Files** as test.pharmacist (merge a deliberately created duplicate patient;
scripts and account follow the survivor; the assistant must be refused). A
patient **delete or deactivate** when they have scripts: record what the
system allows.

### J7 – Returns and credit notes
1. As **test.cashier**: Point of Sale → Returns (F5) → find a J5 sale by
   document number. Return one line partially. The supervisor prompt must
   appear (return authority); enter `test.manager`. Click "Refund and
   credit", then check: credit note PDF, refund tender, stock back on hand,
   and the original sale unchanged.
2. Negative tests: return more than was sold, return the same line twice,
   return a voided or held sale.

### J8 – Quotations (as test.cashier, before J5's F10 step)
Create a quotation for Van Wyk, edit lines, **issue** it (PDF), and create a
second one to leave to **expire**. The first is accepted at the till in J5
through F10. Check that an expired or already accepted quotation cannot be
brought in again.

### J9 – Stocktake
1. As **test.stock**: New Stocktake (for one department). The expected
   quantities are snapshotted at this point.
2. As **test.assistant**: Capture counts. Enter one item short, one over and
   one exact.
3. While the count is open, the cashier sells one counted item. Confirm that
   the variance is **not** distorted by that sale.
4. Variance screen: record every line. As **test.assistant**, try to Post; it
   must be refused.
5. As **test.manager** (or test.stock): **Post**. Stock on hand must be
   adjusted and the variance must reach the ledger. Status must be **Posted**.
   Cancel a second stocktake and confirm it has no effect. Show that the
   **cashier** cannot reach any stocktake screen.

### J10 – Closing the tills
1. As **test.cashier**: Till Session → count the drawer by denomination. The
   "Expected by tender" figure must equal float + cash sales + cash
   co-payments − cash refunds from J5 to J7, **worked out independently from
   your ledger**. Record the variance. Close with a deliberate R10 shortage
   and record how it is reported.
2. Leave till 2 open. As **test.manager**, close the assistant's session.
   As the cashier, try the same; it must be refused.

### J11 – Day end and the books (as test.manager)
1. Accounts → Day-End Posting → pick today → **Look at day**. The totals must
   match your ledger (sales by tender, VAT, returns, medical aid, account
   sales). **Prepare journal** → **Post journal** → **Export CSV** and
   **Export to Excel** (save both files).
2. Post the same day again; it must be refused or idempotent.
3. **Journal Entry CRUD**: post a manual balanced journal. Try an unbalanced
   one (must be refused). Try it as the cashier (must be refused). As the
   pharmacist (no ledger access), confirm the menu entry is unavailable.
4. **Trial Balance**: must balance, and must reflect the day-end, claims,
   stocktake variance and manual journal. **Chart of Accounts**: read and
   drill down. **Patient Accounts**: balances match the J6e and J5 account
   sales.

### J12 – Reports (every role, every preset they can see)
As each of the six users, open Reports (F9), list the presets offered,
compare them with their report grants, and **run every one** with
`RunReport`, saving a screenshot and an export. The figures must
**reconcile with today's transactions**: sales analysis, VAT report, stock
on hand, price changes, expired stock, transfers, breakages, dispensing and
clinical reports, and till reports. Report headers must carry the J1 pharmacy
profile. The Administration menu's VAT Summary and Stock Transactions entries
must open the right report.

### J13 – Permission matrix (negative coverage for every user)
For each of the six users (and `e2e.cashier`), and for **every menu key** in
`ScreenAccess.Keys`: record Enabled/Disabled in the menu, on the home tiles
and through the keyboard shortcut. Compare each result with the expected
value from `ScreenAccess` + `StandardRoleGrants`. Every mismatch is a
defect. Also confirm that a screen reached another way is still refused by
its service (for example a stock controller opening Sell). Reuse
`docs/check-role-matrix.py` to build the expected matrix.

---

## 5. CRUD coverage checklist

Tick each cell with the case ID that proved it. Blank cells mean the run is
not finished.

| Entity | Create | Read/Search | Update | Delete/Deactivate | Validation failures | Role(s) |
|---|---|---|---|---|---|---|
| User | | | | | | admin, manager |
| Branch | | | | | | admin |
| Department / Category / Manufacturer | | | | | | stock, manager |
| Medical aid scheme | | | | | | manager |
| Tariff | | | | | | manager |
| Product (OTC, dispensary, controlled) | | | | | | stock, manager |
| Price | – | | | – | | manager |
| Supplier | | | | | | stock |
| Purchase order (+ lines) | | | | cancel | | stock |
| Goods receipt | | | – | – | | assistant, stock |
| Patient | | | | | | assistant, pharmacist |
| Patient attachment | | | | | | assistant |
| Patient merge | – | | | | | pharmacist |
| Script (+ items) | | | | cancel | | assistant, pharmacist, admin |
| Dispensing | | | | reverse | | assistant, pharmacist |
| Sale | | | hold/recall | void line | | cashier |
| Quotation | | | | expire | | cashier |
| Return / Drug return | | | – | – | | cashier + manager |
| Stocktake | | | capture | cancel | | stock, assistant, manager |
| Till session | open | | – | close | | cashier, manager |
| Journal | | | – | – | | manager |
| Claim | raised by J6c | | part-paid/settled | rejected | | manager |
| Patient account payment | | | – | – | | manager/cashier |

---

## 6. Cross-journey data flow checks (must all PASS)

1. The script number from J6a capture is the same on the dispensing, the
   label, the Collect queue row, the receipt and the dispensing report.
2. Stock for every product: opening + GRN − sales − dispensings + returns ±
   stocktake variance = closing (application and DB).
3. Drawer: float + cash in − cash out = expected at cash-up = day-end cash
   line.
4. VAT on receipts summed = VAT report = day-end VAT = trial balance VAT
   account.
5. Patient account balances = opening + account sales − payments.
6. Scheme portions dispensed = claims raised; claim settlements move to the
   right accounts.
7. Document numbers per series (sale, return, script, PO, GRN, quotation,
   journal) are sequential with no gaps, and an abandoned transaction uses no
   number.
8. Every supervisor override (discount, clinical, controlled, return) has an
   audit row naming both the operator and the authoriser.
9. The pharmacy profile from J1 appears on receipts, labels, credit notes,
   quotations, POs and report headers.

---

## 7. Robustness spot checks
- Close a form with unsaved changes; the app should ask for confirmation.
- Double-click Pay / Issue / Post quickly; no duplicate documents.
- Restart the app mid-day: held sales, open till sessions and the Collect
  queue survive.
- Leave the Collect screen open; it refreshes without flicker.
- Log off with a sale open; record what happens.

---

## 8. Reconciliation ledger
Keep `evidence/e2e-full/ledger.xlsx` (or `.csv`) with one sheet each for
**Stock** (product, batch, running quantity and reason), **Cash** (till,
movement, running total), **Accounts** (patient, movement, balance),
**Claims** and **Documents** (series, number, journey). Update it after
**every** step that moves any of them, and compare it with the application
and the DB at the end of each journey. Record the comparison result as its
own `Rec` row.

---

## 9. Defects
Log each defect in `evidence/e2e-full/defects.md` as:
`DEF-E2E-### | severity (Blocker/Major/Minor/Cosmetic) | journey/step | role | steps to reproduce | expected | actual | screenshot/PDF | workaround used`.
Cross-reference any defect IDs already used in the code (for example DEF-0015,
DEF-0027, DEF-0029) if the old defect has come back.

---

## 10. Deliverables
1. `evidence/e2e-full/results.csv`: every step.
2. `evidence/e2e-full/screenshots/` and `receipts/` (all PDFs, CSV/Excel exports).
3. `ledger` file and `seed-log.md`.
4. `defects.md`.
5. The CRUD coverage table (section 5) and the permission matrix (J13), filled in.
6. A final report (extend `docs/build-e2e-report.js`), organised by
   **journey, not by screen**. For each journey give a one-line verdict, the
   chain of document numbers it produced, the reconciliation result and its
   defects. It must open with an executive summary: journeys passed, failed
   and blocked, steps executed, defects by severity, and whether the day
   reconciled end to end.

**Done means:** every journey J1–J13 has been run to completion or has a
recorded blocker, every CRUD cell has a case ID, and every section 6 check
has a PASS or a defect against it.
