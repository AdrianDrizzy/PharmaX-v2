// Builds the PharmaX user-acceptance test script as a Word document.
const fs = require('fs');
const path = require('path');
const d = require('docx');

const {
  Document, Packer, Paragraph, TextRun, HeadingLevel, AlignmentType,
  Table, TableRow, TableCell, WidthType, ShadingType, BorderStyle,
  ImageRun, PageBreak, LevelFormat, TableOfContents, Header, Footer,
  PageNumber, convertInchesToTwip
} = d;

const SHOTS = 'C:\\Users\\mlilo\\Downloads\\PharmaX\\docs\\screenshots';

// Page is US Letter minus one-inch margins: 6.5in of usable width.
const CONTENT_DXA = 9360;

const INK = '1A1A1A';
const MUTED = '5A626E';
const ACCENT = '005FB8';
const PROBLEM = 'A82020';
const BAND = 'EFF2F6';

function h1(text) {
  return new Paragraph({
    text, heading: HeadingLevel.HEADING_1,
    spacing: { before: 360, after: 160 },
  });
}

function h2(text) {
  return new Paragraph({
    text, heading: HeadingLevel.HEADING_2,
    spacing: { before: 280, after: 120 },
  });
}

/** A bold label that is not a heading, so it stays out of the contents. */
function label(text) {
  return new Paragraph({
    spacing: { before: 200, after: 90 },
    children: [new TextRun({ text, bold: true, size: 22, color: INK })],
  });
}

function h3(text) {
  return new Paragraph({
    text, heading: HeadingLevel.HEADING_3,
    spacing: { before: 220, after: 100 },
  });
}

function p(text, opts = {}) {
  return new Paragraph({
    spacing: { after: opts.after === undefined ? 140 : opts.after },
    alignment: opts.align,
    children: [new TextRun({
      text,
      color: opts.color || INK,
      italics: opts.italics,
      bold: opts.bold,
      size: opts.size || 21,
    })],
  });
}

function runs(parts, opts = {}) {
  return new Paragraph({
    spacing: { after: opts.after === undefined ? 140 : opts.after },
    children: parts.map(part =>
      typeof part === 'string'
        ? new TextRun({ text: part, size: 21, color: INK })
        : new TextRun({
            text: part.t, bold: part.b, italics: part.i,
            color: part.c || INK, font: part.mono ? 'Consolas' : undefined,
            size: 21,
          })),
  });
}

function bullet(text, level = 0) {
  return new Paragraph({
    numbering: { reference: 'dots', level },
    spacing: { after: 90 },
    children: [new TextRun({ text, size: 21, color: INK })],
  });
}

let stepInstance = 0;

/** Starts a fresh run of numbered steps, so each story counts from one. */
function steps() { stepInstance += 1; }

function step(n, text) {
  return new Paragraph({
    numbering: { reference: 'steps', level: 0, instance: stepInstance },
    spacing: { after: 90 },
    children: [new TextRun({ text, size: 21, color: INK })],
  });
}

function mono(text) {
  return new Paragraph({
    spacing: { after: 120 },
    shading: { type: ShadingType.CLEAR, fill: BAND },
    children: [new TextRun({ text, font: 'Consolas', size: 19, color: INK })],
  });
}

/** A note that the reader should not skim past. */
function callout(label, text, colour) {
  return new Table({
    width: { size: CONTENT_DXA, type: WidthType.DXA },
    columnWidths: [CONTENT_DXA],
    borders: {
      top: { style: BorderStyle.SINGLE, size: 2, color: colour },
      bottom: { style: BorderStyle.SINGLE, size: 2, color: colour },
      left: { style: BorderStyle.SINGLE, size: 18, color: colour },
      right: { style: BorderStyle.SINGLE, size: 2, color: colour },
      insideHorizontal: { style: BorderStyle.NONE },
      insideVertical: { style: BorderStyle.NONE },
    },
    rows: [new TableRow({
      children: [new TableCell({
        width: { size: CONTENT_DXA, type: WidthType.DXA },
        margins: { top: 120, bottom: 120, left: 160, right: 160 },
        children: [new Paragraph({
          children: [
            new TextRun({ text: label + '  ', bold: true, color: colour, size: 21 }),
            new TextRun({ text, size: 21, color: INK }),
          ],
        })],
      })],
    })],
  });
}

/** Two-column detail with no header row. */
function detail(rows) {
  const widths = [Math.round(CONTENT_DXA * 0.22),
                  CONTENT_DXA - Math.round(CONTENT_DXA * 0.22)];

  return new Table({
    width: { size: CONTENT_DXA, type: WidthType.DXA },
    columnWidths: widths,
    borders: {
      top: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      bottom: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      left: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      right: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      insideHorizontal: { style: BorderStyle.SINGLE, size: 2, color: 'DCE0E8' },
      insideVertical: { style: BorderStyle.SINGLE, size: 2, color: 'DCE0E8' },
    },
    rows: rows.map(r => new TableRow({
      children: r.map((c, i) => {
        const isObj = c && typeof c === 'object';
        return new TableCell({
          width: { size: widths[i], type: WidthType.DXA },
          shading: i === 0
            ? { type: ShadingType.CLEAR, fill: BAND }
            : undefined,
          margins: { top: 70, bottom: 70, left: 110, right: 110 },
          children: [new Paragraph({
            children: [new TextRun({
              text: isObj ? c.t : String(c),
              bold: i === 0,
              size: 19,
              color: isObj ? (c.c || INK) : INK,
            })],
          })],
        });
      }),
    })),
  });
}

function table(headers, rows, widths) {
  const total = widths.reduce((a, b) => a + b, 0);
  const scaled = widths.map(w => Math.round(w / total * CONTENT_DXA));

  // Rounding can leave the columns a dxa or two short of the table.
  scaled[scaled.length - 1] +=
    CONTENT_DXA - scaled.reduce((a, b) => a + b, 0);

  const cell = (text, opts = {}) => new TableCell({
    width: { size: scaled[opts.i], type: WidthType.DXA },
    shading: opts.head
      ? { type: ShadingType.CLEAR, fill: BAND }
      : undefined,
    margins: { top: 70, bottom: 70, left: 110, right: 110 },
    children: [new Paragraph({
      children: [new TextRun({
        text: String(text),
        bold: opts.head,
        size: 19,
        color: opts.colour || INK,
        font: opts.mono ? 'Consolas' : undefined,
      })],
    })],
  });

  return new Table({
    width: { size: CONTENT_DXA, type: WidthType.DXA },
    columnWidths: scaled,
    borders: {
      top: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      bottom: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      left: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      right: { style: BorderStyle.SINGLE, size: 4, color: 'C8CDD6' },
      insideHorizontal: { style: BorderStyle.SINGLE, size: 2, color: 'DCE0E8' },
      insideVertical: { style: BorderStyle.SINGLE, size: 2, color: 'DCE0E8' },
    },
    rows: [
      new TableRow({
        tableHeader: true,
        children: headers.map((t, i) => cell(t, { head: true, i })),
      }),
      ...rows.map(r => new TableRow({
        children: r.map((c, i) => {
          const isObj = c && typeof c === 'object';
          return cell(isObj ? c.t : c, {
            i,
            colour: isObj ? c.c : undefined,
            mono: isObj ? c.mono : false,
          });
        }),
      })),
    ],
  });
}

/** A screenshot, sized to the content width, with its caption. */
function shot(file, caption, widthIn) {
  const full = path.join(SHOTS, file);

  if (!fs.existsSync(full)) {
    return [p('[screenshot missing: ' + file + ']', { color: PROBLEM })];
  }

  const size = require('child_process');
  // Dimensions come from the PNG header: bytes 16..24, big-endian.
  const buf = fs.readFileSync(full);
  const pxW = buf.readUInt32BE(16);
  const pxH = buf.readUInt32BE(20);

  const w = (widthIn || 6.4) * 96;
  const h = Math.round(w * pxH / pxW);

  return [
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { before: 60, after: 60 },
      children: [new ImageRun({
        type: 'png',
        data: buf,
        transformation: { width: Math.round(w), height: h },
      })],
    }),
    new Paragraph({
      alignment: AlignmentType.CENTER,
      spacing: { after: 200 },
      children: [new TextRun({
        text: caption, italics: true, size: 18, color: MUTED,
      })],
    }),
  ];
}

// ---------------------------------------------------------------------

const children = [];

// ---- Title page ------------------------------------------------------
children.push(
  new Paragraph({ spacing: { before: 2200, after: 0 }, children: [] }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { after: 60 },
    children: [new TextRun({ text: 'PharmaX', bold: true, size: 72, color: INK })],
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { after: 500 },
    children: [new TextRun({
      text: 'User Acceptance Test Script', size: 32, color: ACCENT,
    })],
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { after: 140 },
    children: [new TextRun({
      text: 'Six roles, end to end, with the seed data and the screens',
      size: 22, color: MUTED,
    })],
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    spacing: { before: 900, after: 40 },
    children: [new TextRun({ text: 'Version 1.0', size: 20, color: MUTED })],
  }),
  new Paragraph({
    alignment: AlignmentType.CENTER,
    children: [new TextRun({ text: '13 September 2026', size: 20, color: MUTED })],
  }),
  new Paragraph({ children: [new PageBreak()] }),
);

// ---- Contents --------------------------------------------------------
children.push(
  h1('Contents'),
  new TableOfContents('Contents', {
    hyperlink: true, headingStyleRange: '1-2',
  }),
  new Paragraph({ children: [new PageBreak()] }),
);

// ---- 1. How to use ---------------------------------------------------
children.push(
  h1('1. How to use this document'),

  p('This is a test script, not a manual. It walks one working day through '
    + 'PharmaX from six different chairs - the administrator, the manager, '
    + 'the pharmacist, the pharmacist\u2019s assistant, the cashier and the '
    + 'stock controller - and says at each step what should happen.'),

  p('Every screenshot in it was taken from PharmaX running against a real '
    + 'MySQL database on 13 September 2026. Where a step was actually '
    + 'performed and the result confirmed in the database, the document says '
    + 'so. Where it was not, it says that too. That distinction is the most '
    + 'useful thing here, so it is never blurred.'),

  h2('What each test tells you'),

  bullet('A PASS means the step was carried out and the result seen.'),
  bullet('A NOT RUN means the step is described but nobody has done it yet. '
       + 'It is not a failure; it is untested ground.'),
  bullet('Defects found during the walkthrough are listed in section 10, '
       + 'with what happened to each one.'),

  callout('Before you start',
    'Set up the pharmacy\u2019s main computer first. The tills need a '
    + 'database to connect to, and the main computer is what creates it.',
    ACCENT),
  p(''),
);

// ---- 2. Environment --------------------------------------------------
children.push(
  h1('2. The environment this was run against'),

  table(
    ['What', 'Value'],
    [
      ['Application', 'PharmaX 1.0.0, .NET 8, Windows'],
      ['Database', { t: 'MySQL 8.0.42, schema PharmaX2', mono: true }],
      ['Mode', 'Shared \u2014 one database, every workstation reading it'],
      ['Workstation', 'Branch 1, Till 1'],
      ['Currency', 'US Dollar ($)'],
      ['Date on the machine', 'Sunday 13 September 2026'],
      ['Tests passing', '341 (263 unit, 78 integration)'],
    ],
    [30, 70]),

  p('', { after: 200 }),

  p('The title bar reads TEST DATA (seeded accounts) throughout. That is '
    + 'PharmaX saying it is running on seeded credentials, so a screenshot '
    + 'of it can never be mistaken for a live pharmacy. A real installation '
    + 'does not show it.'),
);

// ---- 3. Accounts -----------------------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('3. The test accounts'),

  p('Six accounts, one per role. They exist only because '
    + 'Development.SeedTestUsers is on; the installer never writes them.'),

  runs([
    'Every account uses the same password: ',
    { t: 'Test#1234', mono: true, b: true },
  ]),

  p('', { after: 120 }),

  table(
    ['Username', 'Role', 'Discount limit', 'What they are for'],
    [
      [{ t: 'test.admin', mono: true }, 'Administrator', '100%',
       'Everything. Users, settings, the books.'],
      [{ t: 'test.manager', mono: true }, 'Manager', '50%',
       'Trading, buying, reports and the books.'],
      [{ t: 'test.pharmacist', mono: true }, 'Pharmacist', '10%',
       'Dispensing, patients, clinical reports.'],
      [{ t: 'test.assistant', mono: true }, 'Assistant', '5%',
       'Selling and looking things up.'],
      [{ t: 'test.cashier', mono: true }, 'Cashier', '5%',
       'The till, and nothing else.'],
      [{ t: 'test.stock', mono: true }, 'Stock Controller', '0%',
       'Deliveries, orders, counting.'],
    ],
    [22, 20, 15, 43]),

  p('', { after: 200 }),

  p('The discount limits differ on purpose. A cashier at 5 per cent is what '
    + 'makes the supervisor-authorisation path reachable at all, and a '
    + 'manager at 50 is what makes it satisfiable.'),

  h2('What each role may do'),

  p('PharmaX hides what a user may not do and then refuses it anyway if the '
    + 'menu is ever wrong. Both halves are worth testing.'),

  table(
    ['Permission', 'Adm', 'Mgr', 'Phm', 'Ast', 'Csh', 'Stk'],
    [
      ['Sell at the till', 'Y', 'Y', 'Y', 'Y', 'Y', '\u2014'],
      ['Discount above limit', 'Y', 'Y', '\u2014', '\u2014', '\u2014', '\u2014'],
      ['Authorise a refund', 'Y', 'Y', '\u2014', '\u2014', '\u2014', '\u2014'],
      ['Dispense a script', 'Y', '\u2014', 'Y', '\u2014', '\u2014', '\u2014'],
      ['Reverse a dispensing', 'Y', '\u2014', 'Y', '\u2014', '\u2014', '\u2014'],
      ['Read products', 'Y', 'Y', 'Y', 'Y', 'Y', 'Y'],
      ['Change a price', 'Y', 'Y', '\u2014', '\u2014', '\u2014', '\u2014'],
      ['Receive goods', 'Y', 'Y', '\u2014', '\u2014', '\u2014', 'Y'],
      ['Post a stocktake', 'Y', 'Y', '\u2014', '\u2014', '\u2014', 'Y'],
      ['Read the books', 'Y', 'Y', '\u2014', '\u2014', '\u2014', '\u2014'],
      ['Post a journal', 'Y', 'Y', '\u2014', '\u2014', '\u2014', '\u2014'],
      ['Assign roles', 'Y', '\u2014', '\u2014', '\u2014', '\u2014', '\u2014'],
      ['Change settings', 'Y', '\u2014', '\u2014', '\u2014', '\u2014', '\u2014'],
    ],
    [40, 10, 10, 10, 10, 10, 10]),

  p('', { after: 160 }),

  callout('Note',
    'Posting a journal is a manager\u2019s job, not a cashier\u2019s. A till '
    + 'shortage that can be journalled away by the person who caused it is '
    + 'not a control at all.', ACCENT),
);

// ---- 4. Seed data ----------------------------------------------------
children.push(
  h1('4. Seed data'),

  p('PharmaX seeds itself on every start-up, adding only what is missing. '
    + 'Three of the four groups below arrive on their own; the fourth is the '
    + 'demonstration data this walkthrough needed.'),

  h2('4.1 Seeded automatically'),

  table(
    ['What', 'How much', 'Where from'],
    [
      ['Permissions and roles', '18 permissions, 6 roles', 'Reference seeder'],
      ['Chart of accounts', '12 accounts', 'Reference seeder'],
      ['Cash denominations', 'Per currency', 'Reference seeder'],
      ['Document number blocks', '9 series', 'Reference seeder'],
      ['Test users', '6 accounts', 'Development only'],
      ['Product catalogue', '759 products',
       'Bethrapha price list'],
    ],
    [34, 26, 40]),

  p('', { after: 200 }),

  p('The product catalogue is the pharmacy\u2019s own price list, loaded from '
    + 'the supplied spreadsheet. It brings in prices, not stock: a price list '
    + 'says what things cost, not what is on the shelf.'),

  h2('4.2 Added for this walkthrough'),

  p('None of the screens below can prove anything against an empty database, '
    + 'so the following was added. Every item is idempotent and can be '
    + 're-run.'),

  h3('Stock levels, so the alert centre has something to say'),

  table(
    ['Code', 'Product', 'On hand', 'Reorder at', 'Gives'],
    [
      [{ t: '1454', mono: true }, 'Ibuprofen 400mg', '40', '100', 'Low stock'],
      [{ t: '1932', mono: true }, 'Vicryl suture 40mm', '0', '20',
       { t: 'Out of stock', c: PROBLEM }],
      [{ t: '469', mono: true }, 'Acetone', '8', '25', 'Low stock'],
    ],
    [12, 34, 14, 16, 24]),

  p('', { after: 200 }),

  callout('Why the reorder level matters',
    'A product with no reorder level has no reorder policy, so the alert '
    + 'centre leaves it alone. Without that rule the catalogue\u2019s 759 '
    + 'products would all have alerted on day one, and an alert centre '
    + 'nobody can face reading is one nobody reads on the day it matters.',
    ACCENT),

  p('', { after: 160 }),

  h3('Batches, one of each severity'),

  table(
    ['Batch', 'Product', 'Expires', 'Days', 'Severity'],
    [
      [{ t: 'IBU-2024-A', mono: true }, 'Ibuprofen 400mg', '01/09/2026',
       '\u221212', { t: 'Expired \u2014 critical', c: PROBLEM }],
      [{ t: 'IBU-2025-B', mono: true }, 'Ibuprofen 400mg', '01/10/2026',
       '18', { t: 'Critical', c: PROBLEM }],
      [{ t: 'ACE-2025-C', mono: true }, 'Acetone', '07/11/2026',
       '55', 'Warning'],
    ],
    [20, 26, 18, 10, 26]),

  p('', { after: 200 }),

  p('The thresholds are configurable. By default anything inside 90 days is '
    + 'a warning and anything inside 30 days, or already expired, is '
    + 'critical.'),

  h3('Patients, scripts and two dispensings waiting at the till'),

  table(
    ['File', 'Patient', 'What it is for'],
    [
      [{ t: 'PF0100', mono: true }, 'Grace Moyo',
       'Dispensing DSP11395600, owes $30.00'],
      [{ t: 'PF0101', mono: true }, 'Tendai Ncube',
       'Dispensing DSP11395601, owes $45.50'],
      [{ t: 'PF0102', mono: true }, 'Lindiwe Dube',
       'Allergic to penicillin; script SC-DEMO-01 with 5 repeats'],
    ],
    [14, 22, 64]),

  p('', { after: 200 }),

  p('Lindiwe Dube exists so that the dispensing screen has a clinical '
    + 'warning to raise rather than a clean run every time. Her script has '
    + 'repeats left, so it can be dispensed during the walkthrough without '
    + 'capturing one first.'),
);

// ---- 5. Signing in ---------------------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('5. Story one \u2014 signing in'),

  p('Every role starts here, so it is tested once.'),

  (steps(), label('Steps')),
  step(1, 'Start PharmaX.'),
  step(2, 'Type the username and password.'),
  step(3, 'Press Enter, or click Log in.'),

  label('Expected'),
  bullet('The sign-in window appears before anything else. No database work '
       + 'is visible and no shell is drawn until the credentials are '
       + 'accepted.'),
  bullet('A wrong username and a wrong password give the same message. The '
       + 'form cannot be used to work out which usernames exist.'),
  bullet('The shell opens on the home screen with the user\u2019s name and '
       + 'role in the status bar.'),

  ...shot('01-sign-in.png',
    'Figure 1 \u2014 The sign-in window. Local credentials, so it works with '
    + 'no host and no internet.', 4.2),

  callout('PASS',
    'Signed in successfully as all six accounts during this walkthrough.',
    '1F7A3D'),
  p(''),
);

// ---- 6. Administrator ------------------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('6. Story two \u2014 the administrator opens up'),

  runs([{ t: 'Signed in as ', }, { t: 'test.admin', mono: true, b: true },
        '. The administrator holds every permission, so this is also the '
        + 'view against which the other five are compared.']),

  (steps(), label('Steps')),
  step(1, 'Sign in as test.admin.'),
  step(2, 'Look at the home screen and the status bar.'),
  step(3, 'Check that every menu heading is available.'),

  label('Expected'),
  bullet('Every tile is enabled. Nothing reads "Not permitted for this '
       + 'user".'),
  bullet('Every menu heading is black rather than grey: File, Dispensary, '
       + 'Point of Sale, Inventory, Accounts, Reports, Stocktake, '
       + 'Administration, Help.'),
  bullet('The status bar carries the user, the branch and till, the session '
       + 'state, and - if there is anything to say - how many scripts are '
       + 'waiting and how many stock alerts are open.'),

  ...shot('02-admin-home.png',
    'Figure 2 \u2014 The administrator\u2019s home screen. Note the status '
    + 'bar: 2 scripts waiting, in blue, and 3 stock alerts, in red.'),

  callout('PASS',
    'All eleven tiles enabled; all nine menu headings available. The status '
    + 'bar read "2 scripts waiting" and "3 stock alerts", both matching the '
    + 'database.', '1F7A3D'),

  p('', { after: 160 }),

  h2('Why the status bar matters'),

  p('The two coloured counts are the only part of PharmaX that goes looking '
    + 'for the user rather than waiting to be opened. They are refreshed '
    + 'every fifteen seconds, on every machine, and they disappear entirely '
    + 'when there is nothing to say - because a count that is almost always '
    + 'zero is a count nobody reads on the day it is not.'),
);

// ---- 7. Pharmacist and the hand-off ----------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('7. Story three \u2014 the pharmacist dispenses'),

  runs([{ t: 'Signed in as ' }, { t: 'test.pharmacist', mono: true, b: true },
        '.']),

  label('Expected on the home screen'),
  bullet('Dispense and Patients are enabled.'),
  bullet('Goods receipt and Trial balance read "Not permitted for this '
       + 'user".'),
  bullet('The Accounts and Administration menus are grey.'),

  ...shot('12-pharmacist-home.png',
    'Figure 3 \u2014 The pharmacist\u2019s home screen. Compare the greyed '
    + 'tiles with Figure 2.'),

  callout('PASS',
    'Permissions rendered correctly. The pharmacist cannot reach the books '
    + 'or the goods-receipt screen, and the menus say so before the services '
    + 'have to.', '1F7A3D'),

  h2('Steps \u2014 dispensing a script'),
  step(1, 'Press F2, or click the Dispense tile.'),
  step(2, 'Find patient PF0102, Lindiwe Dube.'),
  step(3, 'Choose her script SC-DEMO-01.'),
  step(4, 'Add an item, with directions.'),
  step(5, 'Deal with the clinical warning her penicillin allergy raises.'),
  step(6, 'Issue the dispensing.'),

  label('Expected'),
  bullet('Nothing can be issued while a clinical warning is unacknowledged. '
       + 'A hard stop needs a written reason; anything softer needs an '
       + 'acknowledgement, and both are recorded against the dispensing.'),
  bullet('Issuing takes the stock off the shelf and puts the dispensing into '
       + 'the collection queue, where every till can see it within five '
       + 'seconds.'),
  bullet('A dispensing with nothing to pay never reaches the queue. A fully '
       + 'covered medical-aid script is finished the moment it is issued.'),

  callout('NOT RUN',
    'The dispensing screen itself was not driven during this walkthrough. '
    + 'The two dispensings used in section 8 were created directly through '
    + 'the domain, which exercises the same rules and the same persistence '
    + 'but not the screen. Dispensing is covered by automated tests; the '
    + 'screen is not.', 'B26A00'),
);

// ---- 8. Cashier and collection ---------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('8. Story four \u2014 the cashier takes the money'),

  runs([{ t: 'Signed in as ' }, { t: 'test.cashier', mono: true, b: true },
        '. This is the longest story in the document because it is the one '
        + 'that crosses from the dispensary to the till.']),

  h2('8.1 What a cashier may do'),

  ...shot('11-cashier-home.png',
    'Figure 4 \u2014 The cashier\u2019s home screen. Dispense, Patients, '
    + 'Goods receipt, Reports and Trial balance are all closed to them.'),

  callout('PASS',
    'Five tiles correctly greyed. The Dispensary, Accounts, Reports and '
    + 'Administration menus are unavailable.', '1F7A3D'),

  p('', { after: 140 }),

  callout('Observation',
    'Purchase orders and Stocktake are enabled for a cashier. Both are '
    + 'gated on "read products", which a cashier holds. This is wider than '
    + 'it should be and is raised as defect D3 in section 10.',
    'B26A00'),

  h2('8.2 The collection queue'),

  p('The pharmacist has issued; the patient walks to a till. In between, the '
    + 'medicine has left the shelf and no money has been taken. That interval '
    + 'is what this screen shows.'),

  (steps(), label('Steps')),
  step(1, 'Press F11, or click the Collect tile.'),
  step(2, 'Read the queue.'),

  label('Expected'),
  bullet('Both waiting scripts are listed, oldest first, with how long the '
       + 'patient has been waiting.'),
  bullet('The summary line gives the count, the total owed and the longest '
       + 'wait.'),
  bullet('Anyone waiting ten minutes or more is flagged in amber.'),
  bullet('The queue re-reads itself every five seconds, so a patient walking '
       + 'from the dispensary counter finds themselves already on screen.'),

  ...shot('04-collect.png',
    'Figure 5 \u2014 Two scripts waiting, $75.50 owed. Both amber, because '
    + 'both have been waiting over ten minutes.'),

  callout('PASS',
    'Grace Moyo $30.00 and Tendai Ncube $45.50, both listed with the '
    + 'dispensing number, the script number and the pharmacist who issued.',
    '1F7A3D'),
);

children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h2('8.3 Taking the co-payment'),

  (steps(), label('Steps')),
  step(1, 'Select Grace Moyo and click Take Payment.'),
  step(2, 'Check the till opened on the right figure.'),
  step(3, 'Press F6, add the cash tender, and pay.'),

  label('Expected'),
  bullet('The till opens already priced. There is nothing to scan and '
       + 'nothing to key: the amount is the one the pharmacist quoted, to '
       + 'the cent.'),
  bullet('The scan bar is gone and every function key but Pay and Close is '
       + 'disabled. A dispensed script is not editable at a till.'),
  bullet('The window title names the patient.'),
  bullet('Paying closes the till and returns to the queue, with the script '
       + 'gone from it.'),

  ...shot('05-collect-till.png',
    'Figure 6 \u2014 The till in collection mode. One line, priced at the '
    + 'whole co-payment, with the dispensed quantity in the description.'),

  p('The line reads "IBUPROFEN [PHARMANOVA] 400MG x 21" at a quantity of '
    + 'one. That is deliberate: thirty dollars over twenty-one tablets does '
    + 'not divide into cents, and pricing per tablet then multiplying back '
    + 'would lose money off a total that has to match what the patient was '
    + 'told.'),

  ...shot('06-payment.png',
    'Figure 7 \u2014 The payment window, tender added, nothing outstanding.',
    3.6),

  ...shot('07-collect-after.png',
    'Figure 8 \u2014 The queue immediately afterwards. One waiting, $45.50, '
    + 'and Grace Moyo gone.'),

  callout('PASS',
    'Payment taken. The database confirms dispensing DSP11395600 now carries '
    + 'a SaleId and a collection timestamp; DSP11395601 does not. The queue '
    + 'refreshed on its own.', '1F7A3D'),

  h2('8.4 The three rules underneath'),

  bullet('Stock is not issued twice. The medicine moved when the pharmacist '
       + 'issued it, so a collection skips the stock service entirely. '
       + 'Without this every dispensed product would go short by the amount '
       + 'dispensed, and nobody could explain it at the next stocktake.'),
  bullet('Nothing else can be scanned onto a collection. The stock behind a '
       + 'co-payment and the stock behind a bottle of shampoo are accounted '
       + 'for differently.'),
  bullet('A paid script cannot be reversed. That has to go back through '
       + 'returns, which is the path that actually gives the money back.'),

  h2('8.5 Abandoning a collection part-way'),

  p('The case that found two defects. A cashier opens a script, the '
    + 'patient changes their mind or the phone goes, and the till is '
    + 'closed without taking any money.'),

  (steps(), label('Steps')),
  step(1, 'Open a waiting script with Take Payment.'),
  step(2, 'Press F7 to close the till without paying.'),
  step(3, 'Open the same script again.'),

  label('Expected'),
  bullet('Closing without paying says nothing about payment, and the '
       + 'script stays in the queue.'),
  bullet('Opening it again carries on with the same unfinished sale, so '
       + 'the patient can still pay.'),

  callout('FAIL — now fixed',
    'Closing the till reported the patient had paid, and the queue said '
    + '"Tendai Ncube paid. Next patient." when no money had been taken. '
    + 'Worse, opening the same script a second time failed outright: the '
    + 'abandoned draft still held the dispensing, and the database '
    + 'refused a second sale for it. The patient could never pay, on any '
    + 'till, ever. Raised as D5 and D6, both fixed and covered by a new '
    + 'test.',
    PROBLEM),

  p('', { after: 140 }),

  callout('Two cashiers, one queue',
    'Whoever takes the payment first takes the script off every other '
    + 'screen within five seconds. A second cashier who was a moment behind '
    + 'is told the script has already been paid for, and by whom \u2014 not '
    + 'left to charge the patient twice.', ACCENT),
);

// ---- 9. Stock controller ---------------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('9. Story five \u2014 the stock controller'),

  runs([{ t: 'Signed in as ' }, { t: 'test.stock', mono: true, b: true },
        '.']),

  label('Expected on the home screen'),
  bullet('Goods receipt, Purchase orders, Stocktake and Stock alerts are '
       + 'enabled.'),
  bullet('Sell, Collect, Dispense and Patients are all closed to them.'),
  bullet('The status bar shows the stock alerts but no script count - a '
       + 'stock controller cannot sell, so the collection queue is not '
       + 'theirs to watch.'),

  ...shot('14-stock-home.png',
    'Figure 9 \u2014 The stock controller\u2019s home screen. Note the '
    + 'status bar: stock alerts only, no scripts waiting.'),

  callout('PASS',
    'Permissions correct, and the status bar correctly omits the script '
    + 'count for a user without the sale permission.', '1F7A3D'),

  h2('9.1 The stock alert centre'),

  p('The same list on every machine in the pharmacy, because they all read '
    + 'the same database. The pharmacist sees a short-dated batch without '
    + 'waiting for anyone to run a report, and a cashier sees that a line is '
    + 'out before promising a customer it is on the shelf.'),

  (steps(), label('Steps')),
  step(1, 'Press F12, or click the Stock alerts tile.'),
  step(2, 'Read the list.'),
  step(3, 'Narrow it with the Show list.'),

  label('Expected'),
  bullet('Worst first: expired, then expiring inside thirty days, then out '
       + 'of stock, then expiring inside ninety days, then low.'),
  bullet('Critical rows are red; warnings are amber.'),
  bullet('The summary leads with how many need attention today, because that '
       + 'is the number that decides whether anybody does anything.'),
  bullet('Opening a product from here and fixing it takes the row off the '
       + 'list.'),

  ...shot('03-stock-alerts.png',
    'Figure 10 \u2014 Six alerts, three of them urgent, in the order '
    + 'somebody would work through them.'),

  callout('PASS',
    'All six seeded conditions appeared, correctly ordered and correctly '
    + 'coloured. The 759 catalogue products with no reorder level stayed off '
    + 'the list, as intended.', '1F7A3D'),

  p('', { after: 140 }),

  callout('Deliberately stateless',
    'There is nothing to acknowledge and nothing to dismiss. An alert here '
    + 'is a fact about the shelf, not a message about it: it stops being '
    + 'listed when the stock is sold, returned, written off or reordered - '
    + 'which is the same moment the alert was asking for. A dismiss button '
    + 'would let the list be silenced without any of that happening, and the '
    + 'one alert somebody silenced in March is the one that matters in June.',
    ACCENT),

  h2('9.2 Receiving a delivery'),

  (steps(), label('Steps')),
  step(1, 'Press F8, or click the Goods receipt tile.'),
  step(2, 'Choose a supplier - Alpha Pharm or UPD are seeded.'),
  step(3, 'Add the delivered lines, with batch numbers and expiry dates.'),
  step(4, 'Post the receipt.'),

  label('Expected'),
  bullet('Posting is the moment stock moves. A receipt that fails to save '
       + 'leaves no batches and no movements behind.'),
  bullet('A batch-tracked line creates a batch; the same batch received '
       + 'twice is one batch, not two.'),
  bullet('A short delivery leaves the purchase order open rather than '
       + 'looking complete.'),
  bullet('The books are posted at the same moment: Purchases debited, Trade '
       + 'Creditors credited, in the same transaction as the delivery.'),

  callout('NOT RUN',
    'The goods-receipt screen was not driven during this walkthrough. Its '
    + 'rules are covered by five integration tests against a real database, '
    + 'including the batch, the short delivery and the invoice mismatch.',
    'B26A00'),
);

// ---- 10. Manager and the books ---------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('10. Story six \u2014 the manager does the books'),

  runs([{ t: 'Signed in as ' }, { t: 'test.manager', mono: true, b: true },
        '. The manager is the only role besides the administrator that can '
        + 'reach the Accounts menu.']),

  h2('10.1 What posts itself'),

  p('Most of the books write themselves as the pharmacy trades. Every one of '
    + 'these runs inside the transaction of the thing it records, so the '
    + 'books can never describe a sale that did not happen.'),

  table(
    ['When', 'Debit', 'Credit'],
    [
      ['A sale is finalised', 'Cash, Bank or Debtors by tender',
       'Sales (net) and VAT Control'],
      ['A refund is authorised', 'Sales (net) and VAT Control',
       'Cash, Bank or Debtors'],
      ['A delivery is posted', 'Purchases', 'Trade Creditors'],
      ['A till is short', 'Cash Short or Over', 'Cash on Hand'],
      ['A till is over', 'Cash on Hand', 'Cash Short or Over'],
    ],
    [30, 35, 35]),

  p('', { after: 200 }),

  p('VAT comes from each line\u2019s own split rather than one rate applied '
    + 'to the total, because a basket can mix a zero-rated medicine with a '
    + 'standard-rated tube of hand cream, and one rate across the whole would '
    + 'misstate what is owed.'),

  h2('10.2 The chart of accounts'),

  ...shot('09-chart.png',
    'Figure 11 \u2014 The standard chart, seeded on first start.', 5.2),

  p('Twelve accounts, deliberately few. A pharmacy\u2019s books are simple, '
    + 'and a chart with sixty accounts in it on day one is a chart where '
    + 'everything ends up in Sundries because nobody can find the right one. '
    + 'Accounts can be added and renamed; the twelve PharmaX posts to itself '
    + 'cannot be taken out of use, because the next sale of the day would '
    + 'fail at a till a long way from where the mistake was made.'),

  callout('PASS',
    'All twelve accounts present and correctly typed after the first launch '
    + 'against the shared database.', '1F7A3D'),
);

children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h2('10.3 The trial balance'),

  (steps(), label('Steps')),
  step(1, 'Accounts \u2192 Trial Balance.'),
  step(2, 'Check the totals line.'),
  step(3, 'Select an account and click Show Postings.'),

  label('Expected'),
  bullet('Debits equal credits, and the line says "in balance".'),
  bullet('Each account nets to one column. An account debited a thousand '
       + 'times and credited nine hundred owes a hundred, and that is what '
       + 'is printed.'),
  bullet('Show Postings lists every journal line behind the figure.'),

  ...shot('08-trial-balance.png',
    'Figure 12 \u2014 The trial balance after the collection in section 8. '
    + 'Debits $30.00, credits $30.00, in balance.', 5.6),

  p('The three figures on it are the sale from section 8, posted '
    + 'automatically: Cash on Hand debited $30.00, Sales credited $26.09 and '
    + 'VAT Control credited $3.91. Nobody typed any of them.'),

  callout('PASS',
    'The sale posted itself and the trial balance balances. Verified in the '
    + 'database as well as on screen.', '1F7A3D'),

  p('', { after: 140 }),

  callout('Why it is worth printing',
    'It should be impossible for a trial balance to be out: every journal is '
    + 'refused at posting unless it balances. That is exactly why the line is '
    + 'shown. If it ever says OUT OF BALANCE, something has reached the '
    + 'tables without going through a journal, and whoever is looking at it '
    + 'needs to know before they act on the figures.', ACCENT),

  h2('10.4 A hand-written journal'),

  p('The entry PharmaX cannot make for itself: month-end stock, an opening '
    + 'balance, a correction, the accountant\u2019s year-end adjustments.'),

  (steps(), label('Steps')),
  step(1, 'Accounts \u2192 Journal Entry.'),
  step(2, 'Type a reason.'),
  step(3, 'Add lines, choosing an account and either a debit or a credit.'),
  step(4, 'Post it.'),

  label('Expected'),
  bullet('The running totals update as you type, and say how far out the '
       + 'journal is.'),
  bullet('Post Journal is unavailable until debits equal credits. That is '
       + 'kinder than letting it be pressed and refused.'),
  bullet('An account that does not exist, or is no longer in use, is refused '
       + 'by name rather than by a foreign-key error.'),

  ...shot('10-journal.png',
    'Figure 13 \u2014 An empty journal. Out by $0.00, and Post unavailable.',
    5.4),

  callout('PARTIAL',
    'The screen was opened and its balance guard confirmed: Post Journal is '
    + 'correctly disabled while the journal does not balance. No journal was '
    + 'actually posted through the screen during this walkthrough. Journal '
    + 'posting itself is covered by twelve unit tests, including the '
    + 'unbalanced, unknown-account and one-sided cases.', 'B26A00'),
);

// ---- 11. Assistant ---------------------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('11. Story seven \u2014 the assistant'),

  runs([{ t: 'Signed in as ' }, { t: 'test.assistant', mono: true, b: true },
        '. The narrowest role that can still serve a customer.']),

  label('Expected'),
  bullet('Sell, Collect, Returns, Products, Stock alerts and Till session '
       + 'are enabled.'),
  bullet('Dispense, Patients, Goods receipt and Trial balance are not.'),
  bullet('The discount limit is 5 per cent, so any discount above that needs '
       + 'a supervisor at the keyboard.'),

  ...shot('15-assistant-home.png',
    'Figure 14 \u2014 The assistant\u2019s home screen.'),

  callout('PASS',
    'Permissions render as granted. The assistant\u2019s grants are a '
    + 'superset of the cashier\u2019s by exactly one: operational reports.',
    '1F7A3D'),

  h2('11.1 Supervisor authorisation'),

  p('Worth testing because it is the one path where two people are at the '
    + 'keyboard at once.'),

  (steps(), label('Steps')),
  step(1, 'Sign in as the assistant and start a sale.'),
  step(2, 'Add a line and press F5 to discount it.'),
  step(3, 'Enter a discount above 5 per cent.'),
  step(4, 'When asked, have a manager sign in at the prompt.'),

  label('Expected'),
  bullet('PharmaX decides a supervisor is needed and asks for one. It never '
       + 'lets the operator decide.'),
  bullet('A supervisor who declines leaves the discount unapplied and says '
       + 'so.'),
  bullet('The authoriser is recorded on the line and in the audit, because a '
       + 'pattern of one supervisor authorising every discount is what a '
       + 'manager needs to be able to see.'),

  callout('NOT RUN',
    'Not driven during this walkthrough. Covered by unit tests on the '
    + 'presenter and by the discount rules in the domain.', 'B26A00'),
);

// ---- 12. Defects -----------------------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('12. Defects found'),

  p('Six things went wrong during this walkthrough. Four were fixed while '
    + 'writing it, one is raised and left open, and one turned out to be the '
    + 'test machine rather than PharmaX.'),

  h2('D1 \u2014 The till said "Cash sale" over the patient\u2019s name'),

  detail([
      ['Severity', 'Cosmetic'],
      ['Where', 'Point of sale, collection mode'],
      ['What happened', 'The customer line read "Cash sale" instead of the '
       + 'patient whose script was on the screen.'],
      ['Why', 'ClearSale runs after the patient has been set, and reset the '
       + 'customer unconditionally.'],
      ['Status', { t: 'Fixed', c: '1F7A3D' }],
  ]),

  p('', { after: 200 }),

  h2('D2 \u2014 The journal totals ran underneath the Close button'),

  detail([
      ['Severity', 'Cosmetic, but it hid a figure'],
      ['Where', 'Journal entry'],
      ['What happened', 'The credits figure was cut in half by the Close '
       + 'button sitting on top of it.'],
      ['Why', 'A label docked to the top of a panel whose buttons are '
       + 'absolutely positioned spans the full width, underneath them.'],
      ['Status', { t: 'Fixed \u2014 the totals now have a row of their own',
       c: '1F7A3D' }],
  ]),

  p('', { after: 200 }),

  h2('D3 \u2014 A cashier can reach purchase orders and stocktake'),

  detail([
      ['Severity', 'Minor, but it is a control'],
      ['Where', 'Home screen and Inventory menu'],
      ['What happened', 'Both tiles are enabled for a cashier.'],
      ['Why', 'Both are gated on "read products", which every role holds. '
       + 'They should be gated on the permissions that actually govern them '
       + '- raise a purchase order, and post a stocktake.'],
      ['Status', { t: 'Open \u2014 raised, not fixed', c: 'B26A00' }],
  ]),

  p('', { after: 200 }),

  h2('D4 \u2014 Printing a receipt with no printer blocks the till'),

  detail([
      ['Severity', 'Environmental'],
      ['Where', 'Point of sale, after finalising'],
      ['What happened', 'Windows opened a "Save Print Output As" dialog, '
       + 'which blocked the till until it was dismissed.'],
      ['Why', 'No receipt printer is installed on the test machine. PharmaX '
       + 'prints after finalising, which is the correct order: the other way '
       + 'round leaves a printed receipt for a sale that never committed.'],
      ['Status', { t: 'Not a defect \u2014 install a printer, or set one as '
       + 'default', c: MUTED }],
  ]),

  p('', { after: 240 }),

  h2('D5 — Closing a collection reported the patient had paid'),

  detail([
      ['Severity', 'Serious — it said money had been taken'],
      ['Where', 'Point of sale and the collection queue'],
      ['What happened', 'Pressing F7 to close the till without paying '
       + 'returned the same result as paying, so the queue announced '
       + '"Tendai Ncube paid. Next patient."'],
      ['Why', 'Closing the till set the dialog result to OK, and the '
       + 'collection screen reads OK as "the patient paid".'],
      ['Status', { t: 'Fixed — only a finalised payment now '
       + 'reports success', c: '1F7A3D' }],
  ]),

  p('', { after: 200 }),

  h2('D6 — An abandoned collection locked the patient out'),

  detail([
      ['Severity', { t: 'Serious — the patient could not pay at '
       + 'all', c: PROBLEM }],
      ['Where', 'The collection queue'],
      ['What happened', 'After a collection was opened and closed '
       + 'without paying, every later attempt on that script failed '
       + 'with "Something went wrong". The log gives the real reason: '
       + 'Duplicate entry for key sale.IX_Sale_DispensingId.'],
      ['Why', 'Opening a collection creates and saves a draft sale '
       + 'holding the dispensing, and only one sale may hold it - that '
       + 'index is what stops two cashiers charging one patient twice. '
       + 'Every retry tried to create a second and was refused.'],
      ['Status', { t: 'Fixed — a second attempt now resumes '
       + 'the unfinished sale, so the cashier picks up where they left '
       + 'off', c: '1F7A3D' }],
  ]),

  p('', { after: 200 }),

  callout('Why this one matters most',
    'It needed no unusual input and no second workstation. One cashier, '
    + 'one interruption, and that patient could not be served by '
    + 'anybody for the rest of the day. Every automated test opened a '
    + 'collection and finished it; none of them walked away from one. '
    + 'That is the gap a walkthrough closes.', PROBLEM),

  p('', { after: 200 }),

  callout('One I got wrong',
    'Mid-walkthrough I recorded that the collection queue was not refreshing '
    + 'after a payment. It was: a stray keystroke of mine had gone into the '
    + 'Find box and the filter was hiding the rows. The queue behaved '
    + 'correctly throughout. Noted because a test script that quietly drops '
    + 'its own false alarms is not worth much.', ACCENT),
);

// ---- 13. Coverage ----------------------------------------------------
children.push(
  new Paragraph({ children: [new PageBreak()] }),
  h1('13. What this walkthrough did and did not cover'),

  h2('Confirmed end to end, in the running application'),

  table(
    ['Area', 'Evidence'],
    [
      ['Sign-in, all six roles', 'Signed in as each'],
      ['Permissions, all six roles', 'Tiles and menus compared per role'],
      ['Stock alert centre', 'Six seeded conditions, ordered and coloured'],
      ['Collection queue', 'Two scripts listed, one paid, queue updated'],
      ['Collection at the till', 'Priced, paid, closed, verified in the '
       + 'database'],
      ['Abandoning a collection', 'Opened, closed unpaid, reopened — found D5 and D6'],
      ['Automatic journal posting', 'Sale posted itself and balanced'],
      ['Chart of accounts', 'Twelve accounts seeded'],
      ['Trial balance', 'In balance, figures traced to the sale'],
      ['Shared database', 'One MySQL schema, migrations applied on launch'],
    ],
    [38, 62]),

  p('', { after: 240 }),

  h2('Described but not driven'),

  table(
    ['Area', 'Why it matters'],
    [
      ['Dispensing screen', 'The clinical-warning path is the most '
       + 'safety-critical screen in PharmaX'],
      ['Goods receipt screen', 'Where stock and creditors both move'],
      ['Manual journal posting', 'The balance guard was seen; a posted '
       + 'journal was not'],
      ['Supervisor authorisation', 'Two people at one keyboard'],
      ['Cash-up and till session', 'Including the variance posting'],
      ['Returns and credit notes', 'The mirror of a sale'],
      ['Reports and Excel export', 'Including the pharmacy logo'],
      ['Patient attachments', 'Scanned prescriptions'],
      ['First-run wizard', 'Never seen running'],
      ['The installer', 'Inno Setup is not installed; the script is '
       + 'unbuilt'],
    ],
    [38, 62]),

  p('', { after: 240 }),

  callout('The honest summary',
    'The features built most recently \u2014 the dispensing hand-off, the '
    + 'stock alerts and the accounting \u2014 are the ones this walkthrough '
    + 'proves, because they are what it was written to test. The older '
    + 'screens are covered by 341 automated tests but were not clicked '
    + 'through today. The first-run wizard and the installer have never been '
    + 'run at all.', 'B26A00'),

  h2('Automated test coverage'),

  table(
    ['Suite', 'Tests', 'What it proves'],
    [
      ['Unit', '263', 'Domain rules, services, presenters, layering'],
      ['Integration', '78', 'The same against a real database and real '
       + 'migrations'],
      [{ t: 'Total', b: true }, { t: '341', b: true },
       { t: 'All passing', b: true }],
    ],
    [22, 14, 64]),

  p('', { after: 200 }),

  p('Automated tests and this walkthrough answer different questions. The '
    + 'tests say the rules are right; the walkthrough says the screens are '
    + 'there and reachable by the right people. Neither substitutes for the '
    + 'other, which is why D1, D2, D5 and D6 all survived a green test run '
    + 'and were found by looking.'),
);

// ---- Numbering -------------------------------------------------------
const numbering = {
  config: [
    {
      reference: 'dots',
      levels: [
        {
          level: 0, format: LevelFormat.BULLET, text: '\u2022',
          alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 400, hanging: 220 } } },
        },
        {
          level: 1, format: LevelFormat.BULLET, text: '\u25E6',
          alignment: AlignmentType.LEFT,
          style: { paragraph: { indent: { left: 760, hanging: 220 } } },
        },
      ],
    },
    {
      reference: 'steps',
      levels: [{
        level: 0, format: LevelFormat.DECIMAL, text: '%1.',
        alignment: AlignmentType.START,
        style: { paragraph: { indent: { left: 400, hanging: 240 } } },
      }],
    },
  ],
};

const doc = new Document({
  numbering,
  styles: {
    default: {
      document: { run: { font: 'Calibri', size: 21, color: INK } },
    },
    paragraphStyles: [
      {
        id: 'Heading1', name: 'Heading 1', basedOn: 'Normal',
        next: 'Normal', quickFormat: true,
        run: { size: 32, bold: true, color: INK },
        paragraph: { spacing: { before: 360, after: 160 } },
      },
      {
        id: 'Heading2', name: 'Heading 2', basedOn: 'Normal',
        next: 'Normal', quickFormat: true,
        run: { size: 25, bold: true, color: ACCENT },
        paragraph: { spacing: { before: 280, after: 120 } },
      },
      {
        id: 'Heading3', name: 'Heading 3', basedOn: 'Normal',
        next: 'Normal', quickFormat: true,
        run: { size: 22, bold: true, color: INK },
        paragraph: { spacing: { before: 220, after: 100 } },
      },
    ],
  },
  sections: [{
    properties: {
      page: {
        size: { width: 12240, height: 15840 },
        margin: {
          top: convertInchesToTwip(1), bottom: convertInchesToTwip(1),
          left: convertInchesToTwip(1), right: convertInchesToTwip(1),
        },
      },
    },
    headers: {
      default: new Header({
        children: [new Paragraph({
          alignment: AlignmentType.RIGHT,
          children: [new TextRun({
            text: 'PharmaX \u2014 User Acceptance Test Script',
            size: 17, color: MUTED,
          })],
        })],
      }),
    },
    footers: {
      default: new Footer({
        children: [new Paragraph({
          alignment: AlignmentType.CENTER,
          children: [new TextRun({
            children: ['Page ', PageNumber.CURRENT, ' of ',
                       PageNumber.TOTAL_PAGES],
            size: 17, color: MUTED,
          })],
        })],
      }),
    },
    children,
  }],
});

Packer.toBuffer(doc).then(buf => {
  const out = process.argv[2] ||
    'C:\\Users\\mlilo\\Downloads\\PharmaX\\docs\\PharmaX-Test-Script.docx';

  fs.writeFileSync(out, buf);
  console.log('written: ' + out + '  (' +
    Math.round(buf.length / 1024) + ' KB)');
});
