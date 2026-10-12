# Every screen, every button: test sweep

Run on 10 and 11 October 2026 as the Administrator, on a copy of the PharmaX database with the RSI import (PharmaX_DEMO_VIDEO). Each menu entry was opened from the menu and every button and tick box on each screen pressed, with the pointer on screen. Questions were answered No or Cancel and forms closed with Cancel; tick boxes were put back as they were. Every report was previewed from 1 January 2025. Recorded with OBS as videos 4a to 4d.

**338 options pressed** across 59 menu entries and 33 reports: 336 OK, 2 SKIP.

| Menu | OK | SKIP |
|---|---|---|
| Dispensary | 70 | 0 |
| Point of Sale | 25 | 1 |
| Inventory | 84 | 0 |
| Accounts | 41 | 0 |
| Reports | 39 | 0 |
| Stocktake | 24 | 0 |
| Administration | 39 | 1 |
| File | 13 | 0 |
| Help | 1 | 0 |

Status: OK = the option answered (a screen, a question or a message) without an error; FAIL = an error message or a crash; CHECK = worth a look; SKIP = deliberately not pressed; DRIVER = the test's own automation tripped, not PharmaX.

## Findings

No option produced an error message or a crash. Every menu entry opened its screen, and every button answered with a screen, a question or a plain message. Every Retire, Deactivate and Take out of use asks first. The findings below are small.

1. **Till keys that do nothing on an empty sale.** On Point of Sale, F4 Quantity, F5 Discount and Esc Void line return without a word when no line is chosen (`PharmaX.Desktop/Features/Sales/PosPresenter.cs` lines 246, 262, 306). F6 Pay and F8 Hold do explain themselves ("Add at least one item before taking payment."), and Dispensing's F7 says "Choose the item to take off." The fix is a "Choose a line first" message.
2. **Cancel on the variance reason reads as an error.** Till Session > Cash up and close, with a variance that needs a reason: pressing Cancel on the reason prompt shows "A reason is required for this variance." (`PharmaX.Desktop/Features/Tills/TillSessionPresenter.cs` lines 117-124). The session correctly stays open; Cancel could simply step back without the message.
3. **Open stocktake refuses a blank description without saying why.** Pressing Open with the description empty leaves the prompt open with no message (`PharmaX.Desktop/Features/Inventory/frmStocktakeList.cs`, the FormClosing check).
4. **Three Stocktake menu entries open one screen.** Capture, Variance and Post all open the same Stocktake list. New Stocktake opens the list too, under its prompt, and the list stays open when the prompt is cancelled. Nothing is wrong, but three entries for one screen may confuse staff.

### Fixed later on 10 October

All four findings were fixed, along with a crash found earlier. Each was checked in the running app on a fresh copy of the RSI database; all 14 checks passed.

All four sweep videos (4a to 4d) were then recorded again on the fixed app, and every row below comes from that run.

The fixes:

1. **Till keys on an empty sale.** F4, F5 and Esc now say "There is nothing on this sale yet.", or "Choose a line on the sale first." when the sale has lines but none is chosen.
2. **Cancel on the variance reason.** It now steps back with no message, and the session stays open. The stocktake's own variance reason had the same fault and is fixed too.
3. **Blank stocktake description.** Open with no description now says "Give the count a description. It is printed on the variance report."
4. **Stocktake menu.** Each entry now does its own job:
   - **Capture** opens the count in progress.
   - **Variance** opens it with "Variance only" ticked.
   - **Post** opens it and starts posting.
   - With no count in progress, each one says "No count is in progress. Start one with F3 New count."
5. **EcoCash crash.** A negative EcoCash statement on Declare or Cash up used to end in "Something went wrong". It is now refused with "The EcoCash statement cannot be less than zero…", and the app carries on.

Not pressed:
- **Administration > Back Up Now**: it writes a backup into the real backup folder at once.
- **Point of Sale > F1 Lock**: it locks the till until a password is typed. It is shown working in the Cashier video.

What the sweep does and does not cover:
- Each option is pressed once, in the state the screen opens in: nothing filled in, the first row chosen.
- "Nothing opened" means no new window or message appeared. The screen itself may still have changed; for example, Goods Receipt's Add line adds an empty row.
- The full flows are in videos 1 to 3: setup, users, dispensing with the allergy check, labels, selling, holds, refunds, script collection and the cash-up.

## Cross-check of the data (11 October)

These were found while building and checking the role videos. They are about the data, not the screens, and nothing was changed: the working PharmaX database was only read.

1. **The working PharmaX database holds test data from 7 October, 16:37.** It holds:
   - 387 stock movements marked `TESTSTOCK` / `Demo`, which set stock to 10 units, so 1,166 of the 1,328 active products show exactly 10 on hand instead of RSI's counts;
   - 6 test staff accounts (`test.admin`, `test.pharmacist`, `test.cashier`, `test.assistant`, `test.stock`, `test.manager`);
   - 10 test products (`PAN500`, `AMOX50`, `COUGH1` and others);
   - 4 test sales;
   - 7 non-RSI test patients.

   Before going live, this should be removed or the RSI import run again into a clean database. Stock on hand in particular is not RSI's.
2. **Controlled medicines came across from RSI at schedule 3.** Diazepam, phenobarbital and tramadol are examples. PharmaX treats schedule 6 and above as controlled, so these are dispensed without a pharmacist's authorisation, even by a Pharmacist Assistant. The pharmacy should set their schedules in Products.
3. **No pharmacy telephone number is saved.** Labels and trailer labels print the name and address but no number until one is entered in Administration > System Console > Telephone.
4. **RSI's "UF" dosage shorthand is not defined.** It prints as typed on a label until its meaning is added under Dispensary > Dosage Codes. T, TAB, C, CAP, AF and BF are now standard.
5. **Stock is held in packs and dispensed in units.** For example, 20 tramadol tablets take 0.2 of a 100-tablet pack off the shelf. This is by design; it is worth knowing when reading stock figures.

## Videos

In `C:\Users\mlilo\Videos\PharmaX demo`:

- `PharmaX demo 1 - Administrator.mp4`, `2 - Pharmacist.mp4` and `3 - Cashier.mp4`: each role's day, end to end, on the RSI data.
- `PharmaX demo 5 - Pharmacist Assistant.mp4`, `6 - Stock Controller.mp4` and `7 - Manager.mp4`: the other three roles' days, end to end, on the RSI data.
- `PharmaX demo 4a - Every option - Dispensary and Point of Sale.mp4`.
- `4b - Every option - Inventory and Accounts.mp4`.
- `4c - Every option - Reports and Stocktake.mp4`.
- `4d - Every option - Administration, File and Help.mp4`.

## Every option and what it did

### Dispensary > Dispensing

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Dispensing' |
| F3 Suspended | OK | message: No scripts are kept to finish later. |
| F8 Quote | OK | window 'BethRapha Pharmacy - Dispensary quote' |
| F7 Remove item | OK | message: Choose the item to take off. |
| F12 Reprint labels | OK | window 'BethRapha Pharmacy - Labels' |
| F6 Issue | OK | message: Find the patient and the script first. |
| F2 Find script | OK | window 'BethRapha Pharmacy - Find patient' |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Patients

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Patients' |
| F3 Add | OK | window 'BethRapha Pharmacy - New patient' |
| F4 Edit | OK | window 'BethRapha Pharmacy - Patient' |
| F7 Deactivate | OK | message: Deactivate this patient? Their dispensing history is kept - it is a legal record - but they will not appear in searches. |
| F12 Documents | OK | window 'BethRapha Pharmacy - Patient documents' |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Re-Print Script

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Find patient' |
| F3 Add | OK | window 'BethRapha Pharmacy - New patient' |
| F4 Edit | OK | window 'BethRapha Pharmacy - Patient' |
| F12 Documents | OK | window 'BethRapha Pharmacy - Patient documents' |
| F9 Choose | OK | window 'BethRapha Pharmacy - Find script' / closed the screen |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Script Maintenance

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Find patient' |
| F3 Add | OK | window 'BethRapha Pharmacy - New patient' |
| F4 Edit | OK | window 'BethRapha Pharmacy - Patient' |
| F12 Documents | OK | window 'BethRapha Pharmacy - Patient documents' |
| F9 Choose | OK | window 'BethRapha Pharmacy - Scripts' / closed the screen |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Dispensary Quote

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Dispensary quote' |
| F2 Find product | OK | window 'BethRapha Pharmacy - Find product' |
| F9 Resume quote | OK | message: No dispensary quote has been saved yet. |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Labels

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Labels' |
| F2 Find... | OK | window 'BethRapha Pharmacy - Find patient' |
| F12 Claim form | OK | message: Choose a dispensing first. |
| F7 Reverse dispensing | OK | message: Choose a dispensing first. |
| F6 Print labels | OK | message: Choose a dispensing first. |
| F10 What prints | OK | message: Choose a dispensing first. |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Tariffs

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Tariffs' |
| F5 Refresh | OK | nothing opened |
| F7 Retire | OK | message: Retire this tariff? It stays on past claims but will not price anything from now on. |
| F4 Edit | OK | window 'BethRapha Pharmacy - Tariff' |
| F3 Add | OK | window 'BethRapha Pharmacy - Tariff' |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Merge Patient Files

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Merge patient files' |
| F2 Find... | OK | window 'BethRapha Pharmacy - Find patient' |
| F3 Find... | OK | window 'BethRapha Pharmacy - Find patient' |
| Esc Cancel | OK | nothing opened / closed the screen |

### Dispensary > Medical Aid Societies

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Medical aid societies' |
| F5 Refresh | OK | nothing opened |
| F7 Retire | OK | message: Retire this society? It stays on past records but cannot be chosen again. |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit society' |
| F3 Add | OK | window 'BethRapha Pharmacy - New society' |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Prescribers

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Prescribers' |
| F5 Refresh | OK | nothing opened |
| F7 Retire | OK | message: Retire this prescriber? It stays on past records but cannot be chosen again. |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit prescriber' |
| F3 Add | OK | window 'BethRapha Pharmacy - New prescriber' |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Dosage Codes

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Dosage codes' |
| F5 Refresh | OK | nothing opened |
| F7 Retire | OK | message: Retire this dosage code? It stays on past records but cannot be chosen again. |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit dosage code' |
| F3 Add | OK | window 'BethRapha Pharmacy - New dosage code' |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > To Follow

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - To follow' |
| Esc Close | OK | nothing opened / closed the screen |

### Dispensary > Drug Returns

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Returns and credit notes' |
| F2 Find | OK | message: Enter the document number from the receipt. |
| Esc Close | OK | nothing opened / closed the screen |

### Point of Sale > Sell

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Point of Sale' |
| F2 Find item | OK | window 'BethRapha Pharmacy - Find product' |
| F3 Customer | OK | window 'BethRapha Pharmacy - Find account' |
| F4 Quantity | OK | message: There is nothing on this sale yet. |
| F5 Discount | OK | message: There is nothing on this sale yet. |
| F6 Pay | OK | message: Add at least one item before taking payment. |
| F7 Close | OK | nothing opened / closed the screen |
| F8 Hold | OK | message: There is nothing on this sale to hold. |
| F9 Recall | OK | message: No sales are held. |
| F10 Quotation | OK | window 'Quotation' |
| F12 Reprint | OK | window 'Reprint receipt' |
| F1 Lock | SKIP | locks the till until a password is typed; shown in the Cashier video |
| F11 No sale | OK | window 'No sale' |
| Esc Void line | OK | message: There is nothing on this sale yet. |

### Point of Sale > Scripts to Collect

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Scripts to collect' |
| F6 Take Payment | OK | window 'BethRapha Pharmacy - Collect script  -  Reshma Pillay' |
| F5 Refresh | OK | nothing opened |
| Esc Close | OK | nothing opened / closed the screen |

### Point of Sale > Returns and Credit Notes

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Returns and credit notes' |
| F2 Find | OK | message: Enter the document number from the receipt. |
| Esc Close | OK | nothing opened / closed the screen |

### Point of Sale > Quotation

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Quotation' |
| F3 Start quotation | OK | nothing opened |
| F2 Find item | OK | window 'BethRapha Pharmacy - Find product' |
| F4 Open existing | OK | window 'Open quotation' |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Stock Alerts

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Stock alerts' |
| F4 Open Product | OK | window 'BethRapha Pharmacy - Edit product' |
| F5 Refresh | OK | nothing opened |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Departments

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Departments' |
| F5 Refresh | OK | nothing opened |
| F7 Bring back | OK | message: Bring this department back into use? |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit department' |
| F3 Add | OK | window 'BethRapha Pharmacy - New department' |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Categories

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Categories' |
| F5 Refresh | OK | nothing opened |
| F7 Retire | OK | message: Retire this category? It stays on past records but cannot be chosen again. |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit category' |
| F3 Add | OK | window 'BethRapha Pharmacy - New category' |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Suppliers

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Suppliers' |
| F3 Add | OK | window 'BethRapha Pharmacy - New supplier' |
| F4 Edit | OK | window 'BethRapha Pharmacy - Supplier' |
| F7 Deactivate | OK | message: Deactivate this supplier? Their goods receipts and the batches taken in from them are kept; they simply stop appearing when receiving stock. |
| Show deactivated suppliers | OK | ticked and unticked |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Purchase Orders

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Purchase orders' |
| F5 Refresh | OK | nothing opened |
| F6 Raise order | OK | window 'BethRapha Pharmacy - Raise purchase order' |
| F2 What to order | OK | window 'BethRapha Pharmacy - Raise purchase order' |
| F7 Cancel order | OK | window 'Cancel order' |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Goods Receipt

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Goods receipt' |
| F2 Supplier... | OK | window 'BethRapha Pharmacy - Choose supplier' |
| F4 Order... | OK | message: Choose the supplier first. |
| F3 Add line | OK | nothing opened |
| F7 Remove line | OK | nothing opened |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Manufacturers

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Manufacturers' |
| F5 Refresh | OK | nothing opened |
| F7 Retire | OK | message: Retire this manufacturer? It stays on past records but cannot be chosen again. |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit manufacturer' |
| F3 Add | OK | window 'BethRapha Pharmacy - New manufacturer' |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Products

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Products' |
| F6 Next | OK | nothing opened |
| F2 Previous | OK | nothing opened |
| F5 Refresh | OK | nothing opened |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit product' |
| F3 Add | OK | window 'BethRapha Pharmacy - New product' |
| Show batches and expiry | OK | ticked and unticked |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Stock

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Reports' |
| F11 Export to Excel | OK | file dialog 'Export report' |
| Preview (F12) | OK | nothing opened |
| Below reorder level only | OK | ticked and unticked |
| Close (F7) | OK | nothing opened / closed the screen |

### Inventory > Stock Adjustments

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Stock adjustment' |
| F2 Find | OK | nothing opened |
| F6 Record | OK | message: Find the product first. |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Return to Supplier

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Stock adjustment' |
| F2 Find | OK | nothing opened |
| F6 Record | OK | message: Find the product first. |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Move Stock and Pre-packs

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Stock adjustment' |
| F2 Find | OK | nothing opened |
| F6 Record | OK | message: Find the product first. |
| Esc Close | OK | nothing opened / closed the screen |

### Inventory > Transfers

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Reports' |
| F11 Export to Excel | OK | message: No data for the selected period and filters, so there is nothing to export. |
| Preview (F12) | OK | message: No data for the selected period and filters. |
| Close (F7) | OK | nothing opened / closed the screen |

### Inventory > Breakages

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Reports' |
| F11 Export to Excel | OK | message: No data for the selected period and filters, so there is nothing to export. |
| Preview (F12) | OK | message: No data for the selected period and filters. |
| Close (F7) | OK | nothing opened / closed the screen |

### Inventory > Expired Stock

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Reports' |
| F11 Export to Excel | OK | file dialog 'Export report' |
| Preview (F12) | OK | nothing opened |
| Close (F7) | OK | nothing opened / closed the screen |

### Inventory > Price Changes

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Reports' |
| F11 Export to Excel | OK | message: No data for the selected period and filters, so there is nothing to export. |
| Preview (F12) | OK | message: No data for the selected period and filters. |
| Close (F7) | OK | nothing opened / closed the screen |

### Inventory > Mass Price Change

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'Mass price change' |
| Cancel | OK | nothing opened / closed the screen |
| OK | OK | window 'Mass price change' / closed the screen |

### Accounts > Trial Balance

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Trial balance' |
| F2 Show Postings | OK | window 'Postings to 1000' |
| F5 Refresh | OK | nothing opened |
| F11 Export to Excel | OK | file dialog 'Export trial balance' |
| Esc Close | OK | nothing opened / closed the screen |

### Accounts > Journal Entry

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Journal entry' |
| F2 Clear | OK | nothing opened |
| F3 Add line... | OK | window 'BethRapha Pharmacy - Journal line' |
| F7 Remove | OK | nothing opened |
| F11 Export to Excel | OK | file dialog 'Export journal entry' |
| Esc Close | OK | nothing opened / closed the screen |

### Accounts > Patient Accounts

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Patient accounts' |
| F6 Take Payment | OK | window 'Payment on account' |
| F2 Statement | OK | window 'Statement - Absalom Dube' |
| F3 New Account | OK | window 'BethRapha Pharmacy - Find patient' |
| F4 Credit Limit | OK | window 'Credit limit' |
| F8 Hold | OK | window 'Put the account on hold' |
| F5 Refresh | OK | nothing opened |
| F11 Export to Excel | OK | file dialog 'Export patient accounts' |
| Owing only | OK | ticked and unticked |
| Esc Close | OK | nothing opened / closed the screen |

### Accounts > Medical Aid Claims

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Medical aid claims' |
| F3 Pay amount... | OK | message: Choose a claim first. |
| F7 Reject | OK | message: Choose the claim the scheme refused. |
| F4 Age Analysis | OK | message: No scheme owes the pharmacy anything. |
| F12 Claim Form | OK | message: Choose a claim first. |
| F5 Refresh | OK | nothing opened |
| F11 Export to Excel | OK | message: There is nothing on the screen to export. |
| Esc Close | OK | nothing opened / closed the screen |

### Accounts > Chart of Accounts

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Chart of accounts' |
| F3 Add | OK | window 'New account' |
| F2 Rename | OK | window 'Rename account' |
| F7 Take Out of Use | OK | message: Take 1000 Bank out of use? Its history stays in the books. |
| F11 Export to Excel | OK | file dialog 'Export chart of accounts' |
| Show accounts no longer in use | OK | ticked and unticked |
| Esc Close | OK | nothing opened / closed the screen |

### Accounts > Day-End Posting

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Day-end posting' |
| F2 Look at day | OK | nothing opened |
| F4 Export to Excel | OK | file dialog 'Export day-end journal' |
| F11 Export CSV | OK | file dialog 'Export journal' |
| Esc Close | OK | nothing opened / closed the screen |

### Reports > every report

| Option | Status | What happened |
|---|---|---|
| (tree) | OK | 33 reports |
| Dispensing by Doctor (with RSI): Preview | OK | 18 rows |
| Dispensing by Month (with RSI): Preview | OK | 14 rows |
| Most Dispensed (with RSI): Preview | OK | 745 rows |
| Reversed Scripts: Preview | OK | Reversed Scripts  /  no rows for Reversed Scripts / Branch 1 / 01/01/2025 to 10/10/2026 / no filters / message: No data for the selected period and filters. |
| Cash-ups (RSI): Preview | OK | 764 rows |
| Breakages and Disposals: Preview | OK | 1,387 rows |
| Cost Prices to Check: Preview | OK | 791 rows |
| Price Changes: Preview | OK | Price Changes  /  no rows for Price Changes / Branch 1 / 01/01/2025 to 10/10/2026 / no filters / message: No data for the selected period and filters. |
| Returns to Supplier: Preview | OK | Returns to Supplier  /  no rows for Returns to Supplier / Branch 1 / 01/01/2025 to 10/10/2026 / no filters / message: No data for the selected period and filters. |
| Stock Expiring: Preview | OK | 1,374 rows |
| Stock Movements (RSI): Preview | OK | 6,493 rows |
| Stock On Hand: Preview | OK | 1,328 rows |
| Stock On Hand by Batch: Preview | OK | 1,863 rows |
| Stock Transactions: Preview | OK | 2,106 rows |
| Transfers: Preview | OK | Transfers  /  no rows for Transfers / Branch 1 / 01/01/2025 to 10/10/2026 / no filters / message: No data for the selected period and filters. |
| All Transactions: Preview | OK | 4 rows |
| Cash Declaration Form: Preview | OK | 14 rows |
| Daily Sales: Preview | OK | 1 rows |
| Daily Sales (Line Items): Preview | OK | 10 rows |
| Drawer Opened Without a Sale: Preview | OK | History  /  no rows for Drawer Opened Without a Sale / Branch 1 / 01/01/2025 to 10/10/2026 / no filters / message: No data for the selected period and filters. |
| Monthly Sales: Preview | OK | 1 rows |
| Monthly Sales (Line Items): Preview | OK | 11 rows |
| Price Overrides (with RSI): Preview | OK | 4,595 rows |
| Rated - All Transactions: Preview | OK | Transactions  /  no rows for Rated - All Transactions / Branch 1 / 01/01/2025 to 10/10/2026 / no filters / message: No data for the selected period and filters. |
| Sales (By Till Operator): Preview | OK | 1 rows |
| Sales by Day (with RSI): Preview | OK | 341 rows |
| Sales by Month (with RSI): Preview | OK | 13 rows |
| Sales by Tender (with RSI): Preview | OK | 10 rows |
| Sales Summary (By Markup %): Preview | OK | 3 rows |
| Top Sellers (with RSI): Preview | OK | 1,136 rows |
| Transactions (By Tender By Operator): Preview | OK | 4 rows |
| V.A.T Detailed (Above Zero): Preview | OK | 1 rows |
| V.A.T Summary: Preview | OK | 1 rows |
| Export to Excel | OK | file dialog 'Export report' |

### Reports > RSI History

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - RSI history' |
| F2 Find | OK | nothing opened |
| F11 Export | OK | file dialog 'Export rsi history' |
| Esc Close | OK | nothing opened / closed the screen |

### Stocktake > New Stocktake

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Open stocktake' |
| Open | OK | message: Give the count a description. It is printed on the variance report. |
| Batch-tracked items only | OK | ticked and unticked |
| Below reorder level only | OK | ticked and unticked |
| Cancel | OK | nothing opened / closed the screen |
| (after) | OK | left open behind it: 'BethRapha Pharmacy - Stocktake' |

### Stocktake > Capture

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | message: No count is in progress. Start one with F3 New count. / opened 'BethRapha Pharmacy - Stocktake' |
| F3 New count | OK | window 'BethRapha Pharmacy - Open stocktake' |
| F2 Continue | OK | window 'BethRapha Pharmacy - Stocktake capture' |
| F7 Cancel | OK | window 'Cancel stocktake' |
| F5 Refresh | OK | nothing opened |
| Esc Close | OK | nothing opened / closed the screen |

### Stocktake > Variance

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | message: No count is in progress. Start one with F3 New count. / opened 'BethRapha Pharmacy - Stocktake' |
| F3 New count | OK | window 'BethRapha Pharmacy - Open stocktake' |
| F2 Continue | OK | window 'BethRapha Pharmacy - Stocktake capture' |
| F7 Cancel | OK | window 'Cancel stocktake' |
| F5 Refresh | OK | nothing opened |
| Esc Close | OK | nothing opened / closed the screen |

### Stocktake > Post

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | message: No count is in progress. Start one with F3 New count. / opened 'BethRapha Pharmacy - Stocktake' |
| F3 New count | OK | window 'BethRapha Pharmacy - Open stocktake' |
| F2 Continue | OK | window 'BethRapha Pharmacy - Stocktake capture' |
| F7 Cancel | OK | window 'Cancel stocktake' |
| F5 Refresh | OK | nothing opened |
| Esc Close | OK | nothing opened / closed the screen |

### Administration > VAT Summary

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Reports' |
| F11 Export to Excel | OK | message: No data for the selected period and filters, so there is nothing to export. |
| Preview (F12) | OK | message: No data for the selected period and filters. |
| Close (F7) | OK | nothing opened / closed the screen |

### Administration > Users

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Users' |
| F3 Add | OK | window 'BethRapha Pharmacy - New user' |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit user' |
| F2 Reset password | OK | window 'Reset password' |
| F5 Unlock | OK | message: admin is not locked out. |
| F7 Deactivate | OK | message: Deactivate admin? They keep their history and can be reactivated, but cannot sign in. |
| Show deactivated accounts | OK | ticked and unticked |
| Esc Close | OK | nothing opened / closed the screen |

### Administration > Branches

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Branches' |
| F5 Refresh | OK | nothing opened |
| F7 Retire | OK | message: Retire this branch? It stays on past records but cannot be chosen again. |
| F4 Edit | OK | window 'BethRapha Pharmacy - Edit branch' |
| F3 Add | OK | window 'BethRapha Pharmacy - New branch' |
| Esc Close | OK | nothing opened / closed the screen |

### Administration > Stock Transactions

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Reports' |
| F11 Export to Excel | OK | message: No data for the selected period and filters, so there is nothing to export. |
| Preview (F12) | OK | message: No data for the selected period and filters. |
| Close (F7) | OK | nothing opened / closed the screen |

### Administration > System Setup

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - System setup' |
| F9 Choose image... | OK | file dialog 'Choose the pharmacy logo' |
| F7 Remove | OK | nothing opened |
| F6 Save | OK | message: Saved. / closed the screen |
| Esc Cancel | OK | nothing opened / closed the screen |

### Administration > Import from RSI

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Import from RSI' |
| F9 Choose... | OK | file dialog 'The folder the RSI export scripts wrote to' |
| F2 Check (dry run) | OK | nothing opened |
| F3 Import | OK | message: Import the RSI data into this database? Products, prices and stock on hand become RSI's as exported, patients and prescribers are added, and the history is replaced with the export's. Run it once, at the change-over, after the last Z-read in RSI. |
| Esc Close | OK | nothing opened / closed the screen |

### Administration > Back Up Now

| Option | Status | What happened |
|---|---|---|
| (menu entry) | SKIP | writes a backup file into the real backup folder at once; not run on the demo copy |

### Administration > System Console

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - System console' |
| F5 Refresh | OK | nothing opened |
| F6 Save pharmacy details | OK | message: Pharmacy details saved. Receipts, reports, window titles and the sign-in and home screens use them from now on. |
| Esc Close | OK | nothing opened / closed the screen |

### Administration > Control Variables

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Control variables' |
| F5 Refresh | OK | nothing opened |
| Esc Close | OK | nothing opened / closed the screen |

### File > Printer Setup

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Printer setup' |
| F12 Test page | OK | file dialog 'Save Print Output As' |
| F6 Save | OK | message: Saved. Receipts, labels and claim forms will use these printers from the next one printed. / closed the screen |
| Esc Cancel | OK | nothing opened / closed the screen |

### File > Till Session

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Till session' |
| F2 X-read  Ctrl+X | OK | file dialog 'Save Print Output As' / message: X-read 1 printed on Microsoft Print to PDF. |
| F3 Declare | OK | nothing opened |
| F5 Cash up and close  Ctrl+Z | OK | window 'Variance' |
| Esc Close | OK | nothing opened / closed the screen |

### File > Change Password

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | opened 'BethRapha Pharmacy - Change password' |
| Change | OK | nothing opened |
| Cancel | OK | nothing opened / closed the screen |

### Help > About

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | message: PharmaX 1.0.1.0 BethRapha Pharmacy Branch 1, till 1 Signed in as test.admin |

### File > Log Off

| Option | Status | What happened |
|---|---|---|
| (menu entry) | OK | back to Sign in |
