# IND_CRM_API Endpoints (actualizado 2026-02-26)

Base URL: {{baseUrl}} (por defecto https://crm.insertec.biz:7776)

Autenticacion y headers comunes
- Authorization: Bearer {{tokenId}} (requerido en todo endpoint salvo login y health/ping)
- X-IND-Company: {{companyId}} (requerido en endpoints CRM: /api/crm/*)
- X-IND-AxUserId: {{axUserId}} (requerido en endpoints que envian userId a AX; se obtiene de /api/auth/entra/context -> Header.AxUserId)
- companyId se obtiene de /api/auth/entra/context (Items[0].Header.DefaultCompany o Items[0].Companies[*].CompanyId)
- baseUrl se define en la collection Postman como variable compartida.

Reglas
- Todo endpoint que requiera userId debe tomarlo desde el header X-IND-AxUserId.
- Los endpoints de negocio CRM deben exigir companyId por header X-IND-Company.
- Swagger debe documentar resumen, parametros, respuestas y errores.

Endpoints

## Auth
- POST /api/auth/login (AllowAnonymous)
  Body: { "Username": "...", "Password": "..." }
  Response: IndApiResponse con Data.token y Data.expires.
- POST /api/auth/refresh (Authorize)
  Headers: Authorization
- POST /api/auth/entra/context (Authorize)
  Body: { "entraOid": "GUID", "appCode": "APP" }
  Response context fields include: Header.DefaultCurrencyCode, Companies[].CurrencyCode, Companies[].AllowSelfManagement

## Health
- GET /api/health/ping (AllowAnonymous)
- GET /api/health/health (Authorize)

## System
- GET /api/system/getEnvironmentName (Authorize)
- GET /api/system/getCompanyName (Authorize)
- GET /api/system/exchange-rate?baseCurrency=EUR&targetCurrency=USD&date=2026-02-16 (Authorize)
  Query required: baseCurrency, targetCurrency (ISO 4217, 3 letras)
  Query optional: date (yyyy-MM-dd; si no se envia usa latest)
  Response: IndApiResponse<ExchangeRateDto> con BaseCurrency, TargetCurrency, Rate, Date, Source=ECB
  Comportamiento interno: ECB es proveedor primario; si ECB no encuentra la divisa o falla la llamada externa, se activa fallback a ExchangeRate.host.
  Contrato externo: sin cambios en ruta, envelope ni estructura publica (no se expone el fallback).
  Cache: MemoryCache 24h por clave base|target|date (solo resultados exitosos).
  ErrorCode: VALIDATION_ERROR (422), EXCHANGE_RATE_NOT_FOUND (404, legacy), RATE_UNAVAILABLE (404), INTERNAL_ERROR (500)
- GET /api/system/exchange-rate/public-direct?baseCurrency=USD&targetCurrency=EUR&date=2026-02-18 (AllowAnonymous)
  Query required: baseCurrency, targetCurrency (ISO 4217, 3 letras)
  Query optional: date (yyyy-MM-dd; si no se envia usa latest)
  Response: IndApiResponse<ExchangeRateDto> con el mismo contrato que /api/system/exchange-rate
  ErrorCode: VALIDATION_ERROR (422), EXCHANGE_RATE_NOT_FOUND (404, legacy), RATE_UNAVAILABLE (404), INTERNAL_ERROR (500)

## MCP
- GET /api/mcp/tools (Authorize)
  Response: catalogo MCP (MCP_TOOLS.json)

## IA Services
- POST /api/ia/service/speech (Authorize)
  Content-Type: multipart/form-data
  Fields: languageId (required), audioFile (required), temperature (optional 0-1), prompt/context (optional)
- POST /api/ia/service/expensefromticket (Authorize)
  Content-Type: multipart/form-data
  Fields: ticketImage (required), persistTicket (optional true|false), ticketUrlFile (optional; si persistTicket=true y no se envia, se usa URL temporal)
  Headers adicionales cuando persistTicket=true: X-IND-Company, X-IND-AxUserId.
  Si `persistTicket=true`, `Data.TicketCreation.ProcessedByAI` retorna `true` y el ticket queda marcado en AX como procesado por IA.

## Expense Sheets
- GET /api/crm/expensesheets/currencies (Authorize + X-IND-Company)
  Response items: CurrencyCode, CurrencyCodeISO
- GET /api/crm/expensesheets/subordinates (Authorize + X-IND-Company + X-IND-AxUserId)
  Response items: UserId, Name
- POST /api/crm/expensesheets (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required by mode:
  mode 0 (default): description, currencyCode, lines[] (con lines[].price)
  mode 1: description, currencyCode (sin lines)
  mode 2: existingHojaGastosId y lines[] (con lines[].price)
  Optional: mode (0|1|2), existingHojaGastosId, projId, exchRate, expenseSheetStatus, exchangeRateMode, lines[].projId, lines[].internacional, lines[].fileId
- GET /api/crm/expensesheets/fuel-price-km?transDate=2026-02-18 (Authorize + X-IND-Company + X-IND-AxUserId)
  Query optional: transDate (yyyyMMdd o yyyy-MM-dd; si no se envia usa hoy)
  Response: IndApiResponse con PriceKm, Source y TransDate
- GET /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Response header fields include: expenseSheetStatus, estadoComentarios, exchangeRateMode, createdDate
  Response line fields include: price, qty, amount
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId` para evitar colision con `/api/crm/expensesheets/tickets`.
- PUT /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: description, currencyCode (projId optional, exchRate optional, expenseSheetStatus optional, exchangeRateMode optional, estadoComentarios optional)
  Nota: si se envia `estadoComentarios`, tambien se deben enviar `expenseSheetStatus` y `exchangeRateMode`.
- PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: transDate (yyyymmdd), typeValue, description, qty, price
  Optional: fileId (INDFileId), internacional, projId
- DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}?deleteMode=0|1|2 (Authorize + X-IND-Company + X-IND-AxUserId)
  deleteMode: 0=LineOnly, 1=HeaderOnly (alias de WholeSheet), 2=WholeSheet.
  Legacy: deleteWholeSheet=0|1 sigue soportado si no se envia deleteMode. AX procesa 1 y 2 como deleteWholeSheet.
  Nota: si deleteMode no es LineOnly, lineRecId puede ser 0.
- POST /api/crm/expensesheets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize
  Body optional: filter, billedMode, createdDateFrom, createdDateTo, projId, currencyCode, expenseSheetStatus (0 Draft, 1 InReview, 2 Approved, 3 Rejected, 4 Paid)
  Response list fields include: expenseSheetStatus, estadoComentarios, exchangeRateMode, userId, exchRate y createdDate
  billedMode: 0=no facturado, 1=facturado, 2=ambos (default 0).

## Expense Sheet Tickets
- POST /api/crm/expensesheets/tickets (Authorize + X-IND-Company + X-IND-AxUserId)
  mode 0: crea cabecera + lineas + DocuRef.
  mode 1: crea solo cabecera + DocuRef.
  mode 2: agrega lineas a `existingFileId`.
  Body mode 0|1: description, currencyCode, transDate, urlFile.
  Body mode 0|2: lines[] con description, qty, price (totalAmount opcional).
  Optional: totalAmount, comentario, fileExtension, existingFileId.
- GET /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Devuelve cabecera + lineas del ticket.
  Cabecera incluye `processedByAI` (bool) para indicar si el ticket fue procesado por IA.
- POST /api/crm/expensesheets/tickets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize.
  Body optional: filter, status (0 Pending, 1 Assigned).
  Response items incluyen `processedByAI` (bool).
- PUT /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza cabecera y DocuRef (description, currencyCode, totalAmount, status, transDate, comentario, urlFile, fileName, fileExtension, processedByAI).
- POST /api/crm/expensesheets/tickets/{fileId}/ia (Authorize + X-IND-Company + X-IND-AxUserId)
  Reemplaza cabecera + lineas del ticket con datos de IA.
  Reglas:
  - Reemplazo total de lineas (delete + insert).
  - Marca `processedByAI=true`.
  - Usa metodo AX atomico `updateExpenseSheetTicketFromIA`.
  - Compatibilidad de entrada: si llega envelope tipo `expensefromticket` (`{ Success, Message, Data, TraceId }`), el backend adapta automaticamente `Data` al contrato esperado.
  Body: `description`, `currencyCode`, `totalAmount` (opcional), `transDate`, `comentario` (opcional), `urlFile`, `fileName` (opcional), `fileExtension` (opcional), `lines[]`.
- POST /api/crm/expensesheets/tickets/{fileId}/file?extension=jpg (Authorize + X-IND-Company + X-IND-AxUserId)
  Content-Type: multipart/form-data (primer archivo del payload).
  Carga/reemplaza imagen en Azure Blob y actualiza `INDURLFile` + `INDFilename` en AX.
  Formato de nombre aplicado: `yyyyMMddHHmmss_{axUserId}_{fileId}.{ext}`.
- DELETE /api/crm/expensesheets/tickets/{fileId}/file (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina blob asociado y limpia `INDURLFile` + `INDFilename` del ticket en AX.
- DELETE /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina ticket completo.
  Query opcional: lineRecId (si se envia, elimina solo esa linea granular usando el metodo unificado de AX).
- POST /api/crm/expensesheets/tickets/{fileId}/lines (Authorize + X-IND-Company + X-IND-AxUserId)
  Crea una linea granular en `INDTicketInfoLine`.
- PUT /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza una linea granular de `INDTicketInfoLine`.
- DELETE /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina una linea granular de `INDTicketInfoLine`.

## Projects
- GET /api/crm/projects/list?filter=...&page=1&pageSize=50 (Authorize + X-IND-Company)
  Nota: page y pageSize son obligatorios. Si no hay filtro, AX devuelve lista vacia.
