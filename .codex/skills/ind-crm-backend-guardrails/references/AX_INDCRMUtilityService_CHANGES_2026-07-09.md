## Objective

Return the current user's display name in `/api/auth/entra/context`.

## AX class

- `INDCRMUtilityService`

## Method touched

### `loginEntraContext`

Changes applied:

- Extended the successful header contract by appending `userName` after `defaultCurrencyCode`.
- Resolves `userName` from `INDPersonaTable::findByCRM(defaultCrmUsuarioTable.RecId).Name` in the final default company context.
- Kept existing header positions stable:
  - `[1] success`
  - `[2] message`
  - `[3] axUserId`
  - `[4] userActive`
  - `[5] appActive`
  - `[6] defaultCompany`
  - `[7] defaultCurrencyCode`
  - `[8] userName`

## API alignment

- `EntraContextHeaderDto` now exposes `UserName`.
- `AuthController.MapEntraHeader` reads `Header[8]` defensively and leaves it empty for older AX responses.
- `.codex/ENDPOINTS.md` documents `Header.UserName`.

## Compatibility

- Backward compatible for deployments where AX still returns the previous seven-field header.
- No route, request body, response envelope, cache key, or context-token validation behavior changed.

## Verification

- `MSBuild.exe IND_CRM_API.csproj /p:Configuration=Release /p:Platform=x86 /v:minimal` succeeded.
- Pending real AX import/compile of `INDCRMUtilityService`.
