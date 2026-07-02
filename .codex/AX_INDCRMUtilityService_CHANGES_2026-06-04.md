# AX INDCRMUtilityService changes - 2026-06-04

## Objective
Expose reusable `INDControlDataVisibility` wrappers from `INDCRMUtilityService` so API and module services can use one standard entry point.

## Methods added
- `ctrlFindModuleAccessLevel(...)`
- `ctrlResolveVisiblePersonAliasSet(...)`
- `ctrlResolveVisibleAxUserIdSet(...)`
- `ctrlResolveVisibleCrmUserIdSet(...)`
- `ctrlCanViewOwnerAlias(...)`
- `ctrlCanViewOwnerAxUserId(...)`
- `ctrlCanMutateOwnerAxUserId(...)`
- `ctrlGetVisibleUsers(container _data)`

## Behavior
- Delegates the core hierarchy algorithm to `INDControlDataVisibilityResolver`.
- Keeps the public naming short for Axapta import limits while the objects remain identifiable through `INDControlDataVisibility`.
- Returns visible users by AX user id and optional legacy CRM user id for diagnostics and future modules.
- `ctrlGetVisibleUsers` resolves `ctrlResolveVisibleAxUserIdSet` and only returns people with a resolvable `INDPersonaTable.UserId`.
- Does not call or replace `CRMUsuarioSubordinadoTable`; expense sheets keep their existing subordinate logic.

## Pending checks in AX
- Import and compile after importing `INDControlDataVisibilityResolver`.
- Validate the diagnostic container shape with one user for `CRM / VISITAS_GESTION`.
- Validate that a visible hierarchy person without AX user is absent from `Job_INDDVUsers` and the API `visible-users` response.
