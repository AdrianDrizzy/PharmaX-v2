# Change request: Edit the price when dispensing

**Date received:** 18 November 2026 (voice note)
**Status:** Waiting for the pharmacy to confirm

---

## 1. What we understood from your voice note

> "Can the system give us authorisation to edit the price when dispensing? Sometimes the price comes out as 2.52 and we want to charge 2.50. Before we save, we need a way to adjust it."

Our understanding:

- When you dispense, the system works out the charge on its own. Sometimes that charge is an awkward figure, for example **2.52**.
- You want to charge a round figure instead, for example **2.50**, **before the dispensing is saved and issued**.
- You want this to be controlled, so only authorised people can do it.

**Please correct us if any of this is wrong.**

---

## 2. How PharmaX works today

### 2.1 How the dispensing charge is worked out

For a cash patient, when tablets, capsules or millilitres are counted out of a bigger pack, PharmaX charges the patient's share of the pack price:

> pack price × quantity handed over ÷ units in the pack

That figure is then **rounded UP** to the step set in **System Setup → "Dispensing charges round up to"**. The choices are 0.01, 0.05, 0.10, 0.50 and 1.00. If nobody has set it, the system uses 1.00.

| Step set in System Setup | Exact share 2.52 is charged as |
|---|---|
| 0.01 | 2.52 |
| 0.05 | 2.55 |
| 0.10 | 2.60 |
| 0.50 | 3.00 |
| 1.00 | 3.00 |

The system **only rounds up, never down**. No setting turns 2.52 into 2.50 on its own.

Things that are **not** rounded:
- An item handed over whole (an injection, a bottle) is charged its own price.
- A medical aid tariff is never rounded, because the claim has to match the scheme's figure.

### 2.2 You can already change the charge before saving

The dispensing screen already lets you change an item's charge before it is issued:

1. On the **Dispense** screen, **double-click the item's amount** (Tariff or Co-payment column). You can also select the item and click **Change**.
2. Type the new amount, for example 2.50.
3. Type a reason, for example "Rounded to 2.50". The reason is required and is kept on the record.
4. If the person dispensing is not allowed to change charges, the system asks someone who is allowed (a pharmacist, pharmacist assistant, manager or administrator) to sign in and approve it. A user cannot approve their own change.

Every change is written to the audit trail with the original price, the new charge, the reason and who approved it.

**Who is allowed by default ("Charge other than the price"):** Administrator, Manager, Pharmacist, Pharmacist Assistant. **Not** Cashier or Stock Controller. You can change this under user roles.

You can also change an item's charge **after** it is issued from the Labels screen (**Change item...**), but only a pharmacist or pharmacist assistant can do it. You cannot change it there if the item has already been claimed from a medical aid.

---

## 3. What we need you to confirm

Please answer each question. "Yes/No" or a short note is enough.

**Q1. Is the change above (double-click the amount, type 2.50, give a reason) what you need?**
- [ ] Yes. We did not know about it. Just show us / give our staff the permission.
- [ ] No. It works, but it is too slow to do every time (see Q2).
- [ ] No. Something else (please describe).

**Q2. Would you prefer the system to round the figure for you automatically?**
For example, a setting so that 2.52 becomes 2.50 on its own, with no typing.
- [ ] Yes, round to the **nearest** step (2.52 → 2.50, 2.53 → 2.55 with a 0.05 step)
- [ ] Yes, always round **down** (2.52 → 2.50, 2.58 → 2.55)
- [ ] No, keep rounding **up**. We will change the price by hand when needed.

**Q3. If you want automatic rounding, what step?**
- [ ] 0.05 [ ] 0.10 [ ] 0.50 [ ] 1.00 [ ] Other: ______

**Q4. Which items should this apply to?**
- [ ] Only items counted out of a pack (tablets, capsules, ml). This is the current rule.
- [ ] All cash items, including whole packs
- [ ] Medical aid items too. *Note: we do not recommend this, because the claim must match the scheme's tariff.*

**Q5. Who is allowed to change a price by hand?**
- [ ] Keep it as it is now (Pharmacist, Pharmacist Assistant, Manager, Administrator. Others need approval.)
- [ ] Anyone who dispenses, without approval
- [ ] Only a pharmacist or manager
- [ ] Other: ______

**Q6. Should a reason still be required for a manual price change?**
- [ ] Yes, keep it required (recommended, for audit)
- [ ] Yes, but offer a quick pick such as "Rounded" so staff don't have to type
- [ ] No reason needed

**Q7. Should there be a limit on how much the price can be changed?**
- [ ] No limit
- [ ] Only small changes without approval (for example up to 0.10 or 5%). Bigger changes need a manager.
- [ ] Other: ______

**Q8. Can you send one or two real examples?**
For example: the medicine, the quantity, the price shown and the price you wanted to charge. Screenshots are welcome.

---

## 4. What we suggest

If what you need is to **turn figures like 2.52 into 2.50 without extra steps**, we suggest:

1. A new System Setup option next to the current one: **"Round dispensing charges: Up / Nearest / Down"**, together with the step you choose (Q3). Rounding stays up unless you change it, so nothing changes until you choose.
2. The manual change described in 2.2 stays as it is for unusual cases, with the permission and reason rules you confirm in Q5 and Q6.

Once you confirm, we will make the change and send you an update to test before it goes live.
