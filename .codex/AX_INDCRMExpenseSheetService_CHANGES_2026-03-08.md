# AX Change Log - INDCRMExpenseSheetService (2026-03-08)

## Objetivo
Corregir el filtrado por fecha de `getExpenseSheetsList` para que el endpoint `POST /api/crm/expensesheets/list` respete `createdDateFrom/createdDateTo` enviados por la API y descarte siempre registros legacy con `CreatedDate` vacio o `dateNull()`.

## Alcance (fase AX)
- Clase AX: `INDCRMExpenseSheetService`
- Metodo AX impactado:
  - `getExpenseSheetsList(container _data)`
- Endpoint/API relacionado:
  - `CrmExpenseSheetsController.GetExpenseSheetsList`

## Contrato de entrada/salida relevante
- Entrada `getExpenseSheetsList`:
  - `_data[1]` = `companyId`
  - `_data[2]` = `axUserId`
  - `_data[3]` = `filterTxt`
  - `_data[4]` = `billedMode`
  - `_data[5]` = `createdDateFrom` (opcional, `yyyymmdd`)
  - `_data[6]` = `createdDateTo` (opcional, `yyyymmdd`)
  - `_data[7]` = `projId` (opcional)
  - `_data[8]` = `currencyCode` (opcional)
  - `_data[9]` = `expenseSheetStatus` (opcional)
- Salida:
  - Lista de filas de hoja de gastos con `CreatedDate` en formato `yyyyMMdd` para posterior normalizacion API a `DD.MM.YYYY`.

## Cambios aplicados por metodo
### getExpenseSheetsList
- Se reemplazo el parseo de `_data[5]` y `_data[6]` para usar `INDCRMUtilityService::parseYmdDate(...)`.
- Se elimina la dependencia incorrecta de `str2Date(..., 123)` para valores `yyyymmdd`, que desactivaba el filtro de fechas al derivar a `dateNull()`.
- Se agrega una validacion directa en el `while select`:
  - `hoja.CreatedDate != dateNull()`
- Resultado esperado:
  - Si la API envia rango de fechas valido, AX aplica el rango real.
  - Si una hoja no tiene `CreatedDate` real, queda excluida aunque no haya rango informado.

### getExpenseSheetTicketsList
- Se reemplazo el parseo de `_data[5]` y `_data[6]` para usar `INDCRMUtilityService::parseYmdDate(...)`.
- Se elimina la dependencia incorrecta de `str2Date(..., 123)` para valores `yyyymmdd`.
- Se conserva la validacion existente de rango (`from <= to`) y el contrato del contenedor sin cambios.

### Homogeneizacion de clase
- Se verifico toda la clase `INDCRMExpenseSheetService` para parseos de fecha de entrada.
- Resultado:
  - No quedan llamadas a `str2Date(...)` dentro de la clase para fechas de input.
  - Los parseos de entrada de fecha quedan unificados en `INDCRMUtilityService::parseYmdDate(...)`.
  - Las llamadas `date2Str(...)` se mantienen porque forman parte del formateo de salida, no del parseo de entrada.

## Riesgos y mitigaciones
- Riesgo:
  - Algunas hojas legacy sin `CreatedDate` dejaran de aparecer en listados.
- Mitigacion:
  - Es el comportamiento solicitado y evita mezclar registros incompletos con resultados filtrados por fecha.

## Pendientes para API
- No se requieren cambios de contrato ni de routing.
- No se requieren cambios en DTOs ni en el mapeo C#.
- No quedan pendientes de homogeneizacion de parseo de fechas dentro de esta clase AX.

## Checklist de salida
- [x] Plan por clase AX definido.
- [x] Clase AX ajustada con cambio minimo y compatible.
- [x] Archivo temporal de cambios creado y actualizado.
- [x] Pendientes para API documentados.
