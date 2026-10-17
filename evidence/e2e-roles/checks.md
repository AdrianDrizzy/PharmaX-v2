# Questions for the pharmacy

Things that worked but need a decision, or a figure worth a second look.

1. **What should a cashier see when collecting a script?** (C-C5v, all four collections)
   - The queue shows the patient, script number, dispensing number, item count, amount owed and pharmacist. That much is needed to take the money.
   - Take Payment then opens a till screen listing every medicine with its quantity, for example "NIFEDIPINE CALCIGARD (TORRENT) 20MG x 45" or "TRAMADOL ... x 28". The receipt lists them too.
   - The brief says the cashier "is not supposed to be able to view anything that is dispensed". Should the collect screen and the receipt show only "Script SC-… (n items)" to a cashier?

2. **Refunding the difference after changing an imported item.** (A-D8s, P-D8s, S-D8s)
   - Changing a collected dispensing tells the operator to give the difference back through Returns, for example "give 0.08 back through Returns".
   - For a script collected in the old system, the original receipt is the old system's. These were left unrefunded in the run.
   - Should these small differences be refunded at the till, written off, or should changing a collected dispensing be limited?

3. **Cash-ups report row count.** (A-D13)
   - Cash-ups over 01/01/2025 to 15/10/2026 lists **499 rows**.
   - The import report says 764 cash-up lines over 341 sessions.
   - The report seems to group the lines differently. Please confirm it shows what you expect before relying on it.

4. **Dispensing value after the changes.** (A-D13, P-D13)
   - Dispensing by Month over the old data first read **51,775.88** (import: 51,775.96), then **51,775.81**.
   - The differences are exactly the changes made in D8: 0.08 by the administrator and 0.07 by the pharmacist. It shows that changing an imported dispensing changes the history it reports on.
   - Is that what you want, or should history before the change-over be read-only?

5. **Product totals.** (D3)
   - The Products list counts **2,316** items, the inactive ones included, while the home tile says **1,328**, the active ones only.
   - The cashier's list counts **1,508**, again with inactive ones, against a home tile of 779.
   - Both are right; the two screens just count differently.

6. **Schedules of imported products.**
   - A cashier searching "AMOX" finds two amoxicillin lines that came across at schedule 0: 1277 AMOXYCILLIN CAPSULES 42s 250MG and 466 AMOXYCILLIN SUSPENSION 100s 125MG/5ML. Antibiotics usually need a script.
   - The import report also lists 41 products scheduled for dispensing in the files but sold over the counter, which it kept at the till.
   - The cashier sees whatever is below schedule 3. Please review these schedules under Inventory → Products.
