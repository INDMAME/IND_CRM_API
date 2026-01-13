# IND_CRM_API Endpoints (actualizado 2026-01-12)

Base URL: {{baseUrl}} (por defecto https://crm.insertec.biz:7776)

Autenticacion y headers comunes
- Authorization: Bearer {{tokenId}} (requerido en todo endpoint salvo login y health/ping)
- X-IND-Company: {{companyId}} (requerido en endpoints CRM: /api/crm/*)
- companyId se obtiene de /api/auth/entra/context (Items[0].Header.DefaultCompany o Items[0].Companies[*].CompanyId)
- baseUrl se define en la collection Postman como variable compartida.

Reglas nuevas (directrices)
- Todo endpoint de creacion (POST que cree recursos) debe requerir userId en el body.
- Todos los endpoints de negocio deben exigir companyId por header X-IND-Company.
  Excepciones deben documentarse de forma explicita.

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

## Speech
- POST /api/speech/transcribe (Authorize)
  Content-Type: multipart/form-data
  Fields: languageId (required), audioFile (required), temperature (optional 0-1), prompt/context (optional)

## CRM Accounts
- POST /api/crm/accounts/listContacts (Authorize + X-IND-Company)
  Body: { accountNum (required), page (required >0), pageSize (required >0) }
- POST /api/crm/accounts/listAccounts (Authorize + X-IND-Company)
  Body: { accountNum (optional), page (required >0), pageSize (required >0) }

## CRM Activities
- POST /api/crm/activities/create (Authorize + X-IND-Company)
  Body required: accountNum, visitType, userId, createdByUserId, description, transDate (yyyyMMdd or yyyy-MM-dd)
  Optional: comentarios, antecedentes, conclusiones
- GET /api/crm/activities/list?userId=&fromDate=&toDate= (Authorize + X-IND-Company)
  Dates: yyyyMMdd or yyyy-MM-dd
- POST /api/crm/activities/list (Authorize + X-IND-Company)
  Body: { userId, fromDate, toDate }
- PUT /api/crm/activities/{recId} (Authorize + X-IND-Company)
  Body required: accountNum, visitType, userId, description, transDate
  Optional: comentarios, antecedentes, conclusiones
- DELETE /api/crm/activities/{recId} (Authorize + X-IND-Company)
  Respuesta 200 OK con mensaje en body (IndApiResponse). 404/422 si aplica.
- GET /api/crm/activities/{recId} (Authorize + X-IND-Company)
- GET /api/crm/activities/by-code/{code} (Authorize + X-IND-Company)
- POST /api/crm/activities/test (Authorize + X-IND-Company)
  Body: { userId, fromDate, toDate }

## CRM Visits
- POST /api/crm/visits/createVisitaAsistente (Authorize + X-IND-Company)
  Body required: refRecIdActividad, asistenteTipo, asistenteId, contactoRecId, createdByUserId
  Nota: este es un endpoint de creacion; por directriz nueva debe incluir userId en futuras revisiones.
- DELETE /api/crm/visits/deleteVisitaAsistente (Authorize + X-IND-Company)
  Body required: refRecIdActividad, asistenteId
  Respuesta 200 OK con mensaje en body (IndApiResponse). 404/422 si aplica.

## Internal (no expuesto en Swagger)
- GET /api/crm/template/sample
