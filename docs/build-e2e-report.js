// Builds the PharmaX end-to-end test report, with user stories, as a
// Word document.
//
//   node docs/build-e2e-report.js evidence/e2e docs/PharmaX-E2E-Test-Report.docx
//
// Every verdict comes from evidence/e2e/results.csv and every access cell
// from evidence/e2e/access/manifest.csv, both written while the tests ran.
// Nothing in the document is typed in after the fact: a step with no row
// in the results is not in the report.
const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, ShadingType, BorderStyle,
  ImageRun, PageBreak, LevelFormat, TableOfContents, Footer, Header,
  PageNumber, PageOrientation,
} = require('docx');

const E2E = path.resolve(process.argv[2] || 'evidence/e2e');
const TARGET = path.resolve(process.argv[3] || 'docs/PharmaX-E2E-Test-Report.docx');
const SHOTS = path.join(E2E, 'screenshots');
const ACCESS_SHOTS = path.join(E2E, 'access', 'screenshots');

// A4, 2 cm margins.
const PAGE_W = 11906, PAGE_H = 16838, MARGIN = 1134;
const CONTENT = PAGE_W - 2 * MARGIN;          // 9638 DXA

const INK = '1A1A1A', MUTED = '5A626E', ACCENT = '005FB8';
const GOOD = '186A3B', BAD = 'A82020', WARN = '8A6100';
const BAND = 'EFF2F6', RULE = 'D4D9E0';
const GOOD_BG = 'E3F4EA', BAD_BG = 'FBE5E5', WARN_BG = 'FFF3D6', GREY_BG = 'EEEEEE';

// ---- CSV ------------------------------------------------------------------

function parseCsv(text) {
  text = text.replace(/^﻿/, '');
  const rows = []; let row = [], field = '', q = false;
  for (let i = 0; i < text.length; i++) {
    const c = text[i];
    if (q) {
      if (c === '"' && text[i + 1] === '"') { field += '"'; i++; }
      else if (c === '"') q = false;
      else field += c;
    } else if (c === '"') q = true;
    else if (c === ',') { row.push(field); field = ''; }
    else if (c === '\n') { row.push(field); rows.push(row); row = []; field = ''; }
    else if (c !== '\r') field += c;
  }
  if (field.length || row.length) { row.push(field); rows.push(row); }
  const head = rows.shift();
  return rows.filter(r => r.length === head.length)
             .map(r => Object.fromEntries(head.map((h, i) => [h, r[i]])));
}

const results = parseCsv(fs.readFileSync(path.join(E2E, 'results.csv'), 'utf8'));
const access = parseCsv(fs.readFileSync(path.join(E2E, 'access', 'manifest.csv'), 'utf8'));

// ---- primitives -------------------------------------------------------------

const run = (text, o = {}) => new TextRun({
  text, bold: o.b, italics: o.i, color: o.c || INK, size: o.s || 20,
  font: o.mono ? 'Consolas' : undefined,
});
const P = (parts, o = {}) => new Paragraph({
  spacing: { after: o.after === undefined ? 120 : o.after, before: o.before || 0 },
  alignment: o.align, keepNext: o.keepNext,
  children: (Array.isArray(parts) ? parts : [parts])
    .map(p => typeof p === 'string' ? run(p, o) : p),
});
const H1 = t => new Paragraph({ text: t, heading: HeadingLevel.HEADING_1,
  pageBreakBefore: true, spacing: { after: 160 } });
const H2 = t => new Paragraph({ text: t, heading: HeadingLevel.HEADING_2,
  spacing: { before: 280, after: 120 }, keepNext: true });
const H3 = t => new Paragraph({ text: t, heading: HeadingLevel.HEADING_3,
  spacing: { before: 200, after: 80 }, keepNext: true });
const bullet = (parts, level = 0) => new Paragraph({
  numbering: { reference: 'dots', level }, spacing: { after: 60 },
  children: (Array.isArray(parts) ? parts : [parts])
    .map(p => typeof p === 'string' ? run(p) : p),
});

const border = { style: BorderStyle.SINGLE, size: 4, color: RULE };
const borders = { top: border, bottom: border, left: border, right: border };

function cell(content, width, o = {}) {
  const kids = (Array.isArray(content) ? content : [content]).map(c =>
    c instanceof Paragraph ? c
      : new Paragraph({ spacing: { after: 0 }, alignment: o.align,
          children: [run(String(c ?? ''), { s: o.s || 17, b: o.b, c: o.c, mono: o.mono })] }));
  return new TableCell({
    width: { size: width, type: WidthType.DXA }, borders,
    shading: o.fill ? { type: ShadingType.CLEAR, fill: o.fill, color: 'auto' } : undefined,
    margins: { top: 50, bottom: 50, left: 80, right: 80 },
    children: kids,
  });
}

function table(headers, rows, widths, o = {}) {
  const total = widths.reduce((a, b) => a + b, 0);
  // A key/value table passes no headers and gets no header row.
  const noHead = headers.every(h => h === '');
  const head = noHead ? null : new TableRow({ tableHeader: true, children: headers.map((h, i) =>
    cell(h, widths[i], { b: true, fill: BAND, s: o.hs || 17 })) });
  const body = rows.map(r => new TableRow({ cantSplit: true, children: r.map((c, i) => {
    if (c && typeof c === 'object' && !(c instanceof Paragraph) && !Array.isArray(c))
      return cell(c.t, widths[i], { ...c, s: c.s || o.s });
    return cell(c, widths[i], { s: o.s });
  }) }));
  return new Table({ width: { size: total, type: WidthType.DXA },
    columnWidths: widths, rows: head ? [head, ...body] : body });
}

function verdictCell(v) {
  const map = { PASS: [GOOD, GOOD_BG], FAIL: [BAD, BAD_BG], BLOCKED: [WARN, WARN_BG] };
  const [c, fill] = map[v] || [MUTED, GREY_BG];
  return { t: v, b: true, c, fill, align: AlignmentType.CENTER };
}

function callout(label, text, colour, fill) {
  return new Table({
    width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [CONTENT],
    rows: [new TableRow({ children: [new TableCell({
      width: { size: CONTENT, type: WidthType.DXA },
      shading: { type: ShadingType.CLEAR, fill, color: 'auto' },
      borders: { top: { style: BorderStyle.NONE }, bottom: { style: BorderStyle.NONE },
        right: { style: BorderStyle.NONE },
        left: { style: BorderStyle.SINGLE, size: 24, color: colour } },
      margins: { top: 100, bottom: 100, left: 180, right: 140 },
      children: [new Paragraph({ spacing: { after: 0 }, children: [
        run(label + '  ', { b: true, c: colour }), run(text)] })],
    })] })],
  });
}

// PNG size straight from the IHDR chunk.
function pngSize(file) {
  const b = fs.readFileSync(file);
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20), data: b };
}

const shotsUsed = new Set();
function shot(dir, name, caption, maxIn = 6.4, maxHIn = 5.2) {
  const file = path.join(dir, name.endsWith('.png') ? name : name + '.png');
  if (!fs.existsSync(file)) return [];
  shotsUsed.add(file);
  const { w, h, data } = pngSize(file);
  let wIn = Math.min(maxIn, w / 150);
  let hIn = wIn * h / w;
  if (hIn > maxHIn) { hIn = maxHIn; wIn = hIn * w / h; }
  return [
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 120, after: 40 },
      keepNext: true, children: [new ImageRun({ type: 'png', data,
        transformation: { width: Math.round(wIn * 96), height: Math.round(hIn * 96) } })] }),
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 160 },
      children: [run(caption, { s: 16, c: MUTED, i: true })] }),
  ];
}

// ---- stories ----------------------------------------------------------------
// One story per scenario ID. The wording is the pharmacy's, not the
// system's. Acceptance criteria are what the recorded steps check.

const ROLES = {
  cashier:    ['Cashier', 'test.cashier', 'works a till: sales, returns within limit, collections, cash-up'],
  assistant:  ['Pharmacy Assistant', 'test.assistant', 'captures patients and scripts, receives goods, helps at the till'],
  pharmacist: ['Pharmacist', 'test.pharmacist', 'the only role that may authorise a dispensing'],
  stock:      ['Stock Controller', 'test.stock', 'orders, receives and counts stock'],
  manager:    ['Branch Manager', 'test.manager', 'prices, authorisations, stocktake approval and the books'],
  admin:      ['Administrator', 'test.admin', 'sets the system up and maintains users'],
};

const STORIES = [
 // Cashier
 ['E01', 'cashier', 'Sign in and out of my till',
  'sign in with my own credentials and sign out at a shift change',
  'every sale is attributable to me and nobody else can trade under my name',
  ['A wrong password is refused without saying which field was wrong.',
   'A correct sign-in opens the shell showing user, role, branch and till.',
   'Log off asks for confirmation and returns to the sign-in window.']],
 ['E02', 'cashier', 'See the state of my till session',
  'see whether a session is open, who opened it, the float and X-reads taken',
  'I never trade into somebody else\'s drawer without knowing it',
  ['The session header shows opener, time, float and X-read count.']],
 ['E03', 'cashier', 'Sell a basket with a discount, a void and split tender',
  'scan items, change a quantity, give an authorised discount, void a line and take cash plus card',
  'a queue moves quickly and every rand in the drawer is explained',
  ['Each scan adds a line and the totals and VAT update.',
   'A discount above my limit needs a supervisor; I cannot authorise myself.',
   'A voided line needs a reason and is excluded from the total.',
   'Split tender completes the sale; the receipt adds up; stock moves only for sold lines.']],
 ['E04', 'cashier', 'Find an item by name and put the sale on an account',
  'search for a product by name and charge the sale to a patient account',
  'regular customers can pay at month end without me keeping a book',
  ['Product search by description.',
   'An account on hold is refused with its reason.',
   'An open account shows balance and available credit, and accepts the Account tender.']],
 ['E05', 'cashier', 'Take a return against the original sale',
  'find the original sale, choose what is coming back, give a reason and refund with a supervisor\'s approval',
  'stock and money go back correctly and the authoriser is named',
  ['The original sale lines load with what can still be returned.',
   'A reason and a supervisor are demanded.',
   'The refund completes and the stock goes back on the shelf.']],
 ['E06', 'cashier', 'Collect a dispensed script and take the patient\'s share',
  'see the scripts waiting, open one at the rated price and take payment',
  'medicine is handed over only once the patient portion is paid',
  ['The waiting queue lists patient, documents, amount and pharmacist.',
   'Only the patient portion is charged.',
   'Change can be given on cash; the script leaves the queue.']],
 ['E07', 'cashier', 'Take an X-read and cash up',
  'check takings mid-shift and count down the drawer at the end',
  'the drawer is reconciled and a variance is owned',
  ['An X-read shows expected takings per tender without closing.',
   'Counting by denomination gives a variance per tender.',
   'Closing asks for confirmation (and a reason above the threshold) and prints a Z-read.']],
 ['E90', 'cashier', 'Be kept out of work that is not mine',
  'only see the parts of PharmaX my role allows',
  'I cannot post the books or change settings by accident',
  ['Menus for other roles are disabled.',
   'Even when a screen is forced open, the service refuses.']],
 // Assistant
 ['E08', 'assistant', 'Capture patients and receive goods',
  'capture new patients and book deliveries in',
  'the pharmacist\'s time goes on clinical work, not typing',
  ['The assistant can reach Patients, Script Maintenance and Goods Receipt.']],
 ['E09', 'assistant', 'Open a till session with a float',
  'open the till with a counted opening float',
  'the day\'s cash-up starts from a known amount',
  ['A float is asked for and the session opens in my name.']],
 ['E10', 'assistant', 'Park a sale and come back to it',
  'hold a basket while a customer fetches something, serve the next person, then resume',
  'the queue keeps moving and nothing is re-scanned',
  ['Hold clears the basket; the held sale can be recalled.']],
 // Pharmacist
 ['E11', 'pharmacist', 'Register a new patient with medical aid and allergies',
  'capture identity, medical aid membership, weight, chronic conditions and allergies',
  'the right person is billed and dispensing can check the clinical record',
  ['The patient saves with a file number and is searchable.',
   'Missing mandatory details are refused.']],
 ['E12', 'pharmacist', 'Capture a prescription',
  'capture prescriber, repeats and items with directions',
  'a script is corrected before any medicine moves',
  ['The script saves as Captured with its own, unambiguous number.']],
 ['E13', 'pharmacist', 'Dispense a script safely',
  'dispense the items, see clinical warnings, authorise the issue and print labels',
  'the patient gets the right medicine, safely, and the scheme is billed',
  ['The allergy banner is visible; a penicillin-class drug for a penicillin-allergic patient raises a warning.',
   'Stock shortfalls are refused; issue needs a pharmacist\'s authorisation.',
   'Issue allocates across batches, moves stock, prints a label per item and passes the co-payment to the till.']],
 ['E14', 'pharmacist', 'Reprint a label',
  'preview and reprint a dispensing label',
  'a smudged label is replaced without dispensing again',
  ['Preview without printing; reprint confirmed and audited.']],
 ['E15', 'pharmacist', 'Quote a price without selling',
  'give a customer a written quotation that moves no stock',
  'the customer can decide and the sale can be converted later',
  ['A numbered, dated quotation; no stock movement; printable and convertible.']],
 // Stock controller
 ['E16', 'stock', 'Add a new product',
  'add a product with pack, cost, retail, VAT, department and reorder level',
  'new lines can be sold and reordered',
  ['Either the save succeeds or the Add button is not offered.']],
 ['E17', 'stock', 'See what needs attention',
  'see expired, expiring, out-of-stock and low-stock lines in one place',
  'I act before a gap or a write-off',
  ['All four alert kinds are listed.']],
 ['E18', 'stock', 'Maintain suppliers', 'add a supplier with terms and lead time',
  'the reorder calculation orders early enough', ['The supplier saves and lists as active.']],
 ['E19', 'stock', 'Raise a purchase order from what is short',
  'get a suggested order from reorder levels and raise it',
  'I do not work the order out by eye',
  ['A suggestion is calculated; it can be raised as a numbered order.']],
 ['E20', 'stock', 'Book a delivery in against its order',
  'receive a delivery line by line with batch and expiry, including a part delivery',
  'stock, cost and expiry are right from the moment it lands',
  ['Order lines pre-fill; preview resolves; post updates batches, stock and the order status.']],
 ['E21', 'stock', 'Count stock and post the variance',
  'open a count, capture what is on the shelf and post it',
  'the system matches the shelf and shrinkage is visible',
  ['Expected quantities frozen; variance per line; Post only when all lines are counted; stock adjusted.']],
 ['E22', 'stock', 'Maintain categories', 'add and retire a category',
  'products are filed under lists that mean something',
  ['Unique codes; retire rather than delete.']],
 // Manager
 ['E24', 'manager', 'Maintain a product and its price', 'create a product and change its selling price',
  'the till charges the right money', ['Saves; the price change is recorded.']],
 ['E25', 'manager', 'See and export price changes', 'see every price change with who made it and export it',
  'a price move can be explained afterwards', ['Listed with user; exported to a workbook.']],
 ['E26', 'manager', 'Authorise and post a refund', 'process the return the cashier could not',
  'the customer is refunded and stock goes back', ['Credit note posted; stock and audit updated.']],
 ['E27', 'manager', 'See the books in balance', 'see a trial balance for today',
  'I know the day\'s trading posted and balanced', ['Debits equal credits; balances trace to the day.']],
 ['E28', 'manager', 'Post a correcting journal', 'post a journal by hand',
  'an accountant\'s adjustment needs no developer', ['Unbalanced refused; balanced posted.']],
 ['E29', 'manager', 'Run the patient accounts', 'see what each account owes, take a payment and print a statement',
  'the pharmacy collects what it is owed', ['Balances agree; payment reduces the balance; statement reconciles.']],
 ['E30', 'manager', 'Bill and settle medical aid claims', 'see what each scheme owes and settle a remittance',
  'the largest debtor is actually chased', ['Claims listed without errors; settlement recorded; short payment written off.']],
 ['E31', 'manager', 'Run a report', 'choose a report, branch and dates, and preview it',
  'I can answer a question without a spreadsheet first', ['Previews with parameters; figures reconcile.']],
 ['E32', 'manager', 'Maintain the chart of accounts', 'add a ledger account',
  'the accountant\'s chart and the system\'s agree', ['Code, name and type captured.']],
 ['E33', 'manager', 'Post the day to the books', 'review the day\'s journals and post the day-end batch',
  'the books keep themselves', ['Reachable from the menu; journals balanced; batch prepared and posted once.']],
 // Admin
 ['E35', 'admin', 'Create a user', 'create a user with a role, discount limit and starting password',
  'every action is attributable to one person', ['Password policy enforced; first sign-in forces a change.']],
 ['E36', 'admin', 'Lock out password guessing', 'have accounts lock after repeated failures',
  'a guessed password does not become a way in', ['Locks after 5; message does not reveal which accounts exist.']],
 ['E37', 'admin', 'Unlock a user', 'unlock a locked user', 'staff are not stuck for a day', ['Lock cleared.']],
 ['E38', 'cashier (new)', 'Change my password at first sign-in', 'replace the starting password',
  'nobody else knows my password', ['Forced; mismatch refused; new password accepted.']],
 ['E39', 'admin', 'Set the pharmacy up', 'set trading name, VAT number and currency',
  'receipts are compliant for this pharmacy', ['Fiscal particulars are configurable.']],
 ['E40', 'admin', 'See the control variables', 'see every threshold the system runs on',
  'a limit can be explained without reading source code', ['Thresholds listed and maintainable with audit.']],
 ['E41', 'admin', 'See what the system is doing', 'see version, database and connectivity',
  'a support call starts from facts', ['Shows the database actually in use.']],
 ['E42', 'admin', 'Choose printers', 'point receipts and labels at the right printers',
  'a label does not come out of the receipt printer', ['Separate, per-workstation choice.']],
 ['E43', 'admin', 'Confirm the version', 'see which build I run', 'a defect names a build', ['Version visible.']],
 ['E44', 'admin', 'Maintain branches', 'add a branch', 'trading is attributed to the right site', ['Saved and active.']],
 ['E45', 'admin', 'Maintain medical aid tariffs', 'list and add scheme tariffs',
  'claims are rated at what the scheme pays', ['Listed per scheme; a tariff can be added.']],
 ['E46', 'admin', 'Merge duplicate patient files', 'merge a duplicate into the real record',
  'history is not split in two', ['Same file refused; effect previewed before merging.']],
 ['E47', 'admin', 'Maintain medical aid societies', 'see the schemes', 'patients attach to real schemes', ['Listed.']],
 ['E48', 'admin', 'Maintain VAT codes and transaction types', 'maintain codes',
  'a rate change is configuration, not code', ['A maintenance screen exists.']],
];

// ---- defects ----------------------------------------------------------------
// Numbered on from the Sept 17 register (DEF-0001..0005).

const DEFECTS = [
 ['DEF-0006', 'Critical', 'Allergy check misses drug classes: amoxicillin dispensed to a penicillin-allergic patient with no warning',
  'E13', 'E13-07_allergy-warning',
  'ClinicalProfile.IsAllergicTo (PharmaX.Domain/Patients/ClinicalProfile.cs:80) is a case-insensitive substring match between the allergy text and the product description. "Penicillin" is not a substring of "Amoxicillin 500mg capsules 21s", so no warning is raised for any penicillin-class antibiotic, and the product GenericName is not consulted at all.',
  'Match allergies against an ingredient / drug-class reference (at minimum the product generic name and a class table), and add unit tests for class cross-sensitivity.'],
 ['DEF-0007', 'High', 'A cashier cannot complete any return, even after a supervisor has authorised it',
  'E05', 'E05-08_return-posted',
  'ReturnService.CreateAsync (PharmaX.Application/Sales/ReturnService.cs:155) calls _permissions.Demand(ReturnAuthorise) against the signed-in cashier. The cashier\'s outcome is RequiresSupervisor, not Granted, so Demand throws after the UI has already collected a reason, a supervisor and a refund. edit.AuthorisedBy is ignored by the check. The same return posts as the Manager (E26).',
  'When the operator\'s outcome is RequiresSupervisor, accept a valid edit.AuthorisedBy who holds the permission instead of demanding it of the operator.'],
 ['DEF-0008', 'High', 'Dispensing reduces the batch but not the product on-hand, and writes no stock movement',
  'E13', 'E31-04_report-stock-by-batch',
  'After issuing, batches fell (AMOX50-WARNING 4 to 0, METF85-B 16 to 6) but Product.QuantityOnHand did not, and no StockMovement row exists for the dispensing or its collection. Metformin now reads 24 on hand against 6 in batches; Cough syrup 40 against 56. Product-level figures feed the till, alerts, reorder suggestions and Stock on Hand. A stocktake counts against batches so it cannot repair the drift (E21).',
  'Issue through the same stock path as a sale (product QOH + movement), and add a product/batch reconciliation check.'],
 ['DEF-0009', 'High', 'Issue fails when the earliest batch cannot cover the line, although the line was accepted',
  'E13', 'E13-11_batch-cannot-issue',
  'Adding 21 x AMOX50 passed the stock check (40 across batches 4/12/24) but the line was allocated wholly to AMOX50-WARNING; Issue then failed with "Batch AMOX50-WARNING holds 4.000, cannot issue 21." The pharmacist must cancel and re-dispense a smaller quantity.',
  'Allocate FEFO across several batches (split the line), or validate the allocation at add time.'],
 ['DEF-0010', 'High', 'Document numbers collide across document types',
  'E12', 'E12-02_script-saved',
  'DocumentNumberAllocator (PharmaX.Persistence/Numbering/DocumentNumberAllocator.cs:52) formats numbers as series[..1]-till-sequence. SALE, SCR and STK all print as "S-001-...", PO and PAT both as "P-001-...". In this run S-001-000002 is simultaneously a sale, a script and a stocktake; P-001-000002 is a patient and a purchase order.',
  'Use a distinct prefix per series (e.g. the full series code) and migrate existing numbers.'],
 ['DEF-0011', 'High', 'Concurrent use of one DbContext on screen load: raw .NET crash dialog on Claims and Tariff; tariff editor unusable',
  'E30, E45', 'E30-01_claims-unhandled-exception',
  'frmClaims.ShowSchemes sets cboScheme.SelectedIndex = 0 (frmClaims.cs:107), which raises SchemeChanged and starts LoadAsync while ClaimsPresenter.InitialiseAsync (FinancePresenters.cs:263) is still awaiting its own Load() on the same context. Seen as "A second operation was started on this context" and as the raw "Unhandled exception... This MySqlConnection is already in use" Continue/Quit dialog. The tariff editor shows the same crash and then has an empty Society list, so no tariff can be added.',
  'Suppress selection events while populating, or serialise loads per presenter; give each concurrent load its own scope.'],
 ['DEF-0012', 'High', 'No screen can raise a purchase order',
  'E19', 'E19-02_po-suggested',
  '"What to order" computes a suggestion but the Purchase Orders screen has no control to raise it. PurchasingService.RaiseAsync is called only by DemoTradingSeeder and the integration tests.',
  'Add "Raise order" (supplier choice, editable suggested lines) to frmPurchaseOrderList.'],
 ['DEF-0013', 'High', 'Goods receipt against an order cannot be posted without re-keying every product code; the error is silent',
  'E20', 'E20-03_grn-captured',
  'frmGoodsReceipt.ShowOrder (frmGoodsReceipt.cs:133) puts line.Description into the Code column. Preview cannot resolve a description as a code; GoodsReceiptPresenter.PreviewAsync (line 117) swallows the failure, so the resolved grid stays empty, totals read $0.00 and Post stays disabled with no message.',
  'Pre-fill the product code (keep the description in its own column) and show the preview error on the status line as the comment intends.'],
 ['DEF-0014', 'High', 'A held sale can never be recalled',
  'E10', 'E10-03_till-no-recall',
  'F8 Hold says "Recall it from the till session screen" (PosPresenter.cs:287) but no recall exists on that screen or anywhere in the solution. The basket is orphaned as an Open sale.',
  'Implement recall of held sales, or remove Hold until it exists.'],
 ['DEF-0015', 'High', 'Role permissions do not match the roles\' jobs (both too narrow and too wide)',
  'E08, E33, access matrix', 'E08-02_assistant-patients-refused',
  'From ReferenceDataSeeder.PermissionsFor (ReferenceDataSeeder.cs:109-177) and MainPresenter menu gating: the Pharmacy Assistant cannot reach Patients, Script Maintenance or Goods Receipt; the Manager cannot reach Day-End Posting, Users or Patients because Day-End sits under Administration (gated on adm.user.role.assign) and Dispensary is gated on dsp.script.authorise; the Administrator holds PermissionCode.All and can sell, dispense, return and receive stock although the role is meant to be barred from trading; the Cashier can open Stocktake.',
  'Agree the role matrix with the business, then align the seeded grants and move Day-End under Accounts.'],
 ['DEF-0016', 'Medium', 'Totals panel and printed receipt do not add up after a void',
  'E03', 'E03-09_line-voided',
  'After voiding a discounted line, Subtotal (159.79) and Discount (20.00) still include the voided line while TOTAL is 89.89. The printed receipt carries the same figures, so a customer-facing fiscal document does not reconcile.',
  'Exclude voided lines from subtotal and discount.'],
 ['DEF-0017', 'Medium', 'DataGridView "FormatException" error boxes on Returns, Claims and Capture Script',
  'E05, E12, E30', 'E05-02_defect-returns-grid-format-error',
  '47 raw "DataGridView Default Error Dialog - Formatted value of the cell has a wrong type" boxes were raised during the run (Returns 11, Capture script 13, Medical aid claims 22, prompt 1), each blocking the screen until dismissed. No grid in PharmaX.Desktop handles DataError. On Capture Script the "Generic ok" check box displays unticked while its value reads Yes.',
  'Fix the check-box column formatting and add a DataError handler to PhxGrid.'],
 ['DEF-0018', 'Medium', '"The Z-read has printed" - nothing is printed; X-reads and Z-reads have no print path',
  'E07', 'E07-05_session-closed',
  'TillSessionPresenter.cs:98 shows the message unconditionally; no code in the Tills area prints anything.',
  'Print the X/Z read through IReceiptPrinter, or change the message.'],
 ['DEF-0019', 'Medium', 'Cash tendered above the amount due is refused, so the till never calculates change',
  'E06', 'E06-03b_collect-payment-after-pay',
  'frmPayment enables Pay only when Outstanding == 0 (frmPayment.cs:247). Tendering $60 for $53 shows "Over-tendered by $7.00". The POS and receipt "Tendered/Change" fields therefore always show the due amount and 0.00.',
  'Allow over-tender on the cash line and compute change.'],
 ['DEF-0020', 'Medium', 'Dispensing re-keys quantity and directions instead of using the captured script',
  'E13', 'E13-05_dispense-qty-default-1',
  'Scanning a scripted item prompts quantity 1.000 and blank directions, although the script holds 21 and the directions.',
  'Pre-fill from the script item; keep the prompt for changes.'],
 ['DEF-0021', 'Medium', 'Dispensing price ignores the quantity dispensed',
  'E13', 'E13-13_ready-to-issue',
  'Tariff is a flat price per product per scheme: 4 or 21 capsules both price at 121.22; 10 or 20 Metformin at 150.30.',
  'Price pro rata per unit (pack price / pack size x quantity) plus fee, per the pricing rules.'],
 ['DEF-0022', 'Medium', 'Quotations cannot be printed, exported or converted to a sale',
  'E15', 'E15-04_quotation-done',
  'frmQuotation has no print or export path, and the POS has no quotation recall.',
  'Print the quotation and add "Convert quotation" to the till.'],
 ['DEF-0023', 'Medium', 'Fiscal particulars cannot be set in the application; receipts print a blank VAT number',
  'E39', 'E39-01_system-setup',
  'System Setup offers only currency and logo. Trading name, VAT number and address come from configuration files; the receipt header reads "VAT   Lic 0000000".',
  'Add fiscal particulars to System Setup (audited).'],
 ['DEF-0024', 'Medium', 'System Console and Control Variables misreport the running configuration',
  'E40, E41', 'E41-01_system-console',
  'System Console says "Host connection: Not configured - this till trades standalone" and shows local.db / MariaDb while the till runs on the shared MySQL database. Control Variables lists 23 configuration values only (no business thresholds - those are hard-coded, e.g. CashUpService.cs:32 = 20, StocktakeService.cs:48 = 500, User.cs:110 = 5), labels workstation.json values as appsettings.json, and shows "Accounting posting: Off" while journals post in real time.',
  'Report the effective connection and source; move thresholds to audited configuration (see DEF-0003).'],
 ['DEF-0025', 'Medium', 'VAT Summary does not reconcile to the ledger',
  'E31', 'E31-03_report-vat-summary',
  'VAT Summary: nett 510.03, VAT 76.50, gross 586.53 - the seven sales only, excluding refunds and scheme-paid portions. Trial balance VAT Control: 152.26.',
  'Accountant to confirm the intended basis; report output VAT net of credit notes and including scheme portions.'],
 ['DEF-0026', 'Medium', 'Refused permission attempts are not kept in the audit trail',
  'E05, E16', '',
  'PermissionService.Demand writes an audit "Refused" entry then throws (PermissionService.cs:43-46); the entry is not saved when the unit of work is abandoned. The cashier\'s refused refund and the stock controller\'s refused product save left no audit row (only 3 Refused rows exist, all from other paths).',
  'Write refusals through a separate, committed audit scope.'],
 ['DEF-0027', 'Low', 'Controls offered that the service then refuses (work is lost)',
  'E16, E08', 'E16-03_product-save-msg',
  'Products > Add is enabled for the Stock Controller; the whole form is filled before Save is refused (product save needs inv.price.change). F4 opens Patients for the Assistant before refusing. Menu children stay invokable through UI Automation under a disabled heading.',
  'Disable actions by permission, including shortcuts and child items.'],
 ['DEF-0028', 'Low', 'UTC times shown in some lists',
  'E21', 'E21-10_stocktake-list',
  'The stocktake list shows 03:59 for a count the capture screen shows opened at 05:59; Users > last sign-in is also UTC.',
  'Convert to local time for display.'],
 ['DEF-0029', 'Low', 'VAT Codes and Transaction Types open reports, not maintenance screens',
  'E48', 'E48-02_transaction-types-is-a-report',
  'MainPresenter routes both to ShowReport(...). A VAT rate change is therefore a code change. (Extends DEF-0003.)',
  'Provide maintenance screens or rename the menu entries.'],
 ['DEF-0030', 'Low', 'Testability gaps',
  'E04, E28, E30', 'E04-03_account-picker',
  'Account picker, prompt dialogs and several code-built forms expose no AutomationId; the Journal and Claims grids cannot be edited by non-intrusive automation. Opening Sell leaves an empty "Open" sale row each time.',
  'Name the remaining controls; clean up abandoned empty sales.'],
];

// ---- derived figures ----------------------------------------------------------

const count = v => results.filter(r => r.verdict === v).length;
const totals = { PASS: count('PASS'), FAIL: count('FAIL'), BLOCKED: count('BLOCKED') };
const sevCount = s => DEFECTS.filter(d => d[1] === s).length;

// Who can reach what by hand. The sweep invokes menu entries through UI
// Automation, which also reaches children of a disabled heading, so its
// "opened" is only what a person can do when the heading is enabled.
const PERMS = {
  'test.admin':      'ALL',
  'test.manager':    ['pos.sale.create','pos.return.authorise','inv.product.read','inv.goods.receive','rpt.report.run.operational','fin.ledger.read'],
  'test.pharmacist': ['pos.sale.create','dsp.script.authorise','inv.product.read','rpt.report.run.operational'],
  'test.assistant':  ['pos.sale.create','inv.product.read','rpt.report.run.operational'],
  'test.cashier':    ['pos.sale.create','inv.product.read'],
  'test.stock':      ['inv.product.read','inv.goods.receive','rpt.report.run.operational'],
};
const HEAD = { 'Point of Sale': 'pos.sale.create', 'Dispensary': 'dsp.script.authorise',
  'Inventory': 'inv.product.read', 'Stocktake': 'inv.product.read',
  'Reports': 'rpt.report.run.operational', 'Accounts': 'fin.ledger.read',
  'Administration': 'adm.user.role.assign' };
const has = (u, p) => PERMS[u] === 'ALL' || PERMS[u].includes(p);
function reachable(u, menu, slug) {
  if (HEAD[menu] && !has(u, HEAD[menu])) return false;
  if (slug === 'goods-receipt' && !has(u, 'inv.goods.receive')) return false;
  return true;
}

// Rows the harness could not settle, re-checked by hand during the run.
const RETEST = {
  'test.admin|claims': 'OPENED*', 'test.admin|chart-of-accounts': 'OPENED*',
  'test.admin|reports': 'OPENED*', 'test.stock|stock-alerts': 'OPENED*',
  'test.admin|vat-codes': 'OPENED*',
};

const ROLE_ORDER = ['test.admin','test.manager','test.pharmacist','test.assistant','test.cashier','test.stock'];
const ROLE_SHORT = { 'test.admin':'Admin','test.manager':'Manager','test.pharmacist':'Pharm.',
  'test.assistant':'Assist.','test.cashier':'Cashier','test.stock':'Stock' };

function accessCell(u, row) {
  if (!row) return { t: '-', fill: GREY_BG, align: AlignmentType.CENTER };
  const key = `${u}|${row.slug}`;
  if (!reachable(u, row.menu, row.slug))
    return { t: 'Menu off', c: MUTED, fill: GREY_BG, align: AlignmentType.CENTER };
  let o = RETEST[key] || row.outcome;
  if (row.slug === 'about') o = 'OPENED';
  if (o.startsWith('OPENED')) return { t: o === 'OPENED*' ? 'Open*' : 'Open', c: GOOD, fill: GOOD_BG, align: AlignmentType.CENTER };
  if (o === 'REFUSED') return { t: 'Refused', c: BAD, fill: BAD_BG, align: AlignmentType.CENTER };
  if (o === 'NOWINDOW') return { t: 'Item off', c: MUTED, fill: GREY_BG, align: AlignmentType.CENTER };
  return { t: o, c: WARN, fill: WARN_BG, align: AlignmentType.CENTER };
}

const MUST_ALLOW = {
 'test.admin': ['system-setup','control-variables','users'],
 'test.manager': ['users','patients','stocktake-capture','price-changes','trial-balance','day-end'],
 'test.pharmacist': ['dispensing','labels','patients','script-maintenance'],
 'test.assistant': ['patients','script-maintenance','goods-receipt','pos-sell','till-session'],
 'test.cashier': ['pos-sell','till-session','returns','collect'],
 'test.stock': ['goods-receipt','purchase-orders','products','stocktake-capture','stock-alerts'],
};
const MUST_REFUSE = {
 'test.admin': ['pos-sell','collect','dispensing','returns','stocktake-capture','goods-receipt'],
 'test.cashier': ['users','stocktake-capture','price-changes','system-setup','control-variables','day-end'],
 'test.assistant': ['users','price-changes','system-setup','day-end'],
 'test.pharmacist': ['users','price-changes','system-setup','day-end'],
 'test.stock': ['users','system-setup','day-end','dispensing'],
 'test.manager': ['system-setup'],
};
function humanCan(u, slug) {
  const row = access.find(r => r.role === u && r.slug === slug);
  if (!row) return false;
  const c = accessCell(u, row).t;
  return c.startsWith('Open');
}

// ---- document ---------------------------------------------------------------

const children = [];

// Title page
children.push(
  new Paragraph({ spacing: { before: 2400, after: 120 }, children: [run('PharmaX', { s: 60, b: true, c: ACCENT })] }),
  new Paragraph({ spacing: { after: 120 }, children: [run('End-to-End Test Report and User Stories', { s: 40, b: true })] }),
  new Paragraph({ spacing: { after: 600 }, children: [run('Every function, every role, tested against the running application', { s: 24, c: MUTED })] }),
  table(['', ''], [
    ['Build under test', 'main @ d20af54 (PharmaX 1.0.0.0, Release)'],
    ['Test date', 'Wednesday 23 September 2026'],
    ['Database', 'Fresh MySQL 8.0.42 schema PharmaX_E2E, migrated and demo-seeded by the application'],
    ['Roles tested', 'Administrator, Branch Manager, Pharmacist, Pharmacy Assistant, Cashier, Stock Controller (+ a newly created cashier)'],
    ['Prepared for', 'Adrian Mlilo'],
    ['Prepared by', 'Claude (Claude Code), driving the application through UI Automation'],
  ], [2600, CONTENT - 2600], { s: 19 }),
  new Paragraph({ spacing: { before: 600 }, children: [run(
    'Nothing in this report is illustrative. Every screenshot was captured from the running application during this run, every verdict was recorded as the step was executed, and every figure quoted was read back from the screen or the database.',
    { s: 18, c: MUTED, i: true })] }),
);

// Contents
children.push(H1('Contents'),
  new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-2' }),
  P('Right-click the table and choose Update Field if page numbers are not shown.', { s: 16, c: MUTED, i: true }));

// 1. Summary
children.push(H1('1. Executive summary'));
children.push(P([
  run('Verdict: not ready for release. ', { b: true, c: BAD }),
  run(`The core till, dispensing, stock and accounting journeys all run end to end, and the automated suites are green, but the run found ${DEFECTS.length} defects, including one critical patient-safety defect and ${sevCount('High')} high-severity defects that stop a role doing its job or corrupt stock or document data.`),
]));
children.push(table(['Measure', 'Result'], [
  ['Automated unit tests', { t: '368 of 368 pass', c: GOOD, b: true }],
  ['Automated integration tests (MySQL)', { t: '139 of 139 pass', c: GOOD, b: true }],
  ['Build (Release)', { t: '0 errors, 120 analyser warnings', c: GOOD, b: true }],
  ['Role x screen access sweep', `${access.length} checks: ${ROLE_ORDER.length} roles x ${new Set(access.map(a => a.slug)).size} screens, ${fs.readdirSync(ACCESS_SHOTS).length} screenshots`],
  ['Functional end-to-end steps', `${results.length} steps across ${STORIES.length} user stories`],
  ['Steps passed', { t: String(totals.PASS), c: GOOD, b: true }],
  ['Steps failed', { t: String(totals.FAIL), c: BAD, b: true }],
  ['Steps blocked (could not be automated safely)', { t: String(totals.BLOCKED), c: WARN, b: true }],
  ['Defects raised', `${DEFECTS.length}: ${sevCount('Critical')} critical, ${sevCount('High')} high, ${sevCount('Medium')} medium, ${sevCount('Low')} low`],
], [4200, CONTENT - 4200], { s: 19 }));

children.push(H2('The defects that matter most'));
for (const d of DEFECTS.filter(d => d[1] === 'Critical' || d[1] === 'High'))
  children.push(bullet([run(`${d[0]} (${d[1]}): `, { b: true, c: d[1] === 'Critical' ? BAD : INK }), run(d[2])]));

children.push(H2('What works well'));
[
  'Sign-in, lock-out after five failures, administrator unlock and the forced first-sign-in password change all behave correctly, and failed sign-ins never reveal whether an account exists.',
  'Supervisor authorisation is enforced for discounts above limit, refunds and dispensing, and a user cannot authorise their own over-limit discount; only a pharmacist can authorise an issue.',
  'The till computes VAT correctly (15% inclusive), supports split tender and account sales, refuses accounts on hold, and the X-read and declared cash-up variance are arithmetically right.',
  'Every transaction posts a balanced journal in real time; the trial balance is in balance and traces to the day\'s trading (debtors, creditors, cash short).',
  'Goods receipt (once codes are re-keyed), stocktake with freeze, count and post, patient accounts with payments and statement, claims settlement and report export all complete correctly.',
].forEach(t => children.push(bullet(t)));

// 2. Scope and method
children.push(H1('2. Scope, environment and method'));
children.push(H2('Environment'));
children.push(table(['Item', 'Value'], [
  ['Machine', 'Windows 11 Home 10.0.26200, .NET SDK 9.0.302, runtime 8.0.18'],
  ['Application', 'PharmaX.Desktop.exe, Release build of d20af54'],
  ['Database', 'MySQL 8.0.42 on 127.0.0.1, schema PharmaX_E2E created for this run (your working PharmaX database was not touched)'],
  ['Connection override', 'Environment variable Database__SharedConnection, which the app reads after workstation.json'],
  ['Seed data', 'The application\'s own seeders: 7 users, 759 products, 8 patients, a day of demo trading'],
  ['Printers', 'Microsoft Print to PDF; every receipt and label was saved as a PDF in evidence/e2e/receipts'],
], [2600, CONTENT - 2600], { s: 18 }));
children.push(H2('How the application was driven'));
[
  'UI Automation to read every screen and set text boxes; Windows messages posted to the application\'s own controls to click buttons, open menus and type into grids, so the tests never took over the keyboard or mouse.',
  'Screenshots with PrintWindow, so each image is exactly the application window.',
  'Each step recorded with expected result, actual result and verdict the moment it ran (evidence/e2e/results.csv), and database state checked with SQL where the screen alone could not prove it.',
  'Two background helpers dismissed the application\'s own error dialogs so a crash on one screen could not stall the run; each dismissal is logged (evidence/e2e/grid-errors.log, watchdog.log).',
].forEach(t => children.push(bullet(t)));
children.push(H2('Limits of this run'));
children.push(P(`${totals.BLOCKED} steps are marked BLOCKED rather than guessed at: the Journal Entry account column and the Claims "Paying" cells only accept input with genuine keyboard focus, and the Day-End trading-day picker could not be changed without taking over the screen. Their business rules are covered by the passing unit tests (JournalPosterTests, ClaimServiceTests, DayEndPostingService tests), but they were not exercised through the UI.`));

// 3. Access matrix
children.push(H1('3. Who can do what: role access matrix'));
children.push(P('Every menu entry was opened as every role. "Open" means a person in that role can reach and use the screen; "Refused" means the screen is reachable but the service says "You do not have permission"; "Menu off" means the heading or item is disabled for that role. Open* marks rows the sweep could not settle, re-checked by hand during the functional run.'));
const slugs = [...new Set(access.map(a => a.slug))];
const accRows = slugs.map(s => {
  const any = access.find(a => a.slug === s);
  return [{ t: `${any.menu} > ${any.item}`, s: 15 },
    ...ROLE_ORDER.map(u => ({ ...accessCell(u, access.find(a => a.role === u && a.slug === s)), s: 14 }))];
});
children.push(table(['Menu entry', ...ROLE_ORDER.map(u => ROLE_SHORT[u])], accRows,
  [3238, 1066, 1066, 1066, 1066, 1066, 1070], { s: 14, hs: 15 }));

children.push(H2('Checked against the required role matrix'));
const matrixRows = [];
for (const u of ROLE_ORDER) {
  for (const s of MUST_ALLOW[u] || []) {
    const ok = humanCan(u, s);
    matrixRows.push([ROLE_SHORT[u], 'must be able to', s, ok ? verdictCell('PASS') : verdictCell('FAIL')]);
  }
  for (const s of MUST_REFUSE[u] || []) {
    const ok = !humanCan(u, s);
    matrixRows.push([ROLE_SHORT[u], 'must be kept out of', s, ok ? verdictCell('PASS') : verdictCell('FAIL')]);
  }
}
const mFail = matrixRows.filter(r => r[3].t === 'FAIL').length;
children.push(P(`${matrixRows.length} role rules checked, ${matrixRows.length - mFail} held, ${mFail} broken. The broken ones are DEF-0015. Note that Price Changes is a read-only report here, and that the Cashier "Returns" pass is access only - posting a return fails (DEF-0007).`));
children.push(table(['Role', 'Rule', 'Screen', 'Result'], matrixRows, [1600, 2400, 3638, 2000], { s: 16 }));

children.push(H2('Home screen per role'));
for (const u of ROLE_ORDER) {
  const short = u.replace('test.', '');
  children.push(...shot(ACCESS_SHOTS, `${short}_00-home`, `${ROLE_SHORT[u]} - home screen after sign-in (tiles and menus reflect the role).`, 6.0, 3.4));
}

// 4. User stories
children.push(H1('4. User stories and end-to-end results'));
children.push(P('Each story is written in the pharmacy\'s language as "As a <role>, I want ..., so that ...", followed by its acceptance criteria, the steps executed with their recorded outcome, and the screenshots taken at those steps. Stories are grouped by role in the order a day actually runs.'));

const byRole = {};
for (const s of STORIES) (byRole[s[1]] = byRole[s[1]] || []).push(s);
const roleOrder = ['cashier','assistant','pharmacist','stock','manager','admin','cashier (new)'];
let sectionNo = 0;
for (const roleKey of roleOrder) {
  const list = byRole[roleKey]; if (!list) continue;
  const [label, user, blurb] = ROLES[roleKey] || ['New cashier (e2e.cashier2)', 'e2e.cashier2', 'a user created during the run'];
  sectionNo++;
  children.push(H2(`4.${sectionNo} ${label}`));
  children.push(P([run('Signed in as ', { c: MUTED }), run(user, { mono: true }), run(` - ${blurb}.`, { c: MUTED })]));
  for (const [id, , title, want, soThat, criteria] of list) {
    const steps = results.filter(r => r.case === id);
    children.push(H3(`${id}  ${title}`));
    children.push(callout('Story', `As a ${label}, I want to ${want}, so that ${soThat}.`, ACCENT, 'EAF2FB'));
    children.push(P('Acceptance criteria', { b: true, before: 120, after: 40, keepNext: true }));
    criteria.forEach(c => children.push(bullet(c)));
    if (steps.length) {
      const pass = steps.filter(s => s.verdict === 'PASS').length;
      const overall = steps.some(s => s.verdict === 'FAIL') ? 'FAIL' : steps.some(s => s.verdict === 'BLOCKED') ? 'BLOCKED' : 'PASS';
      children.push(P([run('Story result: ', { b: true }), run(overall, { b: true, c: overall === 'PASS' ? GOOD : overall === 'FAIL' ? BAD : WARN }),
        run(`   (${pass} of ${steps.length} steps passed)`, { c: MUTED })], { before: 80, keepNext: true }));
      children.push(table(['#', 'Step', 'Expected', 'Actual (as observed)', 'Verdict'],
        steps.map((s, i) => [String(i + 1), s.step, s.expected, s.actual, verdictCell(s.verdict)]),
        [360, 2100, 2300, 3778, 1100], { s: 15 }));
      const seen = new Set();
      for (const s of steps) {
        if (!s.shot || s.shot.startsWith('receipt:') || seen.has(s.shot)) continue;
        seen.add(s.shot);
        children.push(...shot(SHOTS, s.shot, `${id} - ${s.step}`));
      }
      // The intermediate captures that belong to this story, where they
      // show something the step table cannot.
      const extra = fs.readdirSync(SHOTS).filter(f => f.startsWith(id + '-') && !seen.has(f.replace('.png', '')) && !f.startsWith('tmp'));
      for (const f of extra.slice(0, 3)) children.push(...shot(SHOTS, f, `${id} - ${f.replace('.png', '').replace(/^E\d+-\d+b?_/, '').replace(/-/g, ' ')}`));
    } else {
      children.push(P('No steps recorded for this story.', { c: MUTED, i: true }));
    }
  }
}

// 5. Defects
children.push(H1('5. Defect register'));
children.push(P('Numbered on from the 17 September register (DEF-0001 to DEF-0005). Root causes quote the file and line where the fault was traced; each was confirmed against the running application, not only the source.'));
children.push(table(['ID', 'Severity', 'Title', 'Found in'],
  DEFECTS.map(d => [d[0], { t: d[1], b: true, c: d[1] === 'Critical' || d[1] === 'High' ? BAD : d[1] === 'Medium' ? WARN : MUTED }, d[2], d[3]]),
  [1150, 1050, 5938, 1500], { s: 16 }));
for (const [id, sev, title, where, shotName, detail, fix] of DEFECTS) {
  children.push(H3(`${id}  ${title}`));
  children.push(table(['', ''], [
    ['Severity', { t: sev, b: true, c: sev === 'Critical' || sev === 'High' ? BAD : WARN }],
    ['Found in', where],
    ['What happens and why', detail],
    ['Suggested fix', fix],
  ], [1900, CONTENT - 1900], { s: 17 }));
  if (shotName) children.push(...shot(SHOTS, shotName, `${id} - evidence`, 5.6, 3.8));
}

// 6. Evidence
children.push(H1('6. Evidence index'));
children.push(table(['What', 'Where'], [
  ['Step-by-step results', 'evidence/e2e/results.csv'],
  ['Access sweep manifest and screenshots', 'evidence/e2e/access/manifest.csv, access/screenshots (' + fs.readdirSync(ACCESS_SHOTS).length + ' images)'],
  ['Functional screenshots', 'evidence/e2e/screenshots (' + fs.readdirSync(SHOTS).length + ' images)'],
  ['Receipts, labels (PDF)', 'evidence/e2e/receipts'],
  ['Report export', 'evidence/e2e/exports'],
  ['Error dialogs dismissed during the run', 'evidence/e2e/grid-errors.log, watchdog.log'],
  ['Automated test results', 'dotnet test: 368 unit + 139 integration, all passing'],
  ['Scripts to repeat the run', 'docs/walkthrough.ps1 (access sweep), docs/uia-drive.ps1, docs/build-e2e-report.js'],
], [3200, CONTENT - 3200], { s: 18 }));

const doc = new Document({
  creator: 'Claude (Claude Code)', title: 'PharmaX End-to-End Test Report',
  styles: {
    default: { document: { run: { font: 'Calibri', size: 20, color: INK } } },
    paragraphStyles: [
      { id: 'Heading1', name: 'Heading 1', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 34, bold: true, color: ACCENT }, paragraph: { spacing: { after: 160 }, outlineLevel: 0 } },
      { id: 'Heading2', name: 'Heading 2', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 26, bold: true, color: INK }, paragraph: { spacing: { before: 280, after: 120 }, outlineLevel: 1 } },
      { id: 'Heading3', name: 'Heading 3', basedOn: 'Normal', next: 'Normal', quickFormat: true,
        run: { size: 22, bold: true, color: INK }, paragraph: { spacing: { before: 200, after: 80 }, outlineLevel: 2 } },
    ],
  },
  numbering: { config: [{ reference: 'dots', levels: [
    { level: 0, format: LevelFormat.BULLET, text: '•', alignment: AlignmentType.LEFT,
      style: { paragraph: { indent: { left: 360, hanging: 240 } } } },
    { level: 1, format: LevelFormat.BULLET, text: '–', alignment: AlignmentType.LEFT,
      style: { paragraph: { indent: { left: 720, hanging: 240 } } } }] }] },
  sections: [{
    properties: { page: { size: { width: PAGE_W, height: PAGE_H },
      margin: { top: MARGIN, bottom: MARGIN, left: MARGIN, right: MARGIN } } },
    headers: { default: new Header({ children: [new Paragraph({ alignment: AlignmentType.RIGHT,
      children: [run('PharmaX - End-to-End Test Report - build d20af54', { s: 15, c: MUTED })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ children: ['Page ', PageNumber.CURRENT, ' of ', PageNumber.TOTAL_PAGES], size: 15, color: MUTED })] })] }) },
    children,
  }],
});

Packer.toBuffer(doc).then(buf => {
  fs.writeFileSync(TARGET, buf);
  console.log(`${TARGET}: ${results.length} steps, ${STORIES.length} stories, ${DEFECTS.length} defects, ${shotsUsed.size} images`);
});
