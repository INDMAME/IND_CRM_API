# AX INDCRMVisitsService changes - 2026-06-04

## Objective
Apply the new parallel `INDControlDataVisibility` hierarchy to visits while keeping expense sheet subordinate logic untouched.

## Methods added
- `resolveVisibleActivityOwnerAxUserIds(...)`
- `activityIsVisibleInResolvedSet(...)`
- `applyActivityVisibilityRange(...)`
- `canViewerAccessActivity(...)`
- `canViewerMutateActivity(...)`
- `getActivityOwnerAxUserId(...)`
- `getActivityOwnerAlias(...)`
- `getActivityOwnerCrmUserId(...)`

## Behavior
- Lists visits through the visible AX user id set for `CRM / VISITAS_GESTION`.
- `getActivityContainer` accepts optional `ownerAxUserId` at the end of the container to reduce the already visible set to one visible owner.
- Validates read access for detail lookup by `RecId` and by activity code.
- Validates mutation access for update, delete and visit assistant create/delete.
- Uses `CRMActividadTable.INDCreatedByUserId` as the owner field for the visits pilot.
- Appends optional app/module parameters at the end of existing containers to keep older callers stable.

## Non-goals
- No changes were made to `INDCRMExpenseSheetService`, `CRMUsuarioTable` or `CRMUsuarioSubordinadoTable`.
- Expense sheets continue using the legacy subordinate hierarchy until a later migration is explicitly requested.

## Pending checks in AX
- Import and compile after importing `INDControlDataVisibilityResolver` and `INDCRMUtilityService`.
- Validate that a user with own-only visibility only sees own visits.
- Validate that hierarchy visibility sees descendants configured in `INDModuleDataVisibilityHierarchyLine`.
- Validate that remove targets override add/hierarchy targets.
