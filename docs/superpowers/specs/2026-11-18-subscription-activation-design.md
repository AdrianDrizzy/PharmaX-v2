# Subscription activation codes - design

## Why

PharmaX is to be sold as a membership: Monthly, Quarterly or Yearly. A
pharmacy pays the vendor (Adrian), receives an activation code for the plan
it paid for, enters it, and PharmaX runs until that period ends. Renewing is
a new code.

## Goals

- Three plans: Monthly (1 month), Quarterly (3 months), Yearly (12 months).
- The vendor issues codes by hand, after confirming payment himself, with a
  small code-maker tool on his own PC. Codes go to the pharmacy by WhatsApp
  or email.
- PharmaX checks codes fully offline. No licensing server.
- A code works for one pharmacy only.
- Expiry is firm but never strands a patient (see Behaviour).

## Non-goals

- Online payment, automatic code delivery, a licensing server.
- Per-till or per-user pricing. One code covers the whole pharmacy.
- Feature tiers. Every plan unlocks everything; plans differ only in length.

## Anti-goals

- A pharmacy that has paid is ever locked out (late code, clock drift, till
  offline).
- Anyone other than the vendor can make a valid code.
- Records become unreachable after expiry.

## Approaches considered

1. **Signed code (chosen).** The code-maker signs a small payload with an
   ECDSA P-256 private key that never leaves the vendor's PC. PharmaX holds
   only the public key. Cannot be forged even by decompiling PharmaX. Cost:
   the code is ~100 characters, so it is pasted, not typed.
2. Short HMAC code (~25 chars, typeable). Rejected: the checking secret
   would sit inside PharmaX, and .NET is trivial to decompile, so anyone
   could mint codes.
3. Online activation server. Rejected: codes are issued by hand and PharmaX
   runs offline.

## The code

Payload (binary, then signed):

| Field | Size | Notes |
|---|---|---|
| Version | 1 byte | `1` |
| Plan | 1 byte | 1 = Monthly, 3 = Quarterly, 12 = Yearly |
| Pharmacy ID | 16 bytes | the pharmacy's GUID (below) |
| Valid until | 4 bytes | days since 2000-01-01, inclusive end date |
| Issued on | 4 bytes | days since 2000-01-01 |

Code = Base32(payload + 64-byte P1363 signature), prefixed `PHX-`, grouped
in fives with dashes for readability. PharmaX strips dashes, spaces and case
before checking, so a pasted code with stray whitespace still works.

The code carries an **absolute end date**, not "add N days". The code-maker
computes it as `max(today, current valid-until) + plan length`. Entering the
same code twice changes nothing; entering an older code never shortens the
membership (PharmaX keeps the later valid-until).

Uses only `System.Security.Cryptography.ECDsa` - no new packages.

## Pharmacy ID

Generated once (new GUID) on the main computer the first time the licence is
read, stored in the pharmacy settings. Shown on the activation screen with a
Copy button, displayed as a short grouped string. The pharmacy sends it to
the vendor once; every code is made for it. Unaffected by changing hardware.
Restoring a backup keeps it (it lives in the database).

## Behaviour

States, from the stored valid-until date and today:

| State | When | What the user sees | What works |
|---|---|---|---|
| Active | more than 7 days left | nothing | everything |
| Expiring | 7 days or fewer left | amber banner on the main screen: "Membership ends on {date}. Enter a new code in System Setup." | everything |
| Grace | up to 7 days past expiry | red banner: "Membership ended on {date}. PharmaX becomes read-only on {date+7}." | everything |
| Read-only | more than 7 days past expiry, or no code at all | red banner + activation screen offered at sign-in | look-ups and reports only |
| Read-only (tampered) | today is earlier than the latest date PharmaX has seen, by more than 1 day | as Read-only, banner says the computer's date looks wrong | look-ups and reports only |

**Read-only** is enforced in one place: `PermissionService.Check`. When the
licence is read-only, any permission not on a read-only allow-list returns
`Denied`. The allow-list: `inv.product.read`, the four `rpt.report.run.*`,
`fin.ledger.read`, plus the admin permissions needed to fix the situation
(`adm.settings.change` - to enter a code - and `adm.backup`). Because every
screen and every service already goes through this check, menus, tiles,
shortcuts and services all follow with no further changes.

Signing in, changing password, Ask PharmaX and receipt copies stay
available (they are `_ => true` in `ScreenAccess`).

**Fresh install:** a new install gets a 14-day trial automatically (stored as
a valid-until with no code), so setup and testing are never blocked.

**Clock tampering:** PharmaX stores the latest date it has seen
(`licence.last-seen`), updated at each sign-in. If today is more than one day
earlier, the state is Read-only (tampered) until the clock is right.

## Where things live

- `PharmacySettingKey` gains: `licence.code`, `licence.pharmacy-id`,
  `licence.valid-until`, `licence.last-seen`, stored via `IPharmacySettings`
  (the shared database), so all tills share one licence.
- Tills keep selling offline today; the licence state must be readable
  offline too. Tills cache the last-known valid-until locally and use it
  when the main computer is unreachable.
- New `PharmaX.Application/Licensing/`: `LicenceCode` (parse + verify),
  `LicenceState` (the table above, pure function of dates), `ILicenceService`.
- Public key: a constant in `LicenceCode`.
- **Activation screen**: a section on System Setup (admin only) showing the
  Pharmacy ID with Copy, current plan and end date, a box to paste a code,
  and an Activate button. Clear messages for: not a PharmaX code, made for
  a different pharmacy, already expired.
- **Banner**: on the main shell (`frmMain`), driven by `LicenceState`.

## The code-maker (vendor tool)

A small separate console app, `tools/PharmaX.CodeMaker`, **not shipped in
the installer**:

```
codemaker keygen                       -> writes private.key (keep safe), prints public key
codemaker issue <pharmacy-id> <plan>   -> prints the code, logs it
```

It keeps a local log (`issued.csv`: date, pharmacy ID, name, plan, valid
until, code) so it knows each pharmacy's current valid-until for renewals,
and so the vendor has a record. The private key file must be backed up
off-PC; losing it means no new codes can be issued for existing installs.

## Testing

- Unit: round-trip issue -> verify; tampered byte rejected; wrong pharmacy
  rejected; each state boundary (day 7 before, day 0, day 7 after, clock set
  back); older code never shortens valid-until; permission check denies a
  write permission in Read-only and allows `inv.product.read`.
- Test keys for the unit tests are generated in the test, not the real key.

## Left to the builder

- Exact banner styling (use the existing theme tokens).
- Whether `adm.backup`'s data-export flows need any extra allow-listing.
- Patients: viewing a patient currently needs `dsp.patient.maintain`, which
  also edits. In Read-only, the builder should let the patient screen open
  but block saving (decide the cleanest cut in the patient presenter/service).
