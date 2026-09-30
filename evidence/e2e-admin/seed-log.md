# Direct SQL changes made during the admin run

| When | Statement | Why |
|---|---|---|
| 04:47 | `update supplier set IsActive = 1 where Code = 'E2ESUP'` | ADM-DEF-03: no screen can reactivate a supplier, and E2ESUP is needed for the purchase order and goods receipt steps |
