# PHX-QA-010 — Open Questions and Specification Ambiguities

Raised under **Authority rule 2**: *"If the spec is silent, ambiguous or
self-contradictory on a point you need, raise an Open Question record and mark
the case `BLOCKED` — never guess the expected result and never mark it `PASS`."*

Run `R20260917-01` · build `8c1813e` · raised 2026-09-17

---

## OQ-001 — Two requirements are cited normatively but never defined

**Severity:** blocks 2 RTM rows
**Affects:** `SEC-010`, `ADM-004`

Both IDs are referenced in prose as binding requirements, but neither has a
defining row in any requirements table:

| ID | Cited at | Text as cited |
|---|---|---|
| `SEC-010` | §4.6, one clause | *"Local databases and backup media shall be encrypted at rest (SEC-010)."* |
| `ADM-004` | §2.8 risk narrative, and constraint C-08 | *"…informs requirement ADM-004, which prohibits any licensing mechanism in PharmaX from being capable of stopping the pharmacy from trading."* |

The SEC requirements table runs `SEC-001…SEC-009` and then jumps to `SEC-020`.
`SEC-010` is never defined there or anywhere else. `ADM` defines only four IDs
and `ADM-004` is not among them.

**Why this blocks rather than degrades.** Neither citation is sufficient to
derive an expected result at the depth Section E demands. For `SEC-010`,
"encrypted at rest" does not state the algorithm, the key custody model, or
whether it covers the workstation outbox as well as the host database — and the
difference decides whether a case passes. For `ADM-004`, "no licensing
mechanism may stop trading" is a negative capability with no stated trigger to
test against.

**Question for the specification owner.** Are `SEC-010` and `ADM-004` (a)
requirements omitted from their tables in error, (b) withdrawn requirements
whose prose citations were not removed, or (c) intentionally-narrative design
constraints that should not carry a requirement ID at all? §Appendix C states
*"a requirement withdrawn in a later version is marked withdrawn and its code is
never reused"* — neither is marked withdrawn, which argues for (a).

**RTM treatment:** both rows carry `status = BLOCKED`,
`depth_axes_required = UNDETERMINED`.

---

## OQ-002 — Appendix C's requirement counts contradict the body of the spec

**Severity:** ambiguous coverage denominator for 6 of 16 families
**Affects:** the Section D exit condition

Appendix C ("Requirement Index") states a count per prefix. Counting the
requirements actually *defined in requirement tables* in the body gives a
different figure for six families:

| Prefix | Appendix C says | Defined in body | Δ |
|---|---|---|---|
| SEC | 23 | **20** | −3 |
| NET | 20 | **15** | −5 |
| ADM | 10 | **4** | −6 |
| POS | 25 | **23** | −2 |
| RPT | 12 | **23** | **+11** |
| NFR | 24 | **14** | −10 |
| *(other 10 families)* | — | — | agree |

RPT is the striking one: Appendix C claims **fewer** requirements than the body
defines, while the other five claim more.

Separately, ID numbering is **block-allocated with deliberate gaps** — e.g. SEC
runs 001–009, then 020–024, then 030–035; NFR runs 001–009 then 020–024. The
gaps are structural, not missing requirements, and should not be read as absent
IDs. (This is why the RTM is seeded from *defined rows*, not from a numeric
range.)

**Why it matters.** Section D's exit condition is *"every row reaches `COVERED`
or carries a linked defect ID"*. If Appendix C is the denominator, the
engagement can never reach 100% for SEC, NET, ADM, POS and NFR, because the
missing IDs have no text to test. If the body is the denominator, 11 RPT
requirements would be omitted from a coverage report built off Appendix C.

**Decision taken for this run (stated, not guessed):** the RTM is seeded from
**requirements defined in the body**, giving **211** defined requirements. This
follows Authority rule 1 — a requirement with no text cannot be tested, and the
body is where requirement text lives. Appendix C is treated as a summary index
that has drifted from the body.

**Question for the specification owner.** Should Appendix C be corrected to
match the body, or does it record requirements that exist in a source not
supplied to this run?

---

## OQ-003 — Appendix B parity comparison has no incumbent baseline

**Severity:** blocks the W8 exit criterion for all 48 report rows
**Affects:** `RPT-APXB-01` … `RPT-APXB-48`, TC-11

W8's exit criterion requires each report be *"compared against incumbent output
for the same period (TC-11)"*. Three obstacles, none resolvable by the agent:

1. **No incumbent output has been supplied.** There are no reference exports in
   the repository, and the incumbent system is not reachable from this
   environment. A comparison needs both sides.
2. **The catalogue is self-declared incomplete.** Appendix B states: *"Entries
   22 to 24 and the dispensary catalogue require confirmation against a complete
   scroll of the dropdown; the survey captured the list in overlapping
   segments."* Entries 22–24 are `Sales (By Category)`, `Sales (By Department)`
   and `Sales (By Department) — detail variant` — the third is plainly a
   capture artefact of the overlapping scroll, not a distinct report.
3. **The count is 48, not ~45.** The QA prompt's Section D estimates *"~45
   Appendix B parity reports"*. The appendix table contains **48** numbered
   entries. The RTM uses 48. If entry 24 is a duplicate the true figure is 47,
   which is why item 2 must be resolved before W8 can report a parity
   percentage.

Additionally, Appendix B covers **point-of-sale reports only**. The dispensary,
stock and stocktake catalogues are listed as open item 12 and are *"to be
enumerated on the same basis and appended before the mapping workbook is
signed"*. Parity coverage for those modules cannot be scoped at all yet.

**Question for the specification owner.** (a) Can incumbent exports for a
defined period be supplied for the 48 listed reports? (b) Is entry 24 a distinct
report or a duplicate of entry 23? (c) What is the target date for the
dispensary/stock/stocktake catalogues (open item 12)?

---

## OQ-004 — The spec breaks its own form-naming convention

**Severity:** 4 (documentation) — but it mis-scopes W11
**Affects:** 7 forms, and the RTM `screens_touched` column

§1.6 states: *"Windows Forms are named `frmXxx` throughout and **the name in
this document is the name in the solution**."* Seven forms break this — the
capability exists, under a different name, usually split into a list form and an
edit form:

| Spec name | Actual |
|---|---|
| `frmPatient` | `frmPatientEdit` + `frmPatientList` |
| `frmProduct` | `frmProductEdit` + `frmProductList` |
| `frmStockTake` | `frmStocktakeCapture` + `frmStocktakeList` |
| `frmScriptCapture` | `frmScriptEdit` + `frmScriptList` |
| `frmPurchaseOrder` | `frmPurchaseOrderList` |
| `frmJournalEnquiry` | `frmJournal` |
| `frmStockEnquiry` | *(menu "Stock" → alert/product views — mapping unconfirmed)* |

The list/edit split is a sensible implementation of §18.3's *"list-and-edit
screens"* pattern, so this reads as the spec lagging the build rather than the
build deviating. It is raised because W11 must sweep **46** forms, not the 33 in
Section G, and because the RTM's `screens_touched` column needs the mapping
fixed before cases are written against it.

**Question for the specification owner.** Should §1.6 and Appendix A be updated
to the as-built names, or should the forms be renamed to match the spec?

*(The eleven forms with no equivalent at all are a defect, not an open question
— see DEF-0003.)*

---

## OQ-005 — No .NET 8 SDK on the test host

**Severity:** blocks NFR comparability, not correctness
**Affects:** W10, NFR-001…009

The solution targets `net8.0` / `net8.0-windows`. The host carries **only .NET
SDK 9.0.302**. SDK 9 builds `net8.0` targets, and this did not cause DEF-0001 —
the build reached the C# compiler and failed on source, not on framework
resolution.

It is recorded because §J requires NFR figures be measured against the §21.1
reference hardware and a seeded 50,000-product catalogue, and because roll-forward
to a .NET 9 runtime changes JIT and GC behaviour enough that timings would not be
comparable to a .NET 8 deployment target.

**Question for the project owner.** Is the production deployment target .NET 8,
and if so should the test host install the .NET 8 SDK and runtime before W10?
