# Role gaps

What the pharmacy asked for and the current grants (`PharmaX.Domain/Security/StandardRoleGrants.cs`) do not yet give.

## Closed before the run (16 October 2026)

| Role | Was missing | Added | Proven by |
|---|---|---|---|
| Pharmacist | `inv.purchase.order`, `inv.goods.receive` | both | P-D4 (PO-001-000002), P-D5 (G-001-000002) |
| Pharmacist Assistant | `inv.purchase.order` | yes | S-D4 (PO-001-000003); S-D5 (G-001-000003) already worked |

The change is uncommitted, in `StandardRoleGrants.cs`, `RoleMatrixTests.cs` and `StockWorkflowTests.cs`. All 812 unit tests and 263 integration tests pass. The start-up seeder added the new grants to the existing roles in the copy (preflight row "Pharmacist and assistant rights in the copy").

## Still open: reports on the old data

The pharmacy wants every dispensary role to "do a report on existing old data". Both roles can: the pharmacist ran four old-data reports and the assistant four. Some presets are not offered to them, though:

| Role | Not offered | Permission that would add it | Cases |
|---|---|---|---|
| Pharmacist | Sales by Month, Top Sellers, Cash-ups (and the other management presets: Daily/Monthly Sales, Sales by Day, Sales by Tender, Price Changes, Price Overrides, Cost Prices to Check, Drawer Opened Without a Sale, Sales Summary) | `rpt.report.run.management` | P-D13g |
| Pharmacist Assistant | Dispensing by Month (and the other clinical presets: Dispensing by Doctor, Most Dispensed, Reversed Scripts) | `rpt.report.run.clinical` | S-D13g |
| Pharmacist Assistant | Sales by Month (and the other management presets) | `rpt.report.run.management` | S-D13g |

Neither role sees the VAT reports (`rpt.report.run.financial`). That was not asked for.

These are decisions for the pharmacy, not defects. Each is one line in `StandardRoleGrants.cs`.
