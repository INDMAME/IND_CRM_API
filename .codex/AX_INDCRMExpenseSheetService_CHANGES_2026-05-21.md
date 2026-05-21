# AX INDCRMExpenseSheetService changes - 2026-05-21

## Objective
- Add reimbursable expense support to expense sheet headers and lines.
- Add line currency, company-currency amount, and exchange-rate fields for CRMHojaGastosLine.
- Keep existing COM container contracts backward-compatible by appending new optional positions.

## Methods touched
- `buildExpenseSheetListRow`
- `createExpenseSheet`
- `getExpenseSheet`
- `getExpenseSheetsList`
- `isValidReimbursableExpense`
- `propagateExpenseSheetCurrencyDefaults`
- `propagateExpenseSheetProjectDefault`
- `propagateExpenseSheetReimbursableExpense`
- `updateExpenseSheetHeader`
- `updateExpenseSheetLine`

## Contract adjustments
- `createExpenseSheet` header now accepts optional `headerIn[8] = ReimbursableExpense`.
- `createExpenseSheet` lines now accept optional positions:
  - `line[9] = ReimbursableExpense`
  - `line[10] = Currency`
  - `line[11] = AmountMST`
  - `line[12] = ExchRate`
- `getExpenseSheet` header now returns `ReimbursableExpense` appended after `CreatedDate`.
- `getExpenseSheet` lines now return `ReimbursableExpense`, `Currency`, `AmountMST`, and `ExchRate` appended after `ProjId`.
- `getExpenseSheetsList` now accepts optional `ReimbursableExpense` filter before `includeSubordinates` in the current API shape.
- `propagateExpenseSheetCurrencyDefaults` accepts `[CompanyId, axUserId, HojaGastosId, recalculateAmountMST, force]` and returns `[HojaGastosId, UpdatedLines, RecalculateAmountMST]`. `force` is optional and defaults to false.
- `propagateExpenseSheetProjectDefault` accepts `[CompanyId, axUserId, HojaGastosId]` and returns `[HojaGastosId, UpdatedLines, 0]`.
- `propagateExpenseSheetReimbursableExpense` accepts `[CompanyId, axUserId, HojaGastosId]` and returns `[HojaGastosId, UpdatedLines, 0]`.
- `updateExpenseSheetHeader` now accepts optional `_data[11] = ReimbursableExpense`.
- `updateExpenseSheetLine` now accepts optional `_data[13..16]` for line reimbursable/currency/amountMST/exchRate.

## Compatibility notes
- Existing minimum container lengths are still accepted.
- New response fields are appended at the end of current rows.
- Legacy `getExpenseSheetsList` shape with `includeSubordinates` at `_data[10]` is still recognized when the container length is 10.
- `updateExpenseSheetHeader` treats `_data[10] = "null"` as no change for `EstadoComentarios`, so `_data[11]` can be sent without clearing comments.

## Risks
- AX field names are assumed from table metadata supplied in the request: `ReimbursableExpense`, `Currency`, `AmountMST`, and `ExchRate`.
- `AmountMST` now delegates to `CRMHojaGastosLine.normalizeCurrencyAmounts(...)` so the service does not duplicate AX exchange-rate math.
- Requires the CRMHojaGastosLine table helper changes to be imported with the service XPO.
- Header propagation is explicit. Header update does not propagate lines by itself.
- Currency propagation with `force=true` intentionally overwrites existing multi-currency line values after web confirmation.
- Project propagation does not run when header `ProjId` is `PurchParameters.INDProjIdVarious`; the web must ask the user to choose a real project first.

## Pending API work
- Completed in the same change set: C# DTOs, controller mapping, docs, MCP schema, Postman examples, and frontend temporary notes.
