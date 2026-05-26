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

- Superseded on 2026-05-26 by actor-vs-owner CRM rules. See `.codex/AX_INDCRMExpenseSheetService_CHANGES_2026-05-26.md`.

All email addresses are resolved from `INDPersonaTable.Email`.

## Behavior

- Link generation stays in this CRM/expense-sheet service, not in the generic utility service.
- `sendExpenseSheetPaidNotification` uses `curUserId()` as sender and calls `INDCRMUtilityService::sendInternalApiMail`.
- Missing sender, recipient, or link logs a warning and returns `false`.

## Risks

- Validate the exact `INDPersonaTable.Email` field name in the target AOT before import.
- Recipient resolution was corrected on 2026-05-26; do not use `CRMUsuarioTable.SetSubordinados()` for this email flow.
- Replace `ParameterTable.INDCrmWebBaseUrl` with the final configuration table/field.
