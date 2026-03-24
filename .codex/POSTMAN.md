# IND_CRM_API Postman

Colecciones
- DEV activa: `.codex/Postman/DEV/IND_CRM_API V01.postman_collection.json`
- DEV soporte: `Notes/DEV/IND_CRM_API V01.postman_collection.json`
- Historial PROD: `.codex/Postman/PROD/`
- Historial soporte PROD: `Notes/PROD/`

Ambiente (variables sugeridas)
- `baseUrl` = `https://dev.insertec.biz:7776`
- `tokenId` = token JWT vigente
- `companyId` = compania obtenida desde Entra Context
- `axUserId` = usuario AX obtenido desde Entra Context
- `fileId` = se autocompleta desde respuestas de tickets
- `expenseSheetId` = hoja de gastos destino para pruebas de vinculacion bulk
- `lineRecId` = se autocompleta desde respuestas de lineas de tickets
- `ticketImagePath` = ruta local para pruebas de upload en multipart (opcional)
- `ticketUrlFile` = URL del blob cargado (se autocompleta desde upload)
- `ticketFileName` = nombre final del archivo del ticket (autocompletado)
- `persistedTicketFileId` = fileId de ticket creado por `expensefromticket` cuando `persistTicket=true`
- `lastTraceId` = ultimo traceId retornado por API (autocompletado)
- `quickCreateCompletedStage` = ultima etapa completada de `quick-create` (autocompletado)
- `quickCreateHojaGastosId` = hoja vinculada por `quick-create` cuando aplica (autocompletado)
- `quickCreateLinkedToSheet` = indica si `quick-create` vinculo el ticket a una hoja (autocompletado)
- `quickCreateProcessedByAI` = indica si `quick-create` finalizo con datos IA aplicados (autocompletado)

Notas
- La linea `PROD` conserva intacto el historico existente; la linea `DEV` arranca en `V01` basada en la `V30` mas reciente.
- Todos los endpoints protegidos usan `Authorization: Bearer {{tokenId}}`.
- Endpoints CRM usan `X-IND-Company: {{companyId}}`.
- Endpoints CRM que envian userId a AX usan `X-IND-AxUserId: {{axUserId}}`.
- Regla obligatoria de fechas en tickets y hojas de gastos: request acepta `DDMMYYYY` o `DD.MM.YYYY`; response devuelve siempre `DD.MM.YYYY` (`transDate`, `createdDateFrom`, `createdDateTo`, `createdDate`).
- `POST /api/auth/entra/context` retorna `defaultCurrencyCode`, companias y `allowSelfManagement`.
- Expense Sheets usa `lines[].fileId` (INDFileId) en lugar de `lines[].ticket`.
- `PUT /api/crm/expensesheets/{hojaGastosId}` admite `estadoComentarios` en body (posicion AX `_data[10]`), y cuando se envia requiere `expenseSheetStatus` + `exchangeRateMode`.
- Delete de linea soporta `deleteMode` (0 LineOnly, 1 HeaderOnly alias de WholeSheet, 2 WholeSheet) y conserva `deleteWholeSheet` como legado.
- La coleccion V21 incluye CRUD completo de tickets + endpoints de archivo:
  - `POST /api/crm/expensesheets/tickets/{fileId}/file`
  - `DELETE /api/crm/expensesheets/tickets/{fileId}/file`
- Tickets:
  - `GET /api/crm/expensesheets/tickets/{fileId}` y `POST /api/crm/expensesheets/tickets/list` retornan `processedByAI`.
  - `PUT /api/crm/expensesheets/tickets/{fileId}` admite `processedByAI` en body.
  - `GET /api/crm/expensesheets/tickets/{fileId}` y `POST /api/crm/expensesheets/tickets/list` retornan `gastoType`.
  - `GET /api/crm/expensesheets/tickets/{fileId}` retorna tambien `ocrJson` y `normalizedJson`.
  - `POST /api/crm/expensesheets/tickets/list` devuelve solo `FileId`, `Description`, `Status`, `ProcessedByAI`, `CurrencyCode`, `TotalAmount`, `TransDate`, `FileName` y `GastoType`.
  - `POST /api/crm/expensesheets/tickets`, `PUT /api/crm/expensesheets/tickets/{fileId}` y `POST /api/crm/expensesheets/tickets/{fileId}/ia` admiten `gastoType`.
  - `POST /api/crm/expensesheets/tickets`, `PUT /api/crm/expensesheets/tickets/{fileId}` y `POST /api/crm/expensesheets/tickets/{fileId}/ia` admiten `ocrJson` y `normalizedJson` como payload opcional de OCR/normalizacion.
  - `POST /api/crm/expensesheets/tickets/list` admite `createdDateFrom` y `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`) como filtros opcionales; `searchKey` es filtro preferido (se mantiene compatibilidad con `filter`) y la fecha de referencia es `ticketHeader.createdDate`.
  - `POST /api/crm/expensesheets/tickets/list` admite filtro opcional `processedByAI` (`true|false`).
  - `POST /api/crm/expensesheets/tickets/link/list` devuelve `FileId`, `Description`, `CurrencyCode`, `TotalAmount`, `TransDate`, `FileName`, `ProcessedByAI` y `GastoType`, con prefiltros fijos `Pending` y `totalAmount != 0`.
  - `POST /api/crm/expensesheets/tickets/link/bulk` mantiene compatibilidad con `expenseSheetId` + `ticketIds[]` y ahora soporta `selectionMode=selected|filtered`, `filters` y `excludedIds`, reutilizando `createExpenseSheet` en modo `2` y devolviendo resultado parcial con `linked`, `skipped` y `failed`.
  - `POST /api/crm/expensesheets/tickets/{fileId}/ia` aplica reemplazo total de lineas desde IA y marca `processedByAI`.
  - `POST /api/crm/expensesheets/tickets/quick-create` acepta `multipart/form-data` con `ticketImage` y campos opcionales `currencyCode`, `description`, `comentario`, `existingHojaGastosId` y `projectId`; devuelve `completedStage` y `stepTraceIds` para trazar cada etapa.
- `POST /api/ia/service/expensefromticket` soporta `persistTicket` y `ticketUrlFile` para persistir ticket en AX desde IA.
- V23 agrega tests automatizados para:
- V24 agrega tests automatizados para:
  - `IA Services / ExpenseFromTicket` (persistencia desactivada).
  - `IA Services / ExpenseFromTicket (persist=true)` (requiere `X-IND-Company` + `X-IND-AxUserId`) y valida `ticketCreation.processedByAI=true`.
  - `Create Ticket - Header Only (mode 1)`.
  - `Upload Ticket File (multipart)`.
  - `Apply IA to Ticket (replace lines)`.
  - `Get Ticket by FileId`.
- V25 actualiza contratos de tickets con `gastoType` y filtros de fecha en `tickets/list` (ahora opcionales).
- V26 normaliza contratos completos en request body para endpoints con payload JSON, alinea `tickets/list` al contrato reducido, agrega `tickets/link/list`, agrega `tickets/link/bulk` y mantiene `TransDate` basado en `ticketHeader.createdDate`.
- V28 se basa en la V27 mas reciente, incorpora `tickets/quick-create` y documenta `ocrJson`/`normalizedJson` en contratos de tickets sin romper compatibilidad.
