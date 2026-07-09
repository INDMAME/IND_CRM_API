# AX Change Log - INDCRMExpenseSheetService (2026-07-09)

## Objective
Fix `getExpenseSheetsList(container _data)` so `includeSubordinates = true` returns the current user's expense sheets plus direct subordinate expense sheets.

## Scope
- AX class: `INDCRMExpenseSheetService`
- AX method: `getExpenseSheetsList(container _data)`
- API endpoint: `POST /api/crm/expensesheets/list`
- API contract: `Contracts/Requests/GetExpenseSheetsListRequest.cs`

## Root Cause
- Before this change, the AX branch for `includeSubordinates = false` filtered `CRMHojaGastosTable.UserId == crmUserId`.
- The AX branch for `includeSubordinates = true` used an `exists join` on `CRMUsuarioSubordinadoTable`, so it only returned direct subordinate sheets.
- The web app uses `includeSubordinates = true` for the "All" owner option, where business behavior is current user plus direct subordinates.

## AX Change
- `includeSubordinates = false` is unchanged and still returns only the current user's sheets.
- `includeSubordinates = true` now builds `visibleUserIds` with:
  - the current `crmUserId`;
  - direct subordinate `UserIdSubordinado` values for that `crmUserId`;
  - duplicate protection through `conFind`.
- The list query keeps the existing filters and `CreatedDate DESC` ordering, then appends only rows whose `hoja.UserId` is in `visibleUserIds`.
- Removed the `exists join CRMUsuarioSubordinadoTable` from the `includeSubordinates = true` list query because `conFind(visibleUserIds, hoja.UserId)` is the contract owner for the final visibility decision.
- The latest AX export appends a final created-date column after `TotalAmountMST` in `buildExpenseSheetListRow` and `getExpenseSheet`; the API exposes it as `axCreatedDate`.

## Preserved Filters
- `filter` / `INDSearchKey`
- `billedMode`
- legacy rows with empty `CreatedDate` remain excluded
- `createdDateFrom` / `createdDateTo`
- `projId`
- `currencyCode`
- `expenseSheetStatus`

## API Documentation Update
- `includeSubordinates = true` now means "header user + direct subordinates".
- `axCreatedDate` is documented for expense sheet detail headers and list items.
- Route, verb, headers, request body shape, and response envelope are unchanged.

## Routing Review
- No route template or HTTP verb changed.
- `POST /api/crm/expensesheets/list` remains the same literal child route under `api/crm/expensesheets`.
- No collision is introduced with detail, ticket, or line endpoints.

## Validation Notes
- Expected behavior:
  - `includeSubordinates = false` and `X-IND-AxUserId = MAME`: only MAME sheets.
  - `includeSubordinates = true` and `X-IND-AxUserId = MAME`: MAME sheets plus direct subordinate sheets.
  - A self-reference or duplicate subordinate row does not duplicate current-user sheets.
- Real AX runtime validation still requires importing the updated XPO into Axapta.
