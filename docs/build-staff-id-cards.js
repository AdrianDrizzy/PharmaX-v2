// Builds the BethRapha staff ID cards as Word documents.
//
//   node docs/build-staff-id-cards.js
//
// Both files hold the same card at the real ID-card size, CR80 (85.6 x 54 mm):
//   BethRapha-Staff-ID-Card-CR80.docx  page = card, front on page 1, back on page 2, for a PVC card printer
//   BethRapha-Staff-ID-Cards-A4.docx   four cards on an A4 sheet, front beside back, to cut, fold and laminate
//
// Every name, number and address on the cards is a placeholder to type over in Word;
// the photo is a picture to swap with right-click > Change Picture.
const fs = require('fs');
const path = require('path');
const {
  Document, Packer, Paragraph, TextRun, Table, TableRow, TableCell, ImageRun, PageBreak,
  WidthType, ShadingType, BorderStyle, AlignmentType, VerticalAlign, HeightRule,
  TableLayoutType, TabStopType, LineRuleType, PageOrientation,
} = require('docx');

const ROOT = path.resolve(__dirname, '..');
const LOGO = fs.readFileSync(path.join(ROOT, 'evidence/e2e-admin/bethrapha-logo.png'));
const PHOTO = fs.readFileSync(path.join(__dirname, 'staff-id-photo-placeholder.png'));

const mm = v => Math.round(v * 1440 / 25.4);   // twips (DXA)
const px = v => Math.round(v * 96 / 25.4);     // ImageRun pixels

// CR80. On a card-sized page the card table stops 30 twips short of the page so
// the paragraph Word insists on after every table still fits on the same page.
const CARD_W = mm(85.6), CARD_H = mm(53.98), SPARE = 30;

// The logo's own navy and orange (PharmaX.Desktop/Mvp/Palette.cs).
const NAVY = '213886', ORANGE = 'F36A22', INK = '1F2937', GREY = '6B7280', WHITE = 'FFFFFF';
const CUT = 'BFBFBF';
const FONT = 'Arial';

const run = (text, o = {}) => new TextRun({
  text, font: FONT, size: Math.round((o.pt || 7) * 2),
  bold: o.bold, italics: o.italic, color: o.color || INK,
});

// Cells have no padding of their own (so the card edge lands exactly on the
// page or cut line); the paragraphs indent themselves instead.
const para = (children, o = {}) => new Paragraph({
  children: [].concat(children),
  alignment: o.align || AlignmentType.LEFT,
  indent: { left: o.left ?? mm(3), right: o.right ?? mm(3) },
  spacing: { before: o.before || 0, after: o.after || 0, line: 240, lineRule: LineRuleType.AUTO },
  tabStops: o.tabs,
  border: o.border,
});

const tiny = (children = []) => new Paragraph({
  children, spacing: { before: 0, after: 0, line: 20, lineRule: LineRuleType.EXACT },
});

const image = (data, wMm, hMm) => new ImageRun({
  type: 'png', data, transformation: { width: px(wMm), height: px(hMm) },
});

const NONE = { style: BorderStyle.NONE, size: 0, color: 'auto' };
const LINE = color => ({ style: BorderStyle.SINGLE, size: 2, color });   // 1/4 pt
const boxBorders = edge => ({
  top: edge, bottom: edge, left: edge, right: edge, insideHorizontal: NONE, insideVertical: NONE,
});
const ZERO = { top: 0, bottom: 0, left: 0, right: 0 };

const cell = (children, w, o = {}) => new TableCell({
  children: [].concat(children),
  width: { size: w, type: WidthType.DXA },
  columnSpan: o.span,
  verticalAlign: o.v || VerticalAlign.CENTER,
  shading: o.fill ? { fill: o.fill, type: ShadingType.CLEAR, color: 'auto' } : undefined,
});

const row = (h, cells) => new TableRow({
  height: { value: h, rule: HeightRule.EXACT }, cantSplit: true, children: cells,
});

const table = (grid, rows, border) => new Table({
  columnWidths: grid,
  width: { size: grid.reduce((a, b) => a + b, 0), type: WidthType.DXA },
  layout: TableLayoutType.FIXED,
  margins: ZERO,
  borders: boxBorders(border),
  rows,
});

const band = (h, fill, span, children = tiny()) => row(h, [cell(children, CARD_W, { span, fill })]);

// ---- front --------------------------------------------------------------

const logoH = 12.5, logoW = logoH * LOGO.readUInt32BE(16) / LOGO.readUInt32BE(20);   // PNG IHDR size

const field = (label, value) => para(
  [run(label, { pt: 5.5, color: GREY }), run('\t'), run(value, { pt: 7, bold: true })],
  { left: mm(2), tabs: [{ type: TabStopType.LEFT, position: mm(19) }], after: 20 },
);

function front(border, height) {
  const HEAD = mm(15), ACCENT = mm(1), FOOT = mm(6), BODY = height - HEAD - ACCENT - FOOT;
  const grid = [mm(25), mm(15), CARD_W - mm(40)];
  return table(grid, [
    row(HEAD, [
      cell(para(image(LOGO, logoW, logoH)), grid[0] + grid[1], { span: 2 }),
      cell(para(run('STAFF IDENTITY CARD', { pt: 8, bold: true, color: NAVY }), { align: AlignmentType.RIGHT }), grid[2]),
    ]),
    band(ACCENT, ORANGE, 3),
    row(BODY, [
      cell(para(image(PHOTO, 20, 25.7)), grid[0]),
      cell([
        para(run('FIRSTNAME M. SURNAME', { pt: 9, bold: true, color: NAVY }), { left: mm(2) }),
        para(run('PHARMACIST', { pt: 7, bold: true, color: ORANGE }), { left: mm(2), after: 100 }),
        field('Employee No', 'BR-0001'),
        field('National ID No', '00-000000 A 00'),
        field('PCZ Reg No', 'C 000000'),
        field('Expires', '31-12-2027'),
      ], grid[1] + grid[2], { span: 2 }),
    ]),
    band(FOOT, NAVY, 3, para(
      run('THE HOLDER IS AN EMPLOYEE OF BETHRAPHA PHARMACY', { pt: 5, bold: true, color: WHITE }),
      { align: AlignmentType.CENTER },
    )),
  ], border);
}

// ---- back ---------------------------------------------------------------

const centred = (children, o = {}) => para(children, { align: AlignmentType.CENTER, left: mm(4), right: mm(4), ...o });

const signature = caption => cell(
  centred(run(caption, { pt: 5, color: GREY }), { border: { top: LINE(INK) }, before: 0, after: 60 }),
  CARD_W / 2, { v: VerticalAlign.BOTTOM },
);

function back(border, height) {
  const TITLE = mm(11), ACCENT = mm(1), TERMS = mm(9.5), SIGN = mm(10), FOOT = mm(6);
  const RETURN = height - TITLE - ACCENT - TERMS - SIGN - FOOT;
  const half = Math.floor(CARD_W / 2);
  return table([half, CARD_W - half], [
    row(TITLE, [cell([
      centred(run('This card has been issued by', { pt: 6, color: GREY })),
      centred(run('BethRapha Pharmacy', { pt: 10, bold: true, color: NAVY })),
    ], CARD_W, { span: 2 })]),
    band(ACCENT, ORANGE, 2),
    row(TERMS, [cell(centred(run(
      'This card is the property of BethRapha Pharmacy and is not transferable. '
      + 'Wear it where it can be seen while on duty and return it when your employment ends.',
      { pt: 5.5 },
    )), CARD_W, { span: 2 })]),
    row(SIGN, [signature('Holder’s signature'), signature('Authorised signature')]),
    row(RETURN, [cell([
      centred(run('If found, please return to:', { pt: 6, bold: true })),
      centred(run('[Street address], [Suburb], [City]', { pt: 6 })),
      centred(run('Tel +263 [phone number]   Email: [email address]', { pt: 6 })),
    ], CARD_W, { span: 2 })]),
    band(FOOT, NAVY, 2, centred(run('“We treat and God heal”', { pt: 6.5, bold: true, italic: true, color: ORANGE }))),
  ], border);
}

// ---- documents ----------------------------------------------------------

const doc = (page, children) => new Document({
  creator: 'BethRapha Pharmacy',
  title: 'BethRapha staff ID cards',
  sections: [{ properties: { page }, children }],
});

// One card per page for a card printer: no cut lines, nothing in the margins.
const cr80 = doc(
  {
    size: { width: CARD_H, height: CARD_W, orientation: PageOrientation.LANDSCAPE },
    margin: { top: 0, bottom: 0, left: 0, right: 0, header: 0, footer: 0, gutter: 0 },
  },
  [front(NONE, CARD_H - SPARE), tiny([new PageBreak()]), back(NONE, CARD_H - SPARE), tiny()],
);

// A4: front and back share an edge, so a pair can be cut out as one strip and
// folded along the middle line, or cut into two and laminated back to back.
const A4_W = 11906, A4_H = 16838, SIDE = (A4_W - 2 * CARD_W) / 2;
const pair = () => table([CARD_W, CARD_W], [row(CARD_H + SPARE, [
  cell([front(LINE(CUT), CARD_H), tiny()], CARD_W, { v: VerticalAlign.TOP }),
  cell([back(LINE(CUT), CARD_H), tiny()], CARD_W, { v: VerticalAlign.TOP }),
])], NONE);
const gap = () => new Paragraph({ spacing: { before: 0, after: 0, line: mm(5), lineRule: LineRuleType.EXACT } });

const a4 = doc(
  {
    size: { width: A4_W, height: A4_H },
    margin: { top: mm(12), bottom: mm(10), left: SIDE, right: SIDE, header: 0, footer: 0, gutter: 0 },
  },
  [
    para(run(
      'Print at Actual size (100%), not "Fit to page". Cut along the grey lines, fold each pair along the middle line '
      + 'so the back sits behind the front, then laminate. Each card is 85.6 x 54 mm.',
      { pt: 7, color: GREY },
    ), { left: 0, right: 0, after: 120 }),
    pair(), gap(), pair(), gap(), pair(), gap(), pair(), tiny(),
  ],
);

(async () => {
  for (const [name, d] of [['BethRapha-Staff-ID-Card-CR80.docx', cr80], ['BethRapha-Staff-ID-Cards-A4.docx', a4]]) {
    fs.writeFileSync(path.join(__dirname, name), await Packer.toBuffer(d));
    console.log('wrote docs/' + name);
  }
})();
