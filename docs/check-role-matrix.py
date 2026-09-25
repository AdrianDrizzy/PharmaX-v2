"""Checks an access sweep (docs/walkthrough.ps1 manifest.csv) against the
agreed role matrix and prints every rule with PASS or FAIL.

    python docs/check-role-matrix.py evidence/e2e-retest/access/manifest.csv

"Can open" means the screen opened. A disabled menu entry (DENIED /
NOWINDOW) or a refusal message box (REFUSED) both mean "cannot". A screen
that opens only to refuse is itself a defect (a dead end) and is listed.
"""
import csv
import sys

MUST_ALLOW = {
    'test.admin': ['system-setup', 'control-variables', 'users', 'patients',
                   'script-maintenance', 'dispensing', 'goods-receipt'],
    'test.manager': ['users', 'patients', 'stocktake-capture',
                     'price-changes', 'trial-balance', 'day-end', 'reports'],
    'test.pharmacist': ['dispensing', 'labels', 'patients',
                        'script-maintenance', 'pos-sell'],
    'test.assistant': ['patients', 'script-maintenance', 'goods-receipt',
                       'dispensing', 'pos-sell', 'till-session'],
    'test.cashier': ['pos-sell', 'till-session', 'returns', 'collect',
                     'products'],
    'test.stock': ['goods-receipt', 'purchase-orders', 'products',
                   'stocktake-capture', 'stock-alerts'],
}

MUST_REFUSE = {
    'test.cashier': ['users', 'stocktake-new', 'stocktake-capture',
                     'price-changes', 'system-setup', 'control-variables',
                     'day-end', 'dispensing', 'patients',
                     'script-maintenance', 'labels', 'reports',
                     'goods-receipt', 'purchase-orders', 'stock-alerts',
                     'stock'],
    'test.assistant': ['users', 'price-changes', 'system-setup', 'day-end'],
    'test.pharmacist': ['users', 'price-changes', 'system-setup', 'day-end'],
    'test.stock': ['users', 'system-setup', 'day-end', 'dispensing'],
    'test.manager': ['system-setup', 'dispensing'],
}


def main(path):
    rows = list(csv.DictReader(open(path, encoding='utf-8-sig')))
    outcome = {(r['role'], r['slug']): r['outcome'] for r in rows}

    def can(role, slug):
        return outcome.get((role, slug), 'MISSING').startswith('OPENED')

    failures = 0
    total = 0

    for role, slugs in MUST_ALLOW.items():
        for slug in slugs:
            total += 1
            ok = can(role, slug)
            failures += not ok
            print(f"{'PASS' if ok else 'FAIL'}  {role:16} must be able to     "
                  f"{slug:20} ({outcome.get((role, slug), 'MISSING')})")

    for role, slugs in MUST_REFUSE.items():
        for slug in slugs:
            total += 1
            ok = not can(role, slug)
            failures += not ok
            print(f"{'PASS' if ok else 'FAIL'}  {role:16} must be kept out of "
                  f"{slug:20} ({outcome.get((role, slug), 'MISSING')})")

    # About is a message box by design.
    dead_ends = [r for r in rows
                 if r['outcome'] == 'REFUSED' and r['slug'] != 'about']
    stuck = [r for r in rows if r['outcome'] in ('MODAL-STUCK', 'ERROR')]

    print()
    print(f"{total} rules, {total - failures} held, {failures} broken")
    print(f"{len(rows)} screens tried; {len(dead_ends)} offered then refused; "
          f"{len(stuck)} stuck or errored")

    for r in dead_ends + stuck:
        print(f"  {r['outcome']:12} {r['role']:16} {r['slug']:20} {r['note']}")

    return 1 if failures or dead_ends or stuck else 0


if __name__ == '__main__':
    sys.exit(main(sys.argv[1]))
