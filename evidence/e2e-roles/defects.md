# Defects

Found in the role journeys of 17 October 2026. No step failed: everything the pharmacy asked for worked. These are faults met along the way.

| ID | Severity | Case | Role | Steps to reproduce | Expected | Actual | Evidence | Workaround |
|---|---|---|---|---|---|---|---|---|
| DEF-ROLE-001 | Major | A/P/S-D9, D12 | all dispensary roles | At 150 % scaling (1920×1080), go to Script Maintenance → choose a patient → choose a script → F2 Use again. The same happens with Labels → F3 Use again... | The "New script from an earlier one" window fits on the screen | The window spans y 146 to 1112 on a 1080-high screen (working area 1020). Its **F6 Save** and **Esc Cancel** buttons sit at y 1011 to 1067, under the taskbar, so they cannot be clicked. The Scripts list behind it also overruns the working area (bottom at 1031). | `screenshots/*/?-D9-edited.png`; the window measured during rehearsal | Press **F6** on the keyboard; the videos say so on screen. It looks like the window is cascaded below its owner and not held to the screen as `PhxTheme.HoldToScreen` does for others. |
| DEF-ROLE-002 | Minor | D9, D13 | all | Script Maintenance → Use again (Prescribed on); Reports (From, To) | Dates in one format | Date pickers show **M/d/yyyy** (10/17/2026), while every grid and report shows **dd/MM/yyyy** (17/10/2026). Easy to misread 01/05 as 5 January. | `screenshots/*/?-D9-draft.png`, the report screenshots | None needed |
| DEF-ROLE-003 | Cosmetic | D9, D10 | all | Use again on an imported script whose directions contain "UF" | The shorthand expanded or flagged | "UF" prints on labels as typed ("1 OD M PO PC  UF"). This was already noted on 11 October: the code is not defined under Dispensary → Dosage Codes. | S-D9 result, label PDFs | Define UF under Dosage Codes |

Older defects were watched for and none came back: DEF-0015 and DEF-0027 (screen access, menus and shortcuts) behaved for every role; see `menus-*.csv` and C-C6.
