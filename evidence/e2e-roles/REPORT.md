# Role journeys end to end: Administrator, Pharmacist, Pharmacist Assistant, Cashier

Run on 17 October 2026, following `docs/E2E-Role-Journeys-Test-Prompt.md`.

- **Data:** a copy of the pharmacy's live database, holding the import of 16/10/2026 from the old system: 2,306 products, 6,725 patients, 3,750 prescribers, 250 suppliers and 14,074 scripts.
- **Test layer:** the start-up seeders added the test accounts (`test.*`, password `Test#1234`) and demo reference data on the copy.
- **Display:** the PC's own screen at 150 % scaling.
- **Videos:** each role was driven with the real mouse and keyboard and recorded in OBS Studio. A yellow ring marks the pointer, a red ripple marks each click, and a band over the taskbar gives the heading and description of every step (see `videos.md`).

The live database was not touched. Its SHA-256 hash, last-write time and size are the same before and after (`live-db-before.txt`, `live-db-after.txt`).

**Build tested.** A Release build made on 16/10/2026 at 18:35, from branch `proj/0005` as it stood on disk then:
- commit `df7d62d`;
- the uncommitted changes of 16/10 06:12 to 06:47: the shared pager bar (`PagerBar`), display scaling, and type-ahead (`WordFilter`, `CellSuggestions`) across about 50 files;
- the role-grant change of 16/10 18:17 (`role-gaps.md`).

None of these is committed yet.

## Summary

| | Steps | PASS | CHECK | ROLE-GAP | FAIL |
|---|---|---|---|---|---|
| Preflight (is all the data there?) | 16 | 16 | | | |
| Administrator | 32 | 31 | 1 | | |
| Pharmacist | 28 | 24 | 1 | 3 | |
| Pharmacist Assistant | 26 | 23 | 1 | 2 | |
| Cashier | 26 | 22 | 4 | | |
| **Total** | **128** | **116** | **7** | **5** | **0** |

- **No step failed.** Every function the pharmacy listed worked for every role it was asked of.
- **The day reconciled.** For each of the six products used, stock on hand equals the starting stock plus every delivery, sale, dispensing and change (`stock-reconciliation.csv`). Every till balanced to the cent. Every document series is in sequence with no gaps.
- **Before the run**, the pharmacist was given purchase-order and goods-receipt rights and the assistant purchase-order rights (`role-gaps.md`). The only gaps left are some report presets.
- **Three defects**, none blocking: a window that runs off the screen at 150 % (Save reached with F6), mixed date formats, and an undefined dosage shorthand (`defects.md`).
- **Seven questions** for the pharmacy (`checks.md`). The main one is what a cashier should see when collecting a script.

## Is all the data there? (preflight)

The copy held every imported record, plus the test layer:

| List | In the test | From the import |
|---|---|---|
| Products | 2,316 (1,328 active) | 2,306 + 10 test products |
| Patients | 6,732 | 6,725 + demo/test patients |
| Prescribers | 3,750 | 3,750 |
| Suppliers | 252 | 250 + 2 test suppliers |
| Departments / Categories / Manufacturers / Medical aid societies | 12 / 8 / 4 / 26 | The import brings departments and schemes; categories and manufacturers come from the demo seed |
| Scripts / prescription lines / till sales | 14,074 / 19,338 / 26,612 | the same |
| Waiting under Collect | 15 | the 15 the import left waiting |

The administrator opened each of these lists on camera and read its total from the pager.

## Administrator (`test.admin`), video 1

The chain:

```
PO-001-000001 ($90, PCD PVT LTD)
  → G-001-000001 (C4 syrup 10 → 34, Nifedipine 10 → 20 packs, batches E2E-A-1017-*)
  → imported script 14568 changed
  → SC-001-000001 → D-001-000001 → S-001-000001 ($10.86)
```

and for the cashier: `SC-001-000002`.

- **Every menu entry is enabled** for the administrator.
- **Paging.** Products: 2,316 over 24 pages. Next, Last, First, Ctrl+Page Down and Ctrl+Shift+Page Up all turn the page, and the buttons switch off at each end. Patients: 6,732 over 68 pages, and a new search goes back to page 1. Prescribers: 3,750 over 38 pages. Suppliers: 252 over 3 pages.
- **Exports take every row.** The product export holds all 2,316, and the Stock Transactions export all 49,407.
- **Patient history.** Ncube Portia's 103 imported dispensings show on Labels over two pages.
- **Changing imported history.** Change item on script 14568 took Co-codamol from 30 to 29, with a reason.
  - The message: "Item changed. The patient now pays 10.42. It was already paid, so give 0.08 back through Returns."
  - Stock went up by exactly one tablet (129.167 → 129.25 packs of 12).
  - An audit row holds the old value, the new value and the reason, and the labels were reprinted.
- **History reuse.**
  - Use again drafted a new script dated today, with the prescriber kept, no repeats, and Co-codamol at the 29 last handed over.
  - One quantity was changed and one item removed. Two earlier items were added with From history (Ctrl+click), and Nifedipine was typed in by name.
  - The script was captured as SC-001-000001.
- **Dispensing.**
  - Change item before the issue (Nifedipine 30 → 28) re-priced the line (totals 11.00 → 10.86).
  - The issue printed 5 labels and allocated Nifedipine from the batch received that morning.
  - It was collected and paid in cash, with change of 9.14. The receipt carries "REF: Script No-SC-001-000001".
- **Reports on the old data** (01/01/2025 to 15/10/2026), all with the pharmacy's name in the header:
  - **Sales by Month:** 13 months totalling **98,274.48**, exactly the import's till sales value.
  - **Sales by Tender:** 98,274.48.
  - **Dispensing by Month:** 51,775.88. The import says 51,775.96; the difference is the 0.08 just changed.
  - **Cash-ups:** 499 rows.
  - **Stock Transactions:** 49,407 rows over 495 pages, exported in full.
- **Cash-up:** expected cash 210.86, declared 210.86, Z-read printed.

## Pharmacist (`test.pharmacist`), video 2

The chain:

```
PO-001-000002 ($45)
  → G-001-000002
  → imported script 14041 changed (Nifedipine 14 → 13, give back 0.07)
  → SC-001-000003 → issued → S-001-000002 ($6.66)
```

for the cashier: `SC-001-000004`; reversed: `SC-001-000005` / `D-001-000005`.

- **Menus** match the role exactly. Off: tariffs, the reference lists, stock adjustments, price changes, Post stocktake, Accounts, and Administration apart from Stock Transactions.
- **New rights:** the pharmacist raised and received a purchase order.
- **History.** Siziba Bukani's 79 imported dispensings. Use again with From history added Nifedipine tablets and hydrochlorothiazide; one was removed again, and PCD C4 was typed in by name.
- **Batch allocation.** The issue took PCD C4 from the old system's batch 085i027, the earliest expiry, before the batch received that morning.
- **Reversal.** D-001-000005: "D-001-000005 reversed. The stock is back on the shelf." It left the collection queue (18 → 17).
- **Reports on the old data:**
  - **Dispensing by Month and Dispensing by Doctor:** 51,775.81 each, after the second change.
  - **Most Dispensed:** 743 products; Nifedipine Calcigard comes first.
  - **Stock Transactions:** paged and exported.
  - **Not offered:** Sales by Month, Top Sellers and Cash-ups (ROLE-GAP, see `role-gaps.md`).
- **Cash-up:** 206.66.

## Pharmacist Assistant (`test.assistant`), video 3

The chain:

```
PO-001-000003 ($45)
  → G-001-000003
  → imported script 14259 changed (Spironolactone 5 → 4, give back 0.20)
  → SC-001-000006 → issued → S-001-000003 ($6.40)
```

for the cashier: `SC-001-000007`.

- **Menus** are as the pharmacist's, except that Merge Patient Files is unavailable. Labels has no Reverse dispensing button for the assistant; Change item and Use again are there.
- **New right:** the assistant raised a purchase order.
- **Duplicate patient.** The import has Mhlanga Eucalia twice: file 737 with 67 dispensings and file 6557 with one. The run chose file 737.
- **Reports on the old data:**
  - **All Transactions:** 26,612, the import's till sales.
  - **Breakages and Disposals:** 867, the import's adjustments.
  - **Returns to Supplier:** 717, the import's returns.
  - **Stock Transactions:** paged and exported.
  - **Not offered:** Dispensing by Month and Sales by Month (ROLE-GAP).
- **Cash-up:** 206.40.

## Cashier (`test.cashier`), video 4

- **Menus.** Dispensary, Accounts, Reports, Stocktake and Administration are unavailable. Under Inventory only Products is enabled, and the home tiles say "Not permitted for this user".
- **No session, no sale.** Sell before the till is opened: "Open a till session before selling."
- **Over-the-counter only.**
  - The product list holds 1,508 items, all below schedule 3, against 2,316 for the dispensary roles. It pages over 16 pages, and the export holds the same 1,508.
  - Searches for Calcigard, Enalapril 10mg, Tramadol and Diazepam find nothing.
  - Typing Nifedipine's code (1593) at the till opens Find product with "No products match".
- **Sales.**
  - **Cash sale S-001-000004:** $12.00, change $8.00. PCD C4 went on two lines: one from the old batch's last unit, one from the batch received that morning.
  - **Card sale S-001-000005:** with a reference.
- **Collections.**
  - **Today's scripts:** the three left by the other roles (SC-001-000002, -000004, -000007).
  - **From the old system:** script **14652**, dispensed there on 06/10/2026 and still waiting ($19.00).
  - Each one: "<patient> paid. Next patient.", a receipt, and the script left the queue.
- **What the cashier cannot reach.** F2, F4, F7, F8, F9, F12 and Ctrl+K "patients" on the home screen open nothing.
- **Cash-up.** Cash 243.80: the float of 200.00, plus 12.00 sales, plus 31.80 collections. Card 4.00.

## Checks across the day

| Check | Result |
|---|---|
| Live database unchanged | PASS: same hash, time and size |
| Stock reconciles (C4 syrup, Allergex, Nifedipine, Co-codamol, Arthrexin, Spironolactone) | PASS, all six (`stock-reconciliation.csv`) |
| Cash reconciles for each till session | PASS: 210.86, 206.66, 206.40, 243.80 + card 4.00; no variance |
| Collection queue | PASS: 14 left (the 15 imported less 14652); every script issued today was collected or reversed |
| Every item change has an audit row | PASS: six rows. The three after the issue carry the operator's reason; the three before it record the change. |
| Document numbers in sequence | PASS: PO-001-000001 to 3, G-001-000001 to 3, SC-001-000001 to 7, D-001-000001 to 7, S-001-000001 to 9 |

## History reuse and item change (section 8 of the prompt)

| # | Behaviour | A | P | S |
|---|---|---|---|---|
| H1 | Use again from Script Maintenance on an imported script | A-D9 | P-D9 | S-D9 |
| H2 | Draft: prescriber kept, dated today, 0 repeats, no validity date | A-D9a | P-D9a | S-D9a |
| H3 | Quantities as last handed over (after the D8 change) | A-D9a (Co-codamol 29) | P-D9a (Nifedipine 13) | S-D9a (Spironolactone 4) |
| H5 | From history lists earlier items newest first; multi-select | A-D9 (108 items) | P-D9 (92) | S-D9 (96) |
| H7 | Use again on Labels: "The new script is captured. Open Dispensing to dispense it." | A-D12 | P-D12 | S-D12 |
| H8 | The new script dispenses, issues and collects | A-D10/11 | P-D10/11 | S-D10/11 |
| I1 | Change item before the issue: re-priced | A-D10 | P-D10 | S-D10 |
| I2 | Change item after the issue: reason, stock by the difference, audit | A-D8 | P-D8 | S-D8 |
| I3 | The settlement message matches the money | A-D8 | P-D8 | S-D8 |
| I4 | Labels reprinted to match | A-D8 | P-D8 | S-D8 |
| I6 | Not reachable by the cashier | C-D1m, C-C6 | | |

**Not covered:**
- **H4 and H6** (the refusals for a script with nothing left in the catalogue, and for a patient with no history): every patient used has history.
- **I5** (a claimed dispensing refused): none of the imported dispensings is on a medical aid claim.

## Evidence

| What | Where |
|---|---|
| Every step, with the observed output | `results.csv` (128 rows) |
| Screenshots, one folder per role | `screenshots/<role>/` (130) |
| Receipts, labels, purchase orders, X- and Z-reads | `receipts/` (53 PDFs) |
| Full exports: products, the cashier's products, Stock Transactions by each role | `exports/` |
| Menu entries each role found on and off | `menus-*.csv` |
| Stock, cash and documents as they moved | `ledger.csv` |
| Stock check | `stock-reconciliation.csv` |
| The import report the totals were checked against | `import-20261016-044332.txt` |
| Live database before and after | `live-db-before.txt`, `live-db-after.txt` |

The tools that drove the run are in `%USERPROFILE%\PharmaX-E2E-roles\tools`:

- **`drive.ps1`:** real pointer and keyboard, UI Automation lookups, the OBS WebSocket.
- **`journey.ps1`:** the four journeys.
- **`PhxOverlay`:** the pointer ring and caption band.
- **`DbQuery`:** read-only SQL on the copy.

To run them again, copy the live database to `db\local.db` and start the app with `Development__SeedTestUsers=true`. Then dot-source both scripts and call `Run-Role A`, `P`, `S` or `C`.
