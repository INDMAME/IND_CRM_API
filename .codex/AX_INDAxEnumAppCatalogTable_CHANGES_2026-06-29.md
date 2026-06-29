# AX INDAxEnumAppCatalogTable Changes - 2026-06-29

## Objetivo
- Sugerir automaticamente `SortOrder` al crear una configuracion de catalogo publico.
- Evitar que el usuario tenga que revisar manualmente el ultimo orden usado para el mismo aplicativo y enum AX.

## Metodos tocados
- `INDAxEnumAppCatalogTable.modifiedField`
- `INDAxEnumAppCatalogTable.initFromINDAxEnumsTable`
- `INDAxEnumAppCatalogTable.initSortOrderFromContext`
- `INDAxEnumAppCatalogTable.nextSortOrderForContext`
- `INDAxEnumAppCatalogTable.initValue`
- `INDAxEnumAppCatalogTableForm.INDAxEnumAppCatalogTable.initValue`

## Regla funcional
- El siguiente orden se calcula sobre registros activos en la company actual.
- El contexto es `AppCode + AxEnumName + AxEnumId`, usando `AxEnumsTableRefRecId` para resolver el enum tecnico.
- Si existen registros activos en el contexto, se propone `max(SortOrder) + 1`.
- Si no existe ningun registro activo en el contexto, se mantiene `0` como primer valor valido.
- No se sobrescribe el orden de registros ya guardados ni un valor manual distinto de `0`.

## Compatibilidad
- No cambia la estructura de tablas, indices ni contratos API.
- `SortOrder` sigue siendo editable por el usuario.
- La regla queda alineada con `checkSortOrderUnique`, que valida duplicados activos dentro del mismo aplicativo y enum AX.

## Validacion pendiente
- Importar los XPO en AX y compilar `INDAxEnumAppCatalogTable` y `INDAxEnumAppCatalogTableForm`.
- Probar alta desde el formulario con un enum que ya tenga configuraciones activas y confirmar que propone el ultimo orden + 1.
