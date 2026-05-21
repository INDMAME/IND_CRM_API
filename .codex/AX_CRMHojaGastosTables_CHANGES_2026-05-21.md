# AX CRMHojaGastos Tables Changes - 2026-05-21

## Objective

Add reusable table helpers for expense sheet multi-currency line handling and reimbursable-expense propagation.

## Files

- `.codex/Axapta/CRMHojaGastosLine.xpo`
- `.codex/Axapta/CRMHojaGastosTable.xpo`
- `.codex/Axapta/CRMHojaGastosTableForm.xpo`
- `.codex/Axapta/INDCRMExpenseSheetService.xpo`

## Changes

- Added line helpers:
  - `applyHeaderCurrencyDefaults`
  - `calcExchRateFromAmountMST`
  - `normalizeCurrencyAmounts`
  - `validateCurrencyAmounts`
- Added header helpers:
  - `TotalEfectivo_GasolinaMST`
  - `TotalModalidadPagoMST`
  - `TotalPorModalidadPagoMST`
  - `isVariousCurrencyDefault`
  - `isVariousProjectDefault`
  - `isVariousReimbursableExpenseDefault`
  - `markHeaderVariousFromLine`
  - `canChangeCurrencyDefaults`
  - `validateCanChangeCurrencyDefaults`
  - `firstLineCurrency`
  - `updateCurrencyDefaultsInLines`
  - `updateReimbursableExpenseInLines`
- Updated line insert/update/validateWrite/modifiedField paths to use the new line normalization helper.
- Updated line currency validation so manual lines can use a currency different from the header default.
- Updated saved line synchronization so a line currency different from the header marks `CRMHojaGastosTable.CurrencyCode` with `INDDefaultParameters.CRMCurrencyVarios`.
- Updated saved line synchronization so a line `ProjId` or `ProjIdHornos` different from the header marks `CRMHojaGastosTable.ProjId` with `INDDefaultParameters.CRMProjIdVarios`.
- Updated saved line synchronization so a line `ReimbursableExpense` different from the header marks `CRMHojaGastosTable.ReimbursableExpense` with `INDReimbursableExpense::Both`.
- Updated line defaulting so the header "various" currency marker is not copied into line currency or used for `AmountMST`.
- Updated line defaulting so header `INDReimbursableExpense::Both` is treated as a mixed marker and is not copied into line `ReimbursableExpense`.
- Updated line insert/update so currency data is validated in direct AX/API writes, not only in form `validateWrite`.
- Updated line exchange-rate defaulting so header exchange rate is reused only when the line currency still matches the header currency.
- Updated API create/update line flows so header "various" currency/project markers are not copied into line fields.
- Updated legacy `UpdateDivisaEnLineas` to delegate to `updateCurrencyDefaultsInLines(true)`.
- Removed silent currency propagation from `CRMHojaGastosTable.update()`.
- Removed non-interactive table/form validation that blocked header currency/exchange-rate changes when current lines are multi-currency.
- Added form-level confirmation prompts for propagating header currency/exchange-rate and reimbursable-expense changes to existing lines.
- Added a guard so currency propagation does not copy the header `CRMCurrencyVarios` marker into lines.
- Added a guard so reimbursable propagation does not copy the header `INDReimbursableExpense::Both` marker into lines.
- Updated the endpoint service XPO to call line normalization instead of manually calculating `Amount * ExchRate / 100`.

## Pending

- Import/compile the updated XPOs in AX.
- Run a one-time job to backfill `CRMHojaGastosLine.AmountMST` for existing records.
- Existing MST total/accounting methods are intentionally left unchanged because they are out of scope for this rollout.
