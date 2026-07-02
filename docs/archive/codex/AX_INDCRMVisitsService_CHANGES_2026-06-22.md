# AX INDCRMVisitsService changes - 2026-06-22

## Objective
Fix visit owner propagation after `POST /api/crm/activities/create` so the immediate `GET /api/crm/activities/{recId}` exposes the functional AX owner used by permission checks.

## Methods touched
- `createActivity`
- `updateActivity`

## Contract adjustments
- No input container indices changed.
- The activity detail contract keeps `OwnerAxUserId` as the canonical owner and the API mirrors it to `INDCreatedByUserId`, `CreatedByUserId`, and `UserId` for client compatibility.
- `createActivity` still returns the current action result container; the API derives `RecId` from the AX message and adds owner fields inside `Data`.

## Implementation notes
- `createActivity` now reassigns `CRMActividadTable.INDCreatedByUserId` after `InitFromCustTable` / `InitFromClientePotencialTable` and before `insert`.
- This keeps the owner scoped to the `X-IND-AxUserId` already sent by the API and does not create on behalf of another user.
- `updateActivity` preserves the existing `INDCreatedByUserId` across the same account initialization calls before `update`.

## Risks and validation
- Import and compile `INDCRMVisitsService` in AX after applying the XPO change.
- Validate create + immediate detail read in the target company:
  - Create an activity with `X-IND-AxUserId`.
  - Confirm `GET /api/crm/activities/{newRecId}` returns `OwnerAxUserId` and compatibility aliases with that same AX user.
  - Confirm assistant creation still uses the existing visibility/mutation guard.
