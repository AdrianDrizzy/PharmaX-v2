# PHX-QA-010 — Coverage Report

**Run:** `R20260917-01` · **Build:** `8c1813e` · **Wave:** W0 · **Date:** 2026-09-17

> **Coverage is 0.0%.** Every row in the matrix is `BLOCKED` by **DEF-0001**
> (the solution does not compile). This report records the *shape* of the
> coverage contract that W1–W13 must satisfy, not results — there are none.

## Contract size

| Row class | Count | Source |
|---|---:|---|
| Defined requirements | 211 | requirement tables in PHX-SRS-001 §4–§17 |
| Business rules `BR-*` | 44 | §6.12, §7.12, §8.10, §9.8, §10.6, §13.7 |
| Appendix B parity reports | 48 | Appendix B (one coverage row each) |
| Cited but undefined | 2 | OQ-001 — `SEC-010`, `ADM-004` |
| **Total RTM rows** | **305** | |

**Minimum cases planned: 1780.** This is the §E floor — one case per applicable
axis. The true figure will be materially higher, because §E requires *at least
3* boundary cases per requirement and the axis-6 permission matrix multiplies
by the 6 roles in §F. A realistic projection is 4,000–6,000 cases.

> **Note on `BR-*`.** The QA prompt's §D table does not enumerate the 44
> business rules, but Appendix C lists them as a requirement family and they
> state the prohibitions that §E axis 5 exists to test (e.g. BR-POS-*, the
> expired-batch and scheduled-item rules). They are included as first-class
> RTM rows. Excluding them would have left the negative-path axis with no
> traceable owner.

## Coverage by module

| Module | RTM rows | Wave(s) | Planned | Executed | Passed | Failed | Blocked | Coverage |
|---|---:|---|---:|---:|---:|---:|---:|---:|
| Reports and Analytics (Appendix B parity) | 48 | W8 | 336 | 0 | 0 | 0 | 336 | 0.0% |
| Point of Sale | 23 | W2 | 129 | 0 | 0 | 0 | 129 | 0.0% |
| Reports and Analytics | 23 | W8 | 114 | 0 | 0 | 0 | 114 | 0.0% |
| Security and Audit | 21 | W1 | 127 | 0 | 0 | 0 | 127 | 0.0% |
| Dispensing | 17 | W4 | 93 | 0 | 0 | 0 | 93 | 0.0% |
| Migration and Cutover | 17 | W12 | 78 | 0 | 0 | 0 | 78 | 0.0% |
| Network, Sync, Outbox | 15 | W9 | 87 | 0 | 0 | 0 | 87 | 0.0% |
| Non-Functional | 14 | W10 | 93 | 0 | 0 | 0 | 93 | 0.0% |
| Inventory | 13 | W5 | 63 | 0 | 0 | 0 | 63 | 0.0% |
| Prescription and Claim Mgmt | 11 | W4 | 57 | 0 | 0 | 0 | 57 | 0.0% |
| Point of Sale (business rule) | 10 | W2 | 66 | 0 | 0 | 0 | 66 | 0.0% |
| Dispensing (business rule) | 10 | W4 | 67 | 0 | 0 | 0 | 67 | 0.0% |
| Supplier and Creditor | 10 | W6 | 58 | 0 | 0 | 0 | 58 | 0.0% |
| Patient and Customer | 10 | W4 | 52 | 0 | 0 | 0 | 52 | 0.0% |
| Batch and Expiry | 9 | W5 | 50 | 0 | 0 | 0 | 50 | 0.0% |
| Branch and Resilience | 9 | W9 | 55 | 0 | 0 | 0 | 55 | 0.0% |
| Bookkeeping, VAT, Accounting | 9 | W7 | 46 | 0 | 0 | 0 | 46 | 0.0% |
| Inventory (business rule) | 8 | W5 | 46 | 0 | 0 | 0 | 46 | 0.0% |
| Stocktake | 7 | W5 | 42 | 0 | 0 | 0 | 42 | 0.0% |
| Batch and Expiry (business rule) | 6 | W5 | 38 | 0 | 0 | 0 | 38 | 0.0% |
| Administration | 5 | W1 | 26 | 0 | 0 | 0 | 26 | 0.0% |
| Prescription and Claim Mgmt (business rule) | 5 | W4 | 27 | 0 | 0 | 0 | 27 | 0.0% |
| Branch and Resilience (business rule) | 5 | W9 | 30 | 0 | 0 | 0 | 30 | 0.0% |
| **TOTAL** | **305** | W1–W13 | **1780** | **0** | **0** | **0** | **1780** | **0.0%** |

## Named gaps

§D: *"A requirement with zero executed cases is a failure of the test run, not
an absence of risk."* All 305 rows have zero executed cases. The gaps are named
here rather than left implicit:

### Gap 1 — total, and upstream of everything else

**DEF-0001.** No build, therefore no application, therefore no case. This is
not a coverage gap that can be narrowed by more testing effort; it is a
precondition failure. Nothing below can start until it clears.

### Gap 2 — coverage that will remain blocked after DEF-0001 is fixed

| Blocked scope | Rows affected | Blocker |
|---|---:|---|
| Audit conformance (§E axis 15, **every case**) | all 305 | DEF-0003 — no `frmAuditQuery`; third assertion layer reachable only by direct DB query |
| W1 role × permission grid | SEC rows | DEF-0003 — no `frmRolePermissions` |
| TC-09 batch recall | BAT + STK rows | DEF-0003 — no `frmRecall` |
| W6 three-way match | SUP rows | DEF-0003 — no `frmInvoiceMatch` |
| TC-14 period close | ACC rows | DEF-0003 — no `frmPeriodClose` |
| W9 outbox/reconciliation visibility | BRN + NET rows | DEF-0003 — no `frmSyncMonitor` |
| Control-variable edit audit (33 variables) | ADM rows | DEF-0003 — `frmControlVariables` is read-only |
| Appendix B parity comparison | 48 report rows | OQ-003 — no incumbent output supplied |
| `SEC-010`, `ADM-004` | 2 | OQ-001 — no requirement text to test against |
| Deep UIA assertion on ~47% of controls | all forms | DEF-0002 |

### Gap 3 — not yet scoped at all

Appendix B covers **point-of-sale reports only**. The dispensary, stock and
stocktake report catalogues are spec open item 12 and have not been
enumerated. Parity coverage for those modules cannot be measured, or even
sized, until they are. See OQ-003.

## Depth-axis assignment method

§E requires `depth_axes_required` be recorded per requirement with a stated
reason for any exclusion. Axes were assigned by **keyword rule over the
requirement text**, so the assignment is reproducible and auditable rather
than a judgement made 305 times. Every row receives axes 1 (happy), 11
(integrity), 14 (UI) and 15 (audit) unconditionally; the remaining 11 axes
are added where the requirement text matches their trigger (e.g. axis 5
negative on *shall not / never / prevent / prohibit / refuse*; axis 7
escalation on *escalate / supervisor / override / variance*).

Every excluded axis currently carries the marker
`PENDING-WAVE-JUSTIFICATION` with the excluded axes named. **This is
deliberately not a completed justification.** §E requires the *reason* for
exclusion, and a defensible reason can only be written against observed
behaviour. Each wave must replace the marker with a real reason before its
rows may reach `COVERED`. A row still carrying the marker at W13 is an
incomplete record, not a covered requirement.
