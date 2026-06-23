# AX INDControlDataVisibilityResolver changes - 2026-06-22

## Objective
Fix the visibility hierarchy bug where `ctrlGetVisibleUsers` could return hierarchy users for a viewer whose visible form row showed `Solo propios`.

## Hipotesis de causa raiz tratada
`findModuleAccessLevel` dependia originalmente de que el usuario visor recibido coincidiera exactamente con `INDCiasPermitidas.UserId` y despues usaba un permiso efectivo arbitrario con `firstonly`. En este dataset la persona visible puede identificarse por usuario AX (`MAME`) y por alias (`P00003`), mientras que la jerarquia usa alias. Si existen filas de permiso antiguas o equivalentes entre esas identidades, runtime puede aplicar una fila con jerarquia aunque el administrador vea `Solo propios`.

Esta hipotesis no se considera completamente probada hasta que AX imprima las nuevas lineas diagnosticas `Legacy firstOnly` y `Effective enums` desde `Job_INDDVUsers.xpo` o `Job_INDDVUsersDiag.xpo`.

## Method touched
- `findModuleAccessLevel`
- `resolveActivePersonByAxUserId`

## Behavior
- Replaces arbitrary `firstonly` selection with a deterministic scan of all effective rows for the viewer, company, app and module.
- Resolves the active viewer person first and evaluates candidate rows for the raw viewer id, canonical `INDPersonaTable.UserId`, and viewer alias.
- Avoids `OR` predicates inside the permission join path; the resolver now reads candidate access rows first and validates the linked `INDCiasPermitidas`, `INDWebModule`, and `INDWebApp` rows with explicit selects.
- Resolves an active person by exact `INDPersonaTable.UserId` first and only then falls back to `Alias`, so a viewer AX user id cannot be accidentally interpreted as another person's alias.
- Chooses the most restrictive row when duplicates exist:
  - `OwnOnly` before manual targets, hierarchy modes and all users.
  - `DirectOnly` before `FullDescendants`.
  - `OwnOnly` mutation before module rules and same-as-visibility.
  - Lower access-right value and lower `RecId` as final tie breakers.
- Keeps the public AX wrapper contract unchanged.
- Keeps expenses and `CRMUsuarioSubordinadoTable` untouched.
- Closes `OwnOnly / Solo propios` so it cannot be expanded by manual visibility targets; it returns only the viewer.

## Risks
- If duplicate or identity-equivalent permissions already exist and one is intentionally broader, runtime visibility now fails closed to the most restrictive effective row.
- Existing duplicate data should still be cleaned in AX, but it will no longer overexpose hierarchy while cleanup is pending.

## Validation
- Import and compile `INDControlDataVisibilityResolver`.
- Ejecutar `Job_INDDVUsers.xpo` o `Job_INDDVUsersDiag.xpo` para comparar `Legacy firstOnly` con el `RecId` efectivo seleccionado.
- En la captura de AX debe aparecer `Effective enums ... DataVisibilityMode=0/OwnOnly` para el caso `Solo propios`; si no aparece, AX esta usando otra fila o una version anterior.
- En `ctrlGetVisibleUsers`, comparar `Effective enums` con `Header effective access`; si el header no aparece, falta importar/compilar `INDCRMUtilityService`.
- Run `Job_INDDVUsersCases` for cases A/B/C/D.
