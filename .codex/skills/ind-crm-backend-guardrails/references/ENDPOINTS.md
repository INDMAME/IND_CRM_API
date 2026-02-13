# IND_CRM_API Endpoints (actualizado 2026-02-10)

Base URL: {{baseUrl}} (por defecto https://crm.insertec.biz:7776)

Autenticacion y headers comunes
- Authorization: Bearer {{tokenId}} (requerido en todo endpoint salvo login y health/ping)
- X-IND-Company: {{companyId}} (requerido en endpoints CRM: /api/crm/*)
- X-IND-AxUserId: {{axUserId}} (requerido en endpoints que envian userId a AX; se obtiene de /api/auth/entra/context -> Header.AxUserId)
- companyId se obtiene de /api/auth/entra/context (Items[0].Header.DefaultCompany o Items[0].Companies[*].CompanyId)
- baseUrl se define en la collection Postman como variable compartida.

Reglas nuevas (directrices)
- Todo endpoint que requiera userId debe tomarlo desde el header X-IND-AxUserId.
- Las listas de proyectos y hojas de gastos deben usar paginacion con page y pageSize (>= 1).
- Todos los endpoints de negocio deben exigir companyId por header X-IND-Company.
  Excepciones deben documentarse de forma explicita.
- Cada endpoint debe tener documentacion Swagger completa (summary, params, responses, security) para gestionarlo via MCP.

Endpoints

## Auth
- POST /api/auth/login (AllowAnonymous)
  Body: { "Username": "...", "Password": "..." }
  Response: IndApiResponse con Data.token y Data.expires.
- POST /api/auth/refresh (Authorize)
  Headers: Authorization
- POST /api/auth/entra/context (Authorize)
  Body: { "entraOid": "GUID", "appCode": "APP" }

## Health
- GET /api/health/ping (AllowAnonymous)
- GET /api/health/health (Authorize)

## System
- GET /api/system/getEnvironmentName (Authorize)
- GET /api/system/getCompanyName (Authorize)

## MCP
- GET /api/mcp/tools (Authorize)
  Response: catalogo MCP (MCP_TOOLS.json)

## IA Services
- POST /api/ia/service/speech (Authorize)
  Content-Type: multipart/form-data
  Fields: languageId (required), audioFile (required), temperature (optional 0-1), prompt/context (optional)
- POST /api/ia/service/expensefromticket (Authorize)
  Content-Type: multipart/form-data
  Fields: ticketImage (required), currencyHint (optional)

## Accounts
- POST /api/crm/accounts/listContacts (Authorize + X-IND-Company)
  Body: { accountNum (required), page (required >0), pageSize (required >0) }
- POST /api/crm/accounts/listAccounts (Authorize + X-IND-Company)
  Body: { accountNum (optional), page (required >0), pageSize (required >0) }

## Activities
- POST /api/crm/activities/create (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: accountNum, visitType, description, transDate (yyyyMMdd or yyyy-MM-dd)
  Optional: comentarios, antecedentes, conclusiones
  Nota: userId/createdByUserId se toman de X-IND-AxUserId.
  Dates: yyyyMMdd or yyyy-MM-dd
  Nota: userId en query se ignora; se usa X-IND-AxUserId. accountNum es opcional.
- POST /api/crm/activities/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body: { fromDate, toDate, accountNum (optional) }
- PUT /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: accountNum, visitType, description, transDate
  Optional: comentarios, antecedentes, conclusiones
- DELETE /api/crm/activities/{recId} (Authorize + X-IND-Company)
  Respuesta 200 OK con mensaje en body (IndApiResponse). 404/422 si aplica.
- GET /api/crm/activities/{recId} (Authorize + X-IND-Company)
- GET /api/crm/activities/by-code/{code} (Authorize + X-IND-Company)
  Body: { fromDate, toDate, accountNum (optional) }

## Visits
- POST /api/crm/visits/createVisitaAsistente (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: refRecIdActividad, asistenteTipo, asistenteId, contactoRecId
  Nota: createdByUserId se toma de X-IND-AxUserId.
- DELETE /api/crm/visits/deleteVisitaAsistente (Authorize + X-IND-Company)
  Body required: refRecIdActividad, asistenteId
  Respuesta 200 OK con mensaje en body (IndApiResponse). 404/422 si aplica.

## Expense Sheets
- POST /api/crm/expensesheets (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: description, currencyCode, lines[]
  Optional: projId, exchRate, lines[].projId, lines[].indAttachFiles, lines[].internacional, lines[].ticket
- GET /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
- PUT /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: description, currencyCode (projId optional, exchRate optional)
- PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: transDate (yyyymmdd), typeValue, description, qty, amount
- DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}?deleteWholeSheet=0|1 (Authorize + X-IND-Company + X-IND-AxUserId)
  Nota: si deleteWholeSheet=1, lineRecId puede ser 0 y se elimina cabecera + lineas.
- POST /api/crm/expensesheets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize
  Body optional: filter, billedMode, createdDateFrom, createdDateTo, projId, currencyCode
  Nota: Si no hay filtro, AX devuelve lista vacia.
  billedMode: 0=no facturado, 1=facturado, 2=ambos (default 0).

## Projects
- GET /api/crm/projects/list?filter=...&page=1&pageSize=50 (Authorize + X-IND-Company)
  Nota: page y pageSize son obligatorios. Si no hay filtro, AX devuelve lista vacia.

## Internal (no expuesto en Swagger)
- GET /api/crm/template/sample
