"""User stories for the PharmaX acceptance walkthrough.

One entry per use case. The wording is deliberately in the pharmacy's
language rather than the system's: a story that says "as a cashier I want
to press F3" describes the keyboard, not the job.

`want` / `so_that` complete the sentence "As a <role>, I want to ..., so
that ...". `checks` are what the tester should be able to see on the
screenshot, which is what makes the story acceptance criteria rather than
a description.
"""

STORIES = {
 'home': dict(
   title='Sign in and see the day',
   want='sign in and be shown only the work I am allowed to do',
   so_that='I do not have to learn which menu entries will refuse me',
   checks=['The status bar names me, my role, my branch and till.',
           'The tiles offered match my role.',
           'Connectivity state is visible without opening anything.']),

 'till-session': dict(
   title='Open the till and count it down',
   want='open a till session with a float and cash it up at the end',
   so_that='the drawer is reconciled to the day and a variance is owned by somebody',
   checks=['The session shows who opened it and when.',
           'An X-read can be taken without closing the session.',
           'Declaring cash produces a variance against what the system expected.'],
   spec='POS-017, §6.9'),

 'printer-setup': dict(
   title='Choose where receipts and labels print',
   want='point receipts and labels at the right printers for this till',
   so_that='a label does not come out of the receipt printer mid-queue',
   checks=['Receipt and label printers are chosen separately.',
           'The setting is per workstation, not per user.'],
   spec='§21.2'),

 'change-password': dict(
   title='Change my own password',
   want='change my password without asking an administrator',
   so_that='a shared or stale credential never becomes the normal way in',
   checks=['The current password is required.',
           'The new password is entered twice.'],
   spec='SEC-009'),

 'dispensing': dict(
   title='Dispense a script',
   want='turn a prescription into labelled medicine and a priced line',
   so_that='the patient leaves with the right medicine and the scheme is billed',
   checks=['The patient, their allergies and the script are on one screen.',
           'Issue is the pharmacist’s action and is recorded as theirs.',
           'A label can be reprinted without dispensing again.'],
   spec='DSP-001…017, §7'),

 'patients': dict(
   title='Find and maintain a patient',
   want='find a patient by name and keep their details and scheme current',
   so_that='the right person is billed and the clinical record is theirs',
   checks=['Search is by name as well as code.',
           'Scheme, membership and dependant code are visible together.'],
   spec='PAT-001…010, §12'),

 'script-maintenance': dict(
   title='Capture and correct a script',
   want='capture a script from paper and correct it before it is issued',
   so_that='a mis-keyed prescriber or quantity is fixed before medicine moves',
   checks=['Prescriber and prescriber number are captured.',
           'Items carry quantity, directions and substitution permission.'],
   spec='SCR-001…011, §10'),

 'labels': dict(
   title='Reprint a dispensing label',
   want='reprint a label that jammed or smudged',
   so_that='the patient is not sent home with an unreadable direction',
   checks=['The reprint is available without re-dispensing.',
           'The reprint is recorded as a reprint.'],
   spec='DSP-011, §7.6'),

 'tariffs': dict(
   title='Maintain medical aid tariffs',
   want='keep each scheme’s tariff current',
   so_that='a claim is rated at what the scheme actually pays',
   checks=['Tariffs are listed per scheme.',
           'A tariff can be deactivated rather than deleted.'],
   spec='§7.8'),

 'merge-patients': dict(
   title='Merge two records for one person',
   want='merge a duplicate patient record into the real one',
   so_that='history, account and clinical record stop being split in two',
   checks=['Both candidates are shown side by side before merging.',
           'The merge is a deliberate act, not a side effect of editing.'],
   spec='PAT-004, §12.3'),

 'quotation': dict(
   title='Quote a price without selling',
   want='give a customer a written price that does not move stock',
   so_that='a quote can be converted later without re-keying it',
   checks=['A quotation produces no stock movement.',
           'It can be converted to a sale without re-capture.'],
   spec='POS-010'),

 'schemes': dict(
   title='Maintain medical aid societies',
   want='keep the list of schemes the pharmacy deals with',
   so_that='a patient can be attached to a scheme that actually exists',
   checks=['Schemes can be added and deactivated.',
           'The list is shared across dispensing and the till.'],
   spec='§7.8'),

 'pos-sell': dict(
   title='Sell at the till',
   want='scan a basket, take payment and print a receipt without the mouse',
   so_that='a queue moves at the speed of the scanner',
   checks=['Scanning adds a line and updates the total.',
           'Split tender is possible in one sale.',
           'The whole sale can be completed from the keyboard.'],
   spec='POS-001…016, §6'),

 'collect': dict(
   title='Collect and pay for a script',
   want='find a dispensed script and take the patient’s share',
   so_that='the medicine is handed over only once it is paid for',
   checks=['The queue shows what is waiting to be collected.',
           'Only the patient’s portion is charged, not the scheme’s.'],
   spec='§7.8, POS-002'),

 'returns': dict(
   title='Take a return and credit it',
   want='return an item against its original sale',
   so_that='stock and the books are both put back, with an authoriser named',
   checks=['The original sale is found before anything is credited.',
           'The authoriser is recorded on the credit.'],
   spec='POS-007, POS-008'),

 'stock-alerts': dict(
   title='See what is short and what is expiring',
   want='see in one place what is below reorder and what is near expiry',
   so_that='I order before a gap and sell through before a write-off',
   checks=['Short stock and expiring batches are both shown.',
           'The horizon is the configured one, not a hard-coded guess.'],
   spec='INV-*, BAT-*, §9.5'),

 'departments': dict(
   title='Maintain departments',
   want='keep the department list products are grouped under',
   so_that='the sales-by-department reports mean something',
   checks=['Departments can be added and deactivated.'],
   spec='§8.3'),

 'suppliers': dict(
   title='Maintain suppliers',
   want='keep supplier terms and lead times current',
   so_that='the reorder calculation orders early enough to arrive in time',
   checks=['Terms and lead time are held per supplier.',
           'A supplier is deactivated rather than deleted.'],
   spec='SUP-001…010, §11'),

 'purchase-orders': dict(
   title='Order from a supplier',
   want='raise a purchase order, with a suggestion from what is short',
   so_that='I do not have to work out the order by eye',
   checks=['An order can be suggested from reorder levels.',
           'An order can be cancelled before receipt.'],
   spec='SUP-003, §11.3'),

 'goods-receipt': dict(
   title='Book a delivery in',
   want='receive a delivery against its order, batch and expiry by line',
   so_that='stock, cost and expiry are right from the moment it lands',
   checks=['Lines can be received against an order.',
           'Batch number and expiry are captured per line.'],
   spec='SUP-005, §8.6'),

 'manufacturers': dict(
   title='Maintain manufacturers',
   want='keep the manufacturer list products are attributed to',
   so_that='a recall can be scoped by manufacturer',
   checks=['Manufacturers can be added and deactivated.'],
   spec='§8.3'),

 'products': dict(
   title='Maintain a product and its price',
   want='maintain a product’s description, pack, cost, price and VAT',
   so_that='the till charges the right money and the books get the right VAT',
   checks=['Cost, retail and VAT rate are held together.',
           'Reorder level and schedule are on the same record.'],
   spec='INV-001…013, §8.3'),

 'price-changes': dict(
   title='See what prices have changed',
   want='see every selling-price change that has been made',
   so_that='a price move can be explained afterwards',
   checks=['Price changes are listed for a date range.',
           'GAP: this menu entry opens a REPORT. It routes to '
           'ShowReport("inv.price.changes"), not to a maintenance screen. '
           'The spec names frmPriceChange as a screen; it does not exist. '
           'Prices are changed on the product record instead.'],
   spec='POS-005, §8 — see DEF-0003'),

 'trial-balance': dict(
   title='See the books in balance',
   want='see a trial balance for a date',
   so_that='I can satisfy myself the day’s trading posted and balanced',
   checks=['Debits equal credits.',
           'Each line can be opened to the entries behind it.'],
   spec='ACC-001…009, §15'),

 'journal': dict(
   title='Post a journal by hand',
   want='post a correcting journal myself',
   so_that='an accountant’s adjustment does not need a developer',
   checks=['An unbalanced journal is refused.',
           'The account is chosen from the chart, not typed blind.'],
   spec='ACC-004, §15'),

 'patient-accounts': dict(
   title='Run the patient accounts',
   want='see what each account owes and how old it is',
   so_that='the pharmacy chases what it is owed before it ages out',
   checks=['Balance and ageing are visible per account.'],
   spec='PAT-007, §12.4'),

 'claims': dict(
   title='Bill and settle medical aid claims',
   want='see what each scheme owes, settle it and write off short payment',
   so_that='the largest debtor the pharmacy has is actually chased',
   checks=['Outstanding claims are listed per scheme.',
           'Short payment is written off explicitly rather than hidden.'],
   spec='§7.8, ACC-*'),

 'chart-of-accounts': dict(
   title='Maintain the chart of accounts',
   want='see and maintain the accounts the pharmacy posts to',
   so_that='the accountant’s chart and the system’s agree',
   checks=['Accounts carry a code, a name and a type.',
           'An account is deactivated rather than deleted.'],
   spec='§15.3'),

 'reports': dict(
   title='Run a report',
   want='choose a branch, a date range and a report, and preview it',
   so_that='I can answer a question without exporting to a spreadsheet first',
   checks=['Branch and date range are chosen before the report runs.',
           'The report can be previewed and exported.'],
   spec='RPT-001…, §14'),

 'stocktake-new': dict(
   title='Start a stocktake',
   want='open a stocktake for counting',
   so_that='the count is against a frozen list rather than a moving one',
   checks=['A new stocktake can be started and later continued.'],
   spec='STK-001…007, §8.7'),

 'stocktake-capture': dict(
   title='Capture counted quantities',
   want='capture what was actually on the shelf',
   so_that='the variance is between the count and the system, not guesswork',
   checks=['Counts are captured per product.',
           'A count can be cleared and recaptured before posting.'],
   spec='STK-003, §8.7'),

 'vat-codes': dict(
   title='See VAT by code',
   want='see what VAT has been charged under each code',
   so_that='the VAT return is built from the trading rather than by hand',
   checks=['VAT is summarised per code for a date range.',
           'GAP: this menu entry opens a REPORT. It routes to '
           'ShowReport("pos.vat.summary"). There is no screen on which a '
           'VAT code or rate can be maintained, so a rate change is not '
           'configuration — it is a code change.'],
   spec='ACC-002, §15 — gap raised in this run'),

 'users': dict(
   title='Maintain users and roles',
   want='create users, set their role and unlock them',
   so_that='every action is attributable to one person',
   checks=['A user is deactivated, never deleted.',
           'Roles are assigned from the standard set.',
           'A locked-out user can be unlocked.'],
   spec='SEC-001…009, §4.7'),

 'branches': dict(
   title='Maintain branches',
   want='keep the branch list current',
   so_that='stock and trading can be attributed to the right site',
   checks=['Branches can be added and deactivated.'],
   spec='BRN-*, §13'),

 'transaction-types': dict(
   title='See trading by transaction type',
   want='see the day broken down by transaction type',
   so_that='I can see the shape of the trading, not just its total',
   checks=['Trading is summarised per transaction type.',
           'GAP: this menu entry opens a REPORT, not a maintenance screen. '
           'Transaction types cannot be added or renamed from the '
           'application.'],
   spec='§15 — gap raised in this run'),

 'day-end': dict(
   title='Post the day to the books',
   want='turn a day’s trading into a journal batch and post it',
   so_that='the books keep themselves rather than waiting on somebody',
   checks=['The batch balances before it can be posted.',
           'Each tender is debited to its own account.',
           'VAT is split out of income rather than left inside it.'],
   spec='ACC-001…009, §15.4'),

 'system-setup': dict(
   title='Set the pharmacy up',
   want='set the trading name, VAT number and currency',
   so_that='receipts are compliant for this pharmacy and not a sample one',
   checks=['Fiscal particulars are configuration, not constants.'],
   spec='§6.10, ADM-*'),

 'system-console': dict(
   title='See what the system is doing',
   want='see the system’s own state without a developer',
   so_that='a support call starts from facts rather than description',
   checks=['Version, database and connectivity are visible.'],
   spec='§17.6'),

 'control-variables': dict(
   title='See the control variables',
   want='see every threshold and tolerance the system runs on',
   so_that='a limit can be explained without reading the source',
   checks=['Every threshold in Appendix E is listed.',
           'GAP: read-only. MainPresenter.cs:327 routes this to '
           'ShowSystemInfo(SystemInfoMode.ControlVariables), a grid. '
           'Appendix E requires each variable be "changed only by an '
           'audited edit in frmControlVariables"; that screen does not '
           'exist, so all 33 variables are unmaintainable and no edit '
           'audit can exist.'],
   spec='Appendix E — see DEF-0003'),

 'about': dict(
   title='Confirm the version',
   want='see exactly which build I am running',
   so_that='a defect report names a build rather than "the latest"',
   checks=['Version is visible to any user.'],
   spec='§21.3'),
}

ROLE_LABEL = {
 'test.admin':      ('Administrator', 'sets the system up and maintains users; deliberately barred from trading'),
 'test.manager':    ('Branch Manager', 'runs the branch: prices, authorisations, stocktake and the books'),
 'test.pharmacist': ('Pharmacist', 'the only role that may authorise a dispensing'),
 'test.assistant':  ('Pharmacy Assistant', 'captures patients and scripts and receives goods'),
 'test.cashier':    ('Cashier', 'works a till: sales, returns within limit, receipts'),
 'test.stock':      ('Stock Controller', 'orders, receives and counts stock'),
}

# Section F of PHX-QA-010 has two columns, and both are testable. This is
# the "must be able to" column: a role refused one of these cannot do its
# job, which is as much a defect as reaching something it should not.
MUST_ALLOW = {
 'test.admin':      ['system-setup', 'control-variables', 'users'],
 'test.manager':    ['users', 'patients', 'stocktake-capture', 'price-changes',
                     'trial-balance', 'day-end'],
 'test.pharmacist': ['dispensing', 'labels', 'patients', 'script-maintenance'],
 'test.assistant':  ['patients', 'script-maintenance', 'goods-receipt',
                     'pos-sell', 'till-session'],
 'test.cashier':    ['pos-sell', 'till-session', 'returns', 'collect'],
 'test.stock':      ['goods-receipt', 'purchase-orders', 'products',
                     'stocktake-capture', 'stock-alerts'],
}

# And the "must be refused" column. These are the separation-of-duty
# controls, and a role reaching one of them is a finding, not a feature.
MUST_REFUSE = {
 'test.admin':      ['pos-sell', 'collect', 'dispensing', 'returns',
                     'stocktake-capture', 'goods-receipt'],
 'test.cashier':    ['users', 'stocktake-capture', 'price-changes',
                     'system-setup', 'control-variables', 'day-end'],
 'test.assistant':  ['users', 'price-changes', 'system-setup', 'day-end'],
 'test.pharmacist': ['users', 'price-changes', 'system-setup', 'day-end'],
 'test.stock':      ['users', 'system-setup', 'day-end', 'dispensing'],
 'test.manager':    ['system-setup'],
}
