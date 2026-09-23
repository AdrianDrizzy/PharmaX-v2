# PHX-QA-010 — Environment Preflight

**Run ID:** `R20260917-01`
**Wave:** W0 — Preflight, baseline snapshot, RTM construction, testability audit
**Executed (UTC):** 2026-09-17
**Build under test:** `8c1813e1df5d946e81c76047838d45da1cea7479` (branch `main`, working tree clean)
**Verdict:** ❌ **PREFLIGHT FAILED AT ITEM 1 — ENGAGEMENT ABORTED PER SECTION C**

> Section C: *"run once per session; **abort on any failure**"*.
> Item 1 failed. Items 2–8 were then attempted only so far as they can be
> established **without a running application**; each is marked with what it
> could and could not establish. No functional wave (W1–W13) may start.

---

## Item-by-item result

| # | Item | Verdict | Note |
|---|---|---|---|
| 1 | Build + test | ❌ **FAIL** | `dotnet build -c Release` → **6 errors, 0 warnings**. `dotnet test` → same 6 errors, **0 of 44 test files executed**. See **DEF-0001**. |
| 2 | Version stamp | ⚠️ PARTIAL | Git SHA and declared version captured. Assembly version **not** obtainable — no assembly is produced. No About screen reachable. |
| 3 | Database reset | ⛔ NOT REACHED | Baseline dataset cannot be restored or row-counted; the application that owns the schema does not build. |
| 4 | Determinism controls | ⛔ NOT REACHED | Clock pinning, sequence reset, printer redirection and device stubs all require a running app. |
| 5 | Display controls | ⛔ NOT REACHED | No UI to display. |
| 6 | Topology | ⛔ NOT REACHED | Host + WS-01 + WS-02 cannot be stood up without a build artefact. |
| 7 | **Automation readiness** | ⚠️ **PARTIAL — completed statically** | Performed as a **source-level** audit of all 41 `*.Designer.cs` files instead of a live UIA enumeration. **Result: FAIL.** See **DEF-0002**. |
| 8 | Baseline snapshot | ⛔ NOT REACHED | No screenshots exist for this run. The `evidence/screenshots/` tree is empty and `index.csv` has a header only. |

---

## Item 1 — Build (FAIL)

```
dotnet build -c Release
→ Build FAILED.  0 Warning(s)  6 Error(s)   (elapsed 00:00:20.57)
```

| Error | File | Symbol |
|---|---|---|
| CS0101 | `PharmaX.Application/Abstractions/PharmaXOptions.cs:221` | `AccountingOptions` defined twice |
| CS0102 | `PharmaX.Application/Abstractions/PharmaXOptions.cs:14` | `PharmaXOptions.Accounting` defined twice |
| CS0101 | `PharmaX.Application/Finance/LedgerService.cs:43` | `JournalLineDto` defined twice |
| CS0101 | `PharmaX.Application/Finance/LedgerService.cs:46` | `JournalBatchDto` defined twice |
| CS8863 | `PharmaX.Application/Finance/LedgerService.cs:43` | only one partial declaration may have a parameter list |
| CS8863 | `PharmaX.Application/Finance/LedgerService.cs:46` | only one partial declaration may have a parameter list |

`dotnet test` fails with the identical six errors. **44 test files, 0 executed.**
The architecture tests required by PHX-DEV-007 §4.5 therefore have **no result**,
and no statement can be made about the solution's layering conformance.

Root cause is established in **DEF-0001**: the merge commit `37ac703` combined two
branches that had each introduced a differently-shaped `JournalLineDto` /
`JournalBatchDto` in the same namespace but in **different files**, so Git
auto-merged both without a textual conflict. **Both merge parents compile-clean;
the merge is what broke it.**

Per Authority rule 3 the agent **has not modified any source** to clear this.

## Item 2 — Version stamp

| Field | Value |
|---|---|
| Git commit | `8c1813e1df5d946e81c76047838d45da1cea7479` |
| Branch / tree | `main`, clean |
| Declared version | `1.0.0` / file version `1.0.0.0` (`Directory.Build.props`) |
| Assembly version | **unobtainable** — no assembly produced |
| Build timestamp | n/a — build did not complete |
| SDK present | .NET SDK **9.0.302** only; solution targets `net8.0` / `net8.0-windows` |

> **Observation (not yet a defect):** no .NET 8 SDK is installed on this host.
> SDK 9 can target `net8.0`, so this did not cause the failure — the build
> reached the C# compiler and failed on source, not on framework resolution.
> It is recorded because NFR timings measured on a roll-forward runtime are not
> automatically comparable to the §21.1 reference. To be resolved before W10.

## Item 7 — Automation readiness (static audit)

Full data: [`testability-audit.csv`](testability-audit.csv) — 561 rows, one per declared control.

| Measure | Value |
|---|---|
| Designer files audited | 41 |
| Controls declared | 561 |
| — interactive | 309 |
| — passive chrome (labels, panels, layout) | 252 |
| Interactive controls **deep-automatable** | **160 (51.8%)** |
| Interactive controls with **no `AutomationId` at runtime** | **146 (47.2%)** |
| Designer-generated field names (`textBox3` pattern) | **3** |

**The finding is not the one anticipated.** Field naming in this solution is
good: the designers are hand-written and use stable, intention-revealing names
(`txtUsername`, `btnLogin`, `grdUsers`). Only 3 controls in the whole solution
carry a generated-style field name, and all 3 are the same `scanBar` instance.

The actual defect is narrower and more mechanical: **WinForms derives the UIA
`AutomationId` from the control's `Name` *property*, not from the C# field
name.** 146 interactive controls are declared with a good field name but never
have `.Name` assigned inside `InitializeComponent`, so at runtime they surface
with an empty `AutomationId`.

The casualties are overwhelmingly **buttons** — `btnClose`, `btnSave`,
`btnRefresh`, `btnPost`, `btnCancel` — i.e. precisely the commit controls that
every Section E axis 1 (happy path) and axis 5 (negative/refusal) case must
press. Worst-affected forms:

| Form | Unaddressable interactive controls |
|---|---|
| `frmPatientEdit` | 16 (incl. all 3 `TabPage`s and every identity/clinical/medical-aid field) |
| `frmProductEdit` | 9 (every field: code, description, retail, cost, VAT rate…) |
| `frmSupplierEdit` | 8 (every field **and** `btnSave` / `btnCancel`) |
| `frmUserList`, `frmScriptEdit`, `frmClaims`, `frmPatientList`, `frmGoodsReceipt` | 6 each |
| 26 further forms | 1–5 each |

**Consequence for the engagement.** Section Q requires *"assert on the
accessibility tree, evidence with screenshots… do not read values out of
pixels"*, and Section I.13 makes "no control is reachable only by coordinate" a
UI-conformance line item. At 47% unaddressable, roughly half of all assertions
would have to fall back to coordinate clicking — which Section I.13 classifies
as a defect in its own right. Raised as **DEF-0002** (Severity 2).

**Caveat — this audit is static.** It reads source, not a live UIA tree,
because there is no runnable binary. It must be re-confirmed against the live
tree of `frmLogin` and `frmMain` (as Section C.7 actually specifies) once
DEF-0001 is cleared. Two mechanisms could move the number in either direction:
controls created outside `InitializeComponent`, and any base-class or
theme-application code that assigns `Name` centrally. Neither was found in a
search of `PharmaX.Desktop`, but only a live enumeration settles it.

## Item 8 — Baseline snapshot (not reached)

No baseline screenshots were captured. `evidence/screenshots/index.csv` exists
with its header row and **zero data rows**. This is stated explicitly so that no
later wave mistakes an empty baseline for a clean diff.

---

## Screen inventory reconciliation (performed as part of RTM construction)

Full data: [`screen-inventory.csv`](screen-inventory.csv).

| Measure | Value |
|---|---|
| Forms on disk | **46** |
| Forms named in QA prompt Section G | 33 |
| Forms named anywhere in PHX-SRS-001 | 31 (excl. the `frmXxx` convention placeholder) |
| **Section G forms that do not exist on disk** | **18** |
| Forms on disk not named in Section G | 31 |

Section G is not an accurate inventory of this build and must not be used as the
W11 work list. Of the 18 absent forms, some are **renames** where the capability
exists under a different form name, and some are **genuinely absent
capabilities**. The distinction is made in **DEF-0003**; it decides whether
W5, W7, W9 and W11 have anything to test.

---

## Gate

**W1 cannot start.** The exit criterion for W0 is *"RTM seeded with every
requirement ID; every form enumerable in the UIA tree"*. The first half is met
(305 rows). The second half cannot be met at all while DEF-0001 stands, and
would fail on DEF-0002 even after it is cleared.

| Deliverable | State |
|---|---|
| RTM seeded | ✅ 305 rows — `evidence/coverage/rtm.csv` |
| Every form enumerable in UIA tree | ❌ blocked by DEF-0001; would fail on DEF-0002 |
| Baseline snapshot | ❌ not captured |
| Determinism controls | ❌ not established |

**Required to unblock:** DEF-0001 fixed and `dotnet build -c Release` green;
then re-run W0 in full from item 1.
