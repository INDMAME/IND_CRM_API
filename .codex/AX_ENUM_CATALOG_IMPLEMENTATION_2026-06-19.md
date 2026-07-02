# Enum catalog implementation - 2026-06-19

## AX objects

- `INDAxEnumAppCatalogTable.effectiveSortOrder()` now returns `SortOrder` directly.
- `SortOrder = 0` is valid and must not be treated as empty.
- `INDCRMUtilityService` adds generic enum catalog methods:
  - `getEnumValuesByName(container _data)`
  - `getEnumValuesById(container _data)`

## AX input contract

`getEnumValuesByName(_data)`:
- `_data[1]`: `DataAreaId`
- `_data[2]`: `AppCode`
- `_data[3]`: comma-separated `AxEnumName` values, optional

`getEnumValuesById(_data)`:
- `_data[1]`: `DataAreaId`
- `_data[2]`: `AppCode`
- `_data[3]`: comma-separated `AxEnumId` values, optional

When the third value is empty, AX returns all active enum groups configured for the app and company.

## AX output contract

Both methods return:

```text
[Header, Groups]
```

`Header` is generated with `buildHeader(success, message, [company, appCode])`.

Each group:

```text
[AxEnumName, AxEnumId, Found, Options]
```

Each option:

```text
[EnumValue, EnumIndex, Label, Description, Active, SortOrder, AxEnumsTableRefRecId]
```

## API endpoints

Controller:

- `Controllers/CRM/CrmEnumsController.cs`
- Route prefix: `api/crm/enums`

Routes:

- `GET /api/crm/enums/by-name?appCode=CRM&axEnumNames=CRMGastoType,INDExpenseSheetStatus`
- `GET /api/crm/enums/by-id?appCode=CRM&axEnumIds=61472,61523`

Both endpoints require `Authorization` and `X-IND-Company`. They do not require `X-IND-AxUserId` because they do not execute user-owned business mutations.

## Business endpoint alignment

- Existing business endpoints still receive numeric enum values.
- `CreateActivityRequest.visitType` and `UpdateActivityRequest.visitType` now use `int?`.
- `CreateVisitaAsistenteRequest.asistenteTipo` now uses `int?`.
- Expense sheets and tickets already receive numeric enum values for expense status, reimbursable expense, exchange rate mode and gasto type.
- Server-side fixed validations remain in place for business invariants; the enum catalog feeds UI/select options and does not replace business validation.
