# AX INDCRMExpenseSheetService changes - 2026-05-14

## Objective
- Add informational IVA percentage per ticket line through `INDTicketInfoLine.TaxPercent`.
- Preserve existing amount calculations: `TaxPercent` does not affect `TotalAmount`.

## Methods touched
- `createExpenseSheetTicket`: accepts optional line field `[lineDescr, qty, price, lineTotal, taxPercent]`.
- `createExpenseSheetTicketLine`: accepts optional `_data[8] = taxPercent`.
- `updateExpenseSheetTicketFromIA`: accepts optional line field `[lineDescr, qty, price, lineTotal, taxPercent]`.
- `updateExpenseSheetTicketLine`: accepts optional `_data[9] = taxPercent` and preserves current value when omitted.
- `getExpenseSheetTicket`: appends `TaxPercent` at the end of each line row.

## Contract notes
- Existing callers remain compatible because the new field is optional and appended at the end.
- API request field is `taxPercent`; API detail response field is `TaxPercent`.
- Negative `TaxPercent` values are rejected defensively.

## Risks
- AX `TaxPercent` stores `0` when a new line is created without an explicit value because the AX field is a real/Percent value.
- Consumers that need to distinguish "not detected" from `0%` should rely on the API request/IA draft before persistence.

## API alignment
- API DTOs, line containers, AI prompts, endpoint docs, and MCP schemas were updated in this release.
- Web app contracts and ticket line UI are handled in `IND_CRM_APP` in the same work package.

## Follow-up: signed ticket lines
- Ticket line methods now allow negative `Price` and negative `TotalAmount` for discounts or refund lines.
- `Qty` cannot be negative; `Qty = 0` is only accepted when the line total is negative.
- Expense sheet line validations remain unchanged.
