# AX INDCRMUtilityService changes - 2026-06-09

## Objective
Expose the mutation policy resolved from `INDWebModuleAccessLevel.MutationPolicy` in `ctrlGetVisibleUsers` so the CRM API can tell the frontend whether update/delete are allowed for each visible owner.

## Methods touched
- `ctrlGetVisibleUsers(container _data)`

## Contract adjustment
Input remains unchanged:
- `_data[1] = CompanyId`
- `_data[2] = AppCode`
- `_data[3] = ModuleCode`
- `_data[4] = ViewerAxUserId`
- `_data[5] = AsOfDate optional yyyyMMdd`
- `_data[6] = IncludeCrmUserId optional 0/1`

Output lines keep the existing first five positions and append mutation metadata:
- `Line[1] = personAlias`
- `Line[2] = axUserId`
- `Line[3] = crmUserId`
- `Line[4] = name`
- `Line[5] = source`
- `Line[6] = mutationPolicy`
- `Line[7] = mutationPolicyInt`
- `Line[8] = mutationPolicyLabel`
- `Line[9] = canMutate` (`1` or `0`)

## Behavior
- `MutationPolicy` is read once from the viewer module access row.
- `CanMutate` is calculated per visible AX user with `ctrlCanMutateOwnerAxUserId`.
- `CanMutate` is intended for update/delete UI decisions. It must not be used to create records on behalf of subordinate users.
- The method does not call or replace `CRMUsuarioSubordinadoTable`; expense sheets keep their legacy subordinate behavior.

## Pending checks in AX
- Import and compile `INDCRMUtilityService.xpo`.
- Run `Job_INDDVUsers` for `CRM / VISITAS_GESTION` and confirm each visible line includes positions 6-9.
- Confirm a subordinate row with `MutationPolicy = SameAsVisibility` returns `CanMutate = 1`, while `OwnOnly` and the current MVP `ModuleBusinessRules` keep subordinate rows as `CanMutate = 0`.
