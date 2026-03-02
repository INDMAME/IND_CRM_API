# IND_CRM_API Postman

Colecciones
- Principal: `.codex/Postman/IND_CRM_API V25.postman_collection.json`
- Soporte: `Notes/IND_CRM_API V25.postman_collection.json`

Ambiente (variables sugeridas)
- `baseUrl` = `https://crm.insertec.biz:7776`
- `tokenId` = token JWT vigente
- `companyId` = compania obtenida desde Entra Context
- `axUserId` = usuario AX obtenido desde Entra Context
- `fileId` = se autocompleta desde respuestas de tickets
- `lineRecId` = se autocompleta desde respuestas de lineas de tickets
- `ticketImagePath` = ruta local para pruebas de upload en multipart (opcional)
- `ticketUrlFile` = URL del blob cargado (se autocompleta desde upload)
- `ticketFileName` = nombre final del archivo del ticket (autocompletado)
- `persistedTicketFileId` = fileId de ticket creado por `expensefromticket` cuando `persistTicket=true`
- `lastTraceId` = ultimo traceId retornado por API (autocompletado)

Notas
- Todos los endpoints protegidos usan `Authorization: Bearer {{tokenId}}`.
- Endpoints CRM usan `X-IND-Company: {{companyId}}`.
- Endpoints CRM que envian userId a AX usan `X-IND-AxUserId: {{axUserId}}`.
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
  - `GET /api/crm/expensesheets/tickets/{fileId}` y `POST /api/crm/expensesheets/tickets/list` retornan `hojaGastosIdDisplay`.
  - `POST /api/crm/expensesheets/tickets`, `PUT /api/crm/expensesheets/tickets/{fileId}` y `POST /api/crm/expensesheets/tickets/{fileId}/ia` admiten `gastoType`.
  - `POST /api/crm/expensesheets/tickets/list` admite `createdDateFrom` y `createdDateTo` como filtros opcionales; `searchKey` es filtro preferido (se mantiene compatibilidad con `filter`).
  - `POST /api/crm/expensesheets/tickets/list` admite filtro opcional `processedByAI` (`true|false`).
  - `POST /api/crm/expensesheets/tickets/{fileId}/ia` aplica reemplazo total de lineas desde IA y marca `processedByAI`.
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
