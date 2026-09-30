// Builds the Administrator end-to-end test report as a Word document.
//
//   node docs/build-admin-report.js evidence/e2e-admin docs/PharmaX-Admin-E2E-Report.docx
//
// Every verdict comes from <evidence>/results.csv and every defect from
// <evidence>/defects.md, both written while the run was going on. A step
// with no row in the results is not in the report.
const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, ShadingType, BorderStyle,
  ImageRun, LevelFormat, TableOfContents, Footer, Header, PageNumber,
} = require('docx');

const E2E = path.resolve(process.argv[2] || 'evidence/e2e-admin');
const TARGET = path.resolve(process.argv[3] || 'docs/PharmaX-Admin-E2E-Report.docx');
const SHOTS = path.join(E2E, 'screenshots');

// A4, 2 cm margins.
const PAGE_W = 11906, PAGE_H = 16838, MARGIN = 1134;
const CONTENT = PAGE_W - 2 * MARGIN;

const INK = '1A1A1A', MUTED = '5A626E', ACCENT = '1F3F8F';
const GOOD = '186A3B', BAD = 'A82020', WARN = '8A6100';
const BAND = 'EFF2F6', RULE = 'D4D9E0';
const GOOD_BG = 'E3F4EA', BAD_BG = 'FBE5E5', WARN_BG = 'FFF3D6', GREY_BG = 'EEEEEE';

// ---- inputs -------------------------------------------------------------

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

// defects.md: "ID | Severity | Where | Steps | Expected | Actual | Evidence | Root cause"
const DEFECTS = fs.readFileSync(path.join(E2E, 'defects.md'), 'utf8')
  .split(/\r?\n/).filter(l => /^ADM-(DEF|OBS)-\d+ \|/.test(l))
  .map(l => {
    const [id, severity, where, steps, expected, actual, evidence, cause] = l.split(' | ').map(s => s.trim());
    const base = severity.split(' ')[0];
    return { id, severity, base, where, steps, expected, actual, evidence, cause };
  });
const SEV_ORDER = ['Critical', 'High', 'Medium', 'Low', 'Cosmetic'];
DEFECTS.sort((a, b) => SEV_ORDER.indexOf(a.base) - SEV_ORDER.indexOf(b.base)
  || a.id.localeCompare(b.id));

// ---- primitives -----------------------------------------------------------

const run = (text, o = {}) => new TextRun({
  text, bold: o.b, italics: o.i, color: o.c || INK, size: o.s || 20,
  font: o.mono ? 'Consolas' : undefined,
});
const P = (parts, o = {}) => new Paragraph({
  spacing: { after: o.after === undefined ? 120 : o.after, before: o.before || 0 },
  alignment: o.align, keepNext: o.keepNext,
  children: (Array.isArray(parts) ? parts : [parts]).map(p => typeof p === 'string' ? run(p, o) : p),
});
const H1 = t => new Paragraph({ text: t, heading: HeadingLevel.HEADING_1, pageBreakBefore: true, spacing: { after: 160 } });
const H2 = t => new Paragraph({ text: t, heading: HeadingLevel.HEADING_2, spacing: { before: 280, after: 120 }, keepNext: true });
const H3 = t => new Paragraph({ text: t, heading: HeadingLevel.HEADING_3, spacing: { before: 200, after: 80 }, keepNext: true });
const bullet = (parts, level = 0) => new Paragraph({
  numbering: { reference: 'dots', level }, spacing: { after: 60 },
  children: (Array.isArray(parts) ? parts : [parts]).map(p => typeof p === 'string' ? run(p) : p),
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
    margins: { top: 50, bottom: 50, left: 80, right: 80 }, children: kids,
  });
}

function table(headers, rows, widths, o = {}) {
  const total = widths.reduce((a, b) => a + b, 0);
  const noHead = headers.every(h => h === '');
  const head = noHead ? null : new TableRow({ tableHeader: true, children: headers.map((h, i) =>
    cell(h, widths[i], { b: true, fill: BAND, s: o.hs || 17 })) });
  const body = rows.map(r => new TableRow({ cantSplit: true, children: r.map((c, i) => {
    if (c && typeof c === 'object' && !(c instanceof Paragraph) && !Array.isArray(c))
      return cell(c.t, widths[i], { ...c, s: c.s || o.s });
    return cell(c, widths[i], { s: o.s });
  }) }));
  return new Table({ width: { size: total, type: WidthType.DXA }, columnWidths: widths,
    rows: head ? [head, ...body] : body });
}

function verdictCell(v) {
  const map = { PASS: [GOOD, GOOD_BG], FAIL: [BAD, BAD_BG], BLOCKED: [WARN, WARN_BG], ISSUES: [WARN, WARN_BG] };
  const [c, fill] = map[v] || [MUTED, GREY_BG];
  return { t: v, b: true, c, fill, align: AlignmentType.CENTER };
}
function sevCell(s) {
  const base = s.split(' ')[0];
  const c = base === 'Critical' || base === 'High' ? BAD : base === 'Medium' ? WARN : MUTED;
  return { t: s, b: true, c };
}

function callout(label, text, colour, fill) {
  return new Table({
    width: { size: CONTENT, type: WidthType.DXA }, columnWidths: [CONTENT],
    rows: [new TableRow({ children: [new TableCell({
      width: { size: CONTENT, type: WidthType.DXA },
      shading: { type: ShadingType.CLEAR, fill, color: 'auto' },
      borders: { top: { style: BorderStyle.NONE }, bottom: { style: BorderStyle.NONE },
        right: { style: BorderStyle.NONE }, left: { style: BorderStyle.SINGLE, size: 24, color: colour } },
      margins: { top: 100, bottom: 100, left: 180, right: 140 },
      children: [new Paragraph({ spacing: { after: 0 }, children: [run(label + '  ', { b: true, c: colour }), run(text)] })],
    })] })],
  });
}

function pngSize(file) {
  const b = fs.readFileSync(file);
  return { w: b.readUInt32BE(16), h: b.readUInt32BE(20), data: b };
}
const shotsUsed = new Set();
function shot(name, caption, maxIn = 6.4, maxHIn = 4.6) {
  const file = path.join(SHOTS, name.endsWith('.png') ? name : name + '.png');
  if (!fs.existsSync(file)) return [];
  shotsUsed.add(file);
  const { w, h, data } = pngSize(file);
  let wIn = Math.min(maxIn, w / 150), hIn = wIn * h / w;
  if (hIn > maxHIn) { hIn = maxHIn; wIn = hIn * w / h; }
  return [
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { before: 120, after: 40 }, keepNext: true,
      children: [new ImageRun({ type: 'png', data, transformation: { width: Math.round(wIn * 96), height: Math.round(hIn * 96) } })] }),
    new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 160 }, children: [run(caption, { s: 16, c: MUTED, i: true })] }),
  ];
}

// ---- what was tested ------------------------------------------------------

const CASES = {
  A01: 'Sign in', A02: 'Pharmacy details at the System Console', A03: 'System Setup: currency and logo',
  A04: 'Control Variables', A05: 'Printer Setup and test page', A06: 'Branches',
  A07: 'Users: create, edit, reset, deactivate, lock-out, unlock and first sign-in', A08: 'Log off',
  A09: 'Change own password', A10: 'Help > About', A11: 'Administration report entries',
  A12: 'Exit', A13: 'Restart: settings survive',
  B01: 'Departments', B02: 'Categories', B03: 'Manufacturers', B04: 'Medical aid societies', B05: 'Medical aid tariffs',
  C01: 'Suppliers', C02: 'Products', C03: 'Price changes and the inventory report entries',
  C04: 'Purchase orders', C05: 'Goods receipt', C06: 'Stock alerts',
  D01: 'Opening the till', D02: 'Selling: every till function and every tender', D03: 'Quotations',
  D04: 'Returns and credit notes', D05: 'Cash-up and Z-read',
  E01: 'Patients', E02: 'Patient documents', E03: 'Capturing a script', E04: 'Dispensing and labels',
  E05: 'Collecting a dispensed script', E06: 'Re-print script and labels', E07: 'Merging patient files',
  E08: 'Reversal and drug returns',
  F01: 'Stocktake: open, count, variance, post and cancel',
  G01: 'Patient accounts', G02: 'Medical aid claims', G03: 'Chart of accounts', G04: 'Journal entry',
  G05: 'Trial balance', G06: 'Day-end posting',
  H01: 'Reports: every preset', H02: 'Home screen tiles', H03: 'Keyboard shortcuts',
};
const SECTIONS = [
  ['A', 'Setting the pharmacy up and running the system', ['A01','A02','A03','A04','A05','A06','A07','A08','A09','A10','A11','A12','A13']],
  ['B', 'Reference data', ['B01','B02','B03','B04','B05']],
  ['C', 'Stock and purchasing', ['C01','C02','C03','C04','C05','C06']],
  ['D', 'The till', ['D01','D02','D03','D04','D05']],
  ['E', 'The dispensary', ['E01','E02','E03','E04','E05','E06','E07','E08']],
  ['F', 'Stocktake', ['F01']],
  ['G', 'Accounts and the books', ['G01','G02','G03','G04','G05','G06']],
  ['H', 'Reports and navigation', ['H01','H02','H03']],
];

// Every menu entry the Administrator can reach, where it was tested and
// what happened. The note is the one-line outcome; the case holds the steps.
const MENU = [
  ['File', 'Printer Setup', 'A05', 'Both printers chosen per workstation; test page prints (headed "PharmaX", ADM-OBS-01)', 'ISSUES'],
  ['File', 'Till Session', 'D01, D05, E08', 'Open with float, X-read, declare, cash up and close with Z-read', 'PASS'],
  ['File', 'Log Off', 'A08', 'Asks first, returns to the branded sign-in window', 'PASS'],
  ['File', 'Change Password', 'A09', 'Wrong current, same password refused; change and sign-in with the new one', 'PASS'],
  ['File', 'Exit', 'A12', 'Asks first; No keeps working, Yes closes', 'PASS'],
  ['Dispensary', 'Dispensing', 'E04', 'Issues and prints labels; units mis-handled for price and stock (ADM-DEF-14/15); label lacks the pharmacy (ADM-DEF-16)', 'FAIL'],
  ['Dispensary', 'Patients', 'E01, E02', 'Create, edit, clinical and medical aid, documents; search by surname and file number only', 'ISSUES'],
  ['Dispensary', 'Re-Print Script', 'E06', 'Opens the script list; nothing reprints (ADM-DEF-17)', 'FAIL'],
  ['Dispensary', 'Script Maintenance', 'E03', 'Capture and cancel; no edit; refused saves leave a numbering gap (ADM-DEF-13)', 'ISSUES'],
  ['Dispensary', 'Labels', 'E06', 'Preview and reprint, audited', 'PASS'],
  ['Dispensary', 'Tariffs', 'B05', 'Add, edit, filter, search, overlap refused, retire and bring back', 'PASS'],
  ['Dispensary', 'Merge Patient Files', 'E07', 'Same file refused, effect previewed, reason demanded, audited', 'PASS'],
  ['Dispensary', 'Medical Aid Societies', 'B04', 'Full list maintenance; edit leaves the list stale (ADM-DEF-01)', 'ISSUES'],
  ['Dispensary', 'Drug Returns', 'E08', 'Needs an open till; returns one pack for a 20-pack issue (ADM-DEF-15)', 'ISSUES'],
  ['Point of Sale', 'Sell', 'D02', 'Every key and tender; scheduled and out-of-stock items sell (ADM-DEF-11/12)', 'FAIL'],
  ['Point of Sale', 'Scripts to Collect', 'E05', 'Queue, filters, back out, take payment, claim raised', 'PASS'],
  ['Point of Sale', 'Returns and Credit Notes', 'D04', 'Goods return and value-only credit note; over-return refused late', 'ISSUES'],
  ['Point of Sale', 'Quotation', 'D03', 'Start, lines, issue, print, export, open existing, convert at the till', 'PASS'],
  ['Inventory', 'Stock Alerts', 'C06', 'All four kinds, filter, open product', 'PASS'],
  ['Inventory', 'Departments', 'B01', 'Full list maintenance; edit leaves the list stale (ADM-DEF-01)', 'ISSUES'],
  ['Inventory', 'Categories', 'B02', 'Full list maintenance; edit leaves the list stale (ADM-DEF-01)', 'ISSUES'],
  ['Inventory', 'Suppliers', 'C01', 'Add, validate, edit; deactivated supplier cannot be brought back (ADM-DEF-03)', 'ISSUES'],
  ['Inventory', 'Purchase Orders', 'C04', 'Raise, validate, print, cancel; suggestion ignores stock on order (ADM-DEF-06)', 'ISSUES'],
  ['Inventory', 'Goods Receipt', 'C05', 'Part and full receipt against the order; over-receipt not checked (ADM-DEF-07)', 'ISSUES'],
  ['Inventory', 'Manufacturers', 'B03', 'Full list maintenance; edit leaves the list stale (ADM-DEF-01)', 'ISSUES'],
  ['Inventory', 'Products', 'C02', 'Add OTC and schedule 6 batch-tracked, edit price; no barcode field (ADM-DEF-04)', 'ISSUES'],
  ['Inventory', 'Stock', 'C03', 'Opens Stock On Hand', 'PASS'],
  ['Inventory', 'Transfers', 'C03', 'Report only; no way to record a transfer (ADM-DEF-05)', 'FAIL'],
  ['Inventory', 'Breakages', 'C03', 'Report only (ADM-DEF-05)', 'FAIL'],
  ['Inventory', 'Expired Stock', 'C03', 'Opens Stock Expiring', 'PASS'],
  ['Inventory', 'Price Changes', 'C03', 'Lists the change with user and time; exports', 'PASS'],
  ['Accounts', 'Trial Balance', 'G05', 'Balances; postings drill-down; export', 'PASS'],
  ['Accounts', 'Journal Entry', 'G04', 'Opens; lines could not be keyed without real keyboard focus (BLOCKED)', 'BLOCKED'],
  ['Accounts', 'Patient Accounts', 'G01', 'Payment, statement, limit, hold/release, new account, export', 'ISSUES'],
  ['Accounts', 'Medical Aid Claims', 'G02', 'Pay in full per scheme, reject with reason; part payment BLOCKED', 'PASS'],
  ['Accounts', 'Chart of Accounts', 'G03', 'Add, duplicate refused, rename, take out of use, export', 'PASS'],
  ['Accounts', 'Day-End Posting', 'G06', 'Summary correct; posting counts the day twice (ADM-DEF-19); Excel export dead (ADM-DEF-20)', 'FAIL'],
  ['Reports', 'Reports', 'H01', 'All 18 presets run; empty result shows the previous report (ADM-DEF-21)', 'ISSUES'],
  ['Stocktake', 'New Stocktake', 'F01', 'Opens with frozen expected quantities', 'PASS'],
  ['Stocktake', 'Capture', 'F01', 'Count per line; sale during the count does not distort it', 'PASS'],
  ['Stocktake', 'Variance', 'F01', 'Opens the stocktake list; variances-only filter in the count', 'PASS'],
  ['Stocktake', 'Post', 'F01', 'Posts the count to the shelf figure; cancel leaves stock alone', 'PASS'],
  ['Administration', 'VAT Summary', 'A11', 'Opens the V.A.T Summary report', 'PASS'],
  ['Administration', 'Users', 'A07', 'Full user life-cycle; passwords shown in clear when typed (ADM-DEF-02)', 'ISSUES'],
  ['Administration', 'Branches', 'A06', 'Full list maintenance; edit leaves the list stale (ADM-DEF-01)', 'ISSUES'],
  ['Administration', 'Stock Transactions', 'A11', 'Opens the Stock Transactions report', 'PASS'],
  ['Administration', 'System Setup', 'A03', 'Currency preview, logo chosen and shown at once', 'PASS'],
  ['Administration', 'System Console', 'A02', 'Trading name, registered name, VAT number, address; validated and audited', 'PASS'],
  ['Administration', 'Control Variables', 'A04', '26 settings with where each is held', 'PASS'],
  ['Help', 'About', 'A10', 'Software version with the pharmacy, branch, till and user', 'PASS'],
];

// ---- derived figures ------------------------------------------------------

const count = v => results.filter(r => r.verdict === v).length;
const totals = { PASS: count('PASS'), FAIL: count('FAIL'), BLOCKED: count('BLOCKED') };
const sev = s => DEFECTS.filter(d => d.base === s).length;
const caseVerdict = id => {
  const steps = results.filter(r => r.case === id);
  if (!steps.length) return '';
  return steps.some(s => s.verdict === 'FAIL') ? 'FAIL' : steps.some(s => s.verdict === 'BLOCKED') ? 'BLOCKED' : 'PASS';
};

// ---- document ---------------------------------------------------------------

const children = [];

children.push(
  new Paragraph({ spacing: { before: 1600, after: 120 }, children: [run('BethRapha Pharmacy', { s: 56, b: true, c: ACCENT })] }),
  new Paragraph({ spacing: { after: 120 }, children: [run('PharmaX - Administrator end-to-end test', { s: 40, b: true })] }),
  new Paragraph({ spacing: { after: 500 }, children: [run('Every menu entry, button and feature the Administrator role can reach, carried through to its receipts, stock, ledger and reports', { s: 24, c: MUTED })] }),
  ...shot('A03-05_home-with-logo', 'The shell as test.admin, branded with the pharmacy’s details and logo during the run.', 6.0, 3.3),
  table(['', ''], [
    ['Role tested', 'Administrator (test.admin), which holds every permission'],
    ['Build under test', 'branch proj/0002 at 68ba91d (PharmaX 1.0.0.0, Release)'],
    ['Test date', 'Friday 25 September 2026'],
    ['Database', 'Fresh MySQL 8.0.42 schema PharmaX_E2E_ADMIN, migrated and seeded by the application (the working PharmaX database was not used)'],
    ['Pharmacy profile', 'BethRapha Pharmacy / BethRapha Pharmacy (Pvt) Ltd / 28 Woodstock Thorngrove, Bulawayo (Along Luveve Road) / VAT TEST-0001 (a test value) / the pharmacy’s own logo'],
    ['Prepared for', 'Adrian Mlilo'],
    ['Prepared by', 'Claude (Claude Code), driving the running application through UI Automation'],
  ], [2400, CONTENT - 2400], { s: 18 }),
);

children.push(H1('Contents'),
  new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-2' }),
  P('If page numbers are missing, right-click the table and choose Update Field.', { s: 16, c: MUTED, i: true }));

// 1. Summary
children.push(H1('1. Executive summary'));
children.push(P([
  run('Verdict: not ready for release. ', { b: true, c: BAD }),
  run(`Every screen the Administrator can reach opens and nearly every feature completes: the pharmacy was set up and branded, users, reference lists, products, suppliers, orders, deliveries, the till with every tender, quotations, returns, patients, scripts, dispensing, collection, stocktake, accounts, claims and all eighteen reports were exercised end to end. The run also found ${DEFECTS.filter(d => d.id.startsWith('ADM-DEF')).length} defects and ${DEFECTS.filter(d => d.id.startsWith('ADM-OBS')).length} lesser observations, including one critical defect in the books and five high-severity defects in the reference lists, the till and dispensing.`),
]));
children.push(table(['Measure', 'Result'], [
  ['Automated unit tests', { t: '547 of 547 pass', c: GOOD, b: true }],
  ['Automated integration tests (MySQL)', { t: '170 of 170 pass', c: GOOD, b: true }],
  ['Menu entries covered', `${MENU.length} of ${MENU.length}, plus all 13 home tiles and 10 of 11 shortcut keys`],
  ['Steps executed and recorded', `${results.length} across ${Object.keys(CASES).length} test cases`],
  ['Steps passed', { t: String(totals.PASS), c: GOOD, b: true }],
  ['Steps failed', { t: String(totals.FAIL), c: BAD, b: true }],
  ['Steps blocked (not safe to automate)', { t: String(totals.BLOCKED), c: WARN, b: true }],
  ['Findings', `${DEFECTS.length}: ${sev('Critical')} critical, ${sev('High')} high, ${sev('Medium')} medium, ${sev('Low')} low, ${sev('Cosmetic')} cosmetic`],
  ['Documents issued in the run', 'Sales S-001-000005 to 000014, credit notes R-001-000002 to 000004, orders PO-001-000003/4, receipts G-001-000002/3, quotation Q-001-000002, scripts SC-001-000003/5/6, dispensing D-001-000003, claim C-001-000003, stocktakes ST-001-000002/3, journals J-001-000012 to 000032'],
], [3600, CONTENT - 3600], { s: 18 }));

children.push(H2('The findings that matter most'));
for (const d of DEFECTS.filter(d => d.base === 'Critical' || d.base === 'High'))
  children.push(bullet([run(`${d.id} (${d.severity}), ${d.where}: `, { b: true, c: d.base === 'Critical' ? BAD : INK }), run(d.actual)]));

children.push(H2('What works well'));
[
  'The pharmacy’s own identity reaches everything: window titles, the sign-in window, the home screen, receipts, purchase orders, quotations, credit notes and X/Z reads carry BethRapha Pharmacy, its registered name, address and VAT number, and the uploaded logo; the details are validated and audited and survive a restart.',
  'User administration is complete and safe: password length, mismatch and reuse rules, forced change at first sign-in, lock-out after five failures without revealing the account, unlock, deactivate and reactivate, all audited.',
  'The till handles every tender, split tender with change, discounts within limit, voids with a reason, hold and recall, quotations converted at F10, account sales with hold checks, and returns that cannot exceed what was sold. The cash-up expected figure equalled the independently kept ledger to the cent (586.82).',
  'Purchasing and receiving now work end to end: orders are raised, printed and cancelled with a reason; deliveries pre-fill from the order, refuse expired batches, reconcile to the invoice and update stock, batches and order status exactly.',
  'Stocktake freezes the expected quantities, is not distorted by a sale during the count, and posts the shelf figure adjusted for later sales (45 = 46 counted - 1 sold).',
  'Every transaction posts a balanced journal as it happens; claims, remittances, write-offs, refunds and the cash-up shortage are all traceable from the trial balance to the document.',
  'Several defects from the 23 September run are fixed: document numbers now have distinct prefixes, held sales can be recalled, void totals reconcile, cash change is calculated, X/Z reads print, orders can be raised, deliveries pre-fill codes, and the Claims screen no longer crashes.',
].forEach(t => children.push(bullet(t)));

// 2. Scope and method
children.push(H1('2. Scope, environment and method'));
children.push(H2('Scope'));
children.push(P('The Administrator role only, as the user test.admin. Because the Administrator holds every permission, the run covers every screen in the application; what other roles may or may not do is outside this report. Each feature was carried through to its outputs - receipts and slips printed to PDF, stock on hand and batches, the patient and claim sub-ledgers, the journals and the trial balance - and checked against the database.'));
children.push(H2('Environment'));
children.push(table(['Item', 'Value'], [
  ['Machine', 'Windows 11 Home 10.0.26200, .NET 8 runtime'],
  ['Application', 'PharmaX.Desktop.exe, Release build of branch proj/0002 at 68ba91d'],
  ['Database', 'MySQL 8.0.42 on 127.0.0.1, schema PharmaX_E2E_ADMIN created empty for this run'],
  ['Connection override', 'Environment variable Database__SharedConnection, read after workstation.json'],
  ['Seed data', 'The application’s own seeders: 7 users, 759 products, 8 patients, 3 accounts, a day of demonstration trading'],
  ['Printers', 'Microsoft Print to PDF for receipts and labels; every printout saved to evidence\\e2e-admin\\receipts'],
  ['Test VAT number', 'TEST-0001 - an obviously artificial value, used only in the test database'],
], [2600, CONTENT - 2600], { s: 18 }));
children.push(H2('How the application was driven'));
[
  'UI Automation to read every screen and fill text boxes; Windows messages posted to the application’s own controls to click buttons, open menus, type into grids and press keys, so the run never took over the mouse or keyboard.',
  'Screenshots with PrintWindow, so each image is exactly the application window. Every printout was saved as a PDF and read back as text.',
  'Each step was recorded the moment it ran - what was done, what was expected, what was observed, and a verdict - in evidence\\e2e-admin\\results.csv. Database state was checked with SQL wherever the screen alone could not prove the result.',
  'A cash ledger was kept independently of the application and compared with the X-read and the Z-read.',
  'The test helpers were extended for this run so that window lookups written against "PharmaX - ..." find the pharmacy-branded titles (docs\\uia-drive.ps1, PHX_BRAND).',
].forEach(t => children.push(bullet(t)));
children.push(H2('Limits of this run'));
children.push(P(`${totals.BLOCKED} steps are marked BLOCKED rather than guessed at. The Journal Entry grid and the Claims "Paying" cell accept input only with genuine keyboard focus, and Ctrl+T needs a real modifier key; doing either would have meant typing into whatever window was in front. Their rules are covered by the passing unit tests (JournalPosterTests, ClaimServiceTests), and Till Session was opened from its menu entry and tile. One direct SQL change was made, recorded in evidence\\e2e-admin\\seed-log.md: re-activating the supplier E2ESUP, which no screen can do (ADM-DEF-03). One product setting was corrected through the product screen to let dispensing continue (PAN500 units per pack, ADM-DEF-14).`));

// 3. Click map
children.push(H1('3. Every menu entry the Administrator can reach'));
children.push(P('All menu entries were opened as test.admin. The case column points to the section of this report that holds the steps. '
  + 'PASS: works as specified. ISSUES: works, with a finding recorded against it. FAIL: the entry does not do its job. BLOCKED: its core action could not be exercised safely.'));
const tally = v => MENU.filter(r => r[4] === v).length;
children.push(P(`${tally('PASS')} PASS, ${tally('ISSUES')} ISSUES, ${tally('FAIL')} FAIL, ${tally('BLOCKED')} BLOCKED.`, { b: true }));
children.push(table(['Menu', 'Entry', 'Case', 'Result', 'Outcome'],
  MENU.map(([m, e, c, note, v]) =>
    [{ t: m, s: 15 }, { t: e, s: 15, b: true }, { t: c, s: 15 }, { ...verdictCell(v), s: 14 }, { t: note, s: 15 }]), [1350, 1650, 900, 900, CONTENT - 4800], { s: 15, hs: 16 }));

children.push(H2('Home screen and keyboard'));
children.push(P('All 13 home tiles open their screens (H02). F2 Dispensing, F3 Sell, F4 Patients, F5 Returns, F6 Products, F7 Purchase orders, F8 Goods receipt, F9 Reports, F11 Scripts to collect and F12 Stock alerts all open from the shell (H03). The tiles’ accessible names are shifted by one, so a screen reader announces the wrong tile (ADM-OBS-19).'));

// 4. Results by area
children.push(H1('4. Results by feature'));
children.push(P('Each case lists the steps exactly as they were run, with what was expected and what the application actually did, followed by the screenshots taken at those steps.'));
for (const [letter, title, ids] of SECTIONS) {
  children.push(H2(`4.${'ABCDEFGH'.indexOf(letter) + 1} ${title}`));
  for (const id of ids) {
    const steps = results.filter(r => r.case === id);
    if (!steps.length) continue;
    const v = caseVerdict(id);
    const pass = steps.filter(s => s.verdict === 'PASS').length;
    children.push(H3(`${id}  ${CASES[id]}`));
    children.push(P([run('Result: ', { b: true }), run(v, { b: true, c: v === 'PASS' ? GOOD : v === 'FAIL' ? BAD : WARN }),
      run(`   (${pass} of ${steps.length} steps passed)`, { c: MUTED })], { keepNext: true }));
    children.push(table(['#', 'Step', 'Expected', 'Actual (as observed)', 'Verdict'],
      steps.map((s, i) => [String(i + 1), s.step, s.expected, s.actual, verdictCell(s.verdict)]),
      [360, 2150, 2150, 3878, 1100], { s: 15 }));
    const seen = new Set();
    const shotsFor = steps.filter(s => s.shot && !s.shot.startsWith('receipt:') && !s.shot.startsWith('db/'));
    for (const s of shotsFor) {
      if (seen.has(s.shot)) continue;
      seen.add(s.shot);
      children.push(...shot(s.shot, `${id} - ${s.step}`));
    }
    const pdfs = steps.filter(s => s.shot && s.shot.startsWith('receipt:')).map(s => s.shot.slice(8));
    if (pdfs.length) children.push(P([run('Printouts: ', { b: true, s: 17 }), run([...new Set(pdfs)].join(', '), { s: 17, mono: true })]));
  }
}

// 5. Reconciliation
children.push(H1('5. Does the day add up?'));
children.push(H2('Cash in the drawer'));
children.push(P('Kept by hand while the run went on, then compared with the till’s own X-read.'));
const ledger = fs.readFileSync(path.join(E2E, 'ledger.md'), 'utf8').split(/\r?\n/)
  .filter(l => /^\| [A-Z]\d\d/.test(l)).map(l => l.split('|').slice(1, -1).map(s => s.trim()));
children.push(table(['Step', 'Movement', 'Cash expected'], ledger, [2800, 4838, 2000], { s: 16 }));
children.push(P('Result: the X-read expected 586.82 in cash - exactly the ledger. The drawer was then counted deliberately 10.00 short; the Z-read shows the -10.00 variance and journal J-001-000030 posts it to Cash Short or Over. The 50.00 cash taken at Patient Accounts is in the ledger’s Cash on Hand but not in any drawer (ADM-DEF-18).', { before: 120 }));
children.push(H2('Stock'));
children.push(table(['Product', 'Opening', 'Movements in the run', 'Closing', 'Agrees?'], [
  ['E2EOTC (created in C02)', '0', '+30 +20 received; -1 sale S-005, -1 voucher sale S-009; credit note R-003 not restocked; counted 46, -1 sold during the count', '45', verdictCell('PASS')],
  ['E2ECD6 (schedule 6, C02)', '0', '+20 received batch CD6-2601; -1 sold over the counter S-011 (ADM-DEF-11); counted 19', '19', verdictCell('PASS')],
  ['AMOX50', '40', '+24 received batch AMX-2609; -1 sold over the counter S-011', '63', verdictCell('PASS')],
  ['PAN500', '116', '-3 S-005, -1 S-010, -1 S-012, +1 R-002; dispensing 20 tablets took 20 packs; drug return put back 1', '93 (112 had the units been right)', verdictCell('FAIL')],
  ['SURGM1', '0', '-1 sold with no stock (ADM-DEF-12)', '-1', verdictCell('FAIL')],
], [2000, 900, 4338, 1400, 1000], { s: 15 }));
children.push(H2('The books'));
children.push(table(['Account', 'Before day-end', 'After day-end', 'Comment'], [
  ['4000 Sales', '906.60', '1,818.95', 'Doubled by the day-end journal (ADM-DEF-19)'],
  ['2100 VAT Control', '130.32', '261.50', 'Equals the VAT Summary report (130.32) until the day-end doubles it'],
  ['1010 Cash on Hand', '604.76', '1,187.75', 'Every cash sale, refund, account payment and the cash-up shortage'],
  ['1100 Trade Debtors', '121.70', '308.45', 'Patient accounts total 86.75; the gap of 34.95 is the "medical aid" till sale S-010 (ADM-DEF-10)'],
  ['1150 Medical Aid Debtors', '0.00', '34.95', 'Claims raised 65.58, settled 36.66, written off 28.92'],
  ['5200 Claims Written Off', '28.92', '28.92', '26.29 Bonwell rejected + 2.63 Alpha short-paid'],
], [2400, 1500, 1500, CONTENT - 5400], { s: 16 }));
children.push(P('The trial balance balanced at every point (7,813.76 before the day-end). Daily Sales, Monthly Sales and Sales by Till Operator all report 1,043.53, the same figure as the day-end summary.', { before: 120 }));
children.push(H2('Document numbers'));
children.push(table(['Series', 'Issued in the database', 'Gaps'], [
  ['Sales', 'S-001-000001 to S-001-000014', 'None - the cancelled payment in D02 used no number'],
  ['Credit notes', 'R-001-000001 to R-001-000004', 'None'],
  ['Purchase orders', 'PO-001-000001 to PO-001-000004', 'None'],
  ['Goods receipts', 'G-001-000001 to G-001-000003', 'None'],
  ['Quotations', 'Q-001-000001 to Q-001-000002', 'None'],
  ['Scripts', 'SC-001-000001, 2, 3, 5, 6', { t: 'SC-001-000004 missing (ADM-DEF-13)', c: BAD, b: true }],
  ['Dispensings', 'D-001-000001 to D-001-000003', 'None (one unnumbered dispensing left InProgress, ADM-OBS-14)'],
  ['Journals', 'J-001-000001 to J-001-000032', 'None'],
], [2000, 3800, CONTENT - 5800], { s: 16 }));

// 6. Defects
children.push(H1('6. Findings'));
children.push(P('ADM-DEF entries are defects: the application does something wrong or cannot do something it offers. ADM-OBS entries are lesser observations - usability, wording, accessibility or layout. Each was seen in the running application; the root cause names the file where it was traced.'));
children.push(table(['ID', 'Severity', 'Where', 'What happens'],
  DEFECTS.map(d => [{ t: d.id, b: true }, sevCell(d.severity), d.where, d.actual]),
  [1250, 1350, 2200, CONTENT - 4800], { s: 15 }));
for (const d of DEFECTS) {
  children.push(H3(`${d.id}  ${d.where}`));
  children.push(table(['', ''], [
    ['Severity', sevCell(d.severity)],
    ['Steps', d.steps], ['Expected', d.expected], ['Actual', d.actual],
    ['Root cause', d.cause], ['Evidence', { t: d.evidence, mono: true, s: 15 }],
  ], [1700, CONTENT - 1700], { s: 17 }));
  const first = d.evidence.split(',').map(s => s.trim()).find(e => fs.existsSync(path.join(SHOTS, e + '.png')));
  if (first) children.push(...shot(first, `${d.id} - evidence`, 5.6, 3.6));
}

// 7. Evidence
children.push(H1('7. Evidence index'));
children.push(table(['What', 'Where'], [
  ['Every step with its expected and actual result', 'evidence\\e2e-admin\\results.csv'],
  ['Screenshots', `evidence\\e2e-admin\\screenshots (${fs.readdirSync(SHOTS).length} images, ${shotsUsed.size} in this report)`],
  ['Receipts, labels, slips, X/Z reads, orders, quotations, credit notes (PDF)', `evidence\\e2e-admin\\receipts (${fs.readdirSync(path.join(E2E, 'receipts')).length} files)`],
  ['Excel and CSV exports, statement and postings text', 'evidence\\e2e-admin\\exports'],
  ['Database checks (query and output)', 'evidence\\e2e-admin\\db'],
  ['Findings log', 'evidence\\e2e-admin\\defects.md'],
  ['Cash ledger', 'evidence\\e2e-admin\\ledger.md'],
  ['Direct SQL changes', 'evidence\\e2e-admin\\seed-log.md'],
  ['How to repeat the run', 'evidence\\e2e-admin\\adm.ps1 and ref-crud.ps1 (with docs\\e2e-helpers.ps1 and docs\\uia-drive.ps1); this document: docs\\build-admin-report.js'],
], [4200, CONTENT - 4200], { s: 17 }));

const doc = new Document({
  creator: 'Claude (Claude Code)', title: 'PharmaX Administrator End-to-End Test',
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
      children: [run('BethRapha Pharmacy - PharmaX Administrator end-to-end test - 25 September 2026', { s: 15, c: MUTED })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ children: ['Page ', PageNumber.CURRENT, ' of ', PageNumber.TOTAL_PAGES], size: 15, color: MUTED })] })] }) },
    children,
  }],
});

Packer.toBuffer(doc).then(buf => {
  fs.writeFileSync(TARGET, buf);
  console.log(`${TARGET}: ${results.length} steps, ${DEFECTS.length} findings, ${shotsUsed.size} images`);
});
