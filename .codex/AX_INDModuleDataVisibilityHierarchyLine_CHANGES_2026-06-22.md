# AX INDModuleDataVisibilityHierarchyLine changes - 2026-06-22

## Objective
Reduce Axapta 3.0 import/compile risk in the visibility hierarchy table used by `INDControlDataVisibilityResolver`.

## Object touched
- `INDModuleDataVisibilityHierarchyLine.xpo`

## Methods touched
- `Active`
- `wouldCreateCycleWithOverlap`

## Behavior
- Replaced ternary expressions with equivalent `if/else` blocks.
- No hierarchy behavior changed:
  - `Active` still returns `Yes` only when the line is active today.
  - Cycle and overlap validation still uses the same effective check date.

## Reason
If this table fails to compile during a grouped import, AX can continue running older visibility classes. That matches the current screenshots, where `ctrlGetVisibleUsers` still returns ABI and the header does not include the expected `utilityBuild` marker.

## Validation
- Import and compile `INDModuleDataVisibilityHierarchyLine.xpo` before `INDControlDataVisibilityResolver.xpo` and `INDCRMUtilityService.xpo`.
- Run `Job_INDDVUsers.xpo` or `Job_Test_ctrlGetVisibleUsers.xpo` and confirm the output contains `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard`.
