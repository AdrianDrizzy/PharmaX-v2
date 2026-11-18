# codemaker

Makes PharmaX membership activation codes. For the vendor only: it is not part of the installer.

## Once, ever

```powershell
dotnet run --project tools/PharmaX.CodeMaker -- keygen
```

This writes `%APPDATA%\PharmaX.CodeMaker\private.key` and prints the public key. Paste the public key into `LicenceCode.PublicKey` (`PharmaX.Application/Licensing/LicenceCode.cs`) and ship that build.

**Back up `private.key` somewhere safe, off this PC.** If it is lost, no new codes can be made for any PharmaX already installed. If it leaks, anyone can make codes.

## Each payment

1. The pharmacy sends its **Pharmacy ID** (PharmaX > Admin > Membership > Copy).
2. Once the payment is confirmed:

   ```powershell
   dotnet run --project tools/PharmaX.CodeMaker -- issue <pharmacy-id> monthly --name "BethRapha Pharmacy"
   ```

   The plan is `monthly`, `quarterly` or `yearly`. Paying early carries on from the current end date.
3. Send the printed message, which includes the code, by WhatsApp or email.

`codemaker list` shows each pharmacy's current end date. Every code issued is logged in `%APPDATA%\PharmaX.CodeMaker\issued.csv`.
