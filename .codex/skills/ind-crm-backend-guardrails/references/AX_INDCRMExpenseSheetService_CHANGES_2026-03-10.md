# AX Change Log - INDCRMExpenseSheetService (2026-03-10)

## Objetivo
Extender `getExpenseSheetsList(container _data)` para soportar un nuevo filtro opcional `includeSubordinates` sin romper el contrato legacy actual del metodo.

## Alcance (fase AX)
- Clase AX: `INDCRMExpenseSheetService`
- Metodo AX impactado:
  - `getExpenseSheetsList(container _data)`
- Helper AX nuevo:
  - `buildExpenseSheetListRow(CRMHojaGastosTable _hoja)`
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
  - `_data[10]` = `includeSubordinates` (opcional, `0|1|false|true`)
- Compatibilidad:
  - El metodo sigue aceptando el contrato actual cuando `_data[9]` no viene.
  - El metodo queda preparado para un contrato futuro con indices estables y placeholder `null` en `_data[9]`.
- Salida:
  - Sin cambios en forma ni en orden de columnas del row.

## Cambios aplicados por metodo
### getExpenseSheetsList
- Se agrega parseo defensivo de `_data[9]` para filtrar `expenseSheetStatus` solo cuando llega un valor valido `0..4`.
- Se agrega parseo opcional de `_data[10]` para activar el modo subordinados.
- Se mantiene el flujo actual cuando `includeSubordinates = false`:
  - filtro por `hoja.UserId == crmUserId`.
- Se agrega una segunda rama cuando `includeSubordinates = true`:
  - listado por `exists join CRMUsuarioSubordinadoTable`
  - relacion:
    - `CRMUsuarioSubordinadoTable.UserIdJefe == crmUserId`
    - `CRMUsuarioSubordinadoTable.UserIdSubordinado == hoja.UserId`
- Se conservan en ambas ramas todos los filtros existentes:
  - texto
  - facturacion
  - exclusión de legacy sin `CreatedDate`
  - rango de fechas
  - proyecto
  - moneda
  - estado

### buildExpenseSheetListRow
- Nuevo helper para construir la fila de salida con columnas estables.
- Evita duplicar el armado del container entre la rama actual y la rama de subordinados.

## Riesgos y mitigaciones
- Riesgo:
  - si la tabla `CRMUsuarioSubordinadoTable` contiene duplicados por jefe/subordinado, un `join` normal podria repetir hojas.
- Mitigacion:
  - se usa `exists join`, no `join`.

- Riesgo:
  - el endpoint actual aun no envia `_data[10]`.
- Mitigacion:
  - el metodo mantiene `includeSubordinates = false` por defecto y sigue funcionando igual que hoy.

## Pendientes para API
- Ninguno para el contrato principal del listado.

## Ajuste de integracion API (fase AX->API aplicada en este turno)
- `Contracts/Requests/GetExpenseSheetsListRequest`:
  - nuevo campo opcional `includeSubordinates` (`bool?`).
- `CrmExpenseSheetsController.GetExpenseSheetsList`:
  - se agrega log explicito de `includeSubordinates`.
  - se reemplaza el append variable del container AX por construccion con indices estables.
  - orden final enviado a AX:
    - `_data[1]` = `companyId`
    - `_data[2]` = `axUserId`
    - `_data[3]` = `filter`
    - `_data[4]` = `billedMode`
    - `_data[5]` = `createdDateFrom`
    - `_data[6]` = `createdDateTo`
    - `_data[7]` = `projId`
    - `_data[8]` = `currencyCode`
    - `_data[9]` = `expenseSheetStatus` o token `null`
    - `_data[10]` = `includeSubordinates` como `0|1`
- Documentacion local:
  - se actualiza `.codex/ENDPOINTS.md`
  - se actualiza `.codex/MCP_ENDPOINTS.md`

## Revision de routing
- Se reviso `RoutePrefix("api/crm/expensesheets")` y las rutas hermanas del controlador.
- No se cambiaron plantillas de ruta ni verbos HTTP.
- `POST /api/crm/expensesheets/list` sigue siendo una ruta literal unica y no colisiona con:
  - `GET /api/crm/expensesheets/{hojaGastosId}`
  - `PUT /api/crm/expensesheets/{hojaGastosId}`
  - `POST /api/crm/expensesheets/tickets/*`
- Se mantiene la constraint regex existente para excluir el literal `tickets` en rutas por `hojaGastosId`.

## Checklist de salida
- [x] Plan por clase AX definido.
- [x] Metodo AX ajustado con compatibilidad legacy.
- [x] Archivo temporal de cambios creado y actualizado.
- [x] Integracion AX->API aplicada y documentada.
