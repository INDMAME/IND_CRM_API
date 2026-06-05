# AX INDControlDataVisibility alignment - 2026-06-05

## Objective
Align the new global data-visibility hierarchy so it can coexist with the legacy expense-sheet subordinate logic and be reused by future modules.

## Objects touched
- `INDWebAppTable.xpo`: adds the missing `INDWebApp` table export used by `INDWebApp` form, `INDWebModule`, `INDWebUserEntraIdentity`, and `INDWebModuleAccessLevel`.
- `INDWebModule.xpo`: changes the unique module index to `AppCode + ModuleCode`.
- `INDWebModuleAccessLevel (1).xpo`: aligns `AppCode` and `ModuleCode` sizes with related tables, validates Entra identity by `UserId + AppCode`, and allows permissions to be staged before the Entra identity exists.
- `INDModuleDataVisibilityHierarchyLine.xpo`: adds company-safe hierarchy lookups and validates cycles across the configured validity range.
- `INDModuleDataVisibilityTarget.xpo`: marks `INDWebModuleAccessLevel` as `RefRecId` and makes target resolution company-safe.
- `INDControlDataVisibilityResolver.xpo`: requires the web app and module to be active when resolving module access.
- `INDCRMVisitsService.xpo`: keeps `INDCreatedByUserId` as the functional AX owner, resolves legacy `CRMUsuarioTable.UserId` only when available, and supports old visits without `INDCreatedByUserId`.
- `Job_INDDVUsers.xpo` and `AX_INDCRMUtilityService_CHANGES_2026-06-04.md`: align method names to the actual `ctrl...` AX wrappers.

## Compatibility
- Expense sheets keep using `CRMUsuarioSubordinadoTable` and `CRMUsuarioTable.SetSubordinados`.
- Visits use `INDControlDataVisibility` through `INDCRMUtilityService::ctrl...` wrappers.
- Existing API containers remain unchanged.
- `RecId` parsing in visit jobs/services remains `str2int` because these Axapta 3.0 exports model `RefRecId` fields as integer values. Changing that to int64 helpers would add compile risk in this environment.

## Import notes
- Import enums first, then tables, then forms/classes/jobs.
- `INDCiasPermitidas` is treated as an existing dependency. Only its form is present in this package; do not replace the table with a minimal generated version.
- Compile the touched AX objects after import, especially `INDModuleDataVisibilityHierarchyLine`, `INDModuleDataVisibilityTarget`, `INDWebModuleAccessLevel`, and `INDCRMVisitsService`.

## Pending AX validation
- Create or confirm app `CRM` and module `VISITAS_GESTION`.
- Confirm each test user has `INDPersonaTable.UserId` and, if needed for web login, `INDWebUserEntraIdentity` for `CRM`.
- Run `Job_INDDVUsers`, `Job_INDDVVisits`, `Job_INDDVDetail`, and `Job_INDDVGuard`.
