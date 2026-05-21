# Temporary Frontend Notes: Expense Sheet Header Propagation

Date: 2026-05-21

## Scope

This note documents the new explicit propagation behavior for CRM expense sheet header defaults.

The frontend must not assume that changing the header automatically updates existing lines. Header updates and line propagation are now separate actions.

## Header Update Behavior

Endpoint already existing:

- `PUT /api/crm/expensesheets/{hojaGastosId}`

This updates the header fields, including:

- `currencyCode`
- `exchRate`
- `reimbursableExpense`

It does not propagate those values to existing lines.

AX no longer blocks line-level currency changes just because the header has a currency default.
When a saved line uses a currency different from the header, AX sets `CRMHojaGastosTable.CurrencyCode` to `INDDefaultParameters.CRMCurrencyVarios`.

The same rule applies to projects. When a saved line `ProjId` or `ProjIdHornos` differs from the header `ProjId`, AX sets the header `ProjId` to `INDDefaultParameters.CRMProjIdVarios`.

The same rule applies to reimbursable expense. When a saved line `reimbursableExpense` differs from the header, AX sets the header `reimbursableExpense` to `INDReimbursableExpense::Both`.

The frontend should treat those header values as "mixed values" markers. They are not values to push back into lines.

When the frontend changes a line `currencyCode` to a currency different from the header, it should also send a valid line `exchRate` or `amountMST`. AX only reuses the header `exchRate` when the line currency still matches the header currency.

## Currency Propagation API

Endpoint:

- `POST /api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate?recalculateAmountMST=true`

Headers:

- `Authorization`
- `X-IND-Company`
- `X-IND-AxUserId`
- signed CRM context headers

Behavior:

- Applies the current header `currencyCode` and `exchRate` to all existing lines.
- Recalculates line `amountMST` by default.
- Blocks when the sheet has multi-currency lines.
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
- Header `reimbursableExpense` changes ask whether to update line reimbursable value.
- Line currency changes can mark the header currency as `CRMCurrencyVarios` instead of blocking the line save.
- Line project changes can mark the header project as `CRMProjIdVarios` instead of blocking the line save.
- Line reimbursable changes can mark the header reimbursable value as `Both` instead of blocking the line save.

## Important Non-Scope

MST total/accounting methods were intentionally not changed in this task because they are considered unused for this rollout.
