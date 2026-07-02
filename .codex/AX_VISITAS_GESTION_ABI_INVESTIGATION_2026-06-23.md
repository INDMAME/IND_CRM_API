# AX VISITAS_GESTION ABI investigation - 2026-06-23

## Contexto
Caso observado en `TAZ / MAME / CRM / VISITAS_GESTION`.

Configuracion visible en AX:
- `DataVisibilityMode = OwnOnly / Solo propios`
- `HierarchyDepth = DirectOnly / Solo directos`
- `MutationPolicy = OwnOnly / Solo propios`
- Sin excepciones Add/Remove

Jerarquia visible:
- `P00003 / MAME` tiene directos como `AMA`, `ABP`, `ESG`, `SO`, etc.
- `ABI` cuelga de `AMA`, por tanto es indirecto respecto a `P00003`.

Resultado esperado:
- En `OwnOnly`, solo debe salir `P00003 / MAME`.
- En `OwnAndHierarchy + DirectOnly`, `ABI` no debe salir.
- `ABI` solo debe salir con `OwnAndHierarchy + FullDescendants` o una excepcion Add que lo incluya.

## Evidencia de la captura
La captura de ejecucion AX muestra:
- `Calling ctrlGetVisibleUsers. Company=TAZ Viewer=MAME App=CRM Module=VISITAS_GESTION`
- `Visible users count=11`
- `ABI` aparece como linea visible.
- Las lineas muestran `Policy=Solo propios | PolicyInt=0 | PolicyLabel=Solo propios`.

Esa salida no corresponde a los jobs actuales:
- Falta `build=2026.06.23-runtime-version-gate`.
- Falta `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard`.
- Usa el campo antiguo `Policy=...`; los jobs actuales imprimen `MutationPolicy=...` o `DIAG line ... mutationPolicy=...`.

Conclusion para esa captura concreta: AX esta ejecutando un job/clase anterior o no recompilada. No es evidencia de que los objetos actuales sigan devolviendo ABI.

## Causa raiz original en codigo AX
El diff contra la version base muestra dos problemas reales:

1. `INDControlDataVisibilityResolver.findModuleAccessLevel`
   - Antes usaba un `select firstonly` sobre `INDWebModuleAccessLevel`.
   - No listaba candidatos ni elegia la fila mas restrictiva.
   - Si existian filas efectivas duplicadas o una fila historica con jerarquia, podia seleccionar una fila distinta a la que el formulario hacia pensar.
   - Esto explica un resultado con muchos usuarios si la fila efectiva usada por runtime era `OwnAndHierarchy + FullDescendants`.

2. `INDControlDataVisibilityResolver.resolveVisiblePersonAliases`
   - Antes aplicaba `INDModuleDataVisibilityTarget::resolveTargets(...)` tambien cuando `DataVisibilityMode == OwnOnly`.
   - Ahora `OwnOnly` queda cerrado y no se amplia con targets.
   - En la captura se ve `Sin excepciones`, asi que este punto no explica por si solo ABI en esa ejecucion, pero era un hueco real de seguridad funcional.

## Causa de confusion en la salida
El campo `Policy=Solo propios` de la captura es `MutationPolicy`, no `DataVisibilityMode`.

Por tanto, una linea con:
- `PolicyInt=0`
- `CanMutate=false`

solo demuestra que la politica de modificacion era `OwnOnly`; no demuestra que la visibilidad efectiva fuera `OwnOnly`.

## Riesgo de importacion detectado
En el proyecto existen dos XPO con el mismo nombre de tabla:
- `INDWebModuleAccessLevel.xpo` actual: `AccessRights`, `ModuleCode`, `RefRecIdCiaPermitida`, `AppCode`, `DataVisibilityMode`, `MutationPolicy`, `HierarchyDepth`.
- `INDWebModuleAccessLevel (1).xpo` historico: `MutationPolicy`, `HierarchyDepth`, `DataVisibilityMode`, `AccessRights`, `ModuleCode`, `RefRecIdCiaPermitida`, `AppCode`.

No deben importarse ambos. Los jobs actuales imprimen `FieldIds INDWebModuleAccessLevel ...` y avisan si AX esta usando el layout historico.

## Estado actual de la correccion
Los objetos actuales cierran `OwnOnly` en tres capas:
- `INDControlDataVisibilityResolver.resolveVisiblePersonAliases`
- wrappers publicos de `INDCRMUtilityService.ctrlResolveVisible...`
- defensa final de `INDCRMUtilityService.ctrlGetVisibleUsers`

`INDCRMVisitsService.getActivityContainer` usa `resolveVisibleActivityOwnerAxUserIds`, que llama a `INDCRMUtilityService.ctrlResolveVisibleAxUserIdSet`. No se ha encontrado mezcla con `CRMUsuarioSubordinadoTable` en la ruta de visitas.

`CrmDataVisibilityController.GetVisibleUsers` solo mapea las lineas que devuelve AX. No inventa usuarios.

## Validacion necesaria en AX
Para cerrar el caso, ejecutar en AX con objetos importados y compilados:

1. `Job_INDDV_ABI_OwnOnlySmoke`
   - Debe mostrar `ABI smoke build=2026.06.23-runtime-version-gate`.
   - Debe mostrar `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard`.
   - Debe mostrar `header effective ... dataVis=0/OwnOnly`.
   - Debe mostrar `visible lineCount=1`.

2. `Job_INDDVUsersDiag`
   - Texto plano para copiar/pegar.
   - Debe mostrar `DIAG build=2026.06.23-runtime-version-gate`.
   - Debe mostrar candidatos, fila efectiva, sets base/directos/completos/targets/final.

3. `Job_INDDVUsersCases`
   - Valida el caso activo A/B/C/D.
   - Se corta con `throw error` si `INDCRMUtilityService` no es la version actual.

Si cualquiera de esos jobs no muestra `runtime-version-gate` o `utilityBuild`, hay que reimportar y compilar `INDCRMUtilityService` antes de interpretar ABI.
