# IND_CRM_API MCP Endpoints (actualizado 2026-02-16)

Fuentes: `.codex/ENDPOINTS.md` + Postman V14 (`.codex/Postman/IND_CRM_API V14.postman_collection.json`).
Objetivo: documentacion detallada para exponer la API via MCP (tools con JSON Schema).

Notas MCP (Context7 MCP)
- Context7 MCP expone herramientas (`tools`) con un `input schema` en JSON Schema (type/properties/required).
- La conexion MCP puede ser remota (HTTP) o local (stdio) segun la configuracion del cliente.
- Esta guia describe cada endpoint como un tool: nombre, metodo, ruta, seguridad y esquema de entrada.

Convenciones globales
- Base URL: `https://crm.insertec.biz:7776` (en Postman se usa `{{baseUrl}}`).
- Variables Postman: `baseUrl`, `tokenId`, `companyId`, `axUserId`.
- Auth: `Authorization: Bearer {{tokenId}}`.
- Header empresa: `X-IND-Company: {{companyId}}` (obligatorio en endpoints CRM).
- Header usuario AX: `X-IND-AxUserId: {{axUserId}}` (obligatorio solo cuando el endpoint envia userId a AX).
- `axUserId` se obtiene de `/api/auth/entra/context` (Header.AxUserId).
- Regla: cualquier `userId` enviado en query o body se ignora; se usa `X-IND-AxUserId`.
- Fechas: `yyyyMMdd` o `yyyy-MM-dd` segun endpoint.
- Respuestas estandar:
  - Lecturas: `IndPagedResponse<T>` (items, total/page/pageSize si aplica).
  - Comandos: `IndApiResponse<T>`.
  - Errores: `IndApiResponse<object>` con `success=false`, `errorCode`, `errors`, `traceId`.
- Errores comunes: 400, 401, 404, 422, 500 (todas con envelope estandar).

Catalogo MCP
- Archivo canonico de tools: `.codex/MCP_TOOLS.json`.
- Incluye `inputSchema` por tool y el mapeo HTTP en `x-http` (metodo, ruta, headers, contentType).
- Los ejemplos de llamada estan embebidos en cada tool (campo `examples`).

Endpoints

## Auth

### Tool: auth_login
- HTTP: POST `/api/auth/login`
- Auth: AllowAnonymous
- Headers: `Content-Type: application/json`
- Body (requerido):
  - `Username` (string)
  - `Password` (string)
- Respuesta: `IndApiResponse<{ token, expires }>`
- Notas: login de usuario de servicio.

### Tool: auth_refresh
- HTTP: POST `/api/auth/refresh`
- Auth: Bearer token
- Headers: `Authorization`
- Respuesta: `IndApiResponse<{ token, expires }>`

### Tool: auth_entra_context
- HTTP: POST `/api/auth/entra/context`
- Auth: Bearer token
- Headers: `Authorization`, `Content-Type: application/json`
- Body (requerido):
  - `entraOid` (string GUID)
  - `appCode` (string)
- Respuesta: `IndApiResponse<{ header, items }>`
- Notas: de aqui se obtiene `companyId`, `axUserId`, `header.defaultCurrencyCode` y `companies[].currencyCode`.

## Health

### Tool: health_ping
- HTTP: GET `/api/health/ping`
- Auth: AllowAnonymous
- Respuesta: `IndApiResponse<string>`

### Tool: health_health
- HTTP: GET `/api/health/health`
- Auth: Bearer token
- Headers: `Authorization`
- Respuesta: `IndApiResponse<object>`

## System

### Tool: system_get_environment_name
- HTTP: GET `/api/system/getEnvironmentName`
- Auth: Bearer token
- Headers: `Authorization`
- Respuesta: `IndApiResponse<string>`

### Tool: system_get_company_name
- HTTP: GET `/api/system/getCompanyName`
- Auth: Bearer token
- Headers: `Authorization`
- Respuesta: `IndApiResponse<string>`

### Tool: system_exchange_rate
- HTTP: GET `/api/system/exchange-rate`
- Auth: Bearer token
- Headers: `Authorization`
- Query (requerido):
  - `baseCurrency` (string, ISO 4217, 3 letras)
  - `targetCurrency` (string, ISO 4217, 3 letras)
- Query (opcional):
  - `date` (string, `yyyy-MM-dd`; si no se envia usa latest)
- Respuesta: `IndApiResponse<ExchangeRateDto>`
- Error codes:
  - `VALIDATION_ERROR` (422)
  - `EXCHANGE_RATE_NOT_FOUND` (404)
  - `INTERNAL_ERROR` (500)
- Notas:
  - Idempotencia: si.
  - Side effects: ninguno.
  - Conversion: si base/target no es EUR, se resuelve via EUR con series ECB.
- Input schema JSON:
```json
{
  "type": "object",
  "properties": {
    "headers": {
      "type": "object",
      "properties": {
        "Authorization": {
          "type": "string",
          "description": "Bearer token. Format: Bearer <token>"
        }
      },
      "required": ["Authorization"],
      "additionalProperties": false
    },
    "query": {
      "type": "object",
      "properties": {
        "baseCurrency": { "type": "string", "pattern": "^[A-Za-z]{3}$" },
        "targetCurrency": { "type": "string", "pattern": "^[A-Za-z]{3}$" },
        "date": { "type": "string", "pattern": "^\\d{4}-\\d{2}-\\d{2}$" }
      },
      "required": ["baseCurrency", "targetCurrency"],
      "additionalProperties": false
    }
  },
  "required": ["headers", "query"],
  "additionalProperties": false
}
```

## MCP

### Tool: mcp_tools
- HTTP: GET `/api/mcp/tools`
- Auth: Bearer token
- Headers: `Authorization`
- Respuesta: Catalogo MCP (MCP_TOOLS.json)

## IA Services

### Tool: speech_transcribe
- HTTP: POST `/api/ia/service/speech`
- Auth: Bearer token
- Headers: `Authorization`
- Body (multipart/form-data):
  - `languageId` (string, requerido)
  - `audioFile` (file, requerido)
  - `temperature` (number 0-1, opcional)
  - `prompt` (string, opcional)
- Respuesta: `IndApiResponse<string>` (transcripcion)
- Notas: no incluir contenido sensible en el prompt.

### Tool: expensefromticket_draft
- HTTP: POST `/api/ia/service/expensefromticket`
- Auth: Bearer token
- Headers: `Authorization`
- Body (multipart/form-data):
  - `ticketImage` (file, requerido)
- Respuesta: `IndApiResponse<ExpenseSheetDraftResponse>`
- Notas: no crea el gasto, devuelve sugerencia para el formulario.

## Accounts

### Tool: crm_accounts_list_contacts
- HTTP: POST `/api/crm/accounts/listContacts`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `Content-Type: application/json`
- Body (requerido):
  - `accountNum` (string)
  - `page` (int >= 1)
  - `pageSize` (int >= 1)
- Respuesta: `IndPagedResponse<ContactDto>`

### Tool: crm_accounts_list_accounts
- HTTP: POST `/api/crm/accounts/listAccounts`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `Content-Type: application/json`
- Body (requerido):
  - `accountNum` (string, opcional)
  - `page` (int >= 1)
  - `pageSize` (int >= 1)
- Respuesta: `IndPagedResponse<AccountDto>`

## Activities

### Tool: crm_activities_create
- HTTP: POST `/api/crm/activities/create`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body (requerido):
  - `accountNum` (string)
  - `visitType` (string)
  - `description` (string)
  - `transDate` (string date)
  - `comentarios` (string, opcional)
  - `antecedentes` (string, opcional)
  - `conclusiones` (string, opcional)
- Respuesta: `IndApiResponse<ActivityDto>`
- Notas: `createdByUserId` se toma de `X-IND-AxUserId`.

### Tool: crm_activities_list_post
- HTTP: POST `/api/crm/activities/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body (requerido):
  - `fromDate` (string date)
  - `toDate` (string date)
  - `accountNum` (string, opcional)
- Respuesta: `IndPagedResponse<ActivityDto>`

### Tool: crm_activities_update
- HTTP: PUT `/api/crm/activities/{recId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Path:
  - `recId` (int64)
- Body (requerido):
  - `accountNum` (string)
  - `visitType` (string)
  - `description` (string)
  - `transDate` (string date)
  - `comentarios` (string, opcional)
  - `antecedentes` (string, opcional)
  - `conclusiones` (string, opcional)
- Respuesta: `IndApiResponse<ActivityDto>`

### Tool: crm_activities_delete
- HTTP: DELETE `/api/crm/activities/{recId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`
- Path:
  - `recId` (int64)
- Respuesta: `IndApiResponse<object>`
- Nota: Postman incluye `X-IND-AxUserId`; si no es requerido, tratar como opcional.

### Tool: crm_activities_get_by_recid
- HTTP: GET `/api/crm/activities/{recId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`
- Path:
  - `recId` (int64)
- Respuesta: `IndPagedResponse<ActivityDetailDto>` (items con 1 elemento)
- Nota: Postman incluye `X-IND-AxUserId`; si no es requerido, tratar como opcional.

### Tool: crm_activities_get_by_code
- HTTP: GET `/api/crm/activities/by-code/{code}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`
- Path:
  - `code` (string)
- Respuesta: `IndPagedResponse<ActivityDetailDto>` (items con 1 elemento)
- Nota: Postman incluye `X-IND-AxUserId`; si no es requerido, tratar como opcional.

## Visits

### Tool: crm_visits_create_visita_asistente
- HTTP: POST `/api/crm/visits/createVisitaAsistente`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body (requerido):
  - `refRecIdActividad` (int64)
  - `asistenteTipo` (string o int)
  - `asistenteId` (string)
  - `contactoRecId` (int64)
- Respuesta: `IndApiResponse<object>`

### Tool: crm_visits_delete_visita_asistente
- HTTP: DELETE `/api/crm/visits/deleteVisitaAsistente`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `Content-Type: application/json`
- Body (requerido):
  - `refRecIdActividad` (int64)
  - `asistenteId` (string)
- Respuesta: `IndApiResponse<object>`

## Expense Sheets

### Tool: crm_expensesheets_create
- HTTP: POST `/api/crm/expensesheets`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body (requerido):
  - `description` (string)
  - `currencyCode` (string)
  - `lines` (array, requerido)
    - `lines[].transDate` (string date)
    - `lines[].typeValue` (int)
    - `lines[].description` (string)
    - `lines[].internacional` (bool)
    - `lines[].ticket` (bool)
    - `lines[].qty` (number)
    - `lines[].amount` (number)
    - `lines[].projId` (string, opcional)
    - `lines[].indAttachFiles` (string, opcional)
  - `projId` (string, opcional)
  - `exchRate` (number, opcional)
  - `expenseSheetStatus` (int, opcional)
  - `exchangeRateMode` (int, opcional; requiere `expenseSheetStatus`)
- Respuesta: `IndApiResponse<ExpenseSheetDto>`

### Tool: crm_expensesheets_get
- HTTP: GET `/api/crm/expensesheets/{hojaGastosId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Path:
  - `hojaGastosId` (string)
- Respuesta: `IndApiResponse<ExpenseSheetDto>`
- Campos de salida relevantes: `expenseSheetStatus`, `exchangeRateMode`, `createdDate` en el encabezado.

### Tool: crm_expensesheets_update_header
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Path:
  - `hojaGastosId` (string)
- Body (requerido):
  - `description` (string)
  - `currencyCode` (string)
  - `projId` (string, opcional)
  - `exchRate` (number, opcional)
  - `expenseSheetStatus` (int, opcional)
  - `exchangeRateMode` (int, opcional; requiere `expenseSheetStatus`)
- Respuesta: `IndApiResponse<ExpenseSheetDto>`

### Tool: crm_expensesheets_update_line
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Path:
  - `hojaGastosId` (string)
  - `lineRecId` (int64)
- Body (requerido):
  - `transDate` (string date)
  - `typeValue` (int)
  - `description` (string)
  - `qty` (number)
  - `Amount` (number)
  - `internacional` (bool, opcional)
  - `ticket` (bool, opcional)
  - `projId` (string, opcional)
  - `indAttachFiles` (string, opcional)
- Respuesta: `IndApiResponse<ExpenseSheetLineDto>`

### Tool: crm_expensesheets_delete_line
- HTTP: DELETE `/api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`
- Path:
  - `hojaGastosId` (string)
  - `lineRecId` (int64)
- Query:
  - `deleteWholeSheet` (bool; 0|1)
- Respuesta: `IndApiResponse<object>`
- Nota: si `deleteWholeSheet=1`, `lineRecId` puede ser 0 y se elimina cabecera + lineas.

### Tool: crm_expensesheets_list
- HTTP: POST `/api/crm/expensesheets/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`, `X-IND-AxUserId`, `Content-Type: application/json`
- Body (requerido):
  - `filter` (string, opcional)
  - `page` (int >= 1)
  - `pageSize` (int >= 1)
  - `billedMode` (int 0|1|2, opcional; default 0)
  - `createdDateFrom` (string date, opcional; `yyyyMMdd` o `yyyy-MM-dd`)
  - `createdDateTo` (string date, opcional; `yyyyMMdd` o `yyyy-MM-dd`)
  - `projId` (string, opcional)
  - `currencyCode` (string, opcional)
- Respuesta: `IndPagedResponse<ExpenseSheetListItemDto>`
- Campos de salida relevantes por item: `expenseSheetStatus`, `exchangeRateMode`, `userId`, `exchRate`, `createdDate`.
- Nota: si no hay filtro, AX devuelve lista vacia.

## Projects

### Tool: crm_projects_list
- HTTP: GET `/api/crm/projects/list`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`
- Query (requerido):
  - `filter` (string)
  - `page` (int >= 1)
  - `pageSize` (int >= 1)
- Respuesta: `IndPagedResponse<ProjectDto>`
- Nota: si no hay filtro, AX devuelve lista vacia.

## Internal

### Tool: crm_template_sample
- HTTP: GET `/api/crm/template/sample`
- Auth: Bearer token
- Headers: `Authorization`, `X-IND-Company`
- Respuesta: `IndApiResponse<object>`
- Nota: endpoint interno, no exponer en Swagger.
