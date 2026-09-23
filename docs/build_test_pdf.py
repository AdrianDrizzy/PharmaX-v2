"""Builds the PharmaX acceptance-test PDF from a walkthrough manifest.

    python docs/build_test_pdf.py evidence/walkthrough docs/PharmaX-Acceptance-Tests.pdf

Every screenshot in the document was captured from the running
application during the walkthrough. Nothing is illustrative: a use case
with no screenshot is reported as not evidenced rather than described as
though it had been seen.
"""
import csv, os, sys, datetime, subprocess
from reportlab.lib.pagesizes import A4
from reportlab.lib.units import mm
from reportlab.lib import colors
from reportlab.lib.styles import ParagraphStyle, getSampleStyleSheet
from reportlab.lib.enums import TA_LEFT
from reportlab.platypus import (BaseDocTemplate, PageTemplate, Frame, Paragraph,
                                Spacer, Image, Table, TableStyle, PageBreak,
                                KeepTogether, NextPageTemplate)
from reportlab.platypus.tableofcontents import TableOfContents
from reportlab.pdfgen import canvas as pdfcanvas
from PIL import Image as PILImage

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from stories import STORIES, ROLE_LABEL, MUST_REFUSE, MUST_ALLOW

OUTDIR = sys.argv[1]
TARGET = sys.argv[2]
SHOTS = os.path.join(OUTDIR, 'screenshots')
MANIFEST = os.path.join(OUTDIR, 'manifest.csv')

INK = colors.HexColor('#1A1A1A')
MUTED = colors.HexColor('#5A626E')
ACCENT = colors.HexColor('#005FB8')
GOOD = colors.HexColor('#186A3B')
WARN = colors.HexColor('#8A6100')
BAD = colors.HexColor('#A82020')
BAND = colors.HexColor('#EFF2F6')
RULE = colors.HexColor('#D4D9E0')

PAGE_W, PAGE_H = A4
MARGIN = 18 * mm
CONTENT_W = PAGE_W - 2 * MARGIN

ss = getSampleStyleSheet()


def st(name, **kw):
    base = dict(fontName='Helvetica', fontSize=9.5, leading=13.5,
                textColor=INK, alignment=TA_LEFT)
    base.update(kw)
    return ParagraphStyle(name, **base)


S = {
    'title':   st('title', fontName='Helvetica-Bold', fontSize=26, leading=30),
    'sub':     st('sub', fontSize=12, leading=17, textColor=MUTED),
    'h1':      st('h1', fontName='Helvetica-Bold', fontSize=17, leading=21,
                  spaceBefore=16, spaceAfter=8),
    'h2':      st('h2', fontName='Helvetica-Bold', fontSize=12.5, leading=16,
                  spaceBefore=13, spaceAfter=5),
    'h3':      st('h3', fontName='Helvetica-Bold', fontSize=10.5, leading=14,
                  spaceBefore=9, spaceAfter=3),
    'body':    st('body', spaceAfter=6),
    'small':   st('small', fontSize=8.5, leading=12, textColor=MUTED),
    'cap':     st('cap', fontSize=8, leading=11, textColor=MUTED,
                  spaceBefore=3, spaceAfter=10),
    'story':   st('story', fontSize=10.5, leading=15.5),
    'cell':    st('cell', fontSize=8.5, leading=11.5),
    'cellb':   st('cellb', fontSize=8.5, leading=11.5,
                  fontName='Helvetica-Bold'),
    'mono':    st('mono', fontName='Courier', fontSize=8, leading=11),
}


def P(text, style='body'):
    return Paragraph(text, S[style])


class Doc(BaseDocTemplate):
    def __init__(self, path, **kw):
        BaseDocTemplate.__init__(self, path, pagesize=A4,
                                 leftMargin=MARGIN, rightMargin=MARGIN,
                                 topMargin=MARGIN, bottomMargin=MARGIN + 6 * mm,
                                 title='PharmaX Acceptance Test Report',
                                 author='PharmaX Verification Agent', **kw)
        frame = Frame(MARGIN, MARGIN + 6 * mm, CONTENT_W,
                      PAGE_H - 2 * MARGIN - 6 * mm, id='f')
        self.addPageTemplates([
            PageTemplate(id='cover', frames=[frame], onPage=self._blank),
            PageTemplate(id='body', frames=[frame], onPage=self._chrome)])

    def _blank(self, c, d):
        pass

    # Feeds the contents page. A run of this size is 150 pages or so, and
    # a reader who cannot find one role's section will not read any of it.
    def afterFlowable(self, flowable):
        if not isinstance(flowable, Paragraph):
            return
        name = flowable.style.name
        if name == 'h1':
            self.notify('TOCEntry', (0, flowable.getPlainText(), self.page))
        elif name == 'h2':
            self.notify('TOCEntry', (1, flowable.getPlainText(), self.page))

    def _chrome(self, c, d):
        c.saveState()
        c.setStrokeColor(RULE)
        c.setLineWidth(0.5)
        c.line(MARGIN, PAGE_H - MARGIN + 3 * mm, PAGE_W - MARGIN,
               PAGE_H - MARGIN + 3 * mm)
        c.setFont('Helvetica', 7.5)
        c.setFillColor(MUTED)
        c.drawString(MARGIN, PAGE_H - MARGIN + 5 * mm,
                     'PharmaX — Acceptance Test Report')
        c.drawRightString(PAGE_W - MARGIN, PAGE_H - MARGIN + 5 * mm, RUN_ID)
        c.line(MARGIN, MARGIN + 4 * mm, PAGE_W - MARGIN, MARGIN + 4 * mm)
        c.drawCentredString(PAGE_W / 2, MARGIN - 1 * mm, 'Page %d' % d.page)
        c.restoreState()


def shot(filename, caption, max_w=CONTENT_W, max_h=150 * mm):
    """A screenshot scaled to fit, with its filename recorded beneath it."""
    path = os.path.join(SHOTS, filename)
    if not os.path.exists(path):
        return [P('<i>Screenshot missing: %s</i>' % filename, 'small')]
    with PILImage.open(path) as im:
        w, h = im.size
    scale = min(max_w / w, max_h / h, 1.0)
    img = Image(path, width=w * scale, height=h * scale)
    img.hAlign = 'LEFT'
    return [img, P('%s &nbsp;·&nbsp; <font face="Courier">%s</font>'
                   % (caption, filename), 'cap')]


def hexs(color):
    """reportlab wants '#rrggbb'; Color.hexval() gives '0xrrggbb'."""
    return '#' + color.hexval()[2:]


def badge(text, color):
    t = Table([[Paragraph('<font color="%s"><b>%s</b></font>'
                          % (hexs(color), text), S['cell'])]],
              colWidths=[38 * mm])
    t.setStyle(TableStyle([
        ('BOX', (0, 0), (-1, -1), 0.5, color),
        ('BACKGROUND', (0, 0), (-1, -1), colors.Color(
            color.red, color.green, color.blue, 0.07)),
        ('LEFTPADDING', (0, 0), (-1, -1), 5),
        ('TOPPADDING', (0, 0), (-1, -1), 3),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 3)]))
    t.hAlign = 'LEFT'
    return t


def kv_table(rows, widths=(45 * mm, None)):
    w = [widths[0], CONTENT_W - widths[0]]
    data = [[P('<b>%s</b>' % k, 'cell'), P(v, 'cell')] for k, v in rows]
    t = Table(data, colWidths=w)
    t.setStyle(TableStyle([
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('LINEBELOW', (0, 0), (-1, -2), 0.25, RULE),
        ('TOPPADDING', (0, 0), (-1, -1), 4),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 4),
        ('LEFTPADDING', (0, 0), (-1, -1), 0)]))
    return t


# ---------------------------------------------------------------- data
rows = []
if os.path.exists(MANIFEST):
    with open(MANIFEST, encoding='utf-8-sig') as f:
        rows = list(csv.DictReader(f))

by_role = {}
for r in rows:
    by_role.setdefault(r['role'], []).append(r)

ROLE_ORDER = ['test.admin', 'test.manager', 'test.pharmacist',
              'test.assistant', 'test.cashier', 'test.stock']
roles = [r for r in ROLE_ORDER if r in by_role] + \
        [r for r in by_role if r not in ROLE_ORDER]


def git(*args):
    try:
        return subprocess.check_output(['git'] + list(args),
                                       cwd=os.path.dirname(os.path.dirname(
                                           os.path.abspath(__file__))),
                                       stderr=subprocess.DEVNULL).decode().strip()
    except Exception:
        return 'unknown'


SHA = git('rev-parse', '--short', 'HEAD')
BRANCH = git('rev-parse', '--abbrev-ref', 'HEAD')
TODAY = datetime.date.today().isoformat()
RUN_ID = os.environ.get('PHX_RUN_ID', 'R%s-02' % TODAY.replace('-', ''))

OUTCOME_STYLE = {
    'OPENED':      ('Works', GOOD),
    # An OK-only message box, with no form behind it. Whether that is a
    # refusal or simply a notice depends on what it said, so the wording
    # is neutral and the quoted text decides it.
    'REFUSED':     ('Showed a message', WARN),
    'ASKED':       ('Asked before acting', GOOD),
    'DENIED':      ('Not offered to this role', WARN),
    'NOWINDOW':    ('No screen appeared', BAD),
    'NOSHOT':      ('Opened, capture failed', WARN),
    'MODAL-STUCK': ('Opened, blocked automation', BAD),
    'ERROR':       ('Could not be reached', BAD),
}

story = []
A = story.append

# ---------------------------------------------------------------- cover
A(Spacer(1, 42 * mm))
A(P('PharmaX', 'title'))
A(P('Acceptance Test Report', 'title'))
A(Spacer(1, 6 * mm))
A(P('Every use case, for every role, driven through the running '
    'application and evidenced with a screenshot taken at the moment '
    'it ran.', 'sub'))
A(Spacer(1, 14 * mm))
A(kv_table([
    ('Run', RUN_ID),
    ('Build', '%s (branch %s)' % (SHA, BRANCH)),
    ('Date', TODAY),
    ('Roles exercised', '%d' % len(roles)),
    ('Use cases attempted', '%d' % len(rows)),
    ('Screenshots captured', '%d' % len(
        [r for r in rows if r.get('file')])),
    ('Method', 'UI Automation patterns and PrintWindow capture; no '
               'simulated keystrokes and no window fronting'),
]))
A(Spacer(1, 10 * mm))
A(P('This document is generated from <font face="Courier">manifest.csv</font>, '
    'written by the walkthrough as it ran. Outcomes are recorded as '
    'observed. Where a screen did not open, that is stated rather than '
    'omitted.', 'small'))
A(NextPageTemplate('body'))
A(PageBreak())

# ------------------------------------------------------------- contents
toc = TableOfContents()
toc.levelStyles = [
    ParagraphStyle('toc0', fontName='Helvetica-Bold', fontSize=10,
                   leading=15, spaceBefore=6, textColor=INK),
    ParagraphStyle('toc1', fontName='Helvetica', fontSize=8.5, leading=12,
                   leftIndent=10, textColor=MUTED),
]
# Its own style so the contents heading does not list itself.
A(Paragraph('Contents', ParagraphStyle('h1plain', parent=S['h1'])))
A(toc)
A(PageBreak())

# ---------------------------------------------------------- how to read
A(P('1. How to read this document', 'h1'))
A(P('Each use case is written as a user story — who wants it, what they '
    'want, and why the pharmacy cares — followed by the acceptance checks '
    'a tester should be able to satisfy from the screenshot, and then the '
    'observed outcome.'))
A(P('Outcomes', 'h2'))
legend = [[P('<b>Outcome</b>', 'cell'), P('<b>Means</b>', 'cell')]]
legend += [
    [badge('Works', GOOD),
     P('The screen opened and was captured. The screenshot is the evidence.', 'cell')],
    [badge('Showed a message', WARN),
     P('No screen opened; the application put a message up instead. That may '
       'be a refusal enforcing a rule, or simply a notice — About reports the '
       'version this way. The wording is quoted under each case so the reader '
       'can tell which.', 'cell')],
    [badge('Asked before acting', GOOD),
     P('The application put a question before doing anything. The walkthrough '
       'always answers No or Cancel: it is here to look, not to commit.', 'cell')],
    [badge('Not offered to this role', WARN),
     P('The menu entry is absent for this role. That is the permission model '
       'working, and it is recorded rather than skipped.', 'cell')],
    [badge('No screen appeared', BAD),
     P('The entry was chosen and nothing happened — no screen, and no '
       'message either. Silence is the worst of the outcomes: the operator '
       'is given nothing to act on and cannot tell a refusal from a fault.',
       'cell')],
    [badge('Opened, blocked automation', BAD),
     P('The screen opened but held the UI thread so the run could not '
       'continue — a modal form. Noted for the test harness, not '
       'necessarily a product defect.', 'cell')],
]
t = Table(legend, colWidths=[42 * mm, CONTENT_W - 42 * mm])
t.setStyle(TableStyle([
    ('VALIGN', (0, 0), (-1, -1), 'TOP'),
    ('BACKGROUND', (0, 0), (-1, 0), BAND),
    ('LINEBELOW', (0, 0), (-1, -1), 0.25, RULE),
    ('TOPPADDING', (0, 0), (-1, -1), 5),
    ('BOTTOMPADDING', (0, 0), (-1, -1), 5),
    ('LEFTPADDING', (0, 0), (-1, -1), 3)]))
A(t)

A(P('How the evidence was captured', 'h2'))
A(P('Each screen is opened through UI Automation patterns and photographed '
    'with PrintWindow. Neither step fronts a window or sends a keystroke to '
    'the desktop, so the run cannot fight with whoever is at the machine, '
    'and cannot photograph whatever they switched to instead.'))
A(P('Two properties of the harness are worth stating, because they decide '
    'whether these pages can be trusted:'))
A(P('•&nbsp;&nbsp;<b>Nothing is committed.</b> Where the application asked a '
    'question, the answer given was always No or Cancel. The run opens '
    'screens; it does not post, save or sell.', 'body'))
A(P('•&nbsp;&nbsp;<b>A screen is identified by its window, not its title.</b> '
    'WinForms raises invisible helper windows alongside real ones, and an '
    'earlier version of this harness photographed one of those and reported '
    'it as a working screen. Captures are now accepted only from a window '
    'with a caption and a real size, and every image in this document is of '
    'a window that passed that check.', 'body'))

A(P('What this document does not claim', 'h2'))
A(P('A screenshot proves a screen rendered and accepted the state it was '
    'given. It does not by itself prove the figure on it is arithmetically '
    'right. Where a value matters — a VAT split, a journal that must '
    'balance, a co-payment — the acceptance check says so, and the '
    'automated test suite carries the arithmetic.'))

# ------------------------------------------------------------ accounts
A(P('2. The roles', 'h1'))
A(P('One seeded account per standard role, all sharing a known password so '
    'that the permission model can be walked without an administrator '
    'hand-building six users first.'))
data = [[P('<b>Account</b>', 'cell'), P('<b>Role</b>', 'cell'),
         P('<b>What the role is for</b>', 'cell')]]
for r in roles:
    label, purpose = ROLE_LABEL.get(r, (r, ''))
    data.append([P('<font face="Courier">%s</font>' % r, 'cell'),
                 P('<b>%s</b>' % label, 'cell'), P(purpose, 'cell')])
t = Table(data, colWidths=[34 * mm, 34 * mm, CONTENT_W - 68 * mm])
t.setStyle(TableStyle([
    ('VALIGN', (0, 0), (-1, -1), 'TOP'),
    ('BACKGROUND', (0, 0), (-1, 0), BAND),
    ('LINEBELOW', (0, 0), (-1, -1), 0.25, RULE),
    ('TOPPADDING', (0, 0), (-1, -1), 4),
    ('BOTTOMPADDING', (0, 0), (-1, -1), 4),
    ('LEFTPADDING', (0, 0), (-1, -1), 3)]))
A(t)
A(Spacer(1, 4 * mm))
A(P('Separation of duty is a control, not an oversight. The Administrator '
    'is deliberately barred from trading, and the Pharmacist is the only '
    'role that may authorise a dispensing. Where a role reaches something '
    'it should not, this report says so.', 'small'))

# -------------------------------------------------------------- stories
sec = 3
for role in roles:
    label, purpose = ROLE_LABEL.get(role, (role, ''))
    rrows = by_role[role]
    opened = [r for r in rrows if r['outcome'] == 'OPENED']

    A(PageBreak())
    A(P('%d. %s' % (sec, label), 'h1'))
    A(P('Signed in as <font face="Courier">%s</font>. %s'
        % (role, purpose[0].upper() + purpose[1:] + '.')))
    A(Spacer(1, 2 * mm))
    counts = {}
    for r in rrows:
        counts[r['outcome']] = counts.get(r['outcome'], 0) + 1
    summary = ', '.join('%d %s' % (v, OUTCOME_STYLE.get(k, (k, MUTED))[0].lower())
                        for k, v in sorted(counts.items(), key=lambda kv: -kv[1]))
    A(P('%d use cases attempted: %s.' % (len(rrows), summary), 'small'))

    n = 0
    for r in rrows:
        slug = r['slug']
        meta = STORIES.get(slug)
        if not meta:
            continue
        n += 1
        outcome = r['outcome']
        word, col = OUTCOME_STYLE.get(outcome, (outcome, MUTED))

        article = 'an' if label.lower()[0] in 'aeiou' else 'a'
        block = [P('%d.%d&nbsp;&nbsp;%s' % (sec, n, meta['title']), 'h2')]
        block.append(P(
            '<b>As %s</b> %s, <b>I want to</b> %s, <b>so that</b> %s.'
            % (article, label.lower(), meta['want'], meta['so_that']), 'story'))
        if meta.get('spec'):
            block.append(P('Specification: %s' % meta['spec'], 'small'))
        block.append(Spacer(1, 2 * mm))
        block.append(P('Acceptance checks', 'h3'))
        for c in meta['checks']:
            block.append(P('•&nbsp;&nbsp;%s' % c, 'body'))
        block.append(Spacer(1, 2 * mm))
        block.append(badge(word, col))
        note = (r.get('note') or '').strip()
        if note and note != 'role home screen':
            block.append(Spacer(1, 1.5 * mm))
            block.append(P('Observed: %s' % note, 'small'))
        block.append(Spacer(1, 3 * mm))
        A(KeepTogether(block))

        if r.get('file'):
            for el in shot(r['file'], '%s — %s' % (label, meta['title'])):
                A(el)
        else:
            A(P('<i>No screenshot: the screen did not open, so there is '
                'nothing to show.</i>', 'small'))
            A(Spacer(1, 4 * mm))
    sec += 1

# ------------------------------------------------------- permission grid
A(PageBreak())
A(P('%d. The permission matrix as observed' % sec, 'h1'))
A(P('One row per use case, one column per role, filled in from what each '
    'account could actually reach. This is the separation-of-duty evidence: '
    'a tick where a role should be refused is a finding.'))

slugs = []
for r in rows:
    if r['slug'] not in slugs and r['slug'] in STORIES:
        slugs.append(r['slug'])

look = {(r['role'], r['slug']): r['outcome'] for r in rows}
# Marks stay inside WinAnsi: the built-in Helvetica has no U+2713, and a
# tick outside the encoding renders as a black box.
MARK = {'OPENED': 'Y', 'REFUSED': '!', 'ASKED': '?', 'DENIED': '–',
        'NOWINDOW': 'x', 'NOSHOT': 'Y', 'MODAL-STUCK': 'Y', 'ERROR': 'x'}

SHORT = {'test.admin': 'Admin', 'test.manager': 'Manager',
         'test.pharmacist': 'Pharm.', 'test.assistant': 'Asst.',
         'test.cashier': 'Cashier', 'test.stock': 'Stock'}
head = [P('<b>Use case</b>', 'cell')] + \
       [P('<b>%s</b>' % SHORT.get(r, r.replace('test.', '')), 'cell')
        for r in roles]
REACHED = ('OPENED', 'NOSHOT', 'MODAL-STUCK', 'ASKED')
data = [head]
flags = []        # reached something §F says must be refused
blocked = []      # refused something §F says must be permitted
for i, slug in enumerate(slugs):
    line = [P(STORIES[slug]['title'], 'cell')]
    for j, role in enumerate(roles):
        oc = look.get((role, slug))
        m = MARK.get(oc, '·') if oc else '·'
        over = slug in MUST_REFUSE.get(role, []) and oc in REACHED
        under = (slug in MUST_ALLOW.get(role, []) and
                 oc in ('REFUSED', 'DENIED', 'NOWINDOW'))
        if over:
            flags.append((role, slug))
            line.append(P('<font color="#A82020"><b>%s</b></font>' % m, 'cell'))
        elif under:
            blocked.append((role, slug))
            line.append(P('<font color="#8A6100"><b>%s</b></font>' % m, 'cell'))
        else:
            line.append(P(m, 'cell'))
    data.append(line)

cw = [CONTENT_W - len(roles) * 17 * mm] + [17 * mm] * len(roles)
t = Table(data, colWidths=cw, repeatRows=1)
t.setStyle(TableStyle([
    ('VALIGN', (0, 0), (-1, -1), 'MIDDLE'),
    ('ALIGN', (1, 0), (-1, -1), 'CENTER'),
    ('BACKGROUND', (0, 0), (-1, 0), BAND),
    ('LINEBELOW', (0, 0), (-1, -1), 0.25, RULE),
    ('TOPPADDING', (0, 0), (-1, -1), 3),
    ('BOTTOMPADDING', (0, 0), (-1, -1), 3),
    ('LEFTPADDING', (0, 0), (-1, -1), 3)]))
A(t)
A(Spacer(1, 3 * mm))
A(P('<b>Y</b> reached &nbsp;·&nbsp; <b>!</b> showed a message instead '
    '&nbsp;·&nbsp; <b>?</b> asked a question first &nbsp;·&nbsp; '
    '<b>&#8211;</b> not offered to the role &nbsp;·&nbsp; '
    '<b>x</b> invoked but nothing opened<br/>'
    '<font color="#A82020"><b>red</b></font> = reached something §F says '
    'this role must be refused &nbsp;·&nbsp; '
    '<font color="#8A6100"><b>amber</b></font> = refused something §F says '
    'this role must be able to do', 'small'))

if flags:
    A(Spacer(1, 4 * mm))
    A(P('Separation-of-duty exceptions', 'h2'))
    A(P('The following roles opened a screen that PHX-QA-010 §F says they '
        'must be refused.'))
    for role, slug in flags:
        A(P('•&nbsp;&nbsp;<b>%s</b> reached <b>%s</b>'
            % (ROLE_LABEL.get(role, (role, ''))[0], STORIES[slug]['title']),
            'body'))
    A(Spacer(1, 2 * mm))
    A(P('<b>What this does and does not establish.</b> It establishes that '
        'the screen opened — the menu entry was offered and the form was '
        'shown. It does <i>not</i> establish that the role could complete '
        'the action, because the walkthrough opens screens rather than '
        'finishing transactions. The service layer may still refuse.'))
    A(P('That distinction cuts both ways, and §E axis 6 is explicit about '
        'it: a permission must be enforced at the service layer, and a '
        'hidden button is not a permission. So an open screen is not proof '
        'of a breach, and a hidden one would not have been proof of '
        'safety. Each line above needs a case that drives the action to '
        'completion and asserts both the refusal and the audit row the '
        'refusal must produce.'))

if blocked:
    A(Spacer(1, 4 * mm))
    A(P('Roles refused something they need', 'h2'))
    A(P('The other column of §F. A role that cannot reach part of its own '
        'job is as much a defect as one that reaches too far, and it is the '
        'kind that gets discovered at the counter.'))
    for role, slug in blocked:
        A(P('•&nbsp;&nbsp;<b>%s</b> was refused <b>%s</b>'
            % (ROLE_LABEL.get(role, (role, ''))[0], STORIES[slug]['title']),
            'body'))

A(Spacer(1, 4 * mm))
A(P('How permissions are enforced here', 'h2'))
A(P('Worth stating plainly, because it shapes every row above: PharmaX does '
    'not hide menu entries from roles that lack the permission. Every entry '
    'is offered to everybody, and the refusal happens when it is chosen — a '
    'modal message reading <i>"You do not have permission for this '
    'action."</i>'))
A(P('That is the better of the two designs for the reason §E axis 6 gives: '
    'a hidden button is not a permission, and a menu that hides entries '
    'invites the assumption that hiding is the control. Here the check is '
    'demonstrably made when the action is taken. The message is also plain '
    'language rather than an error code, which §18.5 asks for.'))
A(P('It does mean the column marked <b>&#8211;</b> (not offered) is empty '
    'in this run: no entry was withheld from any role. Refusal shows up as '
    '<b>!</b> instead.'))
sec += 1

# --------------------------------------------------------------- summary
A(PageBreak())
A(P('%d. What this run established' % sec, 'h1'))
tot = len(rows)
ok = len([r for r in rows if r['outcome'] == 'OPENED'])
ref = len([r for r in rows if r['outcome'] == 'REFUSED'])
ask = len([r for r in rows if r['outcome'] == 'ASKED'])
den = len([r for r in rows if r['outcome'] == 'DENIED'])
nw = len([r for r in rows if r['outcome'] == 'NOWINDOW'])
er = len([r for r in rows if r['outcome'] in ('ERROR', 'MODAL-STUCK', 'NOSHOT')])
A(kv_table([
    ('Use cases attempted', str(tot)),
    ('Opened and evidenced', '%d' % ok),
    ('Showed a message instead', '%d' % ref),
    ('Asked before acting', '%d' % ask),
    ('Not offered to the role', '%d' % den),
    ('Invoked, nothing opened', '%d' % nw),
    ('Could not be completed', '%d' % er),
]))

# Gaps recorded against individual use cases, gathered in one place so
# they are not buried in the stories.
gaps = []
for slug, meta in STORIES.items():
    for c in meta['checks']:
        if c.startswith('GAP:'):
            reached = any(r['slug'] == slug and r['outcome'] in
                          ('OPENED', 'NOSHOT') for r in rows)
            if reached:
                gaps.append((meta['title'], c[4:].strip()))
if gaps:
    A(Spacer(1, 5 * mm))
    A(P('Gaps found while walking the application', 'h2'))
    A(P('Each of these opened and worked as built. They are recorded because '
        'what was built is not what the specification describes.'))
    data = [[P('<b>Use case</b>', 'cell'), P('<b>Gap</b>', 'cell')]]
    for title, text in gaps:
        data.append([P(title, 'cell'), P(text, 'cell')])
    t = Table(data, colWidths=[46 * mm, CONTENT_W - 46 * mm])
    t.setStyle(TableStyle([
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('BACKGROUND', (0, 0), (-1, 0), BAND),
        ('LINEBELOW', (0, 0), (-1, -1), 0.25, RULE),
        ('TOPPADDING', (0, 0), (-1, -1), 4),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 4),
        ('LEFTPADDING', (0, 0), (-1, -1), 3)]))
    A(t)

A(Spacer(1, 5 * mm))
A(P('Not covered by this run', 'h2'))
A(P('This walkthrough opens every screen each role can reach and proves it '
    'renders with real seeded data. It does not drive a full transaction to '
    'completion on every screen — a sale through to a printed receipt, a '
    'dispensing through to a label, a stocktake through to a posted '
    'variance. Those paths are covered by the automated suite, and by the '
    'depth axes of PHX-QA-010 which remain to be run.'))
A(P('Nor does it exercise the offline, concurrency, interruption or '
    'performance axes. Those need a host plus two workstations, which this '
    'environment does not yet have.'))
sec += 1

# ------------------------------------------------------- automated tests
TESTS = os.path.join(OUTDIR, 'tests.json')
if os.path.exists(TESTS):
    import json
    # utf-8-sig: PowerShell's Out-File writes a BOM that json.load rejects.
    td = json.load(open(TESTS, encoding='utf-8-sig'))
    A(PageBreak())
    A(P('%d. The automated suite behind these screens' % sec, 'h1'))
    A(P('The walkthrough proves each screen opens and renders real data. It '
        'cannot prove a total is right. That is what the automated suite is '
        'for, and it runs against the same build.'))
    A(Spacer(1, 3 * mm))
    A(kv_table([
        ('Tests run', str(td['totals']['total'])),
        ('Passed', str(td['totals']['passed'])),
        ('Failed', str(td['totals']['failed'])),
        ('Suites', str(len(td['suites']))),
    ]))
    A(Spacer(1, 4 * mm))
    A(P('Worked examples the suite carries that a screenshot cannot: a '
        '115.00 cash sale splitting to 100.00 income and 15.00 VAT on the '
        'correct accounts; a journal batch refusing to post unless debits '
        'equal credits; each tender debited to its own account so the '
        'cash-up reconciles per tender rather than in one lump.', 'body'))
    A(Spacer(1, 3 * mm))
    data = [[P('<b>Area</b>', 'cell'), P('<b>Suite</b>', 'cell'),
             P('<b>Passed</b>', 'cell'), P('<b>Failed</b>', 'cell')]]
    for r in td['suites']:
        data.append([P(r['area'], 'cell'), P(r['suite'], 'cell'),
                     P(str(r['passed']), 'cell'),
                     P(str(r['failed']) if r['failed'] else '-', 'cell')])
    t = Table(data, colWidths=[52 * mm, CONTENT_W - 86 * mm, 17 * mm, 17 * mm],
              repeatRows=1)
    t.setStyle(TableStyle([
        ('VALIGN', (0, 0), (-1, -1), 'TOP'),
        ('ALIGN', (2, 0), (-1, -1), 'RIGHT'),
        ('BACKGROUND', (0, 0), (-1, 0), BAND),
        ('LINEBELOW', (0, 0), (-1, -1), 0.25, RULE),
        ('FONTSIZE', (0, 0), (-1, -1), 8),
        ('TOPPADDING', (0, 0), (-1, -1), 2.5),
        ('BOTTOMPADDING', (0, 0), (-1, -1), 2.5),
        ('LEFTPADDING', (0, 0), (-1, -1), 3)]))
    A(t)
    sec += 1

# --------------------------------------------------------------- appendix
A(PageBreak())
A(P('Appendix A — Full run manifest', 'h1'))
A(P('Every attempt, in the order it ran, exactly as the walkthrough '
    'recorded it.'))
data = [[P('<b>Role</b>', 'cell'), P('<b>Use case</b>', 'cell'),
         P('<b>Outcome</b>', 'cell'), P('<b>Window</b>', 'cell'),
         P('<b>Note</b>', 'cell')]]
for r in rows:
    data.append([
        P(r['role'].replace('test.', ''), 'cell'),
        P(r['slug'], 'cell'),
        P(r['outcome'], 'cell'),
        P(r.get('window', '') or '', 'cell'),
        P((r.get('note', '') or '')[:120], 'cell')])
t = Table(data, colWidths=[20 * mm, 34 * mm, 24 * mm, 38 * mm,
                           CONTENT_W - 116 * mm], repeatRows=1)
t.setStyle(TableStyle([
    ('VALIGN', (0, 0), (-1, -1), 'TOP'),
    ('BACKGROUND', (0, 0), (-1, 0), BAND),
    ('LINEBELOW', (0, 0), (-1, -1), 0.25, RULE),
    ('FONTSIZE', (0, 0), (-1, -1), 8),
    ('TOPPADDING', (0, 0), (-1, -1), 2.5),
    ('BOTTOMPADDING', (0, 0), (-1, -1), 2.5),
    ('LEFTPADDING', (0, 0), (-1, -1), 3)]))
A(t)

# multiBuild: the contents page needs a first pass to learn page numbers.
Doc(TARGET).multiBuild(story)
print('wrote %s' % TARGET)
print('  roles       : %d' % len(roles))
print('  use cases   : %d' % tot)
print('  screenshots : %d' % len([r for r in rows if r.get('file')]))
print('  opened      : %d   refused: %d   denied: %d   nothing: %d   failed: %d'
      % (ok, ref, den, nw, er))
