# Temporary Frontend Notes: Expense Sheet Header Propagation

Date: 2026-05-21

> Superseded project/reimbursement aggregation note (2026-08-24): do not use the
> older "line differs from header" rules below as implementation guidance. AX now
> derives the header from all persisted `ProjIdHornos` values: a common value,
> including empty, is copied to the header; any difference, including empty versus
> non-empty, produces `PurchParameters.INDProjIdVarious`. Hidden `ProjId` alone does
> not produce the marker. Reimbursement derives the common Yes/No value or `Both`
> only when both values exist. The current contract is documented in `ENDPOINTS.md`.

## Scope

This note documents the new explicit propagation behavior for CRM expense sheet header defaults.

The frontend must not assume that changing the header automatically updates existing lines. Header updates and line propagation are now separate actions.

## Header Update Behavior

Endpoint already existing:

- `PUT /api/crm/expensesheets/{hojaGastosId}`

This updates the header fields, including:

- `currencyCode`
- `exchRate`
- `projId`
- `reimbursableExpense`

It does not propagate those values to existing lines.

AX no longer blocks line-level currency changes just because the header has a currency default.
When a saved line uses a currency different from the header, AX sets `CRMHojaGastosTable.CurrencyCode` to `INDDefaultParameters.CRMCurrencyVarios`.

Project aggregation does not compare a line against the previous header. AX recalculates the header from all persisted line `ProjIdHornos` values: a common value, including empty, becomes the header value; any difference becomes `PurchParameters.INDProjIdVarious`.

Reimbursable-expense aggregation follows the same all-lines model. A common Yes or No becomes the header value; `INDReimbursableExpense::Both` is used only when persisted lines contain both values.

The frontend should treat those header values as "mixed values" markers. They are not values to push back into lines.

When the frontend changes a line `currencyCode` to a currency different from the header, it should also send a valid line `exchRate` or `amountMST`. AX only reuses the header `exchRate` when the line currency still matches the header currency.

## Currency Propagation API

Endpoint:

- `POST /api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate?recalculateAmountMST=true`
- `POST /api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate?recalculateAmountMST=true&force=true`

Headers:

- `Authorization`
- `X-IND-Company`
- `X-IND-AxUserId`
- signed CRM context headers

Behavior:

- Applies the current header `currencyCode` and `exchRate` to all existing lines.
- Recalculates line `amountMST` by default.
- Blocks when the sheet has multi-currency lines unless `force=true`.
- Blocks when the header `currencyCode` is the `CRMCurrencyVarios` marker.
- Blocks when the sheet is locked by `Voucher`.

Response data:

```json
{
  "hojaGastosId": "009998",
  "propagationType": "currencyDefaults",
  "updatedLines": 3,
  "recalculateAmountMST": true
}
```

Frontend flow:

1. User changes header `currencyCode` or `exchRate`.
2. Frontend saves the header with `PUT /api/crm/expensesheets/{hojaGastosId}`.
3. If the sheet has existing lines, show a confirmation dialog.
4. If the user confirms, call the propagation endpoint.
5. Refresh the expense sheet detail.

When the current lines are already multi-currency, use the same confirmation as Axapta but call the endpoint with `force=true` only after the user accepts that existing line currencies and exchange rates will be overwritten.

## Project Propagation API

Endpoint:

- `POST /api/crm/expensesheets/{hojaGastosId}/project-default/propagate`

Behavior:

- Applies the current header `projId` to all existing line `projId` and `projIdHornos` values.
- Rebuilds the line project assignment using the header project.
- Blocks when the header `projId` is the `PurchParameters.INDProjIdVarious` marker.
- Blocks when the header `projId` is empty.
- Blocks when the sheet is locked by `Voucher`.

Response data:

```json
{
  "hojaGastosId": "009998",
  "propagationType": "projectDefault",
  "updatedLines": 3,
  "recalculateAmountMST": false
}
```

Frontend flow:

1. User changes header `projId`.
2. Frontend saves the header.
3. If the sheet has existing lines, show a confirmation dialog.
4. If the user confirms, call the propagation endpoint.
5. Refresh the expense sheet detail.

## Reimbursable Expense Propagation API

Endpoint:

- `POST /api/crm/expensesheets/{hojaGastosId}/reimbursable-expense/propagate`

Behavior:

- Applies the current header `reimbursableExpense` value to all existing lines.
- Blocks when the header `reimbursableExpense` is `Both`.
- Blocks when the sheet is locked by `Voucher`.

Response data:

```json
{
  "hojaGastosId": "009998",
  "propagationType": "reimbursableExpense",
  "updatedLines": 3,
  "recalculateAmountMST": false
}
```

Frontend flow:

1. User changes header `reimbursableExpense`.
2. Frontend saves the header.
3. If the sheet has existing lines, show a confirmation dialog.
4. If the user confirms, call the propagation endpoint.
5. Refresh the expense sheet detail.

## Axapta UI Behavior

In the Axapta form, propagation is interactive:

- Header `currencyCode` or `exchRate` changes ask whether to update line currency/exchange-rate and recalculate `amountMST`.
- Header `projId` changes ask whether to update line projects.
- Header `reimbursableExpense` changes ask whether to update line reimbursable value.
- Line currency changes can mark the header currency as `CRMCurrencyVarios` instead of blocking the line save.
- Line project changes recalculate the header across all persisted `ProjIdHornos` values; differences, including empty versus non-empty, produce `PurchParameters.INDProjIdVarious`.
- Line reimbursable changes recalculate the header across all persisted lines; a real Yes/No mixture produces `Both`.

## Important Non-Scope

MST total/accounting methods were intentionally not changed in this task because they are considered unused for this rollout.
