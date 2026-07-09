# AX Change Log - INDTicketInfoTable (2026-07-09)

## Objective
Preserve an explicitly supplied `AmountMST` when a ticket changes to a foreign currency during IA processing.

## Scope
- AX table: `INDTicketInfoTable`
- Method: `update()`
- Related class caller: `INDCRMExpenseSheetService.updateExpenseSheetTicketFromIA(container _data)`

## Root Cause
- `update()` intentionally reset `ExchRate` and `AmountMST` when `CurrencyCode` changed and `ExchRate` still matched the original value.
- That protects manual currency changes from reusing an old reimbursement amount.
- For IA, the caller can now supply a new `AmountMST`; resetting it causes the foreign-currency validation to fail even though the caller provided the reimbursement amount.

## AX Change
- `amountMSTChanged` is calculated before the currency-change reset.
- The reset now only runs when `amountMSTChanged` is false.
- When a caller supplies a new `AmountMST`, the existing table normalization path preserves it and derives `ExchRate` through `calcExchRateFromAmountMST()`.

## Compatibility
- Existing calls that change currency without supplying a new `AmountMST` keep the previous protective reset behavior.
- Existing same-currency tickets keep the current normalization behavior.

## Validation Notes
- Retest IA ticket finalize with a foreign ticket currency and a EUR reimbursement target.
- Expected result: `INDCRMExpenseSheetService.updateExpenseSheetTicketFromIA` saves `AmountMST`, derives or preserves `ExchRate`, and `validateCurrencyAmounts()` passes.
- Real AX runtime validation still requires importing and compiling the updated XPO into Axapta.
