# AX Change Log: CRMHojaGastosTable

Date: 2026-05-25

## Objective

Trigger the paid expense sheet notification from Axapta after payment posting succeeds.

## Method Touched

- `ContabilizaAsientoHojaGastos`

## Change

After `ttscommit` and only when a voucher was created, the method iterates the paid expense sheet set again and calls:

```xpp
INDCRMExpenseSheetService::sendExpenseSheetPaidNotification(hojaGastosTable);
```

The method now returns the created `voucher`, matching its declared return type.

## Behavior

- Email sending is best-effort and happens after posting success.
- Email failures do not abort payment posting.
- `ExpenseSheetPaid` remains Axapta-only and is not triggered by `IND_CRM_API`.

## Risks

- Confirm this method is the authoritative posting hook for all paid remittance flows in the target AOT.
- Confirm the paid set contains only sheets that must notify.
