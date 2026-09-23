# PHX-QA-010 — Run Log

Append-only. Survives context resets. One line per checkpoint (§L.12: every 10
cases) plus a block per wave boundary.

Format: `UTC | run | wave | event | counts | elapsed`

---

## Run R20260917-01 — build `8c1813e` — Wave W0

```
2026-09-17 | R20260917-01 | W0 | WAVE START  | 305 RTM rows to seed | t+0
2026-09-17 | R20260917-01 | W0 | Preflight 1 | dotnet build -c Release -> FAILED, 6 errors 0 warnings | t+0:21
2026-09-17 | R20260917-01 | W0 | Preflight 1 | dotnet test -> FAILED, same 6 errors, 44 test files 0 executed | t+0:45
2026-09-17 | R20260917-01 | W0 | DEFECT      | DEF-0001 raised, Severity 1 - solution does not compile | t+0:50
2026-09-17 | R20260917-01 | W0 | SEV-1 STOP  | Section N: stop the wave, capture state, report before continuing | t+0:50
2026-09-17 | R20260917-01 | W0 | Preflight 1 | Root cause proven: merge 37ac703 unioned two branches' Finance DTOs; both parents compile clean | t+1:05
2026-09-17 | R20260917-01 | W0 | Preflight 2 | Version stamp PARTIAL - git SHA captured, assembly version unobtainable (no artefact) | t+1:10
2026-09-17 | R20260917-01 | W0 | Preflight 3 | NOT REACHED - no schema owner to seed | t+1:10
2026-09-17 | R20260917-01 | W0 | Preflight 4 | NOT REACHED - determinism controls need a running app | t+1:10
2026-09-17 | R20260917-01 | W0 | Preflight 5 | NOT REACHED - no UI to display | t+1:10
2026-09-17 | R20260917-01 | W0 | Preflight 6 | NOT REACHED - no artefact to deploy to WS-01/WS-02 | t+1:10
2026-09-17 | R20260917-01 | W0 | Preflight 7 | PARTIAL - live UIA enumeration impossible; performed as static audit of 41 Designer.cs | t+1:30
2026-09-17 | R20260917-01 | W0 | Preflight 7 | 561 controls: 309 interactive, 160 addressable (51.8%), 146 with no AutomationId | t+1:35
2026-09-17 | R20260917-01 | W0 | DEFECT      | DEF-0002 raised, Severity 2 - TESTABILITY | t+1:40
2026-09-17 | R20260917-01 | W0 | Preflight 8 | NOT REACHED - zero baseline screenshots captured; index.csv has header only | t+1:40
2026-09-17 | R20260917-01 | W0 | RTM         | Spec parsed: 211 defined requirements + 44 BR-* + 48 Appendix B = 305 rows | t+1:55
2026-09-17 | R20260917-01 | W0 | RTM         | rtm.csv written, all 305 rows status=BLOCKED, 1780 minimum cases planned | t+2:00
2026-09-17 | R20260917-01 | W0 | OQ          | OQ-001 raised - SEC-010 and ADM-004 cited normatively but never defined | t+2:05
2026-09-17 | R20260917-01 | W0 | OQ          | OQ-002 raised - Appendix C counts contradict body for 6 of 16 families | t+2:05
2026-09-17 | R20260917-01 | W0 | OQ          | OQ-003 raised - Appendix B is 48 entries (not ~45); no incumbent baseline supplied | t+2:10
2026-09-17 | R20260917-01 | W0 | SCREENS     | 46 forms on disk vs 33 in Section G; 18 Section G forms do not exist | t+2:20
2026-09-17 | R20260917-01 | W0 | DEFECT      | DEF-0003 raised, Severity 2 - 11 spec-named screens absent with no equivalent | t+2:30
2026-09-17 | R20260917-01 | W0 | OQ          | OQ-004 raised - 7 further forms renamed vs spec (doc defect) | t+2:30
2026-09-17 | R20260917-01 | W0 | OQ          | OQ-005 raised - no .NET 8 SDK on host; NFR comparability at risk | t+2:35
2026-09-17 | R20260917-01 | W0 | WAVE END    | W0 INCOMPLETE - exit criteria not met. 0/305 covered, 305 blocked, 3 defects, 5 OQs | t+2:40
```

### W0 exit criteria assessment

| Criterion | Met? | Evidence |
|---|---|---|
| RTM seeded with every requirement ID | ✅ **YES** | `coverage/rtm.csv`, 305 rows |
| Every form enumerable in the UIA tree | ❌ **NO** | Blocked by DEF-0001; would fail on DEF-0002 |
| Preflight passed | ❌ **NO** | Failed at item 1 |
| Baseline snapshot captured | ❌ **NO** | 0 screenshots |

**W1 is NOT authorised to start.**

### Handover for the next run

The next run is **a re-run of W0 from preflight item 1**, not W1. Read this file
and `coverage/rtm.csv` first, then:

1. Confirm DEF-0001 is fixed and `dotnet build -c Release` is green; record the
   warning count and the full `dotnet test` pass/fail including the
   PHX-DEV-007 §4.5 architecture tests.
2. **Re-take DEF-0002 against the live UIA tree** of all 46 forms. The 47%
   figure is static-derived and must not be carried forward as observed.
3. Re-test the 11 DEF-0003 absences against the live menu tree. Start with
   `frmReceipt`, `frmPriceChange` and `frmExpiryDashboard` — all three are
   flagged as likely over-calls.
4. Then complete preflight items 3–6 and 8, which have never been established
   for any build.

Nothing in `cases/`, `performance/`, `resilience/`, `ui/` or
`reports/parity/` has been written, because no case has been executed. Those
directories are empty by design, not by omission.

---

## Run R20260917-02 — remediation and acceptance walkthrough

Authorised by the user after the W0 report: fix DEF-0001 and DEF-0002, then
re-run W0. The chart-of-accounts decision (DEF-0004) was referred back and
answered: adopt `LedgerCode.Standard`, re-point `AccountingOptions` defaults.

```
2026-09-17 | R20260917-02 | REM | DEF-0001 | Merged the two AccountingOptions; both feature sets kept | t+0:10
2026-09-17 | R20260917-02 | REM | DEF-0001 | Renamed day-end DTOs to DayEndJournal*Dto; both branches' view models survive | t+0:15
2026-09-17 | R20260917-02 | REM | DEF-0001 | Removed 3 duplicate DbSets (semantically identical) | t+0:20
2026-09-17 | R20260917-02 | SEV1 | DEF-0004 raised - two incompatible charts of accounts; only 2 of 19 codes agree | t+0:30
2026-09-17 | R20260917-02 | STOP | Section N: stopped and referred the chart decision to the owner | t+0:30
2026-09-17 | R20260917-02 | REM | DEF-0004 | Owner chose Branch A; added VouchersRedeemed 1250 and RoundingDifferences 4900 | t+0:50
2026-09-17 | R20260917-02 | REM | DEF-0004 | AccountingOptions defaults now reference LedgerCode constants, so they cannot drift again | t+0:55
2026-09-17 | R20260917-02 | SEV1 | DEF-0005 raised - merge also deleted MedicalAidClaimConfiguration; EF model would not build at all | t+1:05
2026-09-17 | R20260917-02 | REM | DEF-0005 | Restored the deleted configuration verbatim from d38619e; migration snapshots fixed the expected shape | t+1:10
2026-09-17 | R20260917-02 | REM | DEF-0005 | Removed the stale duplicate ledger migrations (Accounting/HostAccounting) | t+1:20
2026-09-17 | R20260917-02 | REM | DEF-0005 | DayEndPostingService now uses DocumentSeries.Journal, not the literal "JNL" | t+1:25
2026-09-17 | R20260917-02 | BUILD | dotnet build -c Release -> SUCCEEDED, 0 errors, 99 warnings | t+1:30
2026-09-17 | R20260917-02 | TEST | dotnet test -> 407 of 407 pass (322 unit, 85 integration), 0 failed | t+1:35
2026-09-17 | R20260917-02 | REM | DEF-0002 | Naming pass: 145 Name assignments inserted across 31 designer files | t+1:45
2026-09-17 | R20260917-02 | REM | DEF-0002 | Interactive controls addressable 161/309 (52.1%) -> 306/309 (99.0%); 0 without AutomationId | t+1:50
2026-09-17 | R20260917-02 | CORR | DEF-0002 | Pre-fix figure corrected 146 -> 145; colAccount was already named in its initializer | t+1:50
2026-09-17 | R20260917-02 | CORR | DEF-0002 | Audit covered 40 of 46 forms; 6 code-built forms were never examined and remain unfixed | t+1:55
2026-09-17 | R20260917-02 | LIVE | frmLogin enumerated through UIA3 on the running app - every control surfaced its stable id | t+2:00
2026-09-17 | R20260917-02 | WALK | Acceptance walkthrough built: 6 roles x 39 menu entries, UIA-driven, PrintWindow capture | t+2:30
```

### Harness defects found and fixed while building the walkthrough

Recorded because each one produced **false evidence** before it was caught, and
a test harness that lies is worse than no harness:

| Symptom | Cause | Fix |
|---|---|---|
| Every case after the first timed out | A modal form blocks the whole UIA provider | Close with WM_CLOSE (Win32), never through UIA |
| 68 seconds per menu entry | `Invoke()` waits for `ShowDialog()` to return | Fire the pattern call on an abandoned background thread |
| Permission dialogs invisible to the run | A message box carries the application's own caption | Identify windows by HWND, not by title |
| Log off never happened | `LogOff()` asks "Sign out?" first; WM_CLOSE answers No | Press Yes by posting BM_CLICK |
| Three screens captured as small black images | WinForms helper windows were photographed instead of the form | Accept only a window with a caption and a real size |
| `Sell`, `Products`, `Patients`, `Reports` never opened | The home screen has tiles of the same name; a name-only search invoked a panel | Require ControlType = MenuItem, and search the dropdown window |

### Outstanding

DEF-0003 remains OPEN. The walkthrough confirmed two of its entries from the
running application: **Price Changes** and **VAT Codes** open *reports*, not
maintenance screens, and **Control Variables** is a read-only grid. It also
found **Transaction Types** is a report, which was not previously recorded.
