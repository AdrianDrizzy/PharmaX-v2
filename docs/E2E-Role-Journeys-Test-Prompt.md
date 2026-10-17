# PharmaX – Role journeys end to end (Administrator, Pharmacist, Pharmacist Assistant, Cashier)

> Give this whole document to the testing agent as its instructions.
> Written on 16 October 2026 against branch `proj/0005`. That is commit
> `df7d62d` ("Generic data import, history reuse, item changes and
> pre-packs"), plus the same day's change that gave the pharmacist and the
> pharmacist assistant purchase-order and goods-receipt rights.

---

## 0. Your mission

You are the end-to-end tester for **PharmaX**, a WinForms pharmacy system.
Walk four staff members through a working day on a **copy of the pharmacy's
real data**: everything imported from the old system, meaning every product,
patient, prescriber, supplier and twelve months of history. You also record
each journey as a video in **OBS Studio**.

| Order | User | Password | Role | Video |
|---|---|---|---|---|
| 1 | `test.admin` | `Test#1234` | Administrator | `PharmaX E2E 1 - Administrator.mp4` |
| 2 | `test.pharmacist` | `Test#1234` | Pharmacist (reg. P00000) | `PharmaX E2E 2 - Pharmacist.mp4` |
| 3 | `test.assistant` | `Test#1234` | Pharmacist Assistant | `PharmaX E2E 3 - Pharmacist Assistant.mp4` |
| 4 | `test.cashier` | `Test#1234` | Cashier | `PharmaX E2E 4 - Cashier.mp4` |

These are the development test accounts from
`PharmaX.Persistence/Seeding/TestUserSeeder.cs`. They exist only in the test
copy (section 2) and must never reach the live database.

A step only counts once its business result can be seen. For example:

- A purchase order is raised, sent and printed.
- A delivery is posted and the stock has gone up.
- A script is issued, its label is printed and it is paid for at the till.
- A report on the old data has been run and its totals checked against the
  import.

Opening a screen and taking a screenshot proves nothing. Follow the data from
one screen to the next and from one user to the next.

This run focuses on four things:

1. **The connected chain** for each dispensary role: purchase order → goods
   receipt → view the products → view the patient's history → change an
   existing (imported) dispensing → reuse it as a new script → dispense and
   issue → collect and pay → reports on the old data → cash up.
2. **The cashier's limits.** The cashier opens the till, sells over the
   counter, collects scripts and sees **only over-the-counter products**:
   nothing scheduled, nothing that needs a script, no patient history and no
   reports.
3. **History reuse.** This means "Use again" on Script Maintenance and on
   Labels, "From history..." while capturing a script, and changing an item
   before and after the issue.
4. **Paging.** Every list shows 100 rows a page with First, Previous, Next
   and Last, and every export takes **all** rows, not just the page on
   screen.

---

## 1. Ground rules

- **Do not change application code** during the run. Log defects
  (section 13) and carry on. If a defect blocks a step, mark it `BLOCKED`,
  apply the smallest workaround you can document so that the later steps
  still run, and record that workaround.
- **Never touch the live database** (`%LOCALAPPDATA%\PharmaX\local.db`).
  Before you start and after you finish, record its SHA-256 hash and its
  last-write time. They must be identical (section 12).
- **Never guess control names.** Before you drive a form for the first time,
  run `Ids '<window title>'` and use the AutomationIds it reports. Window
  titles start with the pharmacy's trading name; use `Wins` to see them.
- **Statuses** for every step:
  - `PASS`: the expected result happened.
  - `FAIL`: wrong result, an error message or a crash.
  - `ROLE-GAP`: the pharmacy wants the role to do this, but the current role
    grants refuse it (section 3.2). It is not a code defect; record it
    separately.
  - `CHECK`: the step worked, but something needs the pharmacy's decision or
    a closer look, such as a figure that does not match the import report.
  - `BLOCKED`: could not be run. Give the reason.
  - `SKIP`: deliberately not run. Give the reason.
- The pharmacy PC runs at **150 % display scaling** (1920×1080). Run at that
  scaling. Any button, caption or pager that is cut off is a `FAIL` with a
  screenshot.

---

## 2. Environment

Tell the user before you start. **PharmaX must be closed in Visual Studio**,
both so that the database copy is consistent and because Visual Studio locks
`PharmaX.Desktop\bin\Debug`.

### 2.1 Folders

```powershell
$REPO = "C:\Users\user\source\repos\PharmaX"
$RUN  = "$env:USERPROFILE\PharmaX-E2E-roles"   # app build, DB copy, tools - outside the repo
$E2E  = "$REPO\evidence\e2e-roles"             # screenshots, PDFs, exports, results - in the repo
New-Item -ItemType Directory -Force "$RUN\app","$RUN\db","$RUN\tools","$E2E\screenshots","$E2E\receipts","$E2E\exports" | Out-Null
```

### 2.2 A copy of the live database

1. Record the live DB's hash and time:
   `Get-FileHash "$env:LOCALAPPDATA\PharmaX\local.db"` and its
   `LastWriteTime`. Save both to `$E2E\live-db-before.txt`.
2. Copy `local.db`, `local.db-wal` and `local.db-shm` (where present) into
   `$RUN\db\`.
3. The copy holds the import of 16/10/2026 04:43 from
   `C:\Users\user\Downloads\Exports\Exports`. Its report is
   `%PROGRAMDATA%\PharmaX\logs\import-20261016-044332.txt`. Copy that report
   into `$E2E\` because it is the reference for sections 4 and 10.

### 2.3 Build

Run the tests first, then build the release:

```powershell
dotnet test "$REPO\PharmaX.sln" -c Release
dotnet build "$REPO\PharmaX.Desktop\PharmaX.Desktop.csproj" -c Release -p:OutDir="$RUN\app\"
```

Record the pass and fail counts in the report header. A red test suite is a
finding, but it does not stop the UI run.

### 2.4 Start the app on the copy, with the test accounts

```powershell
Remove-Item Env:Database__SharedConnection -ErrorAction SilentlyContinue
$env:Database__LocalConnection   = "Data Source=$RUN\db\local.db"
$env:Development__SeedTestUsers  = 'true'
```

`docs\e2e-helpers.ps1` sets a MySQL `Database__SharedConnection` on its line
7, so **dot-source it first and then run the three lines above again**. Start
the app with `Start-PharmaX -Exe "$RUN\app\PharmaX.Desktop.exe"`. Its
default path points at another PC. Write the PID it returns to
`$E2E\pid.txt`.

With `SeedTestUsers` on, the start-up seeders add the following **to the
copy only**:

- **Test users** (`TestUserSeeder`): the six `test.*` accounts, all with
  `Test#1234`. Discount limits are admin 100 %, pharmacist 10 %,
  assistant 5 % and cashier 5 %.
- **Test catalogue** (`TestCatalogueSeeder`):
  - 10 test products: PAN500, DIS200, VITC1G, ALLERG, COUGH1, AMOX50,
    METF85, INSUL1, PLAST1 and SURGM1.
  - Suppliers UPD and ALPHA.
  - Patients P000001 Ndlovu and P000002 Van Wyk.
- **Demo data** (`DemoDataSeeder`):
  - Departments, categories and manufacturers. The import brings no
    manufacturers.
  - Medical aid schemes ALPHA, BONWELL and CIVSRV. The import brings none.
  - 6 clinical patients: Dube (penicillin allergy), Mokoena, Pillay, Botha,
    Khumalo and Fourie.
  - Tariffs, patient accounts and one day of trading.
  - **Stock topped up to 10** on every active product below 10, under the
    reason `TESTSTOCK`.

The stock top-up means **stock on hand in the copy is not the imported
figure** for products that had fewer than 10. History before the test day is
not affected, which is why the old-data reports in section 10 use a date
range that ends the day before the run.

**If start-up fails** (for example, a test patient's file number clashes
with an imported one), record it as `BLOCKED` with the log line from
`%PROGRAMDATA%\PharmaX\logs`. Then use this fallback:

1. Start without `SeedTestUsers`, running
   `PharmaX.Desktop.exe --reset-admin` on the copy.
2. Sign in as `admin` (password as installed) and create the four users
   through **Administration → Users**, with the same usernames, roles and
   `Test#1234`. Record each one as a test step.

Start-up may write a backup of the copy into
`%PROGRAMDATA%\PharmaX\backups`. Record it and leave it there.

### 2.5 A database cross-check tool

There is no `sqlite3` or Python on this PC. Build a small console tool in
`$RUN\tools\dbq`:

- It uses `Microsoft.Data.Sqlite`, at the version the solution already uses.
- It runs a SQL file or string against
  `Data Source=$RUN\db\local.db;Mode=ReadOnly` and prints the rows as CSV.
- Take table and column names from
  `PharmaX.Persistence/Migrations/PharmaXDbContextModelSnapshot.cs`.

Every step that moves stock, money or a document gets a `dbq` query that
proves it. Save each query and its output with the step.

### 2.6 Printers

As `test.admin`, go to **File → Printer Setup** and set the receipt, label
and report printers to **Microsoft Print to PDF**. Use `SavePrint` to save
every receipt, label, X-read and purchase order into `$E2E\receipts\` as
`<role>_<step>_<docno>.pdf`.

Printer choices are workstation settings. In section 12, confirm that the
live app's printers are unchanged.

### 2.7 OBS Studio

OBS Studio 32.2.2 is installed at
`C:\Program Files\obs-studio\bin\64bit\obs64.exe`. It uses profile and scene
collection "Untitled", 1920×1080 at 60 fps, hybrid MP4, and records to
`C:\Users\user\Videos`.

1. Check that the scene captures the **primary display** and shows the mouse
   pointer. Do not edit the user's scenes without asking.
2. The built-in **WebSocket server is switched off**. **Ask the user** to
   switch it on (Tools → WebSocket Server Settings, port 4455, with
   authentication). Also ask whether you may use the password OBS stores in
   `%APPDATA%\obs-studio\plugin_config\obs-websocket\config.json`. **Never
   print that password, log it or save it anywhere in the evidence.**
3. Start and stop each recording through obs-websocket v5. The helper below
   has not yet been run on this PC, so **make a 5-second test recording
   first**.

```powershell
function Obs-Connect {
  $cfg = Get-Content "$env:APPDATA\obs-studio\plugin_config\obs-websocket\config.json" -Raw | ConvertFrom-Json
  $script:ObsWs = New-Object System.Net.WebSockets.ClientWebSocket
  $script:ObsCt = [Threading.CancellationToken]::None
  $ObsWs.ConnectAsync([Uri]"ws://127.0.0.1:$($cfg.server_port)", $ObsCt).Wait()
  $hello = Obs-Recv
  $id = @{ op = 1; d = @{ rpcVersion = 1; eventSubscriptions = 0 } }
  if ($hello.d.authentication) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $u = [Text.Encoding]::UTF8
    $secret = [Convert]::ToBase64String($sha.ComputeHash($u.GetBytes($cfg.server_password + $hello.d.authentication.salt)))
    $id.d.authentication = [Convert]::ToBase64String($sha.ComputeHash($u.GetBytes($secret + $hello.d.authentication.challenge)))
  }
  Obs-Send $id; $null = Obs-Recv   # op 2 = Identified
}
function Obs-Send($o) { $b = [Text.Encoding]::UTF8.GetBytes(($o | ConvertTo-Json -Depth 6 -Compress)); $ObsWs.SendAsync([ArraySegment[byte]]::new($b), 'Text', $true, $ObsCt).Wait() }
function Obs-Recv { $buf = New-Object byte[] 65536; $sb = New-Object Text.StringBuilder; do { $r = $ObsWs.ReceiveAsync([ArraySegment[byte]]::new($buf), $ObsCt).Result; [void]$sb.Append([Text.Encoding]::UTF8.GetString($buf, 0, $r.Count)) } until ($r.EndOfMessage); $sb.ToString() | ConvertFrom-Json }
function Obs([string]$type) { Obs-Send @{ op = 6; d = @{ requestType = $type; requestId = [guid]::NewGuid().ToString() } }; (Obs-Recv).d }
# Obs-Connect; Obs StartRecord; ...journey...; $out = (Obs StopRecord).responseData.outputPath
```

After `StopRecord`, move the file at `outputPath` to
`C:\Users\user\Videos\PharmaX E2E roles\` under the name from section 0.
If the WebSocket server cannot be switched on, ask the user to start and
stop each recording by hand, and record that you did so.

**While recording:**

- Keep PharmaX maximised on the primary display. Keep the terminal and
  every other window off that screen.
- Use the helpers that move the **real pointer** (`PHover`, `RealClick`,
  `PMenu`) so viewers can see what is clicked.
- Pause about 1.5 seconds on every result (message, grid, printed PDF)
  before moving on.
- Use only the test data described here. Do not open other patients' files
  on camera without a reason.

---

## 3. What each role must be able to do

### 3.1 The pharmacy's requirement (this run's expected results)

| Function | Admin | Pharmacist | Assistant | Cashier |
|---|---|---|---|---|
| Open, X-read, cash up and close own till | ✔ | ✔ | ✔ | ✔ |
| Raise a purchase order | ✔ | ✔ | ✔ | ✘ |
| Goods receipt against the order | ✔ | ✔ | ✔ | ✘ |
| View products: whole catalogue incl. dispensary | ✔ | ✔ | ✔ | ✘ |
| View products: **over-the-counter only** | – | – | – | ✔ |
| View patients and patient history (incl. imported) | ✔ | ✔ | ✔ | ✘ |
| Change an issued (incl. imported) dispensing: Change item | ✔ | ✔ | ✔ | ✘ |
| New script: Use again / From history / capture | ✔ | ✔ | ✔ | ✘ |
| Dispense and issue, print labels | ✔ | ✔ | ✔ | ✘ |
| Collect a script and take payment | ✔ | ✔ | ✔ | ✔ |
| Sell over the counter (tested in the cashier's journey) | – | – | – | ✔ |
| Reports on the old (imported) data | ✔ all | ✔ | ✔ | ✘ |

### 3.2 Rights in this build, and the gaps that remain

On 16 October 2026 the pharmacist was given `inv.purchase.order` and
`inv.goods.receive`, and the pharmacist assistant `inv.purchase.order`
(`PharmaX.Domain/Security/StandardRoleGrants.cs`). The admin, the pharmacist
and the assistant must therefore all be able to open Inventory → Purchase
Orders (F7), raise an order and receive it on Goods Receipt (F8). A refusal
there is a **FAIL**, not a role gap.

The seeder adds standard grants to existing roles at every start-up, so the
copy of the database picks these up when the app starts. Check this with
`dbq` against the role-permission table and record the result in the
preflight.

The only gap left against 3.1 is the **reports**:

- The pharmacist has *operational* and *clinical* reports only. Sales by
  Month, Top Sellers and the other *management* reports are not offered.
- The assistant has *operational* reports only.

**Before the run, read `StandardRoleGrants.cs` and
`PharmaX.Application/Security/ScreenAccess.cs` again.** Where a preset the
pharmacy expects is missing for a role:

1. Record `ROLE-GAP` with a screenshot of the report list.
2. Carry on with the presets the role does have. Do not change the grants.

---

## 4. Preflight: is all the data there?

Sign in as `test.admin`. Open each list and read its total from the pager
label, which reads `1 to 100 of N - page 1 of M`. Confirm each total with
`dbq`. **A count that is short of the import is a FAIL and blocks the run
until it is explained.**

| List | Where | Expected (import of 16/10 + test seed) |
|---|---|---|
| Products | Inventory → Products (F6) | 2,306 imported (988 of them imported inactive) + 10 test products, so **1,328 active**. Record whether the list shows inactive ones. |
| Patients | Dispensary → Patients (F4) | 6,725 imported + 6 demo + 2 test = **6,733** |
| Prescribers | Dispensary → Prescribers | **3,750** (17 of them share code 0, kept as 0-2, 0-3…) |
| Suppliers | Inventory → Suppliers | 250 imported + UPD + ALPHA = **252** (15 names shared, see the import report) |
| Departments | Inventory → Departments | 8 imported + demo DISP, FRONT, SURG and BABY where their codes are free (one demo department retired) |
| Categories | Inventory → Categories | 8 demo |
| Manufacturers | Inventory → Manufacturers | 4 demo. **The import brings none.** Report this as a data gap; do not invent manufacturers. |
| Medical aid societies | Dispensary → Medical Aid Societies | 3 demo. **The import brings none.** Same as above. |
| Dosage codes | Dispensary → Dosage Codes | Record the count. T, TAB, C, CAP, AF and BF must be present. |
| Users | Administration → Users | admin + 7 imported staff (inactive) + 6 test |
| Scripts (history) | `dbq` | 14,074 imported prescriptions, 19,338 lines |
| Waiting under Collect | Point of Sale → Scripts to Collect (F11) | 15 imported (e.g. script 14652 of 06/10/2026, 19.00) plus any from the demo trading day |

Also record the **baseline** values below. They are row 0 of the ledger:

- The home-screen tiles.
- Stock on hand of every product you will use (section 5).
- The open purchase orders.
- The patient account balances you will touch.

---

## 5. Choose the test data (record each choice in `$E2E\ledger.csv`)

Choose these from the **imported** data with `dbq`, and write down why each
was chosen:

| Ref | What | Rule |
|---|---|---|
| SUP | Supplier | Imported, active, with recent goods receipts in the history. |
| OTC1, OTC2 | Over-the-counter products | Imported, active, schedule < 3, has a selling price, supplied by SUP, ideally with a barcode. Not in the import's list of 18 products with no price. |
| DSP1 | Dispensary product | Imported, active, schedule ≥ 3, batch-tracked, has a price, supplied by SUP. Not on the expired-batch list. Ordered, received and dispensed by every dispensary role. |
| DSP2 | Second dispensary product | Imported, active, schedule ≥ 4. Used for the cashier's "not found" searches. |
| H-A, H-P, H-S | History patients, one per dispensary role | Imported patients with **at least 10 issued and collected scripts**, at least one with more than one item, and at least one whose items are all still in the catalogue. Use the patient with the most scripts for H-A, to test paging on Labels. |
| H2-A, H2-P, H2-S | Second history patients | Imported, with one issued script to "Use again" for the cashier's queue (step D12). |
| IMP-W | An imported script still waiting | Taken from the 15 waiting under Collect, e.g. script 14652. The cashier collects it. |
| CL | A claimed dispensing | If one exists. It is used for the item-change refusal in D8. |

---

## 6. Journey D – the dispensary roles (run three times)

Run D0–D16 three times, in this order:

1. As **test.admin** (prefix `A`).
2. As **test.pharmacist** (prefix `P`).
3. As **test.assistant** (prefix `S`).

Each run is one video. Case IDs are `<prefix>-D<n>`, for example `P-D9`.

| Step | Do | Expected output to record |
|---|---|---|
| **D0** | `Obs StartRecord` | Recording active (`GetRecordStatus`). |
| **D1** | Sign in with the role's username and `Test#1234`. | Home screen with the user's name. Tiles show **today's** figures. Screenshot each menu heading opened, with entries enabled or disabled, and compare with `ScreenAccess` for the role. Record any mismatch. |
| **D2** | File → Till Session (Ctrl+T) → open the session with a float of 200.00, counted by denomination. | Session open, float recorded. As **admin only**, try opening a second session: "This till already has an open session. Close it first." |
| **D3** | **Data availability and paging.** Open Products, Patients, Prescribers, Suppliers, Manufacturers, Departments, Categories and Medical Aid Societies. | The same totals as the preflight, for every list the role may open. Record the lists that are disabled for the role. Run the paging checks (section 9) on Products and Patients. |
| **D4** | **Purchase order.** Inventory → Purchase Orders (F7) → F6 Raise order → supplier SUP → lines OTC1 ×24 and DSP1 ×10 (packs) for admin, or OTC1 ×12 and DSP1 ×5 for the pharmacist and the assistant → save → send → print to PDF. Also open F2 What to order and record what it suggests. | PO number, status (Draft → Sent), line totals, PDF with the pharmacy's name. The PO list shows it, paged. Each role raises **its own** order, and a refusal is a FAIL (section 3.2). |
| **D5** | **Goods receipt.** Inventory → Goods Receipt (F8) → F2 Supplier... SUP → F4 Order... the D4 order → receive every line. DSP1 gets batch `E2E-<prefix>-<yyyymmdd>` and an expiry date at least 12 months away. Check the "what will be posted" preview → Post. | GRN number. PO status **Received**; it leaves "orders still expecting stock". Stock on hand goes up by exactly what was received. Stock is held in packs and dispensed in units, so record both. The new batch is visible. `dbq`: GoodsReceipt, its lines, StockMovement and Batch. Each role receives **its own** D4 order. |
| **D6** | **View products.** Products (F6) → search OTC1, then DSP1 → tick "Show batches and expiry". Open DSP1 (F4 Edit) and close it **without saving**. | Stock and batch reflect D5. Price, schedule, department and supplier are shown. Paging checks on a broad search, e.g. "a". **Export** takes every matching row, not 100. |
| **D7** | **Patient history.** Patients (F4) → find H-x by surname, then by phone. Open the file (F4) and close it without saving. Then go to Dispensary → **Labels** → F2 Find... H-x. | Every dispensing for H-x, **including the imported ones**, with dates, items, quantities, amounts and script numbers. Record the count and the oldest and newest dates. If there are more than 100, run the paging checks. Then Script Maintenance → H-x → F9 Choose → the scripts list (paged): record the statuses. |
| **D8** | **Edit history: change an item after the issue.** On Labels, choose an **imported**, issued and collected dispensing of H-x → **Change item...** → pick the item → enter a quantity 1 less than handed over → accept or change the amount → reason `E2E <prefix>: patient took one fewer`. | Record the message **verbatim**. It reads "Item changed. The patient now pays ... give ... back through Returns" or similar. Answer **Yes** to "Print the labels again to match?" and save the label PDF. Checks: (a) the line shows the new quantity and amount; (b) stock moved by the difference only, shown by `dbq` StockMovement and the Stock Transactions report for today; (c) the audit row `dsp.item.amend` holds the old value, the new value and the reason; (d) settle as the message says (Returns or a sale), where the till allows it, and record how. **Negatives:** a blank reason means nothing is changed; quantity 0 is not accepted; on **CL**, if found, expect "…was claimed from the medical aid (…). Reverse it and dispense it again to change it." |
| **D9** | **History reuse: Use again from Script Maintenance.** Script Maintenance → H-x → F9 Choose → pick an **imported** script whose items are still in the catalogue → **Use again**. Where you can, use the same script whose dispensing was changed in D8, so that H3 is proven. | The editor opens as "New script from an earlier one", with the prescriber filled in (from the imported prescribers), dated **today**, repeats 0 and no validity date. Items are at the **quantity last handed over**, which after D8 is the changed one. Items no longer in the catalogue are left out. Then: change one quantity, remove one item, press **From history...**, Ctrl+click two earlier items (descriptions fill in), and add **DSP1** by typing part of its name (type-ahead). Save. Record the new **script number**. The scripts list re-selects it with status Captured. **Negative:** run Use again on a script none of whose items are still in the catalogue, if one exists, and expect "Nothing on script N is still in the catalogue to use again." |
| **D10** | **Dispense and issue.** Dispensary → Dispensing (F2) → F2 Find script → H-x → the D9 script. | The lines appear. Screenshot the clinical warnings panel. Use **Change item** on one line *before* the issue: new quantity, amount left alone, so it is priced as usual and the total recalculates. Then **Issue** (F6) and save the labels PDF. Check the label: patient, drug, directions, dispenser, pharmacy name and address, and the **batch and expiry DSP1 was allocated from**. Record which batch it is (the D5 batch or an earlier-expiring one). Dispensing number; status Issued; co-payment. `dbq`: that batch is down by exactly the units dispensed. |
| **D11** | **Collect.** Point of Sale → Scripts to Collect (F11) → filter by H-x's name, then by the script number → F6 Take Payment → pay cash, over-tendering to check the change. | "<patient> paid. Next patient." The row leaves the queue. The receipt PDF shows the script number, lines, VAT, tender, change and operator. Stock moves **once** only, not again at payment (`dbq`). |
| **D12** | **Leave a script for the cashier.** Labels → F2 Find... H2-x → choose an imported dispensing → **Use again...** → change nothing → Save. Then Dispensing → issue it. **Do not collect it.** | Message: "The new script is captured. Open Dispensing to dispense it." Record the script and dispensing numbers in the ledger under "for the cashier". The script shows under Scripts to Collect. |
| **D13** | **Reports on the old data:** the role's set from section 10. | Each report run, previewed and exported. Totals compared with the import (section 10). Paging checks on one large preview. |
| **D14** | **Role checks** (below). | As listed. |
| **D15** | **Cash up.** File → Till Session → F2 X-read (PDF) → F5 Cash up and close → count the drawer by denomination. | "Expected by tender" equals the float plus the cash you took in D11 (and any D8 settlement), worked out **from your ledger**. Record the variance and close. Then File → Log Off. |
| **D16** | `Obs StopRecord`, then move and rename the video. | The video's path and length in `$E2E\videos.md`. |

**D14 role checks**

| Check | Admin | Pharmacist | Assistant |
|---|---|---|---|
| Labels shows **Reverse dispensing** | visible | visible | **hidden** |
| Labels shows **Change item...** and **Use again...** | visible | visible | visible |
| Script list shows **Use again** | visible | visible | visible |
| Dispensary → Merge Patient Files | enabled | enabled | **disabled** |
| Inventory → Purchase Orders (F7) / Goods Receipt (F8), raise and receive | enabled | enabled | enabled |
| Reports offered (F9) | every preset | operational + clinical | operational |
| Administration menu | every entry | only Stock Transactions (an operational report) | only Stock Transactions |
| Accounts menu | enabled | disabled | disabled |

As the **pharmacist only**, test a reversal:

1. Use again on another script for H2-P, then capture and issue it.
2. Reverse it on Labels → **Reverse dispensing**.
3. Confirm that it leaves Scripts to Collect and that its stock comes back.

Do not reverse the D12 script left for the cashier.

---

## 7. Journey C – the cashier (`test.cashier`, video 4)

Run this last, once D12 has left three scripts waiting.

| Step | Do | Expected output to record |
|---|---|---|
| **C0** | `Obs StartRecord` | Recording active. |
| **C1** | Sign in as `test.cashier` / `Test#1234`. | Screenshot each menu heading opened. The **Dispensary**, **Accounts**, **Reports**, **Stocktake** and **Administration** menus are all disabled. Under **Inventory**, only **Products** is enabled. **Point of Sale** has Sell, Scripts to Collect and Quotation; Returns opens only with a supervisor prompt. The home tiles show no dispensary, patient or report tiles. |
| **C2** | Open the till with a 200.00 float. | Session open. Before opening it, try F3 Sell: it must be refused or must ask for the till to be opened. |
| **C3** | **Products, over the counter only.** Products (F6). | The total N_cashier from the pager label must equal the `dbq` count of active products with **schedule < 3**, and must be smaller than admin's total. Search the following; each must return **nothing**: **DSP1**, **DSP2**, **AMOX50**, an imported schedule-4 product, and an imported scheduled sedative or tramadol line. OTC1 is found, and its stock matches the ledger (baseline + the D5 receipts − anything sold or dispensed so far). Run the paging checks (section 9). **Export** contains only over-the-counter rows (count = N_cashier). Record whether "Show batches and expiry" is offered. |
| **C4** | **Sell.** Point of Sale → Sell (F3). F2 Find item OTC1 → F4 Quantity 2 → add OTC2 (scan its barcode with `Chars ... -Enter` if it has one) → F6 Pay in cash with an over-tender. Make a second sale of OTC1 paid by card with a reference. Try F2 Find item for DSP1 and AMOX50. | Two receipt PDFs: lines, VAT, tender, change, document numbers in sequence. Stock goes down by exactly what was sold. Dispensary items are **not found** at the till. |
| **C5** | **Collect scripts.** Scripts to Collect (F11) → filter by script number → F6 Take Payment for each of the **three D12 scripts** and for **IMP-W**, the imported waiting script. | For each: "<patient> paid. Next patient.", a receipt PDF with the script number, and the row leaves the queue. **Record exactly what the cashier can see** on the queue, the payment window and the receipt: patient name, script number, amount, and whether medicine names or quantities appear. The pharmacy wants cashiers *not* to see dispensed detail, so anything beyond who, which script and how much is a **CHECK** for the pharmacy to decide. |
| **C6** | **What the cashier must not reach.** For each item below, try the menu, the shortcut, the home tile and the Ctrl+K "Go to any screen" box. | Every attempt is refused or not offered. The items are: F2 Dispensing, F4 Patients, F7 Purchase Orders, F8 Goods Receipt, F9 Reports, F12 Stock Alerts, Script Maintenance, Labels, Use again, Change item, any patient history and any report. Record the result of each attempt in the matrix (`$E2E\cashier-matrix.csv`). |
| **C7** | **Cash up.** File → Till Session → F2 X-read (PDF) → F5 Cash up and close. | Expected cash = float + cash sales + cash script payments, from **your ledger**. Record the variance. Then log off. |
| **C8** | `Obs StopRecord`, then move and rename the video. | As in D16. |

---

## 8. History reuse and item change: coverage checklist

Tick each line with the case ID that proved it. One per role where the column
says so.

| # | Behaviour | A | P | S | C |
|---|---|---|---|---|---|
| H1 | Use again from Script Maintenance on an **imported** script | | | | n/a (not reachable) |
| H2 | Draft: prescriber filled, dated today, repeats 0, no validity date | | | | |
| H3 | Draft quantities are **as last handed over** (after a D8 change, the changed quantity) | | | | |
| H4 | Items no longer in the catalogue are left out; a script with none left gives DSP-021 | | | | |
| H5 | From history... lists every earlier item, newest first, with date, description, quantity, directions and script number; multi-select works | | | | |
| H6 | Patient with no history: "Nothing has been prescribed or dispensed for this patient before." (use a patient created in the run, or test patient P000002) | | | | |
| H7 | Use again... on Labels captures the script and says "Open Dispensing to dispense it" | | | | |
| H8 | The new script dispenses, issues and collects like any other | | | | |
| I1 | Change item **before** the issue: re-priced, re-allocated and re-screened | | | | n/a |
| I2 | Change item **after** the issue: reason required, stock moves by the difference, audit row written | | | | n/a |
| I3 | The settlement message matches the money (pay more / give back / nothing changes) | | | | |
| I4 | Labels can be reprinted to match | | | | |
| I5 | A claimed dispensing is refused (DSP-053) | | | | |
| I6 | The button is hidden without `dsp.dispensing.amend` (cashier) | | | | |

---

## 9. Paging: checks for every list

The rule is 100 rows a page. The bar under the list reads
`1 to 100 of N  -  page 1 of M`, or `N shown` when everything fits on one
page. It has **|< First**, **< Previous**, **Next >** and **Last >|**, plus
the keys Ctrl+Page Down / Ctrl+Page Up and, with Shift, the last and first
page.

For each list below, run these checks:

- **P1** The label is correct on page 1.
- **P2** Next and Ctrl+Page Down both move to rows 101–200.
- **P3** Last and Ctrl+Shift+Page Down move to the final page, which holds
  `N mod 100` rows (or 100).
- **P4** First and Previous are disabled on page 1. Next and Last are
  disabled on the last page.
- **P5** A new search goes back to page 1.
- **P6** After an edit and refresh, the list stays on the same page.
- **P7** **Export takes all N rows.** Count the rows in the .xlsx by opening
  the zip and counting `<row>` elements in `xl/worksheets/sheet1.xml` with
  `System.IO.Compression`.
- **P8** At 150 % scaling, no caption is cut. On a narrow list, the buttons
  may shrink to arrows, which is allowed.

| List | Who | Expected N (source) |
|---|---|---|
| Products | A, P, S; C (OTC only) | Preflight; N_cashier for C |
| Patients | A, P, S | 6,733 |
| Prescribers | A | 3,750 |
| Suppliers | A | 252 |
| Labels: one patient's dispensings | A, P, S | H-A's count from `dbq` |
| Script Maintenance: one patient's scripts | A, P, S | `dbq` |
| Scripts to Collect | A, P, S, C | `dbq` |
| Purchase Orders | A, P, S | `dbq` |
| Users | A | Preflight |
| Stock Alerts | A, S | `dbq` |
| Reports preview on old data, e.g. Stock Transactions or All Transactions over the whole history | A, P, S | `dbq`; thousands of rows |

---

## 10. Reports on the old (imported) data

Reports (F9) → choose the preset → set the date range **from 01/01/2025 to
the day before the run** so that the test's own transactions are excluded →
Preview (F12) → screenshot → Export to Excel (F11) into `$E2E\exports\`.

For each report, record:

- The row count.
- The totals.
- The pharmacy name in the header.
- The comparison with the figure from the import report
  (`import-20261016-044332.txt`).

A total that does not match is a `CHECK` unless the two figures clearly
measure the same thing, in which case it is a `FAIL`. Either way, explain the
difference. If `dbq` shows any non-imported rows inside the range, for
example from the demo trading day, say so.

| Report (group) | A | P | S | Compare with (import report) |
|---|---|---|---|---|
| Sales by Month / Sales by Day (management) | ✔ | ROLE-GAP | ROLE-GAP | Till sales value 98,274.48; 26,612 sales; 40,775 lines; 34 refunds totalling 117.02 |
| Monthly Sales / Daily Sales (management) | ✔ | ROLE-GAP | ROLE-GAP | As above |
| Sales by Tender (management) | ✔ | ROLE-GAP | ROLE-GAP | Sales_Receipts TRANSTOTAL 132,442.11 |
| Top Sellers (management) | ✔ | ROLE-GAP | ROLE-GAP | Record the top 10 |
| Cash-ups (management) | ✔ | ROLE-GAP | ROLE-GAP | 764 cash-up lines; 341 sessions; counted 132,259.20; expected 124,096.08 |
| Price Changes / Price Overrides (management) | ✔ | ROLE-GAP | ROLE-GAP | 1,385 / 4,595 rows |
| Dispensing by Month / Dispensing by Doctor / Most Dispensed (clinical) | ✔ | ✔ | ROLE-GAP | 14,074 scripts; 19,338 lines; value dispensed 51,775.96 |
| Reversed Scripts (clinical) | ✔ | ✔ | ROLE-GAP | Record |
| All Transactions / Transactions (By Tender By Operator) / Sales (By Till Operator) (operational) | ✔ | ✔ | ✔ | Totals tie to the sales figures above |
| Stock Transactions (operational; also Administration → Stock Transactions) | ✔ | ✔ | ✔ | For reference: the import made 49,418 stock ledger entries, and deliveries at cost come to 125,573.82 (1,450 GRNs, 4,909 lines) |
| Breakages and Disposals / Returns to Supplier (operational) | ✔ | ✔ | ✔ | 867 adjustments / 717 returns to supplier |
| V.A.T Summary (financial) | ✔ | ✘ | ✘ | Record |
| Cost Prices to Check (management) | ✔ | ROLE-GAP | ROLE-GAP | Record |

In this table, "ROLE-GAP" means the preset should not be listed for that
role under the current grants. Confirm that it is absent, and record it under
the pharmacy's requirement that every dispensary role "can do a report on old
data". The pharmacist and the assistant must each still complete **at least
two** old-data reports from their own groups.

---

## 11. Ledger and the chain of documents

Keep `$E2E\ledger.csv` with one row for every movement:

- **Stock:** product, batch, units before → after, and the reason.
- **Cash:** till session, movement, running total.
- **Documents:** series, number, role, step.

For each role, the chain must be unbroken and visible in the report:

`PO → GRN → product stock → (imported dispensing changed) → new script → dispensing → receipt`

Together with:

`D12 script → dispensing → cashier's receipt`

---

## 12. Closing checks

1. **Live DB untouched.** The SHA-256 hash and LastWriteTime of
   `%LOCALAPPDATA%\PharmaX\local.db` match `live-db-before.txt`.
2. **Live printers** are as they were.
3. **Stock reconciles** for OTC1, OTC2, DSP1 and each D8 item: baseline + GRNs −
   sales − dispensings ± item changes ± reversals = closing. Check this in
   the app (Products) and in `dbq`.
4. **Cash reconciles** for each of the four till sessions: ledger = X-read =
   cash-up expected.
5. **Scripts to Collect** holds the imported waiting scripts less IMP-W,
   plus none of the scripts from this run.
6. **Every item change** has an audit row with the operator and the reason.
7. **Document numbers** in each series are in sequence with no gaps, and an
   abandoned payment uses no number.

---

## 13. Evidence and deliverables

Put everything under `evidence\e2e-roles\`:

1. **`results.csv`.** Call
   `Rec <case> <role> <step> <expected> <actual> <verdict> <shot>` for
   **every** step. The `actual` column holds the observed output: message
   text, document numbers, totals, pager label, PDF name. A plain "OK" is not
   enough.
2. **`screenshots\<role>\`.** Name each `<case>_<what>.png` and capture it
   with `Shot` or `ShotH` (PrintWindow, DPI-aware).
3. **`receipts\`** for every PDF and **`exports\`** for every Excel file.
4. **`ledger.csv`**, `live-db-before.txt` / `live-db-after.txt`, and the
   copied import report.
5. **`defects.md`**. One line per defect:
   `DEF-ROLE-### | Blocker/Major/Minor/Cosmetic | case | role | steps | expected | actual | screenshot | workaround`.
   Cross-reference older defect IDs (for example DEF-0015 and DEF-0027) if
   one has come back.
6. **`role-gaps.md`**: every ROLE-GAP, naming the permission that would
   close it (for example, the pharmacist needs `rpt.report.run.management`
   for Sales by Month).
7. **`checks.md`**: every CHECK, as a question for the pharmacy (for
   example, "The cashier sees the medicine names on the collect window. Is
   that acceptable?").
8. **`videos.md`**: the four video paths, their lengths, and the case IDs
   each one covers, with time stamps for the main steps.
9. **`REPORT.md`**, in the style of `evidence\e2e-oct10-sweep\REPORT.md`:
   - It opens with a summary: steps run by status, defects by severity, role
     gaps, and whether stock and cash reconciled.
   - Then one section per role with the document chain, the old-data report
     totals against the import, and the role's paging results.
   - Then the history reuse checklist (section 8), filled in.

**Done means:**

- All four journeys have been run to the end, or the blocker is recorded.
- Every line in section 8 and every list in section 9 has a case ID.
- Every report in section 10 has been run, or has a ROLE-GAP showing it is
  absent.
- Every closing check in section 12 has a PASS or a defect against it.
- All four videos are saved and listed.

---

## 14. Not in this run

Stocktake, day-end posting, journals, claims, quotations, pre-packs and
returns other than D8's settlement are covered by
`docs\E2E-Full-Journey-Test-Prompt.md`. Run that separately if needed. Do
not change System Setup, branding or the pharmacy profile in this run.
