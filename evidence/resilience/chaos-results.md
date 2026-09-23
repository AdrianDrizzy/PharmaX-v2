# PHX-QA-010 §K — Resilience and Chaos Results

**Run:** `R20260917-01` · **Build:** `8c1813e` · **Wave:** W10 (not started)

**All ten scenarios: `BLOCKED`.** No chaos case was executed. Every scenario
requires a running application on a host plus two workstations; DEF-0001 leaves
no build artefact, so preflight items 3–6 were never reached and the topology
was never stood up.

| # | Scenario | Spec ref | Verdict | Blocker |
|---|---|---|---|---|
| 1 | Host off mid-basket | TC-02 | BLOCKED | DEF-0001 |
| 2 | Full day offline, then restore | TC-03 | BLOCKED | DEF-0001 |
| 3 | Power pulled mid-finalisation | TC-04 | BLOCKED | DEF-0001 |
| 4 | Printer offline at receipt | §6.10 | BLOCKED | DEF-0001 |
| 5 | Card terminal unavailable | POS-014 | BLOCKED | DEF-0001 |
| 6 | Network partition between workstations | TC-08 | BLOCKED | DEF-0001; also DEF-0003 (no `frmSyncMonitor` to surface the review queue) |
| 7 | Disk full / DB locked on workstation | §5.5 | BLOCKED | DEF-0001 |
| 8 | Clock skew workstation vs host | §5.6 | BLOCKED | DEF-0001 |
| 9 | Batch recall across eleven patients | TC-09 | BLOCKED | DEF-0001; **also DEF-0003 — `frmRecall` does not exist**, so this scenario has no user-facing path even once the build is fixed |
| 10 | Deliberate 50.00 cash-up shortage | TC-10 | BLOCKED | DEF-0001 |

## Note on scenarios 6 and 9

These two are blocked twice over. Clearing DEF-0001 makes eight of the ten
runnable; scenarios 6 and 9 additionally need DEF-0003 resolved:

- **Scenario 6** requires conflicts to *"surface in the review queue"*. §13.6
  names `frmSyncMonitor` as that surface and it is not implemented.
- **Scenario 9** requires *"remaining stock quarantined, all sales and
  dispensings listed, all eleven patients identified with contact details"*.
  §9.6.2 names `frmRecall` and it is not implemented. The two `Recall` matches
  in the source are `PosPresenter` / `CollectionService` and refer to recalling a
  **held basket** (POS-006) — an unrelated feature.

Both could in principle be evidenced by direct database assertion, but §L.6
requires all three layers (UI, database, audit) to agree, and there is no UI
layer to read. They would be `PARTIAL` at best.
