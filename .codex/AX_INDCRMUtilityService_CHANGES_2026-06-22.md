# AX INDCRMUtilityService changes - 2026-06-22

## Objective
Fail closed when `CRM / VISITAS_GESTION` resolves an effective permission with `DataVisibilityMode = OwnOnly` but a lower resolver path still returns hierarchy users.

## Evidence
`Job_Test_ctrlGetVisibleUsers` showed hierarchy aliases such as `ABI` for `TAZ / MAME / CRM / VISITAS_GESTION` while the visible form configuration showed `Solo propios`.

The old job labels `Policy`, `PolicyInt` and `PolicyLabel` were misleading: they came from `MutationPolicy`, not `DataVisibilityMode`. The updated jobs now print `DataVisibilityMode`, `HierarchyDepth` and `MutationPolicy` separately.

## Methods touched
- `ctrlGetVisibleUsers`
- `ctrlResolveVisiblePersonAliasSet`
- `ctrlResolveVisibleAxUserIdSet`
- `ctrlResolveVisibleCrmUserIdSet`

## Behavior
- If the effective module permission is `OwnOnly`, each public wrapper rebuilds its output set with only the viewer:
  - Alias set: only the active viewer alias.
  - AX user set: only the active viewer AX user.
  - CRM user set: only the viewer CRM user when the legacy mapping exists.
- `ctrlGetVisibleUsers` now also fails closed immediately before building response lines, resolving the active viewer person inside the target company and rebuilding both alias and AX user sets from that row. The diagnostic/API-facing container cannot publish hierarchy rows if the effective `DataVisibilityMode` is `OwnOnly`.
- `ctrlGetVisibleUsers` appends an internal build marker to the AX header container. Existing API mapping ignores this extra header field, while diagnostics can prove whether the updated Utility class is running.
- The header now also includes the effective permission row used inside `ctrlGetVisibleUsers`: access RecId, `DataVisibilityMode`, `HierarchyDepth`, `MutationPolicy`, `RefRecIdCiaPermitida`, row user/company and access rights. Existing API mapping still ignores these extra fields.
- Other visibility modes keep their previous resolver behavior.
- `ctrlGetVisibleUsers` contract remains unchanged.
- Legacy expense-sheet subordinate logic remains untouched.
- Removed ternary expressions from the XPO to reduce Axapta 3.0 import/compile risk; this is syntax hardening, not a contract change.

## Import order
Import and compile these AX objects before retesting:
- `INDModuleDataVisibilityHierarchyLine.xpo`
- `INDModuleDataVisibilityTarget.xpo`
- `INDControlDataVisibilityResolver.xpo`
- `INDCRMUtilityService.xpo`

Then run `Job_Test_ctrlGetVisibleUsers` for `TAZ / MAME / CRM / VISITAS_GESTION`. With `Solo propios`, expected visible count is `1` and the only line should be the viewer alias `P00003`. The job should also print `Header effective access ... dataVis=0/OwnOnly`.

## Residual risk
This is a defensive closure at the public wrapper boundary. If AX still shows hierarchy rows after importing and compiling these objects, first confirm that the updated jobs print the build marker, then inspect the selected `DataVisibilityMode` and `rowUser`. If `DataVisibilityMode` is not `OwnOnly`, the issue is the effective permission row selected by `INDControlDataVisibilityResolver`; if it is `OwnOnly` and line count is still greater than 1, the running AOS/session is not executing the updated `INDCRMUtilityService`.

Una captura que solo muestre `Header success=true...` sin `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard` ni `Header effective access ...` no demuestra que AX este ejecutando esta version.
