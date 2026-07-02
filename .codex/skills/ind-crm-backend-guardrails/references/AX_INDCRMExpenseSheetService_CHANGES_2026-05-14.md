# AX INDCRMExpenseSheetService changes - 2026-05-14

## Objective
- Remove the ticket-line IVA percentage contract.
- Add receipt header fields `INDTicketInfoTable.TicketDate` and `INDTicketInfoTable.TicketTime`.
- Preserve existing `DocuRef.INDTransDate` behavior for filters, attachment metadata, and old callers.

## Methods touched
- `createExpenseSheetTicket`: accepts optional header fields after JSON metadata: `ticketDate` and `ticketTime`.
- `updateExpenseSheetTicket`: accepts optional `_data[16] = ticketDate` and `_data[17] = ticketTime`.
- `updateExpenseSheetTicketFromIA`: accepts optional header fields after JSON metadata: `ticketDate` and `ticketTime`.
- `getExpenseSheetTicket`: appends `TicketDate` and `TicketTime` to the header response and removes the line-level IVA percentage.
- `getExpenseSheetTicketsList` / `getExpenseSheetTicketsLinkList`: append `TicketDate` and `TicketTime` to list rows.
- `createExpenseSheetTicketLine` / `updateExpenseSheetTicketLine`: no longer accept or return a line-level IVA percentage.

## Contract notes
- New date values are sent as `yyyyMMdd` inside AX containers and exposed by API as `DD.MM.YYYY`.
- New time values are sent as AX time seconds and exposed by API as `HH:mm:ss`.
- Optional fields are appended after the previous header metadata to keep existing container indices stable.

## Risks
- The live AOS class must be imported for `TicketDate` and `TicketTime` to persist in AX.
- The API remains backward-compatible with the previous AX class: extra header fields are ignored by old X++ code, and old line-level IVA output is ignored by the API mapper.

## API alignment
- API DTOs, line containers, endpoint docs, and web contracts are updated in the same work package.
- AI prompt processing no longer analyzes or emits IVA percentage fields.
