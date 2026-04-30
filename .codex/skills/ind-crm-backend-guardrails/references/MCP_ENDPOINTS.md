# IND_CRM_API MCP Endpoints (actualizado 2026-03-17)

Fuentes: `.codex/ENDPOINTS.md` + Postman V28.
Objetivo: documentacion detallada para exponer la API via MCP (tools con JSON Schema).

Convenciones globales
- Base URL: `{{baseUrl}}` (`DEV` = `https://dev.insertec.biz:17776`, `PROD` = `https://crm.insertec.biz:7776`).
- Variables: `baseUrl`, `tokenId`, `companyId`, `axUserId`.
- Auth: `Authorization: Bearer {{tokenId}}`.
- Header empresa: `X-IND-Company: {{companyId}}` (obligatorio en endpoints CRM).
- Header usuario AX: `X-IND-AxUserId: {{axUserId}}` (obligatorio cuando el endpoint envia userId a AX).
- `axUserId` se obtiene de `/api/auth/entra/context` (Header.AxUserId).
- Fechas en tickets y hojas de gastos: request acepta `DDMMYYYY` o `DD.MM.YYYY`; response devuelve `DD.MM.YYYY`.

Catalogo MCP
- Archivo canonico de tools: `.codex/MCP_TOOLS.json`.
- Incluye `inputSchema` por tool y mapeo HTTP en `x-http`.

Endpoints

## Auth

### Tool: auth_login
- HTTP: POST `/api/auth/login`
- Auth: AllowAnonymous
- Headers: `Content-Type: application/json`
- Body: `Username`, `Password`
- Respuesta: `IndApiResponse<{ token, expires }>`

### Tool: auth_refresh
- HTTP: POST `/api/auth/refresh`
- Auth: Bearer token
- Headers: `Authorization`
- Respuesta: `IndApiResponse<{ token, expires }>`

### Tool: auth_entra_context
- HTTP: POST `/api/auth/entra/context`
- Auth: Bearer token
- Headers: `Authorization`, `Content-Type: application/json`
- Body: `entraOid`, `appCode`
- Respuesta: `IndApiResponse<{ header, items }>`
- Notas: de aqui se obtiene `companyId`, `axUserId`, `header.defaultCurrencyCode`, `companies[].currencyCode`, `companies[].allowSelfManagement` y `companies[].crmUserId`.

## Health

### Tool: health_ping
- HTTP: GET `/api/health/ping`
- Auth: AllowAnonymous

### Tool: health_health
- HTTP: GET `/api/health/health`
- Auth: Bearer token
- Headers: `Authorization`

## System

### Tool: system_get_environment_name
- HTTP: GET `/api/system/getEnvironmentName`
- Auth: Bearer token
- Headers: `Authorization`

### Tool: system_get_company_name
- HTTP: GET `/api/system/getCompanyName`
- Auth: Bearer token
- Headers: `Authorization`

### Tool: system_exchange_rate
- HTTP: GET `/api/system/exchange-rate`
- Auth: Bearer token
- Headers: `Authorization`
- Query: `baseCurrency`, `targetCurrency`, `date` (opcional)
- Error codes: `VALIDATION_ERROR`, `EXCHANGE_RATE_NOT_FOUND` (legacy), `RATE_UNAVAILABLE`, `INTERNAL_ERROR`
- Notas internas:
  - Proveedor primario: ECB.
  - Fallback nivel 2: Frankfurter (`https://api.frankfurter.app/latest?from={BASE}&to={TARGET}`).
  - Fallback nivel 3: OpenErApi (`https://open.er-api.com/v6/latest/{BASE}`).
  - Source de respuesta (texto UI):
    - `Banco Central Europeo (ECB)`
    - `Frankfurter API (fallback nivel 2)`
    - `Open ER API (fallback nivel 3)`
  - OpenErApi usa solo latest; si se solicita fecha distinta a hoy, se consulta latest igualmente.
  - Contrato MCP/publico sin cambios: se mantiene el mismo envelope y shape de respuesta.
  - Cache: MemoryCache 24h por `base|target|date` (solo resultados exitosos).

### Tool: system_exchange_rate_public_direct
- HTTP: GET `/api/system/exchange-rate/public-direct`
- Auth: AllowAnonymous
- Query: `baseCurrency`, `targetCurrency`, `date` (opcional)
- Endpoint de consumo recomendado: `{{baseUrl}}/api/system/exchange-rate/public-direct?baseCurrency=AED&targetCurrency=EUR`
- Error codes: `VALIDATION_ERROR`, `EXCHANGE_RATE_NOT_FOUND` (legacy), `RATE_UNAVAILABLE`, `INTERNAL_ERROR`

## MCP

### Tool: mcp_tools
- HTTP: GET `/api/mcp/tools`
- Auth: Bearer token
- Headers: `Authorization`

## IA Services

### Tool: speech_transcribe
- HTTP: POST `/api/ia/service/speech`
- Auth: Bearer token
- Headers: `Authorization`
- Body (multipart): `languageId`, `audioFile`, `temperature` (opcional), `prompt` (opcional)

### Tool: expensefromticket_draft
- HTTP: POST `/api/ia/service/expensefromticket`
- Auth: Bearer token
- Headers: `Authorization`
- Headers opcionales cuando `persistTicket=true`: `X-IND-Company`, `X-IND-AxUserId`
- Body (multipart): `ticketImage`, `persistTicket` (opcional), `ticketUrlFile` (opcional; si no viene se usa URL temporal)
- Respuesta: `Data` incluye `gastoType` (cabecera) y `lines[].typeValue` por linea.
- Respuesta: con `persistTicket=true`, `Data.TicketCreation.ProcessedByAI` debe ser `true`.

## Expense Sheets

### Tool: crm_expensesheets_currencies
- HTTP: GET `/api/crm/expensesheets/currencies`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`

### Tool: crm_expensesheets_create
- HTTP: POST `/api/crm/expensesheets`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body:
  - `mode` (0|1|2)
  - `existingHojaGastosId` (requerido cuando `mode=2`)
  - `description`, `currencyCode` (requeridos cuando `mode=0|1`)
  - `lines` (requerido cuando `mode=0|2`)
  - `lines[].transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `typeValue`, `description`, `qty`, `price`
  - Opcionales: `projId`, `exchRate`, `expenseSheetStatus`, `exchangeRateMode`, `internacional`, `fileId`

### Tool: crm_expensesheets_fuel_price_km
- HTTP: GET `/api/crm/expensesheets/fuel-price-km`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Query: `transDate` (`DDMMYYYY` o `DD.MM.YYYY`, opcional)

### Tool: crm_expensesheets_get
- HTTP: GET `/api/crm/expensesheets/{hojaGastosId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Respuesta (header) incluye: `expenseSheetStatus`, `estadoComentarios`, `exchangeRateMode`, `createdDate`
- Nota de routing: `hojaGastosId` excluye el literal `tickets` para evitar colision con `/api/crm/expensesheets/tickets`.

### Tool: crm_expensesheets_update_header
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body: `description`, `currencyCode`, `projId` (opcional), `exchRate` (opcional), `expenseSheetStatus` (opcional), `exchangeRateMode` (opcional), `estadoComentarios` (opcional)
- Regla: si se envia `estadoComentarios`, se deben enviar tambien `expenseSheetStatus` y `exchangeRateMode`.

### Tool: crm_expensesheets_update_line
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body: `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `typeValue`, `description`, `qty`, `price`, `internacional` (opcional), `fileId` (opcional), `projId` (opcional)

### Tool: crm_expensesheets_delete_line
- HTTP: DELETE `/api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Query:
  - `deleteMode` (0=LineOnly, 1=HeaderOnly alias de WholeSheet, 2=WholeSheet)
  - `deleteWholeSheet` (legacy)
- Nota: AX usa `deleteExpenseSheetLine` con flag `deleteWholeSheet`; `HeaderOnly` y `WholeSheet` se procesan igual.

### Tool: crm_expensesheets_list
- HTTP: POST `/api/crm/expensesheets/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body: `page`, `pageSize`, `filter` (opcional), `billedMode` (opcional), `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`, opcional), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`, opcional), `projId` (opcional), `currencyCode` (opcional), `expenseSheetStatus` (opcional), `includeSubordinates` (bool opcional; `true` = subordinados directos del usuario de header)
- Respuesta por item incluye: `expenseSheetStatus`, `estadoComentarios`, `exchangeRateMode`, `userId`, `userName`, `exchRate`, `createdDate`.

## Expense Sheet Tickets

### Tool: crm_expensesheets_tickets_create
- HTTP: POST `/api/crm/expensesheets/tickets`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body:
  - `mode` (0|1|2)
  - `existingFileId` (requerido cuando `mode=2`)
  - `description`, `currencyCode`, `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `urlFile` (requeridos cuando `mode=0|1`)
  - `totalAmount`, `comentario`, `fileExtension`, `gastoType`, `ocrJson`, `normalizedJson` (opcionales; `gastoType` permitido: 0,1,2,3,4,5,6,7,8,14)
  - `lines[]` con `description`, `qty`, `price`, `totalAmount` (lineas requeridas cuando `mode=0|2`)

### Tool: crm_expensesheets_tickets_quick_create
- HTTP: POST `/api/crm/expensesheets/tickets/quick-create`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: multipart/form-data`
- Body multipart:
  - Requerido: `ticketImage` (jpg/jpeg/png/webp, max 50 MB)
  - Opcional: `currencyCode`, `description`, `comentario`, `existingHojaGastosId`, `projectId`
- Flujo: crea ticket provisional, sube archivo, ejecuta OCR + normalizacion IA, finaliza ticket y opcionalmente lo vincula a una hoja existente.
- Respuesta data: `FileId`, `UrlFile`, `FileName`, `ProcessedByAI`, `LinkedToSheet`, `HojaGastosId`, `CompletedStage`, `StepTraceIds`
- Errores relevantes: 422 validacion, 429 limite IA, 500 error interno

### Tool: crm_expensesheets_tickets_get
- HTTP: GET `/api/crm/expensesheets/tickets/{fileId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Respuesta: cabecera incluye `processedByAI`, `gastoType`, `hojaGastosIdDisplay`, `ocrJson` y `normalizedJson`.

### Tool: crm_expensesheets_tickets_list
- HTTP: POST `/api/crm/expensesheets/tickets/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body:
  - Requerido: `page`, `pageSize`
  - Opcional: `searchKey` (compatibilidad: `filter`), `status` (0|1), `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`), `currencyCode`, `gastoType` (0,1,2,3,4,5,6,7,8,14), `processedByAI` (bool)
  - Regla: para ejecutar consulta siempre deben viajar `X-IND-Company` y `X-IND-AxUserId`; el rango de fechas es opcional y usa `ticketHeader.createdDate`.
- Respuesta: cada item incluye `FileId`, `Description`, `Status`, `ProcessedByAI`, `CurrencyCode`, `TotalAmount`, `TransDate`, `FileName`, `GastoType`.

### Tool: crm_expensesheets_tickets_link_list
- HTTP: POST `/api/crm/expensesheets/tickets/link/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body:
  - Requerido: `page`, `pageSize`
  - Opcional: `searchKey` (compatibilidad: `filter`), `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`), `currencyCode`, `gastoType` (0,1,2,3,4,5,6,7,8,14), `processedByAI` (bool)
  - Regla: el origen sale prefiltrado con `status = Pending` y `totalAmount != 0`; la fecha de referencia es `ticketHeader.createdDate`.
- Respuesta: cada item incluye `FileId`, `Description`, `CurrencyCode`, `TotalAmount`, `TransDate`, `FileName`, `ProcessedByAI`, `GastoType`.

### Tool: crm_expensesheets_tickets_link_bulk
- HTTP: POST `/api/crm/expensesheets/tickets/link/bulk`
  - Auth: Bearer token
  - Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
  - Body:
    - Legacy soportado: `expenseSheetId`, `ticketIds[]` (`selectionMode = selected`)
    - Requerido: `expenseSheetId`
    - Opcional: `selectionMode` (`selected` por defecto, `filtered`)
    - En `selected`: `ticketIds[]` obligatorio
    - En `filtered`: `filters` obligatorio (`searchKey`, `filter`, `createdDateFrom`, `createdDateTo`, `currencyCode`, `gastoType`, `processedByAI`) y `excludedIds[]` opcional
    - Regla: en `filtered` reutiliza la misma resolucion server-side que `tickets/link/list`; la vinculacion final reutiliza `createExpenseSheet` en modo `2` y soporta resultado parcial.
  - Respuesta data: `expenseSheetId`, `requestedCount`, `linkedCount`, `skippedCount`, `failedCount`, `linkedTicketIds`, `skipped[]`, `failed[]`.

### Tool: crm_expensesheets_tickets_update
- HTTP: PUT `/api/crm/expensesheets/tickets/{fileId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body opcional: `description`, `currencyCode`, `gastoType`, `totalAmount`, `status` (0|1), `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `comentario`, `urlFile`, `fileName`, `fileExtension`, `processedByAI`, `ocrJson`, `normalizedJson`

### Tool: crm_expensesheets_tickets_apply_ia
- HTTP: POST `/api/crm/expensesheets/tickets/{fileId}/ia`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body:
  - `description`, `currencyCode`, `gastoType`, `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `urlFile` (se completan desde ticket actual si no se envian)
  - `totalAmount` (opcional)
  - `comentario` (opcional)
  - `fileName` (opcional), `ocrJson` (opcional), `normalizedJson` (opcional), `fileExtension` (opcional si no hay `fileName`)
  - `lines[]` obligatorio con `description`, `qty`, `price`, `totalAmount` (opcional)
- Regla: reemplazo total del detalle de lineas (delete + insert) y `processedByAI=true`.
- Compatibilidad: acepta body directo del contrato IA o envelope de `expensefromticket` (`Success/Message/Data/TraceId`) y mapea `Data` de forma interna.

### Tool: crm_expensesheets_tickets_upload_file
- HTTP: POST `/api/crm/expensesheets/tickets/{fileId}/file`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: multipart/form-data`
- Query opcional: `extension` (si no viene, usa extension del archivo y fallback `jpg`)
- Body multipart: un archivo (primer archivo del payload)
- Regla: nombre final `yyyyMMddHHmmss_{axUserId}_{fileId}.{ext}`

### Tool: crm_expensesheets_tickets_delete_file
- HTTP: DELETE `/api/crm/expensesheets/tickets/{fileId}/file`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`

### Tool: crm_expensesheets_tickets_delete
- HTTP: DELETE `/api/crm/expensesheets/tickets/{fileId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Query opcional: `lineRecId` (si se envia, elimina solo esa linea mediante metodo unificado)

### Tool: crm_expensesheets_tickets_create_line
- HTTP: POST `/api/crm/expensesheets/tickets/{fileId}/lines`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body: `description`, `qty`, `price`, `totalAmount` (opcional)

### Tool: crm_expensesheets_tickets_update_line
- HTTP: PUT `/api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body: `description`, `qty`, `price`, `totalAmount` (opcional)

### Tool: crm_expensesheets_tickets_delete_line
- HTTP: DELETE `/api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`

## Projects

### Tool: crm_projects_list
- HTTP: GET `/api/crm/projects/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`
- Query: `filter` (opcional), `page`, `pageSize`
