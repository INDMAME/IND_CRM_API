# AX INDCRMExpenseSheetService changes - 2026-05-19

## Objective
- Add an API-side validation path for `INDTicketInfoTable.validateUniqueTicketDateTime()` before ticket header writes.
- Prevent API create/update flows from bypassing the table validation when `validateWrite()` is not triggered.
- Preserve the current response envelope shape for every affected method.

## Methods touched
- `validateTicketDateTimeForApi`: new shared helper that runs the table validation and returns the infolog message, or a fallback duplicate date/time message.
- `createExpenseSheetTicket`: validates before `ticketHeader.insert()` and before the final `ticketHeader.update()`.
- `createExpenseSheetTicket_backup`: validates before `ticketHeader.insert()` and before the final `ticketHeader.update()`.
- `createExpenseSheetTicketLine`: validates before recalculated header `ticketHeader.update()`.
- `deleteExpenseSheetTicket`: validates before recalculated header `ticketHeader.update()` in the single-line delete branch.
- `deleteExpenseSheetTicketLine`: validates before recalculated header `ticketHeader.update()`.
- `refreshTicketStatusByFileId`: validates before status-only `ticketHeader.update()` and raises the validation message for the existing caller catch blocks.
- `updateExpenseSheetLine`: keeps `CRMHojaGastosLine.ProjId` explicit and falls back to the header project when the API sends an empty line project.
- `updateExpenseSheetTicket`: validates before `ticketHeader.update()`.
- `updateExpenseSheetTicketFromIA`: validates before `ticketHeader.update()`.
- `updateExpenseSheetTicketLine`: validates before recalculated header `ticketHeader.update()`.

## Contract notes
- There are no new input fields, output fields, routes, or container index changes.
- Validation failures return the existing `INDCRMUtilityService::buildHeader(false, msg, conNull())` format.
- Methods that already return `[header, data]` keep returning `[INDCRMUtilityService::buildHeader(false, msg, conNull()), conNull()]` on validation failure.
- Methods that return only the header keep returning `INDCRMUtilityService::buildHeader(false, msg, conNull())` on validation failure.
- `CRMHojaGastosLine.ProjId` remains at line container index 8 for create and at root index 12 for line update.
- `getExpenseSheet` returns `CRMHojaGastosLine.ProjId` in each line row; API bulk ticket linking now preserves the target header `ProjId` read from that response.

## Risks
- The live AOS class must include `INDTicketInfoTable.validateUniqueTicketDateTime()`.
- `refreshTicketStatusByFileId` returns `void`, so it raises the validation message and relies on its existing API callers to format the response envelope in their catch blocks.
- The final compile check must be done after importing the XPO into Axapta.
