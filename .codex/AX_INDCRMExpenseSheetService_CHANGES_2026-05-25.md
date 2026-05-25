# AX Change Log: INDCRMExpenseSheetService

Date: 2026-05-25

## Objective

Add CRM expense sheet email helper methods for web deep-link generation, persona email lookup, notification recipient resolution, and paid notification sending.

## Methods Added

- `urlEncode`
- `buildExpenseSheetWebLink`
- `getPersonaEmailByUserId`
- `getPersonaNameByUserId`
- `getPersonaEmailByUserIdForApi`
- `getExpenseSheetNotificationRecipients`
- `sendExpenseSheetPaidNotification`

## Contract

Web link format:

```text
{ParameterTable.INDCrmWebBaseUrl}/Gastos/ExpenseSheetLink?hojaGastosId={id}&targetCompanyId={companyId}&source={source}
```

Recipient rules:

- `ExpenseSheetApprovalRequested`: approvers whose subordinate set contains the sheet owner.
- `ExpenseSheetApproved`: `CRMHojaGastosTable.INDCreatedByUserId`.
- `ExpenseSheetPaid`: `CRMHojaGastosTable.INDCreatedByUserId`.

All email addresses are resolved from `INDPersonaTable.Email`.

## Behavior

- Link generation stays in this CRM/expense-sheet service, not in the generic utility service.
- `sendExpenseSheetPaidNotification` uses `curUserId()` as sender and calls `INDCRMUtilityService::sendInternalApiMail`.
- Missing sender, recipient, or link logs a warning and returns `false`.

## Risks

- Validate the exact `INDPersonaTable.Email` field name in the target AOT before import.
- Validate that iterating `CRMUsuarioTable.SetSubordinados()` is acceptable for approver lookup volume.
- Replace `ParameterTable.INDCrmWebBaseUrl` with the final configuration table/field.
