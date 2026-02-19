# IND_CRM_API MCP Endpoints (actualizado 2026-02-19)

Fuentes: `.codex/ENDPOINTS.md` + Postman V18.
Objetivo: documentacion detallada para exponer la API via MCP (tools con JSON Schema).

Convenciones globales
- Base URL: `https://crm.insertec.biz:7776` (en Postman se usa `{{baseUrl}}`).
- Variables: `baseUrl`, `tokenId`, `companyId`, `axUserId`.
- Auth: `Authorization: Bearer {{tokenId}}`.
- Header empresa: `X-IND-Company: {{companyId}}` (obligatorio en endpoints CRM).
- Header usuario AX: `X-IND-AxUserId: {{axUserId}}` (obligatorio cuando el endpoint envia userId a AX).
- `axUserId` se obtiene de `/api/auth/entra/context` (Header.AxUserId).
- Fechas: `yyyyMMdd` o `yyyy-MM-dd` segun endpoint.

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
- Notas: de aqui se obtiene `companyId`, `axUserId`, `header.defaultCurrencyCode`, `companies[].currencyCode` y `companies[].allowSelfManagement`.

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
- Error codes: `VALIDATION_ERROR`, `EXCHANGE_RATE_NOT_FOUND`, `INTERNAL_ERROR`

### Tool: system_exchange_rate_public_direct
- HTTP: GET `/api/system/exchange-rate/public-direct`
- Auth: AllowAnonymous
- Query: `baseCurrency`, `targetCurrency`, `date` (opcional)
- Error codes: `VALIDATION_ERROR`, `EXCHANGE_RATE_NOT_FOUND`, `INTERNAL_ERROR`

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
- Body (multipart): `ticketImage`

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
  - `lines[].transDate`, `typeValue`, `description`, `qty`, `price`
  - Opcionales: `projId`, `exchRate`, `expenseSheetStatus`, `exchangeRateMode`, `internacional`, `ticket`, `indAttachFiles`

### Tool: crm_expensesheets_fuel_price_km
- HTTP: GET `/api/crm/expensesheets/fuel-price-km`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Query: `transDate` (opcional)

### Tool: crm_expensesheets_get
- HTTP: GET `/api/crm/expensesheets/{hojaGastosId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`

### Tool: crm_expensesheets_update_header
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body: `description`, `currencyCode`, `projId` (opcional), `exchRate` (opcional), `expenseSheetStatus` (opcional), `exchangeRateMode` (opcional)

### Tool: crm_expensesheets_update_line
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body: `transDate`, `typeValue`, `description`, `qty`, `price`, `internacional` (opcional), `ticket` (opcional), `projId` (opcional), `indAttachFiles` (opcional)

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
- Body: `page`, `pageSize`, `filter` (opcional), `billedMode` (opcional), `createdDateFrom` (opcional), `createdDateTo` (opcional), `projId` (opcional), `currencyCode` (opcional), `expenseSheetStatus` (opcional)

## Projects

### Tool: crm_projects_list
- HTTP: GET `/api/crm/projects/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`
- Query: `filter` (opcional), `page`, `pageSize`
