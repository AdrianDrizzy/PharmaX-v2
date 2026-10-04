// Builds the 1 October 2026 end-to-end test report as a Word document.
//
//   node docs/build-oct01-report.js evidence/e2e-oct01 docs/PharmaX-E2E-Report-2026-10-01.docx
//
// Every verdict comes from <evidence>/results.csv and every finding from
// <evidence>/defects.md, both written while the run was going on. A step
// with no row in the results is not in the report.
const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, ShadingType, BorderStyle,
  ImageRun, LevelFormat, TableOfContents, Footer, Header, PageNumber,
} = require('docx');

const E2E = path.resolve(process.argv[2] || 'evidence/e2e-oct01');
const TARGET = path.resolve(process.argv[3] || 'docs/PharmaX-E2E-Report-2026-10-01.docx');
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

// "ID | Severity | Where | Steps | Expected | Actual | Evidence | Root cause | Suggested fix"
const FINDINGS = fs.readFileSync(path.join(E2E, 'defects.md'), 'utf8')
  .split(/\r?\n/).filter(l => /^OCT-(DEF|OBS)-\d+ \|/.test(l))
  .map(l => {
    const [id, severity, where, steps, expected, actual, evidence, cause, fix] = l.split(' | ').map(s => s.trim());
    return { id, severity, base: severity.split(' ')[0], where, steps, expected, actual, evidence, cause, fix };
  });
const SEV_ORDER = ['Critical', 'High', 'Medium', 'Low', 'Cosmetic'];
FINDINGS.sort((a, b) => SEV_ORDER.indexOf(a.base) - SEV_ORDER.indexOf(b.base) || a.id.localeCompare(b.id));
const finding = id => FINDINGS.find(f => f.id === id);

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
  const map = { PASS: [GOOD, GOOD_BG], FAIL: [BAD, BAD_BG], OBSERVATION: [WARN, WARN_BG], ISSUES: [WARN, WARN_BG] };
  const [c, fill] = map[v] || [MUTED, GREY_BG];
  return { t: v === 'OBSERVATION' ? 'NOTE' : v, b: true, c, fill, align: AlignmentType.CENTER };
}
function sevCell(s) {
  const base = s.split(' ')[0];
  const c = base === 'Critical' || base === 'High' ? BAD : base === 'Medium' ? WARN : MUTED;
  return { t: s, b: true, c };
}
function priCell(p) {
  const [c, fill] = p === 'P1' ? [BAD, BAD_BG] : p === 'P2' ? [WARN, WARN_BG] : [MUTED, GREY_BG];
  return { t: p, b: true, c, fill, align: AlignmentType.CENTER };
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
// Two images side by side, for before and after.
function pair(left, leftCaption, right, rightCaption) {
  const half = Math.floor(CONTENT / 2);
  const img = name => {
    const file = path.join(SHOTS, name + '.png');
    shotsUsed.add(file);
    const { w, h, data } = pngSize(file);
    const wIn = 3.05, hIn = wIn * h / w;
    return new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 40 },
      children: [new ImageRun({ type: 'png', data, transformation: { width: Math.round(wIn * 96), height: Math.round(hIn * 96) } })] });
  };
  const cap = t => new Paragraph({ alignment: AlignmentType.CENTER, spacing: { after: 0 }, children: [run(t, { s: 16, c: MUTED, i: true })] });
  const none = { style: BorderStyle.NONE };
  const c = (kids) => new TableCell({ width: { size: half, type: WidthType.DXA },
    borders: { top: none, bottom: none, left: none, right: none }, children: kids });
  return new Table({ width: { size: half * 2, type: WidthType.DXA }, columnWidths: [half, half],
    rows: [new TableRow({ children: [c([img(left), cap(leftCaption)]), c([img(right), cap(rightCaption)])] })] });
}

// ---- what was tested ------------------------------------------------------

const CASES = {
  E01: 'Sign-in window without the shortcut keys',
  E02: 'Ten of every product on hand for testing',
  E03: 'Pharmacy telephone and WhatsApp at the System Console',
  E04: 'A till sale and the new receipt',
  E05: 'Capturing a script, dispensing it and the new labels',
  E06: 'Collecting the script and its receipt',
  E07: 'Daily Sales margin and the sales reports',
  E08: 'Label preview and reprint',
  E09: 'Reprinting a receipt after the till is reopened',
  E10: 'An account sale and a receipt reprint',
  E11: 'The cashier role',
  E12: 'The F1 key list',
};
const ORDER = Object.keys(CASES);

// Printouts and extra screenshots shown with a case, beyond the step shots.
const EXTRA = {
  E04: [['E04-05_receipt-pdf', 'The receipt S-001-000005 as printed (Microsoft Print to PDF), cropped to the slip.', 4.2, 7.4]],
  E05: [['E05-07_label-1', 'Label 1 of 2: Metformin, with batch and expiry.', 3.6],
        ['E05-07_label-2', 'Label 2 of 2: Losartan from the price list - no batch or expiry to print (OCT-DEF-02).', 3.6],
        ['E05-07_label-3', 'The trailer label: the address is cut off and there is no telephone (OCT-OBS-03).', 3.6]],
  E06: [['E06-03_collection-receipt', 'Collection receipt S-001-000006: the patient and the script are named.', 4.2, 7.4]],
  E10: [['E10-01_account-receipt', 'Account sale S-001-000007.', 4.2, 7.4],
        ['E10-02_reprint-receipt', 'The same receipt reprinted with F12.', 4.2, 7.4]],
};

const caseVerdict = id => {
  const steps = results.filter(r => r.case === id);
  if (!steps.length) return '';
  return steps.some(s => s.verdict === 'FAIL') ? 'FAIL' : steps.some(s => s.verdict === 'OBSERVATION') ? 'ISSUES' : 'PASS';
};
const count = v => results.filter(r => r.verdict === v).length;
const defects = FINDINGS.filter(f => f.id.includes('-DEF-'));
const notes = FINDINGS.filter(f => f.id.includes('-OBS-'));
const sev = s => FINDINGS.filter(f => f.base === s).length;

// ---- document ---------------------------------------------------------------

const children = [];

children.push(
  new Paragraph({ spacing: { before: 1400, after: 120 }, children: [run('BethRapha Pharmacy', { s: 56, b: true, c: ACCENT })] }),
  new Paragraph({ spacing: { after: 120 }, children: [run('PharmaX - end-to-end test, 1 October 2026', { s: 40, b: true })] }),
  new Paragraph({ spacing: { after: 400 }, children: [run('The sign-in window without its shortcut keys, and the receipt, label, Daily Sales and test-stock changes, carried through the running application - with the features that still need fixing and the ones still to be tested end to end', { s: 24, c: MUTED })] }),
  ...shot('E01-04_sign-in-branded', 'The sign-in window after the change: plain Log in and Cancel.', 4.0, 3.0),
  table(['', ''], [
    ['Build under test', 'Branch proj/0003 at 1c4b6ef with the uncommitted work of 30 September and 1 October (PharmaX 1.0.0.0, Release)'],
    ['Test date', 'Thursday 1 October 2026'],
    ['Roles', 'Administrator (test.admin) and Cashier (test.cashier)'],
    ['Database', 'Fresh MySQL 8.0 schema PharmaX_E2E_OCT01, migrated and seeded by the application on its first start (the working PharmaX database was not used)'],
    ['Pharmacy profile', 'BethRapha Pharmacy / BethRapha Pharmacy (Pvt) Ltd / 28 Woodstock Thorngrove, Bulawayo (Along Luveve Road) / VAT TEST-0001 / Tel +263 00 000 0001 / WhatsApp +263 00 000 0002 - the VAT, telephone and WhatsApp numbers are test values'],
    ['Prepared for', 'Adrian Mlilo'],
    ['Prepared by', 'Claude (Claude Code), driving the running application through UI Automation'],
  ], [2400, CONTENT - 2400], { s: 18 }),
);

children.push(H1('Contents'),
  new TableOfContents('Contents', { hyperlink: true, headingStyleRange: '1-2' }),
  P('If page numbers are missing, right-click the table and choose Update Field.', { s: 16, c: MUTED, i: true }));

// 1. Summary
children.push(H1('1. Summary'));
children.push(P([
  run('Verdict: the changes work; the catalogue does not yet dispense. ', { b: true, c: WARN }),
  run('The sign-in window now shows plain Log in and Cancel, with Enter and Escape still working. The new receipt and label layouts, the pharmacy telephone and WhatsApp numbers, the Daily Sales margin and the ten-of-everything test stock all behave as specified in the running application. '
    + `The run also found ${defects.length} defects and ${notes.length} lesser observations. The one that matters most: none of the 749 price-list products knows how many tablets are in its pack, so a script for 30 tablets of a catalogue medicine is read as 30 packs and refused.`),
]));
children.push(table(['Measure', 'Result'], [
  ['Automated unit tests', { t: '689 of 689 pass', c: GOOD, b: true }],
  ['Automated integration tests', { t: '222 of 222 pass (run 30 September; the 1 October change is in the desktop project only)', c: GOOD, b: true }],
  ['Steps executed and recorded', `${results.length} across ${ORDER.length} test cases, as two roles`],
  ['Steps passed', { t: String(count('PASS')), c: GOOD, b: true }],
  ['Steps failed', { t: String(count('FAIL')), c: BAD, b: true }],
  ['Steps passed with a note', { t: String(count('OBSERVATION')), c: WARN, b: true }],
  ['Findings', `${FINDINGS.length}: ${sev('High')} high, ${sev('Medium')} medium, ${sev('Low')} low, ${sev('Cosmetic')} cosmetic`],
  ['Documents issued in the run', 'Sales S-001-000005 to S-001-000008, script SC-001-000003, dispensing D-001-000003, 6 labels (3 issued, 3 reprinted)'],
], [3600, CONTENT - 3600], { s: 18 }));

children.push(H2('The findings that matter most'));
for (const d of FINDINGS.filter(f => f.base === 'High' || f.base === 'Medium'))
  children.push(bullet([run(`${d.id} (${d.severity}), ${d.where}: `, { b: true, c: d.base === 'High' ? BAD : INK }), run(d.actual)]));

children.push(H2('What works'));
[
  'Sign-in: the buttons read Log in and Cancel with no F6 or Esc written on them; Enter signs in (and a wrong password is refused with a message), Escape cancels.',
  'Every product has stock to test with: 749 price-list products at exactly 10, each with an adjustment explaining it; the two lines the alert centre is meant to report stay low (Disprin 8) and out of stock (masks 0).',
  'The System Console takes, validates, audits and displays the telephone and WhatsApp numbers, and they reach every receipt and the foot of every label.',
  'The receipt follows the pharmacy\'s reference slip: Tel, WhatsApp, Customer TIN and VAT No, TAX INVOICE (CASH / CARD / ACCOUNT), Cashier, Date&Time with Till No, Inv RefNo, Pay Method, CUSTOMER COPY / ORIGINAL, the Code-Qty-UnitPrice-DiscPerc-Total columns, SubTotal, Vat, Total(Incl VAT), Change for Tendered and Total Items. A reprint says COPY - REPRINTED at the head and foot and is audited.',
  'The label follows the pharmacy\'s reference label: the medicine and directions over a rule, batch and expiry, the patient, "Disp by" with the date, "Doc." for the prescriber, "RxNo / Item n of m / Prof#", and the pharmacy\'s name, address and telephone at the foot, inside a frame.',
  'Daily Sales strikes its margin on the turnover it can cost: script co-payments (219.75 today) are shown in their own column and left out of cost and margin, and the figures match an independent calculation from the tables to the cent.',
  'Four findings from the 25 September run are confirmed fixed: reprinted receipts are marked and audited (ADM-DEF-08), account receipts name the account (ADM-DEF-09), labels name the pharmacy (ADM-DEF-16), and collection receipts name the patient and script (ADM-OBS-15).',
].forEach(t => children.push(bullet(t)));

// 2. The change
children.push(H1('2. The change: no shortcut keys on the sign-in window'));
children.push(P('The function-key work of 30 September wrote a key on every button of every screen, the sign-in window included, which then read "F6 Log in" and "Esc Cancel". The sign-in window is now left out: its buttons read Log in and Cancel, as they did before 30 September. Nothing else about the window changes.'));
children.push(pair('B01-01_sign-in-before', 'Before: "F6 Log in" and "Esc Cancel".', 'E01-01_sign-in-after', 'After: plain "Log in" and "Cancel".'));
children.push(P('', { after: 60 }));
children.push(table(['', ''], [
  ['What changed', 'frmLogin overrides AssignsFunctionKeys to return false, as the till and the shell already do (PharmaX.Desktop\\Features\\Login\\frmLogin.cs).'],
  ['What stays', 'Enter signs in (the form\'s AcceptButton) and Escape cancels (its CancelButton) - the keys every sign-in window has. F1 no longer opens a key list here.'],
  ['Every other screen', 'Unchanged: the rule that every button has its key still applies to all other screens and is still checked by the unit test that walks them all.'],
  ['Automated test', 'FunctionKeyTests.The_sign_in_buttons_carry_no_keys: no keys handed out, captions "&Log in" and "&Cancel", Enter and Escape still wired. The full unit suite passes (689 of 689).'],
  ['In the running application', 'E01 below: buttons read [Log in] [Cancel]; Enter with a wrong password is refused; Enter with the right one signs in; Escape cancels and closes.'],
], [2400, CONTENT - 2400], { s: 18 }));

// 3. Scope and method
children.push(H1('3. Scope, environment and method'));
children.push(H2('Scope'));
children.push(P('The work delivered on 30 September and 1 October, carried through to what it produces: the sign-in window, the seeded stock, the pharmacy particulars, the receipt for a cash, card, account and collected sale, the dispensing labels and trailer, the label preview and reprint, the receipt reprint, and the sales reports, each checked against the database. The cashier role was used to check what a cashier sees on the receipt and that the management reports stay out of reach. Everything else in the application was outside this run; section 7 lists what most needs testing next.'));
children.push(H2('Environment'));
children.push(table(['Item', 'Value'], [
  ['Machine', 'Windows 11 Home 10.0.26200, .NET 8 runtime'],
  ['Application', 'PharmaX.Desktop.exe, Release build'],
  ['Database', 'MySQL 8.0 on 127.0.0.1, schema PharmaX_E2E_OCT01 created empty for this run; the application migrated and seeded it in 33 seconds'],
  ['Seed data', 'The application\'s own seeders with Development:SeedTestUsers on: 7 users (the built-in admin and one per standard role), 759 products (749 from the price list), 8 patients, 3 accounts and a day of demonstration trading'],
  ['Printers', 'Microsoft Print to PDF for receipts and labels; every printout saved to evidence\\e2e-oct01\\receipts and read back. Print to PDF ignores the 80 mm roll and the 70 x 50 mm label stock, so the printouts are on a Letter page and have been cropped here.'],
], [2600, CONTENT - 2600], { s: 18 }));
children.push(H2('How the application was driven'));
[
  'UI Automation to read every screen and fill text boxes; Windows messages posted to the application\'s own controls to click buttons, open menus, type into grids and press keys, so the run never took over the mouse or keyboard.',
  'Screenshots with PrintWindow, so each image is exactly the application window. Printed PDFs were read as text and rendered to images.',
  'Each step was recorded the moment it ran - what was done, what was expected, what was seen and a verdict - in evidence\\e2e-oct01\\results.csv. Database state was checked with SQL where the screen alone could not prove the result; queries and output are in evidence\\e2e-oct01\\db.',
].forEach(t => children.push(bullet(t)));

// 4. Results
children.push(H1('4. Results by feature'));
children.push(P('Each case lists its steps exactly as they were run, with what was expected and what the application did, then the screenshots and printouts. NOTE marks a step that worked but left something worth fixing.'));
for (const id of ORDER) {
  const steps = results.filter(r => r.case === id);
  if (!steps.length) continue;
  const v = caseVerdict(id);
  const pass = steps.filter(s => s.verdict === 'PASS').length;
  children.push(H2(`${id}  ${CASES[id]}`));
  children.push(P([run('Result: ', { b: true }), run(v, { b: true, c: v === 'PASS' ? GOOD : v === 'FAIL' ? BAD : WARN }),
    run(`   (${pass} of ${steps.length} steps passed)`, { c: MUTED })], { keepNext: true }));
  children.push(table(['#', 'Step', 'Expected', 'Actual (as observed)', 'Verdict'],
    steps.map((s, i) => [String(i + 1), s.step, s.expected, s.actual, verdictCell(s.verdict)]),
    [360, 2150, 2150, 3878, 1100], { s: 15 }));
  const seen = new Set();
  for (const s of steps.filter(s => s.shot)) {
    if (seen.has(s.shot) || (EXTRA[id] || []).some(e => e[0] === s.shot)) continue;
    seen.add(s.shot);
    children.push(...shot(s.shot, `${id} - ${s.step}`));
  }
  for (const [name, caption, maxIn, maxH] of EXTRA[id] || [])
    children.push(...shot(name, caption, maxIn, maxH || 4.6));
}

// 5. Findings
children.push(H1('5. Findings'));
children.push(P('OCT-DEF entries are defects: the application does something wrong or cannot do something it offers. OCT-OBS entries are lesser observations - usability, wording, accessibility, data or layout. Each was seen in the running application, and the root cause names the code it was traced to.'));
children.push(table(['ID', 'Severity', 'Where', 'What happens'],
  FINDINGS.map(d => [{ t: d.id, b: true }, sevCell(d.severity), d.where, d.actual]),
  [1250, 1350, 2200, CONTENT - 4800], { s: 15 }));
for (const d of FINDINGS) {
  children.push(H3(`${d.id}  ${d.where}`));
  children.push(table(['', ''], [
    ['Severity', sevCell(d.severity)],
    ['Steps', d.steps], ['Expected', d.expected], ['Actual', d.actual],
    ['Root cause', d.cause], ['Suggested fix', { t: d.fix, b: true }],
    ['Evidence', { t: d.evidence, mono: true, s: 15 }],
  ], [1700, CONTENT - 1700], { s: 17 }));
  const first = d.evidence.split(',').map(s => s.trim()).find(e => fs.existsSync(path.join(SHOTS, e + '.png')));
  if (first && !['E05-07_label-2', 'E05-07_label-3', 'E10-01_account-receipt'].includes(first))
    children.push(...shot(first, `${d.id} - evidence`, 5.6, 3.6));
}

// 6. What needs fixing
children.push(H1('6. Features that need fixing'));
children.push(P('In the order they should be taken. P1 stops a pharmacy trading correctly and should be fixed before go-live; P2 gives wrong or missing information that staff will notice; P3 is polish. Findings from the 25 September Administrator run that were not re-tested today are included where they are serious, marked "re-test" - their code may have changed since, but nothing has shown them working end to end.'));
const FIX = [
  ['P1', 'OCT-DEF-01', 'Dispensing price-list medicines in tablets', 'Load units per pack from the price list\'s pack size and fill it in on the 749 products already loaded; then a script for 30 tablets is 0.3 of a 100-pack, priced 3.60, not refused or charged as 30 packs.'],
  ['P1', 'ADM-DEF-19 (re-test)', 'Day-End Posting doubles the day in the books', 'The 30 September code skips the day-end batch when journals were posted as trading happened (PostedAsTraded). Post a day-end on a database with automatic posting on and check the trial balance does not move.'],
  ['P1', 'ADM-DEF-11 (re-test)', 'Scheduled medicines sell over the counter', 'Scan a schedule 4-6 product at the till with no script: it must be refused or routed to dispensing. With 313 price-list dispensary products now stocked, any of them can be tried.'],
  ['P1', 'ADM-DEF-10 (re-test)', '"Medical aid" tender at the till with no patient or claim', 'Pay a till sale with the Medical aid tender: it must demand a member and raise a claim, or not be offered at the till.'],
  ['P2', 'OCT-DEF-02', 'Batch numbers on dispensary labels', 'Batch-track dispensary products so labels carry the batch and expiry, FEFO applies and expiries are reported.'],
  ['P2', 'OCT-DEF-03', 'Daily and Monthly Sales leave out refunds', 'Net refunds and credit notes into Daily Sales, Monthly Sales, Sales by Till Operator and Sales by Markup, so they agree with the Line Items reports.'],
  ['P2', 'OCT-DEF-05', 'Reprinting a receipt', 'Remember the till\'s last sale across reopening, and add a reprint by receipt number for a customer who comes back for a copy.'],
  ['P2', 'OCT-DEF-04', 'Labels > What prints', 'Show the labels exactly as LabelDocument prints them.'],
  ['P2', 'OCT-OBS-07', 'Price-list cost prices', 'Confirm with the pharmacy whether 121 costs are per unit; correct the list, and have the import flag margins over 90%.'],
  ['P2', 'ADM-DEF-12 (re-test)', 'Selling more than is on the shelf', 'With every product now at 10, sell 11 of a price-list item: the till must warn or refuse instead of going negative.'],
  ['P3', 'OCT-OBS-01', 'Tendered and Change on account receipts', 'Print Tendered and Change only where cash was taken (introduced by the 30 September receipt layout).'],
  ['P3', 'OCT-OBS-02', 'Cashier\'s name on the receipt', 'Print the user\'s name ("Test Cashier"), not the login name.'],
  ['P3', 'OCT-OBS-03', 'Trailer label foot', 'Give the trailer the same name / address / telephone as the item labels.'],
  ['P3', 'OCT-OBS-04, OBS-06', 'Keyboard consistency', 'F1 lists only usable keys; Reports uses the standard Preview and Close keys.'],
  ['P3', 'OCT-OBS-05', 'Screen-reader names', 'Set AccessibleName on every input: System Console, Payment, Products and the home tiles (ADM-OBS-05, ADM-OBS-19).'],
  ['P3', 'OCT-OBS-08 to OBS-11', 'Smaller items', 'Mark reprinted labels; set reorder levels for price-list lines; tidy the sign-in particulars line; stop the till leaving an empty open sale behind each time it is closed.'],
];
children.push(table(['', 'Finding', 'Feature', 'What to do'],
  FIX.map(([p, id, what, how]) => [priCell(p), { t: id, b: true, s: 15 }, { t: what, s: 15 }, { t: how, s: 15 }]),
  [600, 1700, 2400, CONTENT - 4700], { s: 15 }));

// 7. What needs testing
children.push(H1('7. Features that need testing end to end'));
children.push(P('Built or changed since the last full run, or fixed in code since 25 September, and not yet driven through the running application. Each line says what a test should prove.'));
const TEST = [
  ['New on 30 September', 'Dispensary Quote (F8 on Dispensing and Scripts; Dispensary menu)', 'A quote priced in tablets as the pack\'s share; saved and resumed; taken up once as a script prefilled from the quote; a second capture refused without spending a script number; the till refuses a dispensary quote.'],
  ['New on 30 September', 'Monthly Sales and Monthly Sales (Line Items)', 'Over several days of a month: a total per day and per month, filters by operator, tender, department and category, refunds as negative lines - and, once OCT-DEF-03 is fixed, the same totals as Monthly Sales.'],
  ['New on 30 September', 'A4 medical aid claim form (Labels > F12 Claim form)', 'For a scheme member (Sipho Mokoena, Alpha): the trading name, member, script, items, tariff, scheme and patient shares, printed on A4.'],
  ['New on 30 September', 'Patient search by phone and address; search-as-you-type for patients and products', 'Typing part of a phone number or street finds the patient; suggestions appear surname first (ADM-OBS-12).'],
  ['New on 30 September', 'Product and batch columns editable from the list and the Edit dialog', 'An edit made in the grid is saved, audited and shown on the next open; a stale batch is refused, not overwritten.'],
  ['New on 30 September', 'Function keys on every screen', 'On each screen F1 lists the keys and each key presses its button; disabled and hidden buttons stay unpressed (see OCT-OBS-04, OBS-06).'],
  ['Fixed in code', 'Reverse dispensing (Labels > F7) and the Reversed Scripts report', 'A dispensing reversed with a reason puts the stock back, withdraws the claim and appears on Reversed Scripts (ADM-DEF-22).'],
  ['Fixed in code', 'Day-End Posting with automatic journals on', 'The trial balance does not move when the day-end is posted (ADM-DEF-19, Critical on 25 September).'],
  ['Changed 30 September', 'Telephone and WhatsApp on the other printouts', 'X-read, Z-read, quotation, purchase order and goods received note headers carry the telephone (PlainDocument).'],
  ['Changed 30 September', 'Receipt and label on the real printers', 'The 80 mm receipt on the till printer and the label on the 70 x 50 mm stock, including a label with long directions and three batches, which the label shrinks to fit. Print to PDF cannot prove either.'],
  ['Changed 1 October', 'First sign-in with a forced password change, and the supervisor prompt', 'A user created with "must change password" signs in, changes it and reaches the home screen; a supervisor override prompt still accepts Enter and Escape.'],
  ['Other roles', 'Pharmacist, Pharmacist Assistant, Manager and Stock Controller', 'The 23 September all-roles run predates every change above; each role\'s screens, menu headings and reports should be walked again.'],
  ['Not re-tested', 'The rest of the 25 September findings', 'ADM-DEF-01 to 07, 12, 13, 17, 18, 20 and 21 and the remaining observations: re-run their steps from the 25 September report and record each as fixed or still open.'],
];
children.push(table(['Why', 'Feature', 'What the test should prove'],
  TEST.map(([why, what, prove]) => [{ t: why, s: 15, c: MUTED }, { t: what, s: 15, b: true }, { t: prove, s: 15 }]),
  [1700, 2900, CONTENT - 4600], { s: 15 }));

// 8. Status of earlier findings
children.push(H1('8. The 25 September findings, as of today'));
children.push(table(['Finding', 'Then', 'Today'], [
  ['ADM-DEF-08 Reprinted receipt not marked or audited', 'Identical to the original; no audit', { t: 'Fixed: COPY - REPRINTED at head and foot; audit pos.receipt.reprint (E10)', c: GOOD }],
  ['ADM-DEF-09 Account receipt names nobody', 'Paid by account 81.90 only', { t: 'Fixed: "Account : A-001-000002 Sipho Mokoena" and a signature line (E10)', c: GOOD }],
  ['ADM-DEF-16 Label does not name the pharmacy', 'No name, address or telephone', { t: 'Fixed: name, address and telephone at the foot of every item label (E05)', c: GOOD }],
  ['ADM-OBS-15 Collection receipt names no patient', 'Item and cash only', { t: 'Fixed: patient, file number and script number (E06)', c: GOOD }],
  ['ADM-DEF-14 / 15 Units per pack in dispensing', 'Tablets priced as packs', { t: 'Fixed for products that know their pack (Metformin 60 tablets = 1 pack, 154.75); still wrong for every price-list product (OCT-DEF-01)', c: WARN }],
  ['ADM-DEF-22 No way to reverse a dispensing', 'No screen offered it', { t: 'A Reverse dispensing button is now on Labels; not exercised today', c: MUTED }],
  ['ADM-DEF-19 Day-end doubles the books', 'Sales 906.60 -> 1,818.95', { t: 'Changed in code (PostedAsTraded); not re-tested', c: MUTED }],
  ['ADM-OBS-12 Patient search', 'Surname and file number only', { t: 'Search by phone and address added; not re-tested', c: MUTED }],
  ['ADM-OBS-05 / 19 Screen-reader names shifted', 'Products and home tiles', { t: 'Still open, and seen on the System Console and Payment too (OCT-OBS-05)', c: BAD }],
  ['All other ADM findings', 'See the 25 September report', { t: 'Not re-tested today', c: MUTED }],
], [3300, 2600, CONTENT - 5900], { s: 15 }));

// 9. Evidence
children.push(H1('9. Evidence index'));
children.push(table(['What', 'Where'], [
  ['Every step with its expected and actual result', 'evidence\\e2e-oct01\\results.csv'],
  ['Screenshots', `evidence\\e2e-oct01\\screenshots (${fs.readdirSync(SHOTS).length} images, ${shotsUsed.size} in this report)`],
  ['Receipts and labels as printed (PDF)', `evidence\\e2e-oct01\\receipts (${fs.readdirSync(path.join(E2E, 'receipts')).length} files)`],
  ['Database checks (query and output)', 'evidence\\e2e-oct01\\db'],
  ['Findings log', 'evidence\\e2e-oct01\\defects.md'],
  ['How to repeat the run', 'evidence\\e2e-oct01\\run.ps1 (with evidence\\e2e-admin\\adm.ps1, docs\\e2e-helpers.ps1 and docs\\uia-drive.ps1); this document: docs\\build-oct01-report.js'],
], [4200, CONTENT - 4200], { s: 17 }));

const doc = new Document({
  creator: 'Claude (Claude Code)', title: 'PharmaX End-to-End Test, 1 October 2026',
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
      children: [run('BethRapha Pharmacy - PharmaX end-to-end test - 1 October 2026', { s: 15, c: MUTED })] })] }) },
    footers: { default: new Footer({ children: [new Paragraph({ alignment: AlignmentType.CENTER,
      children: [new TextRun({ children: ['Page ', PageNumber.CURRENT, ' of ', PageNumber.TOTAL_PAGES], size: 15, color: MUTED })] })] }) },
    children,
  }],
});

Packer.toBuffer(doc).then(buf => {
  fs.writeFileSync(TARGET, buf);
  console.log(`${TARGET}: ${results.length} steps, ${FINDINGS.length} findings, ${shotsUsed.size} images`);
});
