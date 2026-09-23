# PharmaX — Verification Report

> ## ⚠️ SUPERSEDED IN PART — read this first
>
> This report records run **`R20260917-01`**, which found the build broken and
> could execute nothing. A later authorised run, **`R20260917-02`**, fixed the
> blocking defects. As of that run:
>
> | | R20260917-01 (this report) | R20260917-02 (current) |
> |---|---|---|
> | `dotnet build -c Release` | 6 errors | **0 errors** |
> | Tests | 0 of 407 ran | **407 of 407 pass** |
> | Controls with an `AutomationId` | 161 of 309 (52.1%) | **306 of 309 (99.0%)** |
> | DEF-0001 / DEF-0002 | OPEN | **FIXED** |
> | DEF-0004 / DEF-0005 | not yet found | **FIXED** |
> | DEF-0003 | OPEN | **still OPEN**, and partly confirmed against the running app |
>
> What remains accurate here: the coverage contract (305 RTM rows), the five
> open questions, and the statement that **no functional requirement has been
> tested at the depth Section E demands**. Coverage is still 0% against the RTM.
>
> See `run-log.md` for the remediation record, `defects/DEF-0004.md` and
> `defects/DEF-0005.md` for what the merge had also broken, and
> `docs/PharmaX-Acceptance-Tests.pdf` for the role-by-role walkthrough of the
> repaired build.

**Ref:** PHX-QA-010 · **Run:** `R20260917-01` · **Date:** 2026-09-17
**Status:** ⛔ **RUN TERMINATED AT WAVE W0, PREFLIGHT ITEM 1**

> This is an **honest partial run**, reported per §Q: *"an honest partial run is
> worth more than a complete-looking fabricated one."* One wave of fourteen was
> attempted. It did not pass. **No functional test case was executed against the
> application, because there is no application** — the solution does not compile
> on `main`.
>
> Nothing in this report is a `PASS`. No verdict was recorded that was not
> observed.

---

## 1. Build identity and environment

| Field | Value |
|---|---|
| Repository | `C:\Users\mlilo\source\repos\PharmaX` |
| Branch | `main` (working tree clean) |
| Commit under test | `8c1813e1df5d946e81c76047838d45da1cea7479` |
| Declared version | 1.0.0 / file version 1.0.0.0 |
| **Assembly version** | **unobtainable — no assembly is produced** |
| Host OS | Windows 11 Home Single Language 10.0.26200 |
| .NET SDK | **9.0.302 only** — solution targets `net8.0` (see OQ-005) |
| Automation MCP | `winforms-mcp` (UIA3) — connected, **never exercised** |
| Topology | Host + WS-01 + WS-02 — **never stood up** |
| Determinism controls | **none established** — clock, sequences, printer, device stubs all unset |
| Baseline screenshots | **zero** |

Preflight items 3, 4, 5, 6 and 8 were never reached. Any figure in this report
that would normally rest on a seeded database or a captured baseline is marked
`NOT REACHED` rather than estimated.

---

## 2. Coverage summary by module, with named gaps

**Coverage: 0.0%. 305 of 305 RTM rows `BLOCKED`. 0 cases executed of 1,780 minimum planned.**

The matrix itself was built successfully and is the one durable deliverable of
this run: [`coverage/rtm.csv`](coverage/rtm.csv), 305 rows.

| Row class | Count |
|---|---:|
| Defined requirements (parsed from PHX-SRS-001 requirement tables) | 211 |
| Business rules `BR-*` | 44 |
| Appendix B parity reports (one row each) | 48 |
| Cited but never defined (`SEC-010`, `ADM-004`) | 2 |
| **Total** | **305** |

Per-module breakdown: [`coverage/coverage-report.md`](coverage/coverage-report.md).

### Three corrections to the engagement's own scoping

Made under Authority rule 1 (the specification wins), and stated because they
change the denominator every later wave reports against:

1. **The 44 `BR-*` business rules are included as RTM rows.** The QA prompt's §D
   table omits them. Appendix C lists them as a requirement family, and they
   carry the prohibitions that §E axis 5 exists to test. Excluding them would
   leave the entire negative-path axis without a traceable owner.
2. **Appendix B contains 48 reports, not "~45".** The RTM uses 48. One of them
   (entry 24, *"Sales (By Department) — detail variant"*) is probably a capture
   artefact of the overlapping dropdown scroll the appendix itself describes —
   see OQ-003.
3. **`SCR-*` is *Prescription and Claim Management* (spec ch. 10), not "Screen /
   form behaviour"** as the QA prompt's §D table labels it. The count of 11 is
   right; the domain is not. Cases written against the prompt's reading would
   have tested the wrong module.

### Named gaps

| Gap | Scope | Cause |
|---|---|---|
| Everything | all 305 rows | **DEF-0001** — no build |
| Audit conformance (§E axis 15, specified for **every** case) | all 305 rows | **DEF-0003** — no `frmAuditQuery`; the third assertion layer of §L.6 is reachable only by direct DB query, never through the product |
| W1 role × permission grid | SEC | DEF-0003 — no `frmRolePermissions` |
| TC-09 batch recall | BAT, STK | DEF-0003 — no `frmRecall` |
| W6 three-way match | SUP | DEF-0003 — no `frmInvoiceMatch` |
| TC-14 period close | ACC | DEF-0003 — no `frmPeriodClose` |
| W9 outbox / reconciliation visibility | BRN, NET | DEF-0003 — no `frmSyncMonitor` |
| Control-variable edit audit (33 variables) | ADM | DEF-0003 — `frmControlVariables` is read-only |
| Parity comparison | 48 report rows | OQ-003 — no incumbent output supplied |
| Deep UIA assertion on ~47% of controls | all forms | DEF-0002 |
| Dispensary / stock / stocktake report parity | unscoped | OQ-003 — spec open item 12; catalogue not yet enumerated |

---

## 3. Defect register and go-live gate

| ID | Sev | Title | Status |
|---|:--:|---|---|
| **DEF-0001** | **1** | Solution does not compile on `main`; merge `37ac703` duplicated `JournalLineDto` / `JournalBatchDto` / `AccountingOptions` | OPEN |
| **DEF-0002** | **2** | 146 of 309 interactive controls will expose no `AutomationId` | OPEN |
| **DEF-0003** | **2** | Eleven spec-named screens have no implementation and no equivalent | OPEN |

### Go-live gate assessment (§20.3)

> **FAILED.** Severity 1 and Severity 2 both permit **zero** open defects at
> go-live. There is 1 open Severity 1 and 2 open Severity 2.

This assessment is made on a run with **0% functional coverage**. It is a floor,
not a verdict: the true defect count cannot be lower than this and is very
probably much higher, because 1,780 planned cases remain unexecuted. **No
inference of quality should be drawn from the short defect list.**

### DEF-0001 in brief — root cause proven, not inferred

Two branches each introduced a `JournalLineDto` / `JournalBatchDto` into
`PharmaX.Application.Finance`, **in different files** — `LedgerService.cs` on one
side, `DayEndPostingService.cs` on the other — with **different shapes** (one
carries `LineNo`, one does not). Different files meant no textual conflict, so
Git auto-merged both sides into one namespace. `AccountingOptions` was duplicated
outright in the same merge.

| Commit | `AccountingOptions` | Builds |
|---|:--:|---|
| `37ac703^1` = `3a33832` | 1 | ✅ clean |
| `37ac703^2` = `fe6d147` | 1 | ✅ clean |
| **`37ac703` (merge)** | **2** | ❌ **broken** |
| `8c1813e` (HEAD) | 2 | ❌ broken |

**Both parents compile. The merge resolution is the defect**, and PR #1 carried
it to `main` unchanged — so `main` has not built since that merge landed.

Not fixed by the agent, per Authority rule 3. The fix is a design call, not a
mechanical one: choosing between the two `JournalLineDto` shapes decides whether
a journal line is positionally identified, which ACC-001…009 and TC-14 depend on.

### DEF-0002 — the testability finding, and why it differs from the prediction

The anticipated failure was designer-generated control names (`textBox3`). **That
is not what is wrong.** These designers are hand-written and named well — only
3 controls in 561 carry a generated-style field name.

The actual defect is narrower: WinForms derives `AutomationId` from the control's
**`Name` property**, not from the C# field name. 146 interactive controls are
declared as `private Button btnClose = null!;` and never receive
`btnClose.Name = "btnClose";`, so they reach the UIA tree with an empty
`AutomationId`.

| | |
|---|---:|
| Interactive controls | 309 |
| Deep-automatable | 160 (51.8%) |
| **No `AutomationId`** | **146 (47.2%)** |

The casualties are overwhelmingly **commit buttons** — `btnSave`, `btnPost`,
`btnClose`, `btnCancel` — the exact controls every happy-path and every
refusal-path case must press. `frmSupplierEdit` cannot be driven *or committed*
by id at all.

This is mechanically fixable (one line per control, a pattern the solution
already applies 299 times elsewhere) and is best done **before** the first
functional wave, so W1–W13 are not authored twice.

**This figure is static-derived and pending live confirmation.** §C.7 specifies a
live UIA enumeration, which DEF-0001 made impossible. It must be re-taken against
the running app and must not be carried forward as an observed measurement.

---

## 4. NFR results

**All nine targets: `NOT_MEASURED`.** [`performance/nfr-results.csv`](performance/nfr-results.csv)

| Ref | Measure | Target | Median | p95 | n | Verdict |
|---|---|---|---|---|---:|---|
| NFR-001 | Scan to updated basket total | 250 ms | — | — | 0 | BLOCKED |
| NFR-002 | Five-line cash sale to printed receipt | 20 s | — | — | 0 | BLOCKED |
| NFR-003 | Product/patient search, first page | 500 ms | — | — | 0 | BLOCKED |
| NFR-004 | Cold start to usable till screen | 15 s | — | — | 0 | BLOCKED |
| NFR-005 | Day-range / month-range report | 5 s / 30 s | — | — | 0 | BLOCKED |
| NFR-006 | Failover when host disappears | 5 s | — | — | 0 | BLOCKED |
| NFR-007 | Label print to paper | 3 s | — | — | 0 | BLOCKED |
| NFR-008 | Reconcile 2,000 queued transactions | 10 min | — | — | 0 | BLOCKED |
| NFR-009 | Z-read and cash-up report | 10 s | — | — | 0 | BLOCKED |

Even once the build is fixed, §J requires these be measured against a seeded
**50,000-product catalogue with three years of history** on §21.1 reference
hardware. Neither exists. OQ-005 additionally questions whether figures taken on
a .NET 9 roll-forward runtime are comparable to a .NET 8 deployment target.

---

## 5. Parity report results

**All 48 Appendix B reports: `BLOCKED`.** No parity comparison was attempted.
`reports/parity/` is empty.

Blocked twice: by DEF-0001, and by OQ-003 — no incumbent output has been
supplied, and a comparison needs both sides. The appendix also declares itself
incomplete (entries 22–24 unconfirmed; dispensary/stock/stocktake catalogues
outstanding as spec open item 12).

---

## 6. UI conformance summary

**Not started.** [`ui/ui-conformance.csv`](ui/ui-conformance.csv) has a header
row and zero data rows. `ui/visual-diffs/` is empty. No screen was rendered at
any resolution or DPI.

One structural finding was made without running the app: **Section G lists 33
forms; 46 exist; 18 of the 33 do not exist at all.** Section G must not be used
as the W11 work list. The corrected inventory is
[`00-preflight/screen-inventory.csv`](00-preflight/screen-inventory.csv).

W11 is additionally at risk from DEF-0002: §I.13 makes coordinate-only
reachability a conformance failure, so 31 of 46 forms would fail item 13 on the
`AutomationId` defect alone, regardless of how they actually look. The sweep
would be measuring DEF-0002 rather than the UI.

---

## 7. Resilience results

**All ten §K scenarios: `BLOCKED`.** [`resilience/chaos-results.md`](resilience/chaos-results.md)

Eight become runnable once DEF-0001 clears. Two are blocked twice: scenario 6
(network partition) needs `frmSyncMonitor` to surface the conflict review queue,
and scenario 9 (TC-09 batch recall) needs `frmRecall` — neither exists.

---

## 8. Open questions and specification ambiguities

Five raised: [`openquestions/open-questions.md`](openquestions/open-questions.md)

| ID | Question | Blocks |
|---|---|---|
| **OQ-001** | `SEC-010` and `ADM-004` are cited normatively but never defined in any requirements table | 2 RTM rows |
| **OQ-002** | Appendix C's counts contradict the body for 6 of 16 families (RPT: appendix says 12, body defines 23) | the coverage denominator |
| **OQ-003** | Appendix B has 48 entries not ~45, declares itself incomplete, and no incumbent output was supplied | all 48 parity rows, W8 |
| **OQ-004** | 7 forms are renamed relative to the spec, breaking §1.6's own naming promise | W11 scoping, RTM `screens_touched` |
| **OQ-005** | No .NET 8 SDK on the host; solution targets `net8.0` | NFR comparability, W10 |

OQ-002 required a decision to proceed at all. **Stated, not guessed:** the RTM is
seeded from requirements **defined in the body** (211), not from Appendix C's
totals, because a requirement with no text cannot be tested. If Appendix C is
authoritative, the matrix must be re-seeded.

---

## 9. What was NOT tested, and why

Stated explicitly, as §P requires.

**Not tested: the application.** In full. No sale, no dispensing, no stock
movement, no journal, no report, no login, no permission check, no offline
transition, no reconciliation, no migration. Not one of the 1,780 planned cases
ran. Not one screenshot was captured. Not one database assertion was made. Not
one audit row was inspected.

**Why:** `dotnet build -c Release` fails with 6 errors on `main` at `8c1813e`.
There is no binary to launch, so §C aborts the session at item 1 and every
downstream wave is unreachable.

| Wave | Scope | State |
|---|---|---|
| **W0** | Preflight, baseline, RTM, testability | ⚠️ **INCOMPLETE** — RTM built (305 rows); preflight failed at item 1; baseline never captured |
| W1–W13 | All functional, NFR, resilience, UI, migration | ⛔ **NOT STARTED** |

Also not done, and not attributable to the build failure:

- **The live UIA enumeration §C.7 actually specifies.** DEF-0002's 47% figure is
  from static source analysis. It is a strong indicator, not a measurement.
- **Justification of excluded depth axes.** Every RTM row carries
  `PENDING-WAVE-JUSTIFICATION` naming its excluded axes. §E requires a *reason*,
  and a defensible reason needs observed behaviour. A row still carrying that
  marker at W13 is an incomplete record, not a covered requirement.
- **Any claim about the 11 absent screens being truly absent.** DEF-0003 is a
  static reconciliation probed three ways (source keyword, `frmMain` menu tree,
  `MainPresenter` routing). `frmReceipt`, `frmPriceChange` and
  `frmExpiryDashboard` are flagged as likely over-calls and must be re-tested
  first against the running app.

---

## 10. Required to resume

The next run is **a re-run of W0 from preflight item 1** — not W1.

1. **Fix DEF-0001.** Resolve the duplicate `JournalLineDto` / `JournalBatchDto` /
   `AccountingOptions` declarations. A development decision, not a QA one.
2. **Fix DEF-0002 before W1**, not after. It is a mechanical naming pass, and
   deferring it means authoring the whole suite against coordinates and then
   rewriting it.
3. **Answer OQ-002.** It sets the coverage denominator the entire engagement
   reports against.
4. **Triage DEF-0003.** Five waves have exit criteria naming a capability that
   may not exist. Better to know before writing 1,780 cases.
5. **Supply incumbent report output** (OQ-003) if W8 parity is to mean anything.
6. Then re-run W0 in full: live UIA enumeration, database seed and row counts,
   determinism controls, topology, baseline snapshot.

---

*Produced under PHX-QA-010 Authority rules. No application source, configuration,
seed data or database was modified. No verdict was recorded that was not
observed. No `PASS` appears in this run.*
