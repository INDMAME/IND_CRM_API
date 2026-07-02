# AX INDControlDataVisibilityResolver changes - 2026-06-04

## Objective
Implement the first version of `INDControlDataVisibilityResolver` for the simplified module data visibility model.
The class was renamed from the earlier working name `INDModuleDataVisibilityResolver` so the global solution is easy to identify with the `INDControlDataVisibility` prefix.

## Methods added
- `resolveActivePersonByAxUserId(UserId _axUserId)`
- `resolveActivePersonByAlias(str _personAlias)`
- `resolvePersonAliasByAxUserId(UserId _axUserId)`
- `resolveAxUserIdByAlias(str _personAlias)`
- `resolveCrmUserIdByAlias(str _personAlias)`
- `resolveVisiblePersonAliases(UserId _viewerAxUserId, DataAreaId _companyId, str _appCode, str _moduleCode, date _asOfDate)`
- `resolveVisibleAxUserIds(UserId _viewerAxUserId, DataAreaId _companyId, str _appCode, str _moduleCode, date _asOfDate)`
- `resolveVisibleCrmUserIds(UserId _viewerAxUserId, DataAreaId _companyId, str _appCode, str _moduleCode, date _asOfDate)`
- `canViewPersonAlias(UserId _viewerAxUserId, DataAreaId _companyId, str _appCode, str _moduleCode, str _ownerAlias)`
- `canViewOwnerAxUserId(UserId _viewerAxUserId, DataAreaId _companyId, str _appCode, str _moduleCode, UserId _ownerAxUserId)`
- `canMutateOwnerAxUserId(UserId _viewerAxUserId, DataAreaId _companyId, str _appCode, str _moduleCode, UserId _ownerAxUserId)`
- `findModuleAccessLevel(UserId _viewerAxUserId, DataAreaId _companyId, str _appCode, str _moduleCode)`
- `addSetToSet(Set _targetSet, Set _sourceSet)`

## Behavior
- Resolves the viewer person through `INDPersonaTable.UserId`.
- Requires `INDPersonaTable.Blocked = No`.
- Requires an existing `INDWebModuleAccessLevel` row with access rights for the viewer, company, app and module.
- Applies base visibility from `DataVisibilityMode`.
- Applies hierarchy using `INDModuleDataVisibilityHierarchyLine::resolveDescendants`.
- Applies manual `Add` targets first.
- Applies manual `Remove` targets last, so remove always wins.
- Removes duplicated aliases through `Set`.
- Converts final aliases to AX user ids and CRM user ids for module-specific filters.
- Keeps mutation checks aligned with effective visibility; own-only mutation also requires the owner to be visible and active.

## Conservative decisions
- `ModuleBusinessRules` in `canMutateOwnerAxUserId` falls back to own-only plus visibility. A module service can apply broader business rules after using this resolver.
- `AllUsers` adds all non-blocked `INDPersonaTable` aliases in the company context. The business module must still filter records by company.

## Pending checks in AX
- Compile after importing the new enums, table fields and table methods.
- Confirm enum value names match `INDDataVisibilityMode`, `INDDataVisibilityHierarchyDepth`, `INDMutationPolicy`, `INDDataVisibilityTargetAction` and `INDDataVisibilityTargetScope`.
- Confirm `INDPersonaTable.RefRecIdCRM` is available in the target environment for CRM legacy mapping.
- Confirm `INDWebModuleAccessLevel.AppCode`, `DataVisibilityMode`, `HierarchyDepth` and `MutationPolicy` exist before compiling this class.
