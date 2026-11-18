# Subscription Activation Codes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Monthly / Quarterly / Yearly memberships unlocked by an offline, ECDSA-signed activation code, with a 7-day warning, 7-day grace, then read-only.

**Architecture:** A pure `LicenceCode` (issue + verify) and pure `LicenceState` (dates -> state) in `PharmaX.Application/Licensing`. A singleton `LicenceGate` holds the current state in memory so the synchronous `PermissionService.Check` can deny write permissions when read-only. `LicenceService` loads/saves through the existing `IPharmacySettings` store (shared DB) and a local cache file for offline tills. A `frmMembership` dialog and a shell banner show it. A separate console tool issues codes.

**Tech Stack:** .NET 8, WinForms, xUnit + FluentAssertions + NSubstitute, `System.Security.Cryptography.ECDsa` (no new packages).

**Spec:** `docs/superpowers/specs/2026-11-18-subscription-activation-design.md`

## Global Constraints

- Plans: Monthly = 1 month, Quarterly = 3 months, Yearly = 12 months. Plan byte = 1 / 3 / 12.
- Warning window: 7 days before valid-until. Grace: 7 days after valid-until. Fresh-install trial: 14 days.
- Clock tamper tolerance: today more than 1 day earlier than last-seen -> read-only.
- Code = `PHX-` + Base32 (RFC 4648 alphabet, no padding) of payload + 64-byte IEEE P1363 ECDSA P-256 / SHA-256 signature, in dash-separated groups of 5.
- Payload (26 bytes, big-endian): version(1)=1, plan(1), pharmacy id(16, `Guid.ToByteArray()`), valid-until(4, days since 2000-01-01), issued-on(4, days since 2000-01-01).
- Parsing ignores case, spaces and dashes.
- An older code never shortens valid-until.
- No new NuGet packages. The code-maker is not added to the installer.
- Settings keys: `licence.code`, `licence.pharmacy-id`, `licence.valid-until` (ISO `yyyy-MM-dd`), `licence.last-seen` (ISO), `licence.trial-started` (ISO).
- Local cache: `C:\ProgramData\PharmaX\licence.json` (`Environment.SpecialFolder.CommonApplicationData`).
- Read-only allow-list: `inv.product.read`, `rpt.report.run.operational`, `rpt.report.run.management`, `rpt.report.run.financial`, `rpt.report.run.clinical`, `fin.ledger.read`, `adm.settings.change`, `adm.backup`, `dsp.patient.maintain` (opens patient look-up; saving blocked in Task 6).

## Review Focus

1. A pasted code with leading/trailing whitespace, a line break from WhatsApp, or lower case -> still accepted.
2. Main computer unreachable from a till -> till uses cached valid-until, never falls to read-only just because it is offline.
3. Renewal entered before expiry -> valid-until moves forward from the current end, not from today (code-maker), and re-entering the previous code doesn't move it back (app).
4. Valid code for a different Pharmacy ID -> refused with "This code was made for a different pharmacy."
5. Read-only and the admin signs in -> can still open System Setup -> Membership and activate (else they are stuck forever).

---

### Task 1: LicenceCode - issue and verify

**Files:**
- Create: `PharmaX.Application/Licensing/LicenceCode.cs`
- Test: `PharmaX.Tests.Unit/Application/LicenceCodeTests.cs`

**Interfaces:**
- Produces:
  - `public enum MembershipPlan : byte { Monthly = 1, Quarterly = 3, Yearly = 12 }`
  - `public sealed record LicencePayload(MembershipPlan Plan, Guid PharmacyId, DateOnly ValidUntil, DateOnly IssuedOn)`
  - `public static class LicenceCode`
    - `public const string PublicKey` (Base64 SubjectPublicKeyInfo; placeholder until Task 7 generates the real one)
    - `public static string Issue(LicencePayload payload, ECDsa privateKey)`
    - `public static LicencePayload? Verify(string code)` - uses `PublicKey`; null if malformed or bad signature
    - `public static LicencePayload? Verify(string code, ECDsa publicKey)` - for tests
    - `public static string FormatPharmacyId(Guid id)` - 32 hex upper in groups of 4 with dashes
    - `public static Guid? ParsePharmacyId(string text)` - inverse, tolerant of dashes/spaces/case

- [ ] **Step 1: Write the failing tests**

```csharp
public class LicenceCodeTests
{
    private static readonly ECDsa Key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
    private static readonly LicencePayload Sample = new(MembershipPlan.Quarterly,
        Guid.Parse("6f1c2d3e-4a5b-4c6d-8e9f-0a1b2c3d4e5f"),
        new DateOnly(2027, 2, 18), new DateOnly(2026, 11, 18));

    [Fact] public void Issued_code_verifies_to_same_payload() =>
        LicenceCode.Verify(LicenceCode.Issue(Sample, Key), Key).Should().Be(Sample);

    [Fact] public void Code_starts_with_PHX_and_is_grouped() =>
        LicenceCode.Issue(Sample, Key).Should().MatchRegex("^PHX-([A-Z2-7]{1,5}-)*[A-Z2-7]{1,5}$");

    [Fact] public void Pasted_code_with_whitespace_and_lower_case_verifies()
    {
        var code = LicenceCode.Issue(Sample, Key);
        LicenceCode.Verify("  " + code.ToLowerInvariant().Replace("-", "- \r\n") + " ", Key)
            .Should().Be(Sample);
    }

    [Fact] public void Changed_character_is_rejected()
    {
        var code = LicenceCode.Issue(Sample, Key).ToCharArray();
        code[10] = code[10] == 'A' ? 'B' : 'A';
        LicenceCode.Verify(new string(code), Key).Should().BeNull();
    }

    [Fact] public void Code_signed_by_another_key_is_rejected() =>
        LicenceCode.Verify(LicenceCode.Issue(Sample,
            ECDsa.Create(ECCurve.NamedCurves.nistP256)), Key).Should().BeNull();

    [Theory, InlineData(""), InlineData("hello"), InlineData("PHX-")]
    public void Garbage_is_rejected(string text) =>
        LicenceCode.Verify(text, Key).Should().BeNull();

    [Fact] public void Pharmacy_id_round_trips() =>
        LicenceCode.ParsePharmacyId(LicenceCode.FormatPharmacyId(Sample.PharmacyId)!.ToLowerInvariant())
            .Should().Be(Sample.PharmacyId);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test PharmaX.Tests.Unit --filter LicenceCodeTests`
Expected: build error, `LicenceCode` not defined.

- [ ] **Step 3: Implement `LicenceCode` per the Interfaces block and Global Constraints**

Sign with `SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)`. Write a small private Base32 encode/decode (RFC 4648, no padding). Reject unknown version or plan byte. `Verify` catches all `FormatException`/`CryptographicException` and returns null.

- [ ] **Step 4: Run tests, expect PASS**

Run: `dotnet test PharmaX.Tests.Unit --filter LicenceCodeTests`

- [ ] **Step 5: Commit** `feat: signed membership activation codes`

---

### Task 2: LicenceState - dates to state

**Files:**
- Create: `PharmaX.Application/Licensing/LicenceState.cs`
- Test: `PharmaX.Tests.Unit/Application/LicenceStateTests.cs`

**Interfaces:**
- Produces:
  - `public enum LicenceStatus { Active, Expiring, Grace, ReadOnly, ClockWrong }`
  - `public sealed record LicenceSnapshot(LicenceStatus Status, DateOnly? ValidUntil, MembershipPlan? Plan, bool IsTrial)` with `bool IsReadOnly => Status is LicenceStatus.ReadOnly or LicenceStatus.ClockWrong;`
  - `public static class LicenceState { public static LicenceStatus Evaluate(DateOnly? validUntil, DateOnly today, DateOnly? lastSeen); public static string? BannerText(LicenceSnapshot s); }`

- [ ] **Step 1: Write the failing tests**

```csharp
public class LicenceStateTests
{
    private static readonly DateOnly End = new(2026, 12, 31);

    [Theory]
    [InlineData(-8, LicenceStatus.Active)]      // 8 days before end
    [InlineData(-7, LicenceStatus.Expiring)]
    [InlineData(0, LicenceStatus.Expiring)]     // last valid day
    [InlineData(1, LicenceStatus.Grace)]
    [InlineData(7, LicenceStatus.Grace)]
    [InlineData(8, LicenceStatus.ReadOnly)]
    public void State_by_day(int offset, LicenceStatus expected) =>
        LicenceState.Evaluate(End, End.AddDays(offset), null).Should().Be(expected);

    [Fact] public void No_licence_is_read_only() =>
        LicenceState.Evaluate(null, End, null).Should().Be(LicenceStatus.ReadOnly);

    [Fact] public void Clock_set_back_two_days_is_clock_wrong() =>
        LicenceState.Evaluate(End, End.AddDays(-30), End.AddDays(-28))
            .Should().Be(LicenceStatus.ClockWrong);

    [Fact] public void Clock_one_day_behind_is_tolerated() =>
        LicenceState.Evaluate(End, End.AddDays(-30), End.AddDays(-29))
            .Should().Be(LicenceStatus.Active);

    [Fact] public void Active_has_no_banner() =>
        LicenceState.BannerText(new(LicenceStatus.Active, End, MembershipPlan.Yearly, false))
            .Should().BeNull();
}
```

- [ ] **Step 2: Run, expect build failure**

Run: `dotnet test PharmaX.Tests.Unit --filter LicenceStateTests`

- [ ] **Step 3: Implement**

Banner copy (dates as `d MMM yyyy`):
- Expiring: `Membership ends on {end}. Enter a new code in System Setup > Membership.` (trial: `Trial ends on {end}. ...`)
- Grace: `Membership ended on {end}. PharmaX becomes read-only on {end+8}. Enter a new code in System Setup > Membership.`
- ReadOnly: `PharmaX is read-only: no current membership. An administrator can enter a code in System Setup > Membership.`
- ClockWrong: `PharmaX is read-only: this computer's date looks wrong. Correct the date and time, then sign in again.`

- [ ] **Step 4: Run, expect PASS**

- [ ] **Step 5: Commit** `feat: membership state from dates`

---

### Task 3: LicenceGate + read-only in PermissionService

**Files:**
- Create: `PharmaX.Application/Licensing/LicenceGate.cs`
- Modify: `PharmaX.Application/Security/PermissionService.cs` (constructor + `Check`)
- Modify: `PharmaX.Application/DependencyInjection.cs` (register `s.AddSingleton<Licensing.ILicenceGate, Licensing.LicenceGate>();`)
- Test: `PharmaX.Tests.Unit/Security/LicenceGateTests.cs`

**Interfaces:**
- Consumes: `LicenceSnapshot` (Task 2), `PermissionCode` constants.
- Produces:
  - `public interface ILicenceGate { LicenceSnapshot Current { get; } void Set(LicenceSnapshot snapshot); event EventHandler? Changed; bool Allows(string permissionCode); }`
  - `LicenceGate` starts as `Active` (so nothing is blocked before the first load and existing tests keep passing).
  - `PermissionService(ICurrentUser, IPermissionRepository, IAuditWriter, ILicenceGate? licence = null)` - optional last parameter so the 14 existing `new PermissionService(...)` call sites in tests compile unchanged.

- [ ] **Step 1: Write the failing tests**

```csharp
public class LicenceGateTests
{
    private static LicenceGate ReadOnlyGate()
    {
        var g = new LicenceGate();
        g.Set(new(LicenceStatus.ReadOnly, null, null, false));
        return g;
    }

    [Fact] public void Read_only_denies_selling() =>
        ReadOnlyGate().Allows(PermissionCode.SaleCreate).Should().BeFalse();

    [Theory]
    [InlineData(PermissionCode.ProductRead)]
    [InlineData(PermissionCode.ReportFinancial)]
    [InlineData(PermissionCode.SettingsChange)]
    public void Read_only_allows_lookups_and_fixing(string code) =>
        ReadOnlyGate().Allows(code).Should().BeTrue();

    [Fact] public void Grace_allows_everything()
    {
        var g = new LicenceGate();
        g.Set(new(LicenceStatus.Grace, new(2026, 1, 1), MembershipPlan.Monthly, false));
        g.Allows(PermissionCode.SaleCreate).Should().BeTrue();
    }

    [Fact] public void Permission_service_denies_held_permission_when_read_only()
    {
        var user = new FakeCurrentUser();
        var repo = new FakePermissionRepository();
        repo.Grant(user.Id, PermissionCode.SaleCreate);   // use whatever the fake exposes
        new PermissionService(user, repo, new FakeAuditWriter(), ReadOnlyGate())
            .Check(PermissionCode.SaleCreate).Should().Be(PermissionOutcome.Denied);
    }
}
```

- [ ] **Step 2: Run, expect build failure**

Run: `dotnet test PharmaX.Tests.Unit --filter LicenceGateTests`

- [ ] **Step 3: Implement**

In `Check`, first line: `if (_licence is not null && !_licence.Allows(permissionCode)) return PermissionOutcome.Denied;` (returns Denied, not RequiresSupervisor: a supervisor cannot override a lapsed membership). `UserHolds` gets the same guard. `Allows` uses the allow-list from Global Constraints when `Current.IsReadOnly`, else true. `Set` raises `Changed`.

- [ ] **Step 4: Run the whole unit suite, expect PASS (existing tests unaffected)**

Run: `dotnet test PharmaX.Tests.Unit`

- [ ] **Step 5: Commit** `feat: read-only mode when membership lapses`

---

### Task 4: LicenceService - load, activate, cache

**Files:**
- Create: `PharmaX.Application/Licensing/LicenceService.cs`
- Modify: `PharmaX.Application/Abstractions/IWorkstationSettings.cs` (`PharmacySettingKey`: add the five `licence.*` keys from Global Constraints)
- Modify: `PharmaX.Application/DependencyInjection.cs` (`s.AddScoped<Licensing.ILicenceService, Licensing.LicenceService>();`)
- Test: `PharmaX.Tests.Unit/Application/LicenceServiceTests.cs`

**Interfaces:**
- Consumes: `IPharmacySettings`, `IClock`, `ILicenceGate`, `LicenceCode`, `LicenceState`.
- Produces:
  - `public interface ILicenceCache { (DateOnly? ValidUntil, DateOnly? LastSeen)? Read(); void Write(DateOnly? validUntil, DateOnly lastSeen); }` with `FileLicenceCache` (JSON at the path in Global Constraints; swallows IO errors) in the same file.
  - `public sealed record ActivationResult(bool Ok, string Message);`
  - `public interface ILicenceService { Task<LicenceSnapshot> RefreshAsync(CancellationToken ct = default); Task<Guid> GetPharmacyIdAsync(CancellationToken ct = default); Task<ActivationResult> ActivateAsync(string code, CancellationToken ct = default); }`
  - `LicenceService(IPharmacySettings settings, IClock clock, ILicenceGate gate, ILicenceCache cache, Func<string, LicencePayload?>? verify = null)` - `verify` defaults to `LicenceCode.Verify`; tests pass one bound to a test key.

Behaviour:
- `GetPharmacyIdAsync`: read `licence.pharmacy-id`; if blank, create `Guid.NewGuid()` and save.
- `RefreshAsync`: read valid-until, last-seen, code, trial-started. If no valid-until and no trial-started, save trial-started = today and valid-until = today + 14 days (trial). Evaluate state; if today > last-seen save last-seen = today. Write cache, `gate.Set(...)`, return snapshot. If any settings call throws (main computer unreachable), use the cache's values instead; with no cache, leave the gate as it is.
- `ActivateAsync`: verify -> null => `"That is not a valid PharmaX code. Paste the whole code exactly as you received it."`; pharmacy mismatch => `"This code was made for a different pharmacy."`; payload valid-until < today => `"This code has already expired (it ran until {date})."`; else save code, valid-until = max(stored, payload), clear trial, refresh => `"Membership active until {date} ({plan})."`

- [ ] **Step 1: Write the failing tests** (in-memory `IPharmacySettings` fake: a `Dictionary<string,string?>`; `FakeClock`; fake cache)

```csharp
[Fact] public async Task First_run_starts_14_day_trial()  // snapshot.IsTrial, ValidUntil == today+14, Status Active
[Fact] public async Task Activating_valid_code_sets_valid_until_and_plan()
[Fact] public async Task Older_code_does_not_shorten_membership()   // activate Dec 31 then Nov 30 -> still Dec 31
[Fact] public async Task Code_for_other_pharmacy_is_refused()       // Message == "This code was made for a different pharmacy."
[Fact] public async Task Expired_code_is_refused()
[Fact] public async Task Settings_unreachable_uses_cached_valid_until() // settings throw; cache says today+30 -> Active, not ReadOnly
[Fact] public async Task Refresh_pushes_state_to_gate()             // gate.Current.Status == snapshot.Status
[Fact] public async Task Pharmacy_id_is_created_once_and_kept()
```

Each asserts the exact field/message named in its comment.

- [ ] **Step 2: Run, expect build failure** - `dotnet test PharmaX.Tests.Unit --filter LicenceServiceTests`
- [ ] **Step 3: Implement per Interfaces/Behaviour. Register `ILicenceCache` as singleton `FileLicenceCache` in the same DI file.**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat: membership activation and offline cache`

---

### Task 5: Membership dialog, banner, sign-in refresh

**Files:**
- Create: `PharmaX.Desktop/Features/Admin/frmMembership.cs` (code-built like `frmSystemSetup`, inherits `PhxFormBase`, `RequiredPermission => PermissionCode.SettingsChange`)
- Modify: `PharmaX.Desktop/DependencyInjection.cs` (`s.AddTransient<frmMembership>();`)
- Modify: `PharmaX.Application/Security/ScreenAccess.cs` (`["membership"] = p => p.Has(PermissionCode.SettingsChange)`)
- Modify: `PharmaX.Desktop/Shell/frmMain.Designer.cs` (menu `Item("Membership", "membership")` after System Setup), `frmMain.cs` (icon + search words for `"membership"`; `SetMembership(string? text, bool urgent)` banner - a docked label at the top of the shell, amber `Expiring`, red otherwise, hidden when null; clicking it opens `"membership"`)
- Modify: `PharmaX.Desktop/Shell/IMainView.cs` (`void SetMembership(string? text, bool urgent);`)
- Modify: `PharmaX.Desktop/Shell/MainPresenter.cs` (inject `ILicenceGate`; on `Subscribe` and on `gate.Changed`: `View.SetMembership(LicenceState.BannerText(gate.Current), gate.Current.Status != LicenceStatus.Expiring)` and re-run `ApplyAccess`; `case "membership": Open<Features.Admin.frmMembership>(key); return;`)
- Modify: sign-in path (where the login presenter completes successfully) - call `ILicenceService.RefreshAsync()` before the shell opens.
- Modify: `PharmaX.Desktop/Features/Assistant/GuideScreens.cs` if it must list every form (follow the `system-setup` entry).
- Test: `PharmaX.Tests.Unit/Presenters/` - one test that `MainPresenter` calls `SetMembership` with the banner text when the gate changes to Grace (follow existing presenter test patterns).

Dialog contents, top to bottom: **Pharmacy ID** (read-only text, monospace, `LicenceCode.FormatPharmacyId`) + **Copy** button; **Status** line (`Monthly membership, active until 18 Feb 2027` / `Trial, ends ...` / the banner text); multiline **Activation code** box; **Activate** (default button) and **Close**. Activate shows `ActivationResult.Message` in green when Ok, red otherwise, and refreshes the status line.

- [ ] **Step 1: Write the presenter test; run, expect fail**
- [ ] **Step 2: Implement the view/presenter/menu changes**
- [ ] **Step 3: `dotnet build PharmaX.sln` and `dotnet test PharmaX.Tests.Unit`, expect PASS** (check `RoleMatrixTests` / `Architecture` tests: new menu key may need adding to their expected lists)
- [ ] **Step 4: Run the app (developer machine), confirm: banner shows on trial expiry date set via `FakeClock`/settings; Membership opens; pasting a test code activates.**
- [ ] **Step 5: Commit** `feat: membership screen and banner`

---

### Task 6: Patient screen opens but cannot save when read-only

**Files:**
- Modify: the patient save path in `PharmaX.Application/Patients/PatientService.cs` (the method(s) that create/update a patient)
- Test: `PharmaX.Tests.Unit/Patients/` - new test file `PatientReadOnlyTests.cs`

**Interfaces:**
- Consumes: `ILicenceGate` (inject; optional ctor param `ILicenceGate? licence = null` like Task 3).

- [ ] **Step 1: Failing test:** with a read-only gate, saving a patient throws `PermissionDeniedException` with message `"PharmaX is read-only until the membership is renewed."`; with an Active gate it saves.
- [ ] **Step 2: Run, expect FAIL**
- [ ] **Step 3: Add the guard at the top of each write method.**
- [ ] **Step 4: Run, expect PASS**
- [ ] **Step 5: Commit** `feat: block patient edits while read-only`

---

### Task 7: Code-maker tool + real key

**Files:**
- Create: `tools/PharmaX.CodeMaker/PharmaX.CodeMaker.csproj` (console, net8.0, references `PharmaX.Application`), `tools/PharmaX.CodeMaker/Program.cs`, `tools/PharmaX.CodeMaker/README.md`
- Modify: `PharmaX.sln` (add project; `dotnet sln add`)
- Modify: `.gitignore` - add `private.key` and `issued.csv`
- Modify: `PharmaX.Application/Licensing/LicenceCode.cs` - `PublicKey` set to the generated value

Commands:
```
codemaker keygen                         -> writes private.key (PKCS#8 Base64) beside the exe; refuses if one exists; prints the public key
codemaker issue <pharmacy-id> <monthly|quarterly|yearly> [--name "Pharmacy name"]
                                         -> valid-until = max(today, last valid-until for that id in issued.csv) + plan months (AddMonths)
                                         -> prints the code and a ready-to-send message; appends date,pharmacyId,name,plan,validUntil,code to issued.csv
codemaker list                           -> prints issued.csv as a table, latest valid-until per pharmacy
```

- [ ] **Step 1: Test** in `PharmaX.Tests.Unit/Application/LicenceCodeTests.cs`: `Renewal_extends_from_current_end` - extract the date rule as `public static DateOnly LicenceCode.NextValidUntil(DateOnly today, DateOnly? currentEnd, MembershipPlan plan)`; assert `(2026-11-18, 2026-12-10, Monthly) -> 2027-01-10` and `(2026-11-18, 2026-10-01, Quarterly) -> 2027-02-18`.
- [ ] **Step 2: Implement `NextValidUntil`, the tool, run test, PASS**
- [ ] **Step 3: Run `codemaker keygen` once on the vendor's PC; paste the public key into `LicenceCode.PublicKey`.** (The implementer stops and asks Adrian to run keygen himself, so the private key is created only on his PC; until then a placeholder key stays and the plan notes it.)
- [ ] **Step 4: End-to-end:** issue a code for the dev machine's Pharmacy ID, paste into Membership, see `Membership active until ...`.
- [ ] **Step 5: Commit** `feat: vendor code-maker tool` (never commit `private.key`)

---

### Task 8: Docs

**Files:**
- Modify: `README.md` - "Membership" under Features; `licence.json` in "Where PharmaX keeps things".
- Modify: `docs/How-to-use-PharmaX.md` - short "Entering or renewing your membership" section.

- [ ] **Step 1: Write, commit** `docs: membership activation`
