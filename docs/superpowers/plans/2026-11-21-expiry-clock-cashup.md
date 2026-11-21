# Label expiry, till clock and cash-up Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans (Native, chosen earlier) to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Stop labels printing without an expiry, stop a wrong PC clock from moving sales, and make cash-up differences explained.

**Architecture:** Every change goes through the existing services:
- ReceivingService
- ProductGridEdit
- DispensingService
- StockAlertService
- CashUpService
- the shared `IClock`

Screens only ask the questions. The clock check runs once at sign-in and corrects the singleton `SystemClock`.

**Tech Stack:** .NET 8 WinForms (MVP), EF Core (MariaDB shared / SQLite standalone), xUnit + FluentAssertions + NSubstitute.

**Spec:** `docs/superpowers/specs/2026-11-21-expiry-clock-cashup-design.md`

## Global Constraints

- Error codes follow the existing series (GRN-, INV-, DSP-, POS-, SEC-) and take the next free number in each.
- No new NuGet packages.
- Analysers stay clean: no member named `Set` or `To` (CA1716), and the Domain treats warnings as errors.
- Cash-up: a reason is needed for **any** non-zero difference. A supervisor is needed above **$5**. A warning shows if the session is closed within **5 minutes** of opening.
- Clock: sign-in is blocked when the gap is **more than 2 minutes** or the UTC offset differs. This applies to the shared database only.

## Review Focus

- **Receiving an expiry with no batch number on an untracked product:** it must fail with GRN-007 and change nothing. Batch tracking must not be turned on before the save. Test this in Task 1.
- **Auto-tracking a product that already has stock:** the old stock stays where it is, on no batch. Task 3's prompt at dispensing picks it up. Test this in Task 1.
- **Cancelling the batch prompt at dispensing:** the line keeps today's shortfall behaviour, and nothing is written. Test this in Task 3.
- **A template label that deliberately leaves the expiry off** (`Omit.Expiry == true`): it must not trigger the missing-expiry check. Test this in Task 4.
- **The database server cannot be reached at sign-in:** sign-in goes ahead, because the existing offline behaviour stands. The check is skipped and the clock is not corrected. Test this in Task 6.

---

### Task 1: Receiving turns batch tracking on

**Files:**
- Modify: `PharmaX.Application/Suppliers/ReceivingService.cs`, specifically ResolveAsync (around lines 414-435) and ReceiveAsync (around line 219)
- Test: `PharmaX.Tests.Integration/Persistence/ReceivingFlowTests.cs`

**Interfaces:** none new.

- [ ] **Step 1: Write failing tests** in ReceivingFlowTests.
  - `Untracked_product_received_with_batch_and_expiry_becomes_batch_tracked`:
    - After ReceiveAsync, `product.IsBatchTracked` is true.
    - One Batch exists, with that number, expiry and quantity.
  - `Untracked_product_received_with_expiry_but_no_batch_is_refused`:
    - The result fails with code `GRN-007`.
    - The product is still not batch tracked after reloading it from a fresh context.
  - `Auto_tracked_product_keeps_its_existing_unbatched_stock`:
    - Start with QuantityOnHand 10 and no batch, then receive 5 with a batch.
    - QuantityOnHand is 15, and the batch holds 5.
- [ ] **Step 2: Run the tests.** Expect them to fail.
  ```
  dotnet test PharmaX.Tests.Integration --filter ReceivingFlowTests
  ```
- [ ] **Step 3: Implement.**
  - In ResolveAsync, run the BR-INV-02 checks when `product.IsBatchTracked || line.ExpiryDate is not null`.
  - In ReceiveAsync, after resolving and before `receipt.AddLine`, handle each line that has an expiry on an untracked product:
    - Call `product.SetBatchTracked(true)`.
    - Write the audit entry `_audit.Applied("inv.product.maintain", "Product", product.Id, $"Batch tracked: received with expiry on {number}")`.
    - Pass `true` as `isBatchTracked` to AddLine.
- [ ] **Step 4: Run the tests.** Expect them to pass. Also run `dotnet test PharmaX.Tests.Unit --filter GoodsReceipt`.
- [ ] **Step 5: Commit** with the message `Receiving an expiry turns batch tracking on`.

### Task 2: Turning batch tracking on moves existing stock onto a batch

**Files:**
- Modify: `PharmaX.Application/Inventory/ProductGridEdit.cs`, specifically EditBatchTrackedAsync (around line 185)
- Modify: `PharmaX.Application/Inventory/ProductService.cs` and the `IProductService` interface
- Modify: the products screen that calls the grid edit. Find the INV-036 handling in `PharmaX.Desktop/Features/Inventory`.
- Test: `PharmaX.Tests.Unit/Application/` (alongside the existing ProductGridEdit tests)

**Interfaces:**
- Produces:
  - `Task<Result> IProductService.TrackBatchesAsync(Guid productId, string batchNumber, DateOnly expiry, CancellationToken ct = default)`
  - the constant `ProductGridEdit.NeedsBatchForStock = "INV-037"`

- [ ] **Step 1: Write failing tests.**
  - `Turning_tracking_on_with_stock_asks_for_a_batch`: EditBatchTrackedAsync with "Yes" on a product holding 10 fails with `INV-037`, and the product is still not tracked.
  - `Turning_tracking_on_with_no_stock_just_turns_it_on`.
  - `TrackBatchesAsync_moves_stock_onto_new_batch`:
    - The Batch holds QuantityOnHand 10 and the given expiry.
    - The product is tracked.
    - The product's QuantityOnHand is still 10.
  - `TrackBatchesAsync_refuses_past_expiry`: an expiry of today or earlier fails with `GRN-009`-style wording.
- [ ] **Step 2: Run the tests.** Expect them to fail.
- [ ] **Step 3: Implement.**
  - EditBatchTrackedAsync returns INV-037, with the message "{description} has {qty} in stock. Enter the batch number and expiry of that stock.", when turning tracking on with QuantityOnHand > 0.
  - TrackBatchesAsync:
    - Demands ProductMaintain.
    - Uses `Batch.Receive(product.Id, batch, expiry, product.QuantityOnHand, product.CostPrice, _clock.UtcNow)`.
    - Calls `SetBatchTracked(true)`, writes the audit entry and saves.
  - The screen catches INV-037 and shows a small dialog with Batch and Expiry fields, then calls TrackBatchesAsync.
- [ ] **Step 4: Run the tests.** Expect them to pass.
- [ ] **Step 5: Commit** with the message `Batch tracking an item in stock asks for its batch`.

### Task 3: Found at dispensing

**Files:**
- Modify: `PharmaX.Application/Dispensing/DispensingService.cs` and the `IDispensingService` interface
- Modify: `PharmaX.Application/Inventory/StockAdjustmentService.cs`, adding `FoundAtDispensing` to `StockAdjustmentKind`
- Modify: `PharmaX.Desktop/Features/Dispensing/DispensePresenter.cs`, `IDispenseView.cs` and `frmDispense.cs`
- Test: `PharmaX.Tests.Unit/Application/` (the dispensing tests)

**Interfaces:**
- Produces:
  - `Task<Result> IDispensingService.RecordFoundBatchAsync(Guid productId, string batchNumber, DateOnly expiry, decimal packs, CancellationToken ct = default)`
  - `(string Batch, DateOnly Expiry)? IDispenseView.AskBatchFromBox(string productDescription)`

- [ ] **Step 1: Write failing tests.**
  - `Found_batch_is_created_and_recorded_as_found_at_dispensing`:
    - The Batch exists.
    - One stock movement exists, with reason text "Found at dispensing" and quantity = packs.
    - Product QuantityOnHand goes up by packs.
  - `After_found_batch_the_line_allocates_to_it`: AddLine for the product now has a batch allocation carrying that expiry.
  - Presenter test `Cancelled_batch_prompt_leaves_shortfall`: the view returns null, RecordFoundBatchAsync is never called, and the line keeps its shortfall.
- [ ] **Step 2: Run the tests.** Expect them to fail.
- [ ] **Step 3: Implement.**
  - RecordFoundBatchAsync demands the stock adjustment permission. It creates the batch and writes the movement through the same writer RecordCoreAsync uses.
  - The presenter applies only when, after adding a line, the product is batch tracked and the line has a shortfall with no batch allocated. In that case it:
    - asks the view,
    - calls RecordFoundBatchAsync with packs = ceiling(shortfall / UnitsInPack),
    - removes and re-adds the line so the allocation picks the batch up.
- [ ] **Step 4: Run the tests.** Expect them to pass.
- [ ] **Step 5: Commit** with the message `Dispensing asks for the batch on the box when none is in stock`.

### Task 4: No silent label without an expiry

**Files:**
- Create: `PharmaX.Application/Dispensing/LabelExpiryCheck.cs`
- Modify: DispensePresenter.cs (the print loop around line 399), the frmLabels reprint paths, and IDispenseView/frmDispense
- Test: `PharmaX.Tests.Unit/Application/LabelExpiryCheckTests.cs`

**Interfaces:**
- Produces:
  - `static IReadOnlyList<LabelDto> LabelExpiryCheck.Missing(IEnumerable<LabelDto> labels)`: labels where `Expiry is null && Omit?.Expiry != true`.
  - `IReadOnlyDictionary<int, DateOnly>? IDispenseView.ConfirmMissingExpiry(IReadOnlyList<LabelDto> missing)`. It returns null to cancel printing, an empty dictionary for "print without", or the expiries entered keyed by ItemNumber.

- [ ] **Step 1: Write failing tests.**
  - `Missing_lists_labels_without_expiry`.
  - `Missing_ignores_template_label_that_omits_expiry`.
  - Presenter test: `Printing_without_expiry_is_audited_with_user`, which checks for the audit action `disp.label.no-expiry`.
- [ ] **Step 2: Run the tests.** Expect them to fail.
- [ ] **Step 3: Implement.**
  - Before the print loop, the presenter asks the view.
  - An entered expiry is printed with `label with { Expiry = d }` and audited as `disp.label.expiry-entered`.
  - "Print without" writes the audit entry `disp.label.no-expiry` naming the items.
  - Cancel prints nothing and shows "Labels not printed. Print them later from Dispensary > Labels."
- [ ] **Step 4: Run the tests.** Expect them to pass.
- [ ] **Step 5: Commit** with the message `Labels without an expiry are stopped before printing`.

### Task 5: Stock alert, "Dispensed with no batch or expiry"

**Files:**
- Modify: `PharmaX.Application/Alerts/StockAlertService.cs`, adding `DispensedWithoutExpiry = 6`
- Modify: the repository query this service uses, plus the F12 screen's kind grouping
- Test: the existing StockAlertService tests

**Interfaces:** none new beyond the enum value.

- [ ] **Step 1: Write a failing test.** `Todays_batch_tracked_line_with_no_batch_is_an_alert`:
  - a line issued today with no batch allocation raises one alert, whose Key includes the dispensing line id;
  - yesterday's line raises none.
- [ ] **Step 2: Run the test.** Expect it to fail.
- [ ] **Step 3: Implement.** Query issued dispensing lines from `_clock.Today` whose product is batch tracked and whose `Batches` is empty. The sentence reads "{product} dispensed on {script} with no batch or expiry."
- [ ] **Step 4: Run the test.** Expect it to pass.
- [ ] **Step 5: Commit** with the message `Stock alert for items dispensed without a batch`.

### Task 6: Clock check at sign-in

**Files:**
- Modify: `PharmaX.Application/Abstractions/Abstractions.cs`, adding `IServerClock`
- Modify: `PharmaX.Persistence/Security/InfrastructureServices.cs`. In SystemClock, add `public void Correct(TimeSpan offset)`, so that UtcNow becomes `DateTime.UtcNow + offset`.
- Create: `PharmaX.Persistence/ServerClock.cs`. On a shared database it runs `SELECT UTC_TIMESTAMP(6), TIMESTAMPDIFF(MINUTE, UTC_TIMESTAMP(), NOW())`. On standalone it returns null.
- Modify: `PharmaX.Persistence/DependencyInjection.cs` and `PharmaX.Desktop/Features/Login/LoginPresenter.cs`
- Create: `PharmaX.Application/Security/ClockCheck.cs`
- Test: `PharmaX.Tests.Unit/Application/ClockCheckTests.cs`

**Interfaces:**
- Produces:
  - `interface IServerClock { Task<(DateTime UtcNow, TimeSpan UtcOffset)?> ReadAsync(CancellationToken ct = default); }`
  - `static string? ClockCheck.Problem(DateTime pcUtc, TimeSpan pcOffset, DateTime serverUtc, TimeSpan serverOffset)`, which returns null when the clocks are fine and otherwise the message to show.

- [ ] **Step 1: Write failing tests.**
  - `Ten_hours_out_is_blocked`.
  - `Ninety_seconds_out_is_fine`.
  - `Three_minutes_out_is_blocked`.
  - `Different_time_zone_is_blocked`: the PC is +00:00 and the server is +02:00, with the same UTC time. The message mentions the time zone.
  - Presenter: `Unreachable_server_lets_sign_in_continue`, where ReadAsync throws or returns null.
- [ ] **Step 2: Run the tests.** Expect them to fail.
- [ ] **Step 3: Implement.**
  - LoginPresenter runs the check after authentication succeeds and before CloseView.
  - When there is a problem, it shows the message and stays on the login screen.
  - Otherwise it calls `SystemClock.Correct(serverUtc - DateTime.UtcNow)` through a small `IClockCorrection` registered as the same singleton.
  - Messages:
    - "This computer's clock is {n} minutes out from the server. Set the correct date and time in Windows, then sign in again."
    - "This computer's time zone (UTC{pc}) is not the server's (UTC{server}). Set the time zone in Windows, then sign in again."
- [ ] **Step 4: Run the tests.** Expect them to pass.
- [ ] **Step 5: Commit** with the message `Sign-in checks the PC clock against the server`.

### Task 7: Cash-up differences explained

**Files:**
- Modify: `PharmaX.Application/Tills/CashUpService.cs`. Set `VarianceReasonThreshold = 0m`, add `SupervisorThreshold = 5m`, and add `ShortSessionMinutes = 5`.
- Modify: `CloseAsync(string? varianceReason, Guid? sessionId = null, Guid? authorisedBy = null, CancellationToken ct = default)`, and update all callers.
- Modify: `PharmaX.Desktop/Features/Tills/frmCashUp.cs` and its presenter.
- Test: the existing CashUpService tests.

**Interfaces:**
- Produces:
  - `CashUpService.NeedsSupervisor = "POS-0xx"`
  - `CashUpService.NeedsReason = "POS-023"` (the existing code)
  - `bool CashUpService.IsShortSession(TillSession s, DateTime nowUtc)`

- [ ] **Step 1: Write failing tests.**
  - `Any_difference_without_reason_is_refused`: a variance of $0.50 with no reason fails with POS-023.
  - `Difference_over_five_without_supervisor_is_refused`: a variance of $5.01 with a reason and no authoriser fails with NeedsSupervisor.
  - `Difference_over_five_with_supervisor_closes_and_audits_authoriser`.
  - `Exactly_five_needs_no_supervisor`.
  - `Session_closed_after_three_minutes_is_short`.
- [ ] **Step 2: Run the tests.** Expect them to fail.
- [ ] **Step 3: Implement.**
  - Enforce the rules in CloseAsync, in the service and not only on the screen.
  - The supervisor must hold `SessionCloseOtherUser`.
  - The screen handles NeedsSupervisor with `frmSupervisorPrompt(auth, message, PermissionCode.SessionCloseOtherUser)`, then retries the close with `authorisedBy`.
  - For a short session it shows a Yes/No warning before closing.
- [ ] **Step 4: Run the tests.** Expect them to pass. Then run both full suites:
  ```
  dotnet test PharmaX.Tests.Unit
  ```
  ```
  dotnet test PharmaX.Tests.Integration
  ```
  Expect both to pass.
- [ ] **Step 5: Commit** with the message `Cash-up needs a reason for any difference and a supervisor above $5`.

Already in place and left alone: the cash-up screen already shows the opening float and the payouts ("Money out").
