# AX Change Log: INDCRMExpenseSheetService

Date: 2026-05-26

## Objective

Correct expense sheet email participant resolution so it follows the CRM actor vs expense sheet owner rule instead of searching approvers through subordinate sets.

## Methods Touched

- `INDCRMExpenseSheetService::getExpenseSheetNotificationRecipients`

## Contract Adjustment

`getExpenseSheetNotificationRecipients` now expects:

```text
_data[1] = CompanyId
_data[2] = HojaGastosId
_data[3] = EventType
_data[4] = ActorAxUserId
```

The API still calls the same AX method name. The public HTTP contract does not change.

## Participant Rules

- Resolve `ActorAxUserId` to `CRMUsuarioTable.UserId` with `SysUserInfo::getCRMUsuarioTable`.
- Resolve expense sheet owner from `CRMHojaGastosTable.UserId`.
- If actor CRM user and owner CRM user are the same, skip email as self-managed.
- `ExpenseSheetApprovalRequested`: sender is the sheet owner; recipient is the actor CRM user.
- `ExpenseSheetApproved`: sender is the actor CRM user; recipient is the sheet owner.
- `ExpenseSheetPaid`: recipient remains the sheet owner for AX-originated paid notifications.

All email addresses continue to come from `INDPersonaTable.Email`.

## API Alignment

`ExpenseSheetNotificationService` now:

- Uses `After.UserId` as sender for `ExpenseSheetApprovalRequested`.
- Uses `ActorAxUserId` as sender for `ExpenseSheetApproved`.
- Passes `ActorAxUserId` to `getExpenseSheetNotificationRecipients`.

## Risks

- Import the updated XPO before retesting CRM API email notifications.
- Validate that `CRMHojaGastosTable.UserId` is the business owner field in the target AOT.
- If `ActorAxUserId` cannot be resolved to a CRM user, the notification is skipped and logged.
