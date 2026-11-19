# How to use PharmaX

Click paths and the words on the buttons, for every menu. Written from the screens as they are. Ask PharmaX reads this file: a question about how to use the program is answered with the matching section, word for word, and is not sent to the model.

Each section's `Words:` line is what the question is matched on. Keep those words to what staff actually type.

Ask PharmaX draws the screen a section's menu path opens (**Dispensary > Dispensing**), or a window that screen opens (payment, edit, count), under every line whose **bold** name is a button, tick box, tab or heading on it, with it outlined in red. Write a button's name in bold exactly as the screen shows it, without its F-key, or its step gets no picture.

**How to read a step.** "Point of Sale > Sell (F3)" means the menu, then the entry. The keys in brackets are the menu shortcuts. On a button, the underlined letter is Alt plus that letter. On most screens the function keys are handed out from the button words when the window opens, so follow the button name, not a remembered F-key. The till (Sell) is the exception: its keys are fixed and are listed in that section. A button whose caption already writes a key ("X-read Ctrl+X", "Preview (F12)") keeps that key.

**Who sees a menu.** A menu entry this person may not use is grey, and the home tile says "Not permitted for this user". The same check applies to the home tiles, the shortcuts, and the "Go to any screen" box.

| | Administrator | Pharmacist | Pharmacist assistant | Cashier | Stock controller |
|---|---|---|---|---|---|
| Sell, collect, open own till | Yes | Yes | Sell and collect; till yes | Yes | No |
| Cash up own till | Yes | Yes | Yes | Yes | No |
| Cash up someone else's till, end of day | Yes | Yes | Yes | No | No |
| Dispense, patients, labels | Yes | Yes | Yes | No | No |
| Reverse a dispensing, merge patients | Yes | Yes | No | No | No |
| Purchase orders, goods receipt | Yes | Yes | Yes | No | Yes |
| Products | Whole catalogue, edit | Whole catalogue, edit | Whole catalogue, edit | Over the counter, view only | Whole catalogue, add; cannot change a selling price |
| Stocktake post, stock adjustments, write-offs, transfers | Yes | Count only, cannot post | Count only, cannot post | No | Yes |
| Reports | All | Operational and clinical | Operational | No | Operational |
| Accounts, users, system setup | Yes | No | No | No | No |

---

## 1. Getting around

1. Sign in on **Log in**: type **Username**, **Password**, press **Log in**. **Cancel** leaves the program.
2. The home screen shows tiles for the work this person may do. Click a tile, or use its shortcut (F3 Sell, F11 Collect, F2 Dispense, and so on).
3. Or click the box **Go to any screen** (Ctrl+K) and type a few words, for example "cash up", "patients", "receive".
4. The menus along the top are File, Dispensary, Point of Sale, Inventory, Accounts, Reports, Stocktake, Administration, View, Help.
5. A long list shows 100 rows at a time, with **|< First**, **< Previous**, **Next >** and **Last >|**. Previous is Ctrl+Page Up, Next is Ctrl+Page Down, First is Ctrl+Shift+Page Up, Last is Ctrl+Shift+Page Down. **Export** on that list takes every row, not only the page on screen.
6. The status bar names the person, the branch and till, and whether a till session is open. If the till was opened on an earlier day it reads **Session open since** that day, and sign-in asks whether to cash it up now. **Yes** opens Cash Up. A night shift that runs past midnight is still one shift; the question does not stop selling.
7. **File > Log Off** returns to the sign-in window. **File > Change Password** asks for the new password twice and **Change**. **File > Exit** (Ctrl+X) closes PharmaX.
8. **View > Zoom** is Zoom in (Ctrl++), Zoom out (Ctrl+-), Actual size (Ctrl+0). **View > Aspect ratio** and **View > Resolution** change this window only.
9. **Help > Ask PharmaX** (Ctrl+Shift+A) answers a question or, for a few jobs, opens the screen and talks it through. See section 10.
10. **File > Printer Setup** is for this computer: choose the receipt printer and the label printer, **Test receipt**, then **Save**. Tick **Every dispensary computer prints labels on this label printer** only on the computer whose label printer the others should share. **Find on a computer...** looks for a printer that is not in the list.

---

## 2. One working day, end to end

This is the chain. Each piece is written out again, with every button, in the sections after it.

1. **File > Till Session** (Ctrl+T). Count the float into the notes and coins. Press **Open session**.
2. **Inventory > Purchase Orders** (F7). **Raise order**. Choose the supplier (**Supplier...** or type the name). **Add line** for each product, quantity and cost. **What to order** lists what is short, if you want it. **Raise order**.
3. **Inventory > Goods Receipt** (F8). **Supplier...**. **Order...** the order just raised, or leave it and **Add line** for a delivery with no order. Type the invoice number and the date received. On each line: quantity, unit cost, and for a batch-tracked item the batch and expiry (day first, `31/12/2029` or `12/2029`). Check **Resolved - what will be posted**. The **Sell at** column is the new selling price: cost plus the markup from System setup (50% until it is changed). A line with a zero cost does not change the price. Type the invoice total. **Post receipt**.
4. **Inventory > Products** (F6). Search the product. Stock and the new batch are there. **Edit** to change it, or **Add** for a new one. Leave the selling price at 0 and a cost above 0, and Save fills the selling price the same way (cost plus markup).
5. **Dispensary > Patients** (F4). Search. **Add** for a new file, or open one and **Save**.
6. **Dispensary > Script Maintenance**. Find the patient. **Use again** on an earlier script, or capture a new one: prescriber, **Add** items (code or name, quantity in tablets not packs, directions). **From history...** to pull items off an earlier script. **Save**.
7. **Dispensary > Dispensing** (F2). **Find script**. Check the lines and any clinical warning. **Change item** or **Remove item** before the issue if the patient is taking only part of it. Leave **Print labels** and **Print trailer label** ticked. **Issue**.
8. **Point of Sale > Scripts to Collect** (F11). Find the patient or the script number. **Take Payment**. Choose the tender and the amount. **Pay**. The receipt prints. The script leaves the queue. Stock moved when it was issued, not again here.
9. **Point of Sale > Sell** (F3), for something over the counter. **F2 Find item**, **F4 Quantity**, **F6 Pay**, tender, amount, **Pay**.
10. During the day, money taken out of the drawer (drawings, lunch, an expense): **File > Cash Up** (Ctrl+U), or the till session, then **Money out...**. Choose the till, **For**, **Amount**, **Note**, **Add**.
11. **File > Cash Up** (Ctrl+U). This PC's till is the first shift, marked **(this PC)**. Check the totals against **Cash in drawer**. **Count cash and close till...**. On the count window, type how many of each note and coin, type **EcoCash per statement** and **Card collected** (blank if none), **Declare**, then **Cash up and close** (Ctrl+Z). A variance over 20.00 asks for a reason. The Z-read shows. **History...** finds a cash-up from another day.
12. A pharmacist, an assistant or the administrator then runs **File > End-of-Day Cash-Up** for every till that day, and **Accounts > Day-End Posting** to put the day into the books: **Look at day**, **Prepare journal**, **Export CSV** if the accountant wants the file.

---

## 3. The till and the money

### Open the till

Words: open the till, till session, float, open session, x-read, no sale

**File > Till Session** (Ctrl+T), or the **Till session** tile.

1. The window shows the session, or that none is open.
2. Count the float: type how many of each note and coin under **Count the drawer**. **Counted in drawer** adds them up.
3. **Open session**. A second open on the same till is refused: close the first one first.
4. **X-read** (Ctrl+X) prints what the drawer should hold so far and does not close the till.
5. **Money out...** records drawings, lunch or another expense. See below.
6. **Declare** saves the count. **Cash up and close** (Ctrl+Z) closes the session. The same close can be done from Cash Up, which is the screen that lists every till.
7. **Close** leaves the window without closing the session.

**No sale.** On Sell, **F11 No sale** opens the drawer with no sale. A reason is asked and kept.

### Sell over the counter

Words: sell, point of sale, find item, take payment, discount, void a line, no sale, lock the till, customer account

**Point of Sale > Sell** (F3), or the **Sell** tile. The till must be open.

Keys on this screen only:

| Key | Button | What it does |
|---|---|---|
| F2 | Find item | Search or scan. Type a code, a name or a barcode, then Enter. |
| F3 | Customer | Put the sale on a patient. Cash sale is the default. |
| F4 | Quantity | How many of the selected line. |
| F5 | Discount | A discount on the line, up to this person's limit. Over the limit, a supervisor signs in on **Authorise**. |
| F6 | Pay | Opens payment. |
| F8 | Hold | Parks this basket. |
| F9 | Recall | Brings a held basket back, from any till. |
| F10 | Quotation | Brings an issued quotation onto this sale. |
| F12 | Reprint | Prints the last receipt again. |
| F1 | Lock | Locks the till. The next person signs in. |
| F11 | No sale | Opens the drawer. A reason is asked. |
| Esc | Void line | Takes the selected line off. A reason is asked. Several lines can be selected. |
| F7 | Close | Closes the till window. An empty sale is dropped. A sale with lines asks before it is lost. |

**Pay:**

1. **Tender**: Cash, Card, EFT, EcoCash, Account, Medical Aid or Voucher.
2. **Amount**. Cash can be more than the total; the change is shown.
3. **Reference** where the tender needs one (a card slip, an EcoCash reference).
4. Add more than one tender to split the payment. **Remove** takes a tender off.
5. **Pay**. Account is refused until a customer is chosen (F3). The receipt prints.

A cashier's search does not find a dispensary item (schedule 3 and above).

### Hold and recall

Words: hold a sale, recall a sale, parked sale

1. With lines on the sale, **F8 Hold**.
2. Later, **F9 Recall**, pick the basket, and it comes back onto the sale.
3. Finish it with **F6 Pay**.

### Quotation

Words: quotation, quote a customer, quote onto a sale

**Point of Sale > Quotation.**

1. **Customer**, then **Start quotation**.
2. Add lines the same way as a sale. **Remove line** takes one off (several can be selected).
3. **Print**. **Open existing** brings one back.
4. On Sell, **F10 Quotation** puts an issued quotation onto the sale, then **F6 Pay**.

### Returns and credit notes

Words: return, credit note, refund, back on the shelf

**Point of Sale > Returns and Credit Notes** (F5). A cashier is asked for a supervisor (**Authorise**: username and password) unless they already hold that right.

1. Type the receipt number (it looks like `S-001-000001`) and press **Find**. The sale's lines appear. Until then the screen says **(no sale found yet)**.
2. On each line, type how many are coming back. **Back on shelf** ticked puts that quantity back into stock. A line note says why that line.
3. **Refund and credit**. The refund window uses the same tenders, and its button reads **Refund**. The credit total is shown before you post.

### Scripts to collect

Words: collect a script, scripts to collect, take payment for a script, delete a script

**Point of Sale > Scripts to Collect** (F11), or the **Collect** tile, or the waiting count on the status bar.

1. **Find**: patient name, script number or dispensing number.
2. **Take Payment**. Same payment window as a sale. **Pay**.
3. The row leaves the queue. The receipt shows the script.
4. **Delete selected** reverses a script that was dispensed and never collected, so the medicine goes back to stock. A script waiting more than 24 hours is cleared the same way the next time this list is opened or refreshed.
5. **Refresh** re-reads the queue.

### Receipts

Words: reprint a receipt, copy of a receipt, hide a receipt

**Point of Sale > Receipts (Reprint).**

1. **From** and **To** the dates. **Find** a receipt number or a cashier.
2. Select the row and press **Reprint**, or double-click it. The copy is marked as a copy, and the reprint is recorded.
3. **Hide...** takes a receipt off the ordinary list. A reason is asked. **Show hidden** brings those back. Both are only there for someone allowed to hide a receipt.

### Money out of the drawer

Words: money out, drawings, lunch, expense from the till, void a payout

From Cash Up or from Till Session, **Money out...**.

1. **Till**: the open tills this person may take money from. This PC's till is listed even when someone else opened it.
2. **For**: Drawings, Lunch or another expense.
3. **Amount**, **Note** (what it was for), **Add**.
4. It comes off the cash the cash-up expects, and it prints on the cash-up slip and the Z-read.
5. **Void...** on a wrong entry, while the till is still open. A reason is asked. Several rows can be voided with one reason.

### Cash up and close

Words: cash up, close the till, count the drawer, z-read, cash-up history, on the cashier's behalf

**File > Cash Up** (Ctrl+U).

1. **Day**: today, unless an earlier day still needs cashing up. The date cannot be in the future.
2. **Shift**: every till that was open at any time that day. This PC's till is first and marked **(this PC)**, with the name of whoever is signed in on it. A closed till reads **(closed)**.
3. Read **Totals for the shift**, **By tender** and **By cashier**. **Cash in drawer** is the float plus the cash taken, minus what was paid out.
4. Tick **Show every transaction** to list each sale, refund, account payment and payout. The printed slip and the exports then list them too.
5. **Money out...** while the till is still open.
6. **Print slip (F9)** shows the slip first, then prints it.
7. **Export PDF** and **Export Excel** save the same figures.
8. Press **Count cash and close till...**. Your own till is titled **Count and close**. Someone else's says **on the cashier's behalf**. A cashier cannot count or close another person's till.
9. Type how many of each note and coin you have.
10. Type **EcoCash per statement** from the merchant statement. Leave it blank if none came in.
11. Type **Card collected** from the card machine. Leave it blank if no card was taken. A blank is counted as nothing, not as whatever the till expected.
12. Press **Declare**. Then press **Cash up and close** (Ctrl+Z).
13. If the count is more than 20.00 out, type the reason it asks for.
14. The Z-read opens. Press **Print**, or close it and keep it.
15. **Refresh** re-reads the day and stays on the same shift.
16. **History...** lists past cash-ups. **From** and **To** start at the last 30 days. Press **Show**.
17. The newest is at the top. A shortfall is shown in red. Double-click a row, or press **Open on Cash Up**, to see that shift here and print its slip again.

### End of the day

Words: end of day, end-of-day cash-up, every till

**File > End-of-Day Cash-Up.** A cashier does not see it.

1. **Day**, then **Show (F12)**.
2. Every till that day, and each cashier's own takings, for that day only. A till left open overnight does not pull earlier days in.
3. **Money out...** lists what left the drawers. **Export Excel** and **Export PDF** save it.

---

## 4. Dispensary and patients

### Dispense and issue

Words: dispense, issue a script, find a script, suspended script, change an item before issue

**Dispensary > Dispensing** (F2), or the **Dispense** tile.

1. **Find script**. Pick the patient, then the script.
2. The lines show, with any clinical warning.
3. **Change item** before the issue: a different quantity or charge. It is priced and checked again.
4. **Remove item** takes a line off before the issue. Several can be selected.
5. **Print labels** and **Print trailer label after the item labels** are ticked by default.
6. **Issue**. Labels print. Status becomes Issued. The script appears under Scripts to Collect.
7. **Reprint labels** prints them again.
8. **Quote** prices it for a customer who has not decided, and saves the quote.
9. **Suspended** lists scripts kept to finish later. Pick one to carry on. Closing an unfinished dispensing asks whether to keep it under Suspended.
10. **Close**.

**Dispensary > Re-Print Script** opens the same labels screen for a script already issued.

### Capture a script, and use an old one again

Words: capture a script, script maintenance, use again, new script, from history

**Dispensary > Script Maintenance.**

1. **Find** the patient, then **Choose** that patient. The buttons are **Capture new**, **Edit script**, **Cancel script**, **Refresh**, **Quote** and **Use again**.
2. **Use again** on an earlier script. The new script is dated today, the prescriber is filled in, and the quantities are what was last handed over. Anything no longer in the catalogue is left out.
3. Or **Capture new**. Prescriber (name, clinic, code or practice number). **Prescriber set a validity date** only when there is one.
4. **Add** an item: code or a few words of the name, quantity in tablets, capsules or ml (not packs), directions (on the label). **Generic** ticked means a generic may be given.
5. **From history...** to copy items off an earlier script.
6. **Remove** takes an item off.
7. **Save**. The script is Captured, ready for Dispensing.
8. **Quote** prices the script without dispensing it.

### Labels

Words: print labels, reprint a label, reverse a dispensing, change an item, claim form, re-print script

**Dispensary > Labels.** **Dispensary > Re-Print Script** opens this same screen.

1. Today's labels are listed. **Find patient** for an older one. **Today** comes back to today.
2. **What prints** previews a label. **Print labels** prints the selected one.
3. **Reverse dispensing** (pharmacist and administrator; hidden for an assistant). A reason is asked. It cannot be undone. The script leaves Scripts to Collect and the stock comes back.
4. **Claim form** prints the medical-aid claim for that dispensing.
5. **Change item...** after the issue: quantity, and the amount if it should change, then a reason. The patient then pays the difference or is refunded, as the message says. A line already claimed from a medical aid cannot be changed; reverse it and dispense it again.
6. **Use again...** starts a new script from this dispensing.
7. **Deactivate** retires a label template.
8. Tabs: **Dispensed**, **Pre-packs**, **Template Label**. **Pre-pack stickers...** prints stickers for packs made up in the dispensary.

### Patients

Words: add a patient, find a patient, allergies, medical aid, patient documents, deactivate a patient

**Dispensary > Patients** (F4), or the **Patients** tile.

1. **Search**. The box takes a name, a file number or a phone number. **Show deactivated** includes files taken off the list.
2. **Add**, or **Edit** the selected row. **Deactivate** takes a file off the list; on a file already off, the same button reads **Reactivate**. **Export** saves the list. **Documents** opens the scans kept for that patient: **Add scan...**, **Open**, **Remove**. The scans stay on this computer, so they need to be in the pharmacy's backup. **Choose** is only there when another screen opened this list to pick a patient.
3. The file has three tabs.
   - **Identity.** **File number** is given on save and cannot be typed. **Surname**, **First names**, **ID number**, **Telephone**, **Email**, **Address**. Tick **Date of birth known** and the date can be set; the age is shown beside it. The date cannot be in the future.
   - **Clinical.** **Weight (kg)**, **Pregnant**, **Breastfeeding**. **Allergies**: **Add**, type it, **Remove** to take one off (several can be selected). **Chronic conditions**. **Notes**.
   - **Medical aid.** **Scheme** (chosen from the list, not typed), **Membership no.** as on the card, **Dependant code**.
4. **Save**. **Cancel** leaves the file as it was.

### Merge two patient files

Words: merge patients, duplicate patient

**Dispensary > Merge Patient Files.** Pharmacist or administrator.

1. Choose the file to keep and the **Duplicate to merge**.
2. **Reason**.
3. **Merge**. Scripts, dispensings and accounts move onto the file that is kept.

### Dispensary quote

Words: dispensary quote, save a quote, create a script from a quote

**Dispensary > Dispensary Quote.**

1. **Customer**.
2. **Find product** and add the items. **Remove line** takes one off. The total is on the window.
3. **F8 Save quote**. **F9 Resume quote** brings a saved one back. Until it is saved the number reads **Not saved**.
4. **Print**.
5. **F6 Create script** when the customer goes ahead. Then dispense that script.

### To follow

Words: to follow, owed medicine, part supply

**Dispensary > To Follow.** Lists medicine owed to a patient (a part-supply). It shows since when, the patient, the telephone, what is owed and the dispensing number.

### Drug returns

Words: drug return, medicine brought back

**Dispensary > Drug Returns** opens **Returns and Credit Notes**. A medicine coming back from a patient is credited on that same screen.

### Reference lists

Words: tariff, medical aid society, prescriber, dosage code, add a doctor

**Dispensary > Tariffs**, **Medical Aid Societies**, **Prescribers**, **Dosage Codes**.

1. **Search**.
2. **Add** or **Edit**.
3. **Code**, **Name**, and for a tariff the product (**Find...**), the price and the dates. **Until further notice** when it has no end date.
4. **Save**.

---

## 5. Stock and suppliers

### Products

Words: add a product, edit a product, change a price, barcode, on hand, schedule, reorder level, batch tracked

**Inventory > Products** (F6), or the **Products** tile.

1. **Search**: code, description or generic name. **Show batches and expiry** adds the batch rows. **Show deactivated** includes products taken off sale.
2. **Add** or **Edit**. A cashier's button reads **View** and cannot save, and a cashier's list is over-the-counter items only. **Deactivate** takes the selected products off sale. **Export** saves the whole search, not just the page on screen. **Refresh** re-reads it. **Choose** is only there when another screen opened this list to pick a product.
3. On the product itself:
   - **Code**, **Description**, **Generic name**.
   - **Retail price**, **Cost price**. Leave the retail price at 0 with a cost filled in and **Save** sets the selling price to cost plus the markup.
   - **VAT rate %**, **Tax code**.
   - **Schedule**, from 0 to 8. Schedule 3 and above is a dispensary item and does not appear on a cashier's list or at the till.
   - **Reorder level**. Stock at or under this is a low-stock alert and is what **What to order** suggests.
   - **Pack size** and **Units per pack** (how many tablets, capsules or ml the priced pack holds). A script is priced from this.
   - **Department**, **Category**, **Manufacturer**, each chosen from its list.
   - **Barcodes**. One per line, or separated by commas. Scan them; Enter adds a line rather than saving.
   - **Batch tracked (FEFO applies)**. The till and the dispensary then take the batch that expires first. **Active (offered for sale)** unticked keeps the product for its history and stops it being sold.
   - **On hand**. Changing this number posts the difference as a stock adjustment. **Reason** is required: **Counted on the shelf**, **Damaged or broken**, **Expired**, **Found**, **Correcting a mistake**, **Opening stock**, or type one. Once the product is batch tracked, On hand is the total of the batches, and each batch is counted on its own row under **Batches and expiry**.
4. **Save**. **Cancel** drops the edits.

### Stock alerts

Words: stock alert, expiring, out of stock, low stock, reorder

**Inventory > Stock Alerts** (F12), the **Stock alerts** tile, or the count on the status bar.

1. **Show** is one of **Everything**, **Expired**, **Expiring soon**, **Out of stock**, **Low stock** (on hand at or under the reorder level).
2. **Open Product** goes to that product. **Refresh** re-reads the list. **Delete** clears a line that has been dealt with (pharmacist, assistant, administrator).
3. At sign-in, **Stock assistant** opens when something needs ordering. **Open Stock Alerts** or **View draft orders**. A draft is not sent until someone opens it and presses **Send to supplier**.

### Purchase orders

Words: purchase order, order stock, order from a supplier, what to order, send an order, draft order

**Inventory > Purchase Orders** (F7), or the tile.

1. The list is **Orders still expecting stock**. Select one and its **Lines** show underneath: product, quantity, unit cost, total. **Refresh** re-reads it. **Cancel order** stops one that has not been received. **Choose** is only there when Goods Receipt opened this list to pick an order.
2. **Raise order**.
3. **Supplier...**, or type the name. **Expected** date.
4. **Add line**: code or name, quantity in packs, unit cost. **Remove line** takes one off. **What to order** suggests what is at or under its reorder level.
5. **Raise order**. It is numbered and sent, and it appears on the list until the goods receipt posts it.
6. A draft that was prepared by the stock assistant is not on that list yet. Open it from the stock assistant, **View draft orders**. Change a quantity (0 takes the line off). **Send to supplier**, **Export PDF**, **Export Excel**, **Discard draft**, or **Discard several...**. Nothing is sent until **Send to supplier**.

### Goods receipt

Words: goods receipt, receive stock, book in a delivery, sell at, post a receipt, invoice

**Inventory > Goods Receipt** (F8), or the tile.

1. **Supplier...**.
2. **Order...** to receive against an order, or leave it on (none).
3. Invoice number, **Received on**.
4. **Add line** for anything not on the order. Quantity, unit cost, batch, expiry. Expiry is day first (`31/12/2029`) or a month (`12/2029`, taken as the last day of that month).
5. **Remove line** takes a line off.
6. Read **Resolved - what will be posted**. **Sell at** is the price the item will sell at after this delivery. **Lines total** should match **Invoice total**; the variance is shown.
7. **Post receipt**. Stock goes up. The order, if there was one, becomes Received.

### Stock on hand, and the stock reports

Words: stock on hand, check stock, transfers report, breakages report, expired stock report, price changes report

These open a report, not a data-entry window. Pick it, set the dates, **Preview (F12)**, then **Export to Excel** or the PDF. See Reports.

- **Inventory > Stock on hand**
- **Inventory > Transfers report**
- **Inventory > Breakages report**
- **Inventory > Expired stock report**
- **Inventory > Price changes report**
- **Administration > VAT Summary**
- **Administration > Stock Transactions**

### Stock adjustments, returns to a supplier, moves

Words: stock adjustment, breakage, write off expired, transfer stock, return to supplier, move stock

**Inventory > Stock Adjustments**, **Return to Supplier**, and **Move Stock and Pre-packs** open the same window on a different reason. Posting them needs the stocktake-post right (administrator or stock controller). A pharmacist corrects a product's quantity from the product itself, with a reason, which is a different right.

1. The reason is one of: **Breakage - damaged and thrown away**, **Expired stock written off**, **Transfer out to another branch**, **Transfer in from another branch**, **Returned to the supplier**, **Moved into another item or pre-packs**.
2. **Find** the product (barcode, code or name).
3. For a batch-tracked item, the batch. An expiry write-off only accepts a batch that is already expired. A transfer in asks for the batch and the expiry of what arrived.
4. Quantity. A move into another item asks for that item's code, and converts packs to the other item's units.
5. **Why** — the reason is kept with the movement.
6. **Record**.

### Mass price change

Words: mass price change, put prices up, reprice a department

**Inventory > Mass Price Change.** Needs the price-change right. It is two questions, not a window.

1. **Whose prices**: **Every department**, or one department.
2. **Percentage to put the prices up by**.
3. Confirm **Put every price in ... up by ...%, rounded as System setup says?**
4. The message says how many prices changed. They are on the Price changes report.

### Suppliers, departments, categories, manufacturers

Words: add a supplier, department, category, manufacturer

**Inventory > Suppliers**, **Departments**, **Categories**, **Manufacturers**.

1. **Search**. **Add** or **Edit**.
2. A supplier is **Code**, **Name**, **Contact**, **Telephone**, **Email**, **Account no.**, then **Save**. **Cancel** leaves it.
3. A department, category or manufacturer is **Code**, **Name**, **Save**. These are the lists a product is filed under, so add the department before you can pick it on a product.

---

## 6. Stocktake

Words: stocktake, stock count, new count, post a stocktake, variance

**Stocktake** menu, or the **Stocktake** tile (opens Capture).

1. The list shows each count: how many lines are left, and the variance so far. **New count** starts one. It is refused while a count is already in progress: **Continue** that one, or **Cancel** it, first. **Refresh** re-reads the list.
2. **New count** asks **What is being counted?** **Description** (printed on the variance report, for example "Shelf 4, cough and cold"). **Product filter** limits it to a code or a name; leave it blank for everything. **Batch-tracked items only** and **Below reorder level only** narrow it further. **Open** starts it. **Cancel** backs out. Counting does not change stock yet.
3. **Capture** (the menu, or **Continue** on the list). Find the product. Type the quantity counted. **Count line (Enter)** records that line. **Variances only** hides lines that match. **Post count** saves the counts. It does not yet change the quantities on hand.
4. **Variance**. Read what was counted against what the system held.
5. **Post**. This is the step that changes stock. It needs the stocktake-post right, so a pharmacist or an assistant can count and cannot post.

### Pre-packs

Words: pre-pack, prepack, split a bulk pack, pre-pack stickers

**Dispensary > Pre-packs.** Making small packs out of a bulk item.

1. **Bulk item**: scan or type the code or name, **Find**.
2. **Batch counted out of**: the bulk batch the tablets come from.
3. **Each pre-pack holds**: how many tablets, capsules or ml in one small pack.
4. **How many pre-packs**.
5. **Price of one pre-pack**. Leave it blank and the price is the bulk price worked out per unit.
6. **Record**. The bulk batch goes down by what was packed. Print the stickers from **Dispensary > Labels**, **Pre-pack stickers...**.

---

## 7. Accounts

Needs the accounts rights. A pharmacist, an assistant and a cashier do not see this menu.

### Patient accounts

Words: patient account, statement, payment on account, owing

**Accounts > Patient Accounts.**

1. **Find** the account. **Owing only** hides accounts with nothing owed.
2. **Statement** shows the account, and can be printed.
3. **Take Payment**. Amount, then the tender, then a reference where the tender needs one. The payment reduces what the patient owes and is money into the till that is open.

### Medical aid claims

Words: medical aid claim, remittance, pay a claim, reject a claim, age analysis, claim form

**Accounts > Medical Aid Claims.**

1. **Scheme** filters the list. **Received** is the date the money came in. **Reference** is the scheme's remittance number.
2. **Pay in Full** marks the selected claim paid in full. **Pay amount...** records part of it. **Reject** marks one the scheme refused. **Record Remittance** records the payment for the whole scheme on that date. **Age Analysis** shows how long each claim has been waiting. **Claim Form** prints the form for the selected claim.
3. **Refresh** re-reads the list. **Export to Excel** saves it.

### Day-end posting

Words: day-end posting, day end, post the day, export the journal

**Accounts > Day-End Posting.**

1. Pick the day. **Look at day**.
2. **Prepare journal**. This writes the day's takings into the books.
3. **Export CSV** for the accountant.

### Trial balance

Words: trial balance, the books

**Accounts > Trial Balance.** **As at** the date, **Refresh**, **Export to Excel**.

### Journal entry

Words: journal entry, post a journal, manual journal

**Accounts > Journal Entry.**

1. **Date**, **Reason**.
2. **Add line...** for each side. The journal has to balance.
3. **Post Journal**. **Export to Excel** saves a copy.

### Chart of accounts

Words: chart of accounts, add an account, ledger account

**Accounts > Chart of Accounts.**

1. The list of accounts. **Show accounts no longer in use** includes retired ones.
2. Add or edit an account from the row. Deactivate retires one (several can be selected).
3. **Export to Excel**.

---

## 8. Reports

Words: run a report, sales report, export to excel, preview a report, graph, suggest a report

**Reports > Reports** (F9), or the **Reports** tile. The list shows only the reports this person may run.

1. Click the report.
2. **From**, **To**, **Branch** where the report has them.
3. **Preview (F12)**. **Close (F7)** leaves the preview.
4. **Export to Excel**. PDF is the other export on the same screen.
5. **Graph** includes the chart on the preview, the PDF and the Excel file.
6. **Ask**, then **Suggest**, types a question in ordinary words ("sales last week") and picks the report and the dates. The model has to be installed on this computer; without it the box is not there.

---

## 9. Administration

Administrator only, except Stock Transactions, which is a report and follows the report rights.

### Users

Words: add a user, reset a password, unlock a user, deactivate a user, discount limit, roles

**Administration > Users.**

1. The list shows Username, Name, Roles, discount limit, whether the account is active, whether it is locked, whether the password must be changed, and the last sign-in. **Show deactivated accounts** includes the ones taken off.
2. **Add** or **Edit**. **Username**, **Name**, **Roles** (more than one can be ticked), **Discount limit %** (how far they may discount a line before a supervisor is asked). **Save**.
3. **Reset password** sets a new one and makes them change it at the next sign-in.
4. **Unlock** clears a lock from too many wrong passwords.
5. **Deactivate** takes the account off. It reads **Reactivate** on an account already off. A person cannot deactivate the account they are signed in with.

### Role permissions

Words: permissions, role permissions, tick a permission, give a role access, take away access

**Administration > Role permissions.** Administrator only, until they tick this for someone else.

1. Each row is a permission. Each column is a role, including Administrator. A tick is what that role may do.
2. To give a role a screen, find its row and tick the box under that role. To take it away, untick it.
3. A person gets the permissions of the roles on their user, not a list of their own. Change the role here, then give them that role under Users.
4. **Save.** Sign out and back in before it applies. Leave **Role permissions** ticked for a role you hold, or you cannot open this screen again. **Close** leaves without saving.
5. Each administration screen has its own row: System setup, Branches, Receipt template, Print sizes, Import data, Back up now, System console (with control variables and pharmacy details) and Label templates (the Templates tab on Labels). Tick one for a role to hand over that screen alone.
6. **Print sizes** is ticked for the manager and the pharmacist from the start, so they can match the paper in their own printer.

### System setup

Words: system setup, markup, rounding, currency, logo, colour palette, change the colours

**Administration > System Setup.** For the whole pharmacy, every till, except the theme, which is this computer's own.

1. **Trades in**: the currency. The sample shows how money will read, and which notes and coins the cash-up counts.
2. **Choose image...** for the logo, **Remove** to drop it.
3. **Windows**: the colours of every window. **From the logo** takes them out of the logo, so a new logo brings its own colours. Or pick a palette from the list; each shows its two colours beside its name. The Fluent palettes (Fluent blue and sky, Crimson and tangerine, Orchid and magenta, Indigo and lavender, Graphite and mint, Mahogany and sand) are the Windows 11 accent colours.
4. **Theme**: light or dark for this computer. The same choice as the **Theme** menu (see Themes).
5. **Markup %**. Every delivery prices the item at its cost plus this. At 50%, a cost of 10.00 sells at 15.00. It is 50 until it is changed.
6. **Round up to**. Prices round up to this step. Left blank, they round up to 0.01, so 32.505 becomes 32.51.
7. **Dispensing charges round up to** is the same idea for a script charge.
8. **Save**. The new colours and theme show at once, on every open window.

### Membership

Words: membership, subscription, activation code, renew, licence, license, read-only, Pharmacy ID

**Administration > Membership**, on the **main computer** (the server, or the PC if PharmaX runs on one PC only). One code covers the whole pharmacy: every till takes the membership from the main computer, and the code works on no other computer. On a till this screen only shows where the membership stands. A new installation runs a 14-day trial.

1. **Copy** the **Pharmacy ID** and send it with your payment (monthly, quarterly, yearly or lifetime).
2. When the activation code arrives, paste the whole code into **Activation code**.
3. **Activate**. The screen shows the plan and the date it runs until. Paying before the end adds the new period after the current one. A lifetime membership shows "active for life" and never ends.

The Pharmacy ID belongs to the main computer. If the main computer is replaced, or Windows on it is reinstalled, it gets a new Pharmacy ID and the old code stops working: send the new ID for a new code.

Seven days before the end, a yellow banner appears under the menu. After the end there are seven days' grace with everything still working, under a red banner. After that PharmaX is read-only: patients, products, the ledger and reports can still be looked up, but nothing can be sold, dispensed, received or changed until a new code is entered. Clicking the banner opens Membership. If the computer's date is set back, PharmaX is also read-only until the date is correct.

### Branches

Words: branch, add a branch

**Administration > Branches.** Code and name, then save. A transfer between branches uses this name. The branch on this computer is set when PharmaX was installed, not from this list.

### Receipt template

Words: receipt template, what the receipt prints

**Administration > Receipt Template.** This is the layout every till's receipt uses. Change the lines, **Save**. It is not Printer Setup, which only chooses which printer.

To try the layout on real items, type a few letters of a product or the digits of a script number above the preview and pick one from the list. **Find product...** opens the product list. **Find script...** lists every patient's scripts captured on the day shown, today first. Untick the day to search them all.

### Import data

Words: import data, import from the old system, dry run

**Administration > Import Data.**

1. **Import from**, then **Folder...** for a folder of files or **Files...** for chosen files.
2. **Check (dry run)** reads them and changes nothing. Read what it says it would load.
3. **Import** loads them. Each file is recognised by its columns.
4. This is a one-time load from the old system, not a daily job.

### Back up

Words: back up, backup

**Administration > Back Up Now.** It copies the database on the computer that holds it. On any other computer it says to back up from the main computer. The nightly copy is a separate scheduled task; this button is the copy you take yourself, before a stocktake or an import.

### Pharmacy details

Words: pharmacy name, pharmacy address, telephone, save pharmacy details

The trading name, address, telephone and VAT number are not in System Setup. Click the pharmacy's name on the home screen and press **Save pharmacy details**.

### VAT summary and stock transactions

Words: vat summary, stock transactions

**Administration > VAT Summary** and **Administration > Stock Transactions** open reports. Set the dates, **Preview (F12)**, then **Export to Excel**. VAT summary needs the accounts reports right. Stock transactions is the operational report a pharmacist and an assistant also get.

### System console and control variables

Words: system console, control variables

**Administration > System Console** and **Administration > Control Variables** are the technical screens. Leave them unless you were told to change one. They are not part of the day's work.

### Sign in, log off, and the password

Words: sign in, log in, log off, change my password, exit

1. **Log in**: **Username**, **Password**, **Log in**. **Cancel** leaves the program.
2. **File > Log Off** goes back to that window. The till session stays open; signing out does not cash the till up.
3. **File > Change Password**: the new password twice, then **Change**.
4. **File > Exit** (Ctrl+X) closes PharmaX.

### Find a screen

Words: go to any screen, find a menu, ctrl k

Click **Go to any screen** on the home screen, or press Ctrl+K, and type a few words: "cash up", "patients", "receive". The same words work as the menu. A menu this person may not use is grey, and the tile says **Not permitted for this user**.

### Who can see a menu

Words: why can't I see, not permitted, greyed out, which menu

A grey menu entry, or a tile that says **Not permitted for this user**, is the role. The same answer is given by the menu, the tile, the shortcut and Ctrl+K.

- A cashier sells, collects, reprints, opens and cashes up their own till, and looks up over-the-counter products. No dispensary, no reports, no accounts, no stocktake, no other person's till.
- A pharmacist and an assistant dispense, order, receive and count. They cash up any till. An assistant cannot reverse a dispensing or merge patients. Neither posts a stocktake, a write-off or a transfer, and neither sees Accounts or Users.
- A stock controller orders, receives, counts and posts stock, and adds products, but does not sell and does not change a selling price.
- The administrator sees every menu.

### Printer setup

Words: printer setup, receipt printer, label printer, test receipt

**File > Printer Setup.** Choose the receipt printer and the label printer for this computer, **Test receipt**, **Save**. Tick **Every dispensary computer prints labels on this label printer** only on the computer the others should share. **Find on a computer...** looks for a printer that is not already listed.

### Print sizes

Words: print sizes, label size, receipt size, claim form size, paper size, font size, line spacing, padding

**File > Print Sizes.** This computer's label, receipt and claim-form sizes. Needs the **Print sizes** permission (the manager, the pharmacist and the administrator have it from the start).

1. Choose the tab: **Labels**, **Receipts** or **Claim forms**. **Current size** shows what prints now.
2. **Stock / size**: pick the paper in the printer. Pick a custom size to type your own.
3. **Width (mm)** and **Height (mm)**: the paper's size.
4. **Font size %** makes the words bigger or smaller. **Line spacing %** opens up or closes the lines.
5. **Padding** (hundredths of an inch): **Top**, **Right**, **Bottom**, **Left** move the print away from the paper's edge. Use them when the print runs off the label.
6. The sample on the right changes as you type. **Print preview** prints a test on the real printer.
7. **Save**. **Reset to default** puts that tab back as it was installed. **Cancel** leaves without saving.

### Themes

Words: theme, themes, dark mode, light mode, fluent, midnight, warm paper, colours too bright

**Theme** at the right-hand end of the menu bar, or **Theme** in **Administration > System Setup**. This computer only; other tills keep their own.

1. **Light**: the usual look.
2. **Dark**: dark windows for a dim dispensary.
3. **Midnight neon**: deep navy with a cyan glow.
4. **Warm paper**: cream instead of white, easier on the eyes over a long shift.
5. **Fluent light** and **Fluent dark**: the Windows 11 look, grey page with white boxes, or the Windows dark greys.
6. Click one. Every open window changes at once, and PharmaX remembers it on this computer. The menu ticks the one in use.
7. The theme only decides light or dark. The colours of the buttons and titles come from the palette in System Setup.

### Zoom

Words: zoom, bigger text, aspect ratio

**View > Zoom**: Zoom in (Ctrl++), Zoom out (Ctrl+-), Actual size (Ctrl+0). **View > Aspect ratio** and **View > Resolution** change this window only, and are remembered on this computer.

### About

Words: about, which version

**Help > About** shows the program name and the version. It changes nothing.

---

## 10. Ask PharmaX

Words: ask pharmax, how do I ask, attach, attachment, upload a file, save answer, save the answer, export answer, chat history, calculate

**Help > Ask PharmaX** (Ctrl+Shift+A), from any screen.

1. Type the question. **Ask**.
2. A question about how to use PharmaX is answered from this guide. The model is not asked.
3. These open the screen and fill in what was typed, and nothing is saved until you finish it there:
   - "sell 2 panado"
   - "add a patient called John Moyo"
   - "add a new product called Panado 500mg"
   - "receive stock from Pharmanova"
   - "order 20 panado from Pharmanova"
   - "dispense amoxicillin for Mary Banda"
4. A question about a medicine, with no matching section here, is answered by the offline model. That answer can be wrong. The line at the top of the window says to check it against an approved reference or with a pharmacist. The model does not know today's prices or today's stock. How-to answers still work when the model is not installed.
5. **Attach** a picture, PDF, Excel workbook, CSV, Word document or text file, or drop it on the window, then ask about it. Videos cannot be attached. The file is read on this computer and nothing is sent anywhere, and no internet is needed: the words in a picture are read by Windows where it has text recognition installed, and otherwise by the reader installed with PharmaX.
6. **Save answer** saves the last answer, pictures and all, as a PDF, an Excel workbook or a Word document. Typing "save that as a PDF" does the same. Once it is saved, **Open file** opens it.
7. Ask for a report in your own words, such as "sales for March 2026" or "cash-up report for yesterday". It runs here, and if no period was given it asks which one. **Save answer** saves the whole report with the letterhead. **Reports** opens the reports screen to change the filters.
8. Ask for a document: "create an invoice" or "create a purchase order" talks you through it here, while "create a quotation", "print labels" or "make a receipt template" opens its screen to fill in. Nothing is saved until you finish it.
9. Type a sum, such as "230 + 15%" or "15% of 480", to have it worked out exactly.
10. Today's chats are listed on the left. **New chat** starts another, and clicking a chat opens it again. Chats are kept for today only and deleted after midnight, so save anything you want to keep.

---

## 11. What this document does not pretend

- The exact function key on a button is decided when that window opens, from the button's words. Where this document names a key, the key is part of the menu or part of the button caption.
- A site can turn a menu off for a person by changing their role. The table at the top is the standard roles as PharmaX ships them.
