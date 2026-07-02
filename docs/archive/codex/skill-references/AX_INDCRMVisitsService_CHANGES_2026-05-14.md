# AX INDCRMVisitsService Changes 2026-05-14

## Scope
- Add `CRMActividadTable.ContactMethod` support for the `INDContactMethod` enum.
- Enum values:
  - `InPerson = 0`
  - `PhoneCall = 1`
  - `OnlineMeeting = 2`

## Methods Updated
- `createActivity`: accepts optional `_data[11] = contactMethod`; defaults to `InPerson` when missing.
- `updateActivity`: accepts optional `_data[11] = contactMethod`; defaults to `InPerson` when missing.
- `getActivityByCode`: returns `ContactMethod` as its numeric enum value after `TipoVisita`.
- `getActivityByRecIdContainer`: now returns the same full activity shape as `getActivityByCode`, including `RecId`, `AccountNum`, narrative fields and `ContactMethod`.
- `getActivityContainer`: returns `RecId`, `AccountNum`, `TipoVisita` and `ContactMethod` in list rows so web/API clients can preserve the CRUD contract.

## Deployment Note
- Import `.codex/Axapta/INDCRMVisitsService.xpo` into the target AOS after the `CRMActividadTable.ContactMethod` field and `INDContactMethod` enum exist in AX.
