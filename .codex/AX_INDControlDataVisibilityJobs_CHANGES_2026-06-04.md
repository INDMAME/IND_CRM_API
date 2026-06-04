# AX INDControlDataVisibility jobs changes - 2026-06-04

## Objective
Add non-destructive Axapta jobs to validate the new `INDControlDataVisibility` flow before testing through Postman or the web.

## Jobs added
- `Job_INDDVUsers`
- `Job_INDDVVisits`
- `Job_INDDVDetail`
- `Job_INDDVGuard`

## Behavior
- Jobs use `curext()` and `curUserId()` by default, avoiding hardcoded company and user ids.
- Jobs use `CRM / VISITAS_GESTION` as the visits pilot module.
- Jobs do not call update or delete methods.
- Detail and mutation jobs can be pointed to a specific `RecId`; if left as `0`, they use the first visible visit from the list method.

## Pending checks in AX
- Import jobs after importing and compiling the new resolver, utility and visits service changes.
- Run jobs with a user that has a configured `INDWebModuleAccessLevel` row for `CRM / VISITAS_GESTION`.
- Run the same jobs with an own-only user and with a hierarchy user to compare results.
