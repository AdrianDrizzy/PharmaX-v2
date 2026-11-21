# Label expiry, till clock and cash-up fixes

Approved 2026-11-21.

## Why

At a live pharmacy, three problems were found:

- Labels printed with `EXP:__/__/____` because stock was never on a batch.
- A till PC with a clock 10 hours out put sales on the wrong day.
- Cash-ups closed with unexplained differences.

These are live problems and come before any new features.

## Goals

1. **Receiving:** a Goods Receipt line that has an expiry, on a product that is not batch tracked, turns batch tracking on for that product. The batch is created from the line, so the expiry is never dropped.
2. **Turning batch tracking on** (ProductGridEdit.EditBatchTrackedAsync) when stock on hand is above 0 needs a batch number and an expiry for that stock. That stock moves onto the new batch.
3. **Dispensing a batch-tracked item with no available batch:**
   - The pharmacist is asked for the batch and expiry from the box.
   - The batch is created, a stock adjustment is recorded with the reason "Found at dispensing", and the line is allocated to that batch.
   - Cancelling leaves today's behaviour (a shortfall).
4. **Before a label prints:**
   - Any line that would print without an expiry stops printing and names the item.
   - The pharmacist either enters the expiry or confirms printing without it.
   - The confirmation is audit-logged with the user's name.
5. **Stock Alerts (F12):** a new alert, "Dispensed with no batch or expiry", listing today's items.
6. **Clock check at sign-in:**
   - Compare the PC with the database server on two things: UTC time, and UTC offset (the time zone).
   - A gap of more than 2 minutes, or a different offset, blocks sign-in with plain instructions.
   - After that, IClock gives server-corrected time (the PC clock plus the measured offset).
   - SQLite standalone has no separate server, so it skips the check.
7. **Cash-up:**
   - Any non-zero difference needs a reason; the old threshold was $20.
   - A difference above $5 needs a supervisor (a user holding the cash-up override permission) to sign in.
   - The carried float and each payout, with its note, are shown on the cash-up.
   - A session closed within 5 minutes of opening shows a warning, but is not blocked.

## Non-goals

- A Cash-up reconciliation report.
- Moving the daily reports onto the trading day. Both of these are later work.
- Warning about sales made after the day's cash-up.

## Approaches considered

- **Receiving:** asking whether to turn on batch tracking, or turning it on automatically. Automatic was chosen because it has one less question and the same outcome.
- **Clock:** swapping every `DateTime.UtcNow` call, or correcting the one shared IClock. The shared IClock was chosen because every service already uses it.

## Left to the builder

- Exact wording of the messages.
- Where the supervisor sign-in dialog lives (reuse an existing override prompt if there is one).
- Permission codes: reuse existing ones where they fit.
