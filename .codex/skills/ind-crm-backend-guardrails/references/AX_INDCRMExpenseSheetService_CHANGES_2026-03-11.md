# AX Change Log - INDCRMExpenseSheetService (2026-03-11)

## Objetivo
Aplicar tres ajustes coordinados para tickets y hojas de gastos:
- adelgazar el contrato del listado generico de tickets,
- crear un listado AX especifico para vinculacion,
- dejar documentada la base AX que reutiliza el endpoint bulk de vinculacion.

## Alcance (fase AX)
- Clase AX: `INDCRMExpenseSheetService`
- Metodos AX impactados:
  - `getExpenseSheetTicketsList(container _data)`
  - `getExpenseSheetTicketsLinkList(container _data)` (nuevo)
  - helpers privados nuevos para construir filas de listados
- Endpoint/API relacionados:
  - `POST /api/crm/expensesheets/tickets/list`
  - `POST /api/crm/expensesheets/tickets/link/list`
  - `POST /api/crm/expensesheets/tickets/link/bulk`

## Decision funcional de fechas
- Se mantiene como fuente de verdad `ticketHeader.createdDate`.
- `TransDate` de salida se devuelve desde `ticketHeader.createdDate`.
- Los filtros `createdDateFrom/createdDateTo` tambien se alinean a `ticketHeader.createdDate`.
- Se elimina la inconsistencia previa donde `getExpenseSheetTicketsList` filtraba por `DocuRef.INDTransDate` pero devolvia `ticketHeader.createdDate`.

## Contratos AX relevantes
### getExpenseSheetTicketsList
- Entrada:
  - `_data[1]` = `companyId`
  - `_data[2]` = `axUserId`
  - `_data[3]` = `searchKey/filterTxt` (opcional)
  - `_data[4]` = `statusFilter` (opcional, `0|1`)
  - `_data[5]` = `createdDateFrom` (opcional, `yyyymmdd`)
  - `_data[6]` = `createdDateTo` (opcional, `yyyymmdd`)
  - `_data[7]` = `currencyCode` (opcional)
  - `_data[8]` = `gastoType` (opcional)
  - `_data[9]` = `processedByAI` (opcional, `0|1`)
- Salida nueva por fila:
  - `[FileId, Description, Status, ProcessedByAI, CurrencyCode, TotalAmount, TransDate, FileName, GastoType]`

### getExpenseSheetTicketsLinkList
- Entrada:
  - `_data[1]` = `companyId`
  - `_data[2]` = `axUserId`
  - `_data[3]` = `searchKey/filterTxt` (opcional)
  - `_data[4]` = `createdDateFrom` (opcional, `yyyymmdd`)
  - `_data[5]` = `createdDateTo` (opcional, `yyyymmdd`)
  - `_data[6]` = `currencyCode` (opcional)
  - `_data[7]` = `gastoType` (opcional)
  - `_data[8]` = `processedByAI` (opcional, `0|1`)
- Prefiltros fijos:
  - `Status = Pending`
  - `TotalAmount != 0`
- Salida por fila:
  - `[FileId, Description, CurrencyCode, TotalAmount, TransDate, FileName, ProcessedByAI, GastoType]`

## Reutilizacion AX para bulk link
- El endpoint bulk no usa `updateExpenseSheetTicket`.
- Reutiliza `createExpenseSheet(mode = 2)` para anadir una linea existente a una hoja.
- Validaciones AX ya reutilizadas por ese flujo:
  - hoja existente,
  - hoja abierta (`Voucher` vacio),
  - propiedad/permisos por usuario,
  - unicidad de `FileId` en `CRMHojaGastosLine`,
  - refresco de estado del ticket tras insertar la linea.

## Riesgos y decisiones
- No se aplica el filtro extra `GastoType > 0` en AX para vinculacion.
- Motivo: la regla actual de validacion del backend/AX sigue aceptando `0` como valor valido en `isValidGastoType(...)` y en `createExpenseSheet`.
- Filtrarlo en origen seria un cambio funcional no justificado por las reglas actuales.

## Integracion API aplicada
- `Contracts/Responses/ExpenseSheetTicketDtos.cs`
  - `ExpenseSheetTicketListItemDto` reduce el contrato generico a 9 campos.
  - Se agregan `ExpenseSheetTicketLinkListItemDto`, `ExpenseSheetTicketBulkLinkResultDto` y `ExpenseSheetTicketBulkLinkIssueDto`.
- `Contracts/Requests/GetExpenseSheetTicketLinkListRequest.cs`
  - Nuevo request para el listado de vinculacion.
- `Contracts/Requests/BulkLinkExpenseSheetTicketsRequest.cs`
  - Nuevo request para vinculacion bulk.
- `Controllers/CRM/CrmExpenseSheetTicketsController.cs`
  - `POST /api/crm/expensesheets/tickets/list` alineado al contrato reducido.
  - `POST /api/crm/expensesheets/tickets/link/list` nuevo endpoint con mapeo al metodo AX nuevo.
  - `POST /api/crm/expensesheets/tickets/link/bulk` nuevo endpoint que reutiliza `createExpenseSheet(mode = 2)` por ticket y soporta resultados parciales.
- Documentacion local actualizada:
  - `.codex/ENDPOINTS.md`
  - `.codex/MCP_ENDPOINTS.md`

## Revision de routing
- `RoutePrefix("api/crm/expensesheets/tickets")` revisado.
- No hay colision entre:
  - `POST /api/crm/expensesheets/tickets/list`
  - `POST /api/crm/expensesheets/tickets/link/list`
  - `POST /api/crm/expensesheets/tickets/link/bulk`
  - `GET|PUT|DELETE /api/crm/expensesheets/tickets/{fileId}`
- El controlador de hojas mantiene la exclusion regex del literal `tickets` en rutas por `hojaGastosId`.
