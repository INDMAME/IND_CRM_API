# AX Context: Expense Sheet + Tickets Refactor (2026-02-24)

## Scope
- Updated AX: `.codex/Axapta/INDCRMExpenseSheetService.xpo`
- Updated API (.NET):
  - `Controllers/CRM/CrmExpenseSheetsController.cs`
  - `Controllers/System/INDSpeechController.cs`
  - Request/response contracts de ExpenseSheet/Tickets
  - OpenAI draft mapper (`Services/IND_OpenAiExpenseTicketDraftService.cs`)
- This is a breaking change by design (requested): `ticket` removed and replaced by `FileId`.

## Breaking Changes Applied
- Expense-sheet line contract now uses `FileId` (EDT `INDFileId`) in the same index previously used by `ticket`.
- AX class no longer uses `CRMHojaGastosLine.Ticket` in the service methods.
- AX class now maps and returns `CRMHojaGastosLine.FileId`.

## Existing Expense-Sheet Methods Updated

### `createExpenseSheet(container _data)`
- Line input shape remains 9 fields.
- Index replacement:
  - Previous: line[5] = `ticket` (int/bool style)
  - Current: line[5] = `fileId` (`INDFileId`)
- New validations when `fileId` is provided:
  - Ticket exists in `INDTicketInfoTable`.
  - Ticket ownership matches `axUserId` (`CreatedByUserId`).
  - `FileId` is unique across `CRMHojaGastosLine` (cannot be assigned to another line).
- Side effect:
  - After line insert, ticket status is synced to `Assigned` via helper.

### `updateExpenseSheetLine(container _data)`
- `_data[9]` now receives `fileId` (`INDFileId`) instead of `ticket`.
- New validations:
  - Provided `fileId` must exist and belong to `axUserId`.
  - Provided `fileId` must not be assigned to another line (`RecId != current`).
- Status sync behavior:
  - If `FileId` changed, previous `FileId` is recalculated to `Pending/Assigned` depending on remaining usage.
  - New `FileId` is recalculated to `Assigned` if linked.

### `getExpenseSheet(container _data)`
- Line output shape keeps same position count used by API mapping.
- Index replacement in output line row:
  - Previous position 6: `line.Ticket`
  - Current position 6: `line.FileId`

### `deleteExpenseSheetLine(container _data)`
- On line delete (single or whole sheet), captured `FileId` is status-synced.
- Ticket status recalculation is centralized through helper based on real line usage.

## New Ticket CRUD Methods Added in AX (same class)

### 1) `createExpenseSheetTicket(container _data)`
Mode-based create:
- Mode `0`: create header + lines + `DocuRef` metadata.
- Mode `1`: create header + `DocuRef` metadata only.
- Mode `2`: add lines to existing ticket by `existingFileId`.

Input contract:
- `_data[1]` companyId
- `_data[2]` headerIn
  - Mode 0/1: `[axUserId, description, currencyCode, totalAmount, transDate(yyyymmdd), comentario, urlFile, fileName]`
  - Mode 2: `[axUserId]`
- `_data[3]` linesIn
  - Each line: `[description, qty, price, lineTotal(optional)]`
- `_data[4]` optionsIn (optional)
  - `[mode, existingFileId]`

DocuRef fixed rules applied:
- `TypeId = "Imagen"`
- `INDFileAllocationType = ExpenseSheet`
- `INDFileLocationType = Cloud`
- Also set: `INDDescription`, `INDComentario`, `INDTransDate`, `INDURLFile`, `INDFilename`, `INDCreatedByUserId`, `RefTableId`, `RefRecId`, `INDFileId`.

### 2) `getExpenseSheetTicket(container _data)`
- Gets one ticket header + lines by `FileId` with ownership validation.

Input:
- `_data[1]` companyId
- `_data[2]` axUserId
- `_data[3]` fileId

Output:
- `[headerOut, linesOut]`
- Header includes ticket + docuref core metadata, including `ProcessedByAI`.

### 3) `getExpenseSheetTicketsList(container _data)`
- Returns ticket list for a user.

Input:
- `_data[1]` companyId
- `_data[2]` axUserId
- `_data[3]` filter (optional)
- `_data[4]` statusFilter (optional 0|1)

### 4) `updateExpenseSheetTicket(container _data)`
- Updates ticket header and related docuref metadata.

Input:
- `_data[1]` companyId
- `_data[2]` axUserId
- `_data[3]` fileId
- `_data[4]` description (optional)
- `_data[5]` currencyCode (optional)
- `_data[6]` totalAmount (optional)
- `_data[7]` status (optional: 0 Pending, 1 Assigned)
- `_data[8]` transDate yyyymmdd (optional)
- `_data[9]` comentario (optional)
- `_data[10]` urlFile (optional)
- `_data[11]` fileName (optional)
- `_data[12]` processedByAI (optional: 0|1)

### 5) `deleteExpenseSheetTicket(container _data)`
- Supports two modes with same method:
  - Granular line delete (when `_data[4] = lineRecId` is informed):
    - Deletes only one row from `INDTicketInfoLine`.
    - Keeps `INDTicketInfoTable` and `DocuRef`.
    - Recalculates `INDTicketInfoTable.TotalAmount`.
  - Full ticket delete (when `_data[4]` is empty/not informed):
    - Physical delete across `INDTicketInfoTable` + `INDTicketInfoLine` + `DocuRef`.
    - If `FileId` is currently assigned to any `CRMHojaGastosLine`, delete is blocked.

Input:
- `_data[1]` companyId
- `_data[2]` axUserId
- `_data[3]` fileId
- `_data[4]` lineRecId (`RecId`, optional)

### 6) `createExpenseSheetTicketLine(container _data)`
- New dedicated granular create for one detail line in `INDTicketInfoLine`.
- Validates company + user ownership of `FileId` ticket.
- Recalculates `INDTicketInfoTable.TotalAmount` after insert.

Input:
- `_data[1]` companyId
- `_data[2]` axUserId
- `_data[3]` fileId
- `_data[4]` description
- `_data[5]` qty
- `_data[6]` price
- `_data[7]` lineTotal (optional; if empty/<=0, AX uses `qty * price`)

### 7) `updateExpenseSheetTicketLine(container _data)`
- New dedicated granular update for one detail line in `INDTicketInfoLine`.
- Validates company + user ownership of ticket and line (`FileId` + `LineRecId`).
- Recalculates `INDTicketInfoTable.TotalAmount` after update.

Input:
- `_data[1]` companyId
- `_data[2]` axUserId
- `_data[3]` fileId
- `_data[4]` lineRecId (`RecId`)
- `_data[5]` description
- `_data[6]` qty
- `_data[7]` price
- `_data[8]` lineTotal (optional; if empty/<=0, AX uses `qty * price`)

### 8) `deleteExpenseSheetTicketLine(container _data)`
- New dedicated granular delete for one detail line in `INDTicketInfoLine`.
- Deletes only one line by `FileId` + `LineRecId`.
- Recalculates `INDTicketInfoTable.TotalAmount` after delete.

Input:
- `_data[1]` companyId
- `_data[2]` axUserId
- `_data[3]` fileId
- `_data[4]` lineRecId (`RecId`)

### 9) `updateExpenseSheetTicketFromIA(container _data)`
- New atomic method for IA processing over an existing ticket.
- Updates header + DocuRef, deletes all existing detail lines, inserts IA lines, and marks `ProcessedByAI = 1`.
- Recalculates header total amount from input/lines and preserves transaction integrity in one `ttsbegin/ttscommit`.

Input:
- `_data[1]` companyId
- `_data[2]` headerIn = `[axUserId, fileId, description, currencyCode, totalAmount, transDate, comentario, urlFile, fileName]`
- `_data[3]` linesIn = `[[description, qty, price, lineTotal(optional)], ...]`

## New Helper Methods Added
- `calculateTicketTotalAmount(INDFileId _fileId, RecId _ticketRecId)`
- `refreshTicketStatusByFileId(UserId _axUserId, INDFileId _fileId)`

## Ownership and Transaction Rules Implemented
- Ownership validation by `axUserId` (`CreatedByUserId`) on ticket operations.
- `ttsbegin/ttscommit` enforced in ticket create/update/delete critical flows.

## API Work Completed
1. Se eliminaron referencias de `ticket` y se migraron contratos de lineas de hoja de gastos a `fileId`.
2. Se agregaron endpoints de tickets bajo `/api/crm/expensesheets/tickets`:
   - Crear / obtener / listar / actualizar / eliminar ticket.
   - Crear / actualizar / eliminar linea granular de ticket.
3. Se implemento generador de nombre de archivo en API con formato:
   - `yyyymmddhhmmss_{axUserId}_{fileId}.{ext}`
   - Flujo aplicado: create ticket (nombre provisional) + update ticket (nombre final).
4. Se ajusto `POST /api/ia/service/expensefromticket`:
   - Mantiene extraccion de draft.
   - Soporta persistencia opcional de ticket en AX (`persistTicket=true`), incluyendo cabecera, lineas y DocuRef.
   - Si falta `ticketUrlFile`, usa URL temporal y agrega warning en el draft.
5. Se anadieron codigos de error de negocio para tickets en `IndErrorCodes`.
6. Los endpoints de tickets se separaron en un controlador dedicado:
   - `Controllers/CRM/CrmExpenseSheetTicketsController.cs`
   - `CrmExpenseSheetsController` mantiene unicamente endpoints de hoja de gastos existentes.
7. Se limpio `CrmExpenseSheetsController` removiendo metodos privados de tickets que quedaron sin uso tras la separacion.
8. Se agrego integracion Azure Blob para archivo de ticket con endpoints dedicados:
   - `POST /api/crm/expensesheets/tickets/{fileId}/file` (multipart upload)
   - `DELETE /api/crm/expensesheets/tickets/{fileId}/file`
9. El nombre de archivo se mantiene con formato requerido:
   - `yyyyMMddHHmmss_{axUserId}_{fileId}.{ext}`
   - Se construye en API usando `X-IND-AxUserId` + `fileId`.
10. Configuracion de Blob en API:
   - `AZURE_BLOB_CONNECTION_STRING` (obligatoria)
   - `AzureBlob:Container` (default `tickets`)
   - `AzureBlob:BasePrefix` (default `crmtickets`)
11. Ruta de blob simplificada para tickets:
   - Formato final: `crmtickets/{companyId}/{yyyyMMddHHmmss_axUserId_fileId.ext}`
   - Se elimina segmentacion adicional por usuario y fecha en carpetas.
12. Hardening de `POST /api/crm/expensesheets/tickets`:
   - Se cambio parseo del body a deserializacion manual dentro del action para evitar fallos previos al action por binder.
   - Cuando el JSON es invalido se devuelve 422 con error de validacion (`body`), en lugar de 500 generico.
13. Diagnostico de pipeline reforzado:
   - Nuevo log `[API-PIPE-MATCH]` con pre-resolucion de ruta.
   - `[API-PIPE-OUT]` ahora registra preRoute y postRoute.
   - Nuevo log `[API-PIPE-500]` con reason/contentType/contentLength para aislar errores previos al action.
14. Fix de enrutamiento entre hojas de gasto y tickets:
   - Se detecto colision de rutas para `POST /api/crm/expensesheets/tickets` por coincidencia con `api/crm/expensesheets/{hojaGastosId}`.
   - Se agrego constraint regex en `CrmExpenseSheetsController` para excluir el literal `tickets` en las rutas de detalle/actualizacion por `hojaGastosId`.
15. Nuevo endpoint para aplicar datos IA sobre ticket existente:
   - `POST /api/crm/expensesheets/tickets/{fileId}/ia`
   - Invoca metodo AX atomico `updateExpenseSheetTicketFromIA` para reemplazo total de lineas y actualizacion de cabecera.
16. Persistencia IA con bandera de procesamiento:
   - `POST /api/ia/service/expensefromticket` con `persistTicket=true` ahora fuerza `ProcessedByAI=Yes` al crear ticket en AX.
   - La respuesta expone `Data.TicketCreation.ProcessedByAI=true` para validacion en frontend/Postman.

## Notes for Next Iteration
- Evolucionar upload directo a SAS (frontend->blob) para evitar paso binario por API.
- Validar en QA la coleccion Postman V21 para tickets, archivo blob y flujo IA.
