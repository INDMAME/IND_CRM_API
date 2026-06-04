# AX INDCRMUtilityService changes - 2026-06-04

## Objective
Expose reusable `INDControlDataVisibility` wrappers from `INDCRMUtilityService` so API and module services can use one standard entry point.

## Methods added
- `controlDataVisibilityFindModuleAccessLevel(...)`
- `controlDataVisibilityResolveVisiblePersonAliasSet(...)`
- `controlDataVisibilityResolveVisibleAxUserIdSet(...)`
- `controlDataVisibilityResolveVisibleCrmUserIdSet(...)`
- `controlDataVisibilityCanViewOwnerAlias(...)`
- `controlDataVisibilityCanViewOwnerAxUserId(...)`
- `controlDataVisibilityCanMutateOwnerAxUserId(...)`
- `controlDataVisibilityGetVisibleUsers(container _data)`

## Behavior
- Delegates the core hierarchy algorithm to `INDControlDataVisibilityResolver`.
- Keeps the public naming identifiable through the `controlDataVisibility` prefix.
- Returns visible users by AX user id and optional legacy CRM user id for diagnostics and future modules.
- Does not call or replace `CRMUsuarioSubordinadoTable`; expense sheets keep their existing subordinate logic.

## Pending checks in AX
- Import and compile after importing `INDControlDataVisibilityResolver`.
- Validate the diagnostic container shape with one user for `CRM / VISITAS_GESTION`.
