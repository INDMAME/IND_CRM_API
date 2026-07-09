# AX INDWebModuleAccessLevel changes - 2026-06-22

## Objective
Prevent new ambiguous module-permission rows from being saved for the same effective user/company/module context.

## Object touched
- `INDWebModuleAccessLevel.xpo`
- `INDWebModuleAccessLevel (1).xpo` mirror export

## Import note
- Do not import both table exports blindly. `INDWebModuleAccessLevel (1).xpo` is a historical mirror export and can differ in field ordering from the current table export.
- Prefer the current `INDWebModuleAccessLevel.xpo` unless the target AX layer explicitly requires the mirror export.
- After import, run `Job_INDDVUsers.xpo` and compare the printed `FieldIds INDWebModuleAccessLevel ...` line with the expected table fields before trusting UI/runtime diagnostics.
- The current export declares the functional fields after `AppCode`: `DataVisibilityMode`, `MutationPolicy`, `HierarchyDepth`. The mirror export declares `MutationPolicy`, `HierarchyDepth`, `DataVisibilityMode` before `AccessRights`; that layout must not be mixed with the current export during this ABI investigation.

## Method touched
- `validateWrite`

## Behavior
- Keeps the existing Entra warning behavior.
- Adds a defensive duplicate check when `AccessRights != NoAccess`.
- Blocks saving a second active permission for the same effective `UserId + CiaId + AppCode + ModuleCode`, even if it points through a different `INDCiasPermitidas.RecId`.
- The validation only affects module access configuration and does not change API contracts.
- Removed the remaining ternary expression from both table exports to reduce Axapta 3.0 import/compile risk; behavior is unchanged.

## Risks
- Existing duplicate rows can still exist until cleaned manually.
- Editing a duplicate active row can fail validation until the conflicting row is removed or set to no access.

## Validation
- Import and compile only the table export that applies to the target AX layer.
- Try saving a duplicate active row for the same effective user/company/app/module and confirm AX blocks it.
- Confirm saving the existing unique `VISITAS_GESTION` permission still works.
