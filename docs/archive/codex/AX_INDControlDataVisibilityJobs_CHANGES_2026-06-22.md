# AX INDControlDataVisibility jobs changes - 2026-06-22

## Objective
Add diagnostic evidence for the visibility hierarchy bug investigation without changing runtime behavior.

## Jobs added
- `Job_INDDVUsersDiag`
- `Job_INDDVUsersCases`
- `Job_INDDV_ABI_OwnOnlySmoke`

## Jobs updated
- `Job_INDDVUsers.xpo`

## Import adjustments
- Rebuilt `Job_INDDVUsersDiag.xpo` as a complete AX job XPO instead of the empty skeleton that was present in the latest export.
- Kept `curext()` and `curUserId()` defaults so the jobs import without hardcoded environment users or companies.
- Simplified `Job_INDDVUsersCases.xpo` to avoid a conditional container assignment and to guard null `Set` values before iterating.
- Kept comments and messages ASCII-only for safer import/export handling.
- Fixed `Job_INDDVUsersDiag.xpo` after import failure:
  - Replaced the nonexistent `INDControlDataVisibilityResolver::resolveAxUserIdByPerson` call with `person.UserId`.
  - Replaced `enum2Value(...)` with `any2int(...)`, matching the current AX code style.
  - Removed `%10` placeholders from job `strFmt` calls to avoid Axapta 3.0 formatting issues.
  - Replaced table display label calls in jobs with `enum2str(...)` to reduce import-order dependencies.
- Converted `Job_INDDVUsersDiag.xpo` to plain X++ job text for copy/paste into the Axapta job editor. It is no longer wrapped as an importable XPO export file.
- Updated `Job_Test_ctrlGetVisibleUsers.xpo` to print the effective `DataVisibilityMode`, `HierarchyDepth` and `MutationPolicy` before calling `ctrlGetVisibleUsers`.
- Renamed the line output fields from `Policy` to `MutationPolicy` to avoid confusing mutation policy with data visibility mode.
- Extended `Job_INDDVUsersDiag.xpo` with a `DIAG candidateAccess[...]` section that prints every `INDWebModuleAccessLevel` candidate for the viewer/company/app/module and marks which row `ctrlFindModuleAccessLevel` selected.
- Added `DIAG aliasCandidateAccess[...]` to check whether the same module has permission rows tied to the viewer alias instead of the AX user id.
- Renamed the final diagnostic line fields from `policy` to `mutationPolicy` for the same reason.
- Added build markers to `Job_Test_ctrlGetVisibleUsers` and `Job_INDDVUsersDiag` so screenshots prove that the updated jobs are being executed.
- Unified current job build markers on `2026.06.22-effective-access-guard`.
- Added `Job_INDDV_ABI_OwnOnlySmoke.xpo` with a unique AOT job name so AX cannot accidentally run an older `Job_Test_ctrlGetVisibleUsers` object with the same name. It uses `curext()` and `curUserId()`, prints the `INDCRMUtilityService` build marker, prints the effective row before and inside `ctrlGetVisibleUsers`, and raises an error if `OwnOnly` returns more than the viewer.
- Added a `DIAG Calling ctrlGetVisibleUsers build=...` line to the plain-text diagnostic job so copied job screenshots are also versioned.
- Added `DIAG runtimeCandidateAccess[...]`, which uses the same identity-equivalent candidate set as `findModuleAccessLevel`: raw viewer id, canonical AX user id and viewer alias.
- Replaced the identity-equivalent candidate joins in `Job_INDDVUsers.xpo` and the plain-text `Job_INDDVUsersDiag.xpo` with explicit `INDCiasPermitidas` selects, matching the resolver and avoiding `OR` predicates inside AX joins.
- Added `consideredByFind` to candidate diagnostics so the output shows whether the current resolver would ignore a row because of the `AccessRights != 0` filter.
- Printed the optional `utilityBuild` header field returned by `ctrlGetVisibleUsers` so the diagnostic can distinguish a newly imported job from an updated `INDCRMUtilityService` class.
- Printed the extended `Header effective access ...` fields returned by `ctrlGetVisibleUsers`, so the diagnostics compare the permission row calculated before the call with the row used inside the public method.
- Reforzado `Job_Test_ctrlGetVisibleUsers` para incluir el marcador de build en la misma linea `Calling...`, de forma que una captura antigua se detecte de inmediato.
- Anadida una validacion explicita en ambos jobs: si falta `utilityBuild`, la `INDCRMUtilityService` importada/compilada es anterior al guard `ownonly-guard`.
- Anadida una validacion explicita de `OwnOnly`: si el permiso efectivo es `Solo propios` y aparece un alias distinto del visor, el job informa error.
- Reforzado `Job_INDDVUsers.xpo`, que sigue siendo un XPO importable, con la misma evidencia critica:
  - marcador `Job_INDDVUsers build=2026.06.22-effective-access-guard`;
  - `FieldIds INDWebModuleAccessLevel ...` para detectar layouts de tabla distintos en AOT;
  - warnings explicitos si `DataVisibilityMode` o `MutationPolicy` aparecen en el orden del layout historico;
  - `Candidate[...]` para listar todas las filas candidatas reales de `INDWebModuleAccessLevel`;
  - `Legacy firstOnly ...` para reproducir la fila que habria elegido la consulta anterior sin ranking restrictivo;
  - `Effective enums ...` para separar `DataVisibilityMode` de `MutationPolicy`;
  - `utilityBuild` para confirmar que AX ejecuta la clase `INDCRMUtilityService` con el guard `ownonly-guard`;
  - errores explicitos cuando `OwnOnly` devuelve mas de un usuario o un alias no propio.

## Behavior
- Prints the input company, viewer AX user, app, module and as-of date.
- Prints the diagnostic build marker and resolved identity (`raw`, canonical AX user and alias).
- Prints the effective `INDWebModuleAccessLevel` row selected by `ctrlFindModuleAccessLevel`.
- Prints all runtime-equivalent and direct candidate `INDWebModuleAccessLevel` rows before comparing expected vs actual visibility.
- Prints `AccessRights`, `DataVisibilityMode`, `HierarchyDepth` and `MutationPolicy` as integer plus label.
- Prints the viewer alias, base aliases, direct children, full descendants, add targets, remove targets, expected final aliases, actual resolver aliases, and final AX users after filtering aliases without `INDPersonaTable.UserId`.
- Compares expected aliases for the effective row against the actual resolver output and warns on missing or unexpected aliases.
- Calls `ctrlGetVisibleUsers` at the end to print the API-facing output lines, including mutation metadata.
- Si `ctrlGetVisibleUsers` devuelve un header sin `utilityBuild`, el diagnostico marca que AX no esta ejecutando la version esperada de `INDCRMUtilityService`.
- Si `ctrlGetVisibleUsers` devuelve un header sin `Header effective access ...`, la utilidad compilada no incluye todavia el diagnostico de fila efectiva.
- Si `OwnOnly` devuelve mas de un usuario o un alias no propio, los jobs lo elevan como error para no depender de revisar visualmente cada linea.
- `Job_INDDVUsers.xpo` is now the preferred importable smoke diagnostic for the current ABI issue. A screenshot that does not show `build=2026.06.22-effective-access-guard` and `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard` is not evidence from the updated objects.
- `Job_INDDV_ABI_OwnOnlySmoke.xpo` is the safest first smoke when old jobs may still exist in AOT. A valid screenshot must show `ABI smoke build=2026.06.22-ownonly-smoke`, `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard`, and `ABI smoke header effective ... dataVis=0/OwnOnly` for the `Solo propios` case.
- If the job warns that `INDWebModuleAccessLevel` uses the historical layout, stop validating ABI with that import set: import only the applicable current table export and recompile before trusting `DataVisibilityMode`.
- La seccion `Legacy firstOnly` muestra la fila de permiso que podia seleccionar la consulta anterior del resolver. Si esa fila es jerarquica y `Effective enums` es `OwnOnly`, la fuga de ABI queda explicada por la seleccion efectiva anterior.
- Does not update data and does not change runtime visibility behavior.
- `Job_INDDVUsersCases` validates the active A/B/C/D case without changing configuration:
  - A: `Solo propios` returns only the viewer.
  - B: `Propios + jerarquia` with `Solo directos` returns viewer plus direct reports and no indirect reports.
  - C: `Propios + jerarquia` with `Jerarquia completa` includes full descendants.
  - D: `Igual que visibilidad` checks `CanMutate=true` for visible rows; `Solo propios` mutation checks that non-own rows are not mutable.

## Validation
- Import and compile the job after importing the current visibility objects.
- Compile the dependency objects first: `INDModuleDataVisibilityHierarchyLine`, `INDModuleDataVisibilityTarget`, `INDControlDataVisibilityResolver` and `INDCRMUtilityService`.
- Run it in the target company as the viewer being diagnosed, for example `TAZ / MAME`.
- Re-run after setting the module permission to each case under investigation:
  - A: `Solo propios`.
  - B: `Propios + jerarquia` with `Solo directos`.
  - C: `Propios + jerarquia` with `Jerarquia completa`.
  - D: same as B with `Igual que visibilidad`.
- Run `Job_INDDVUsersCases` after each configuration change and review warnings/errors.

## Pending
- Capture the printed diagnostic values from AX after import and compile in the target environment.
- Use those values to confirm the effective permission row and the A/B/C/D outcomes.
- For the current `TAZ / MAME / CRM / VISITAS_GESTION` case, if `Effective enums` shows `DataVisibilityMode=0/OwnOnly` and ABI still appears, treat it as a runtime bug in the imported classes. If it shows any other `DataVisibilityMode`, use the `Candidate[...]` rows to identify the permission row actually selected.
- Compare `Effective enums` with `Header effective access`. If they differ, `ctrlGetVisibleUsers` is not using the same compiled resolver/table metadata as the job context.
- Comparar `Legacy firstOnly` con `Effective enums` para confirmar si la seleccion antigua con `firstonly` es la razon por la que ABI aparecia en las capturas anteriores.
