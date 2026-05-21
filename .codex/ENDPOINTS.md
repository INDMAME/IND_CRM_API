# IND_CRM_API Endpoints (actualizado 2026-04-16)

Base URL: `{{baseUrl}}`

- DEV: `https://dev.insertec.biz:2083`
- PROD: `https://crm.insertec.biz:7776`

Autenticacion y headers comunes
- Authorization: Bearer {{tokenId}} (requerido en todo endpoint salvo login y health/ping)
- X-IND-Company: {{companyId}} (requerido en endpoints CRM: /api/crm/*)
- X-IND-AxUserId: {{axUserId}} (requerido en endpoints que envian userId a AX; se obtiene de /api/auth/entra/context -> Header.AxUserId)
- X-IND-EntraOid: {{entraOid}} (requerido en endpoints que validan el contexto firmado de companias)
- X-IND-Context-Version: {{contextVersion}} (requerido en endpoints que validan el contexto firmado de companias)
- X-IND-Permissions-Revision: {{permissionsRevision}} (requerido en endpoints que validan el contexto firmado de companias)
- X-IND-Context-Token: {{contextToken}} (requerido en endpoints que validan el contexto firmado de companias)
- companyId se obtiene de /api/auth/entra/context (Items[0].Header.DefaultCompany o Items[0].Companies[*].CompanyId)
- contextVersion, permissionsRevision y contextToken se obtienen de /api/auth/entra/context (Items[0].ContextVersion, Items[0].PermissionsRevision, Items[0].ContextToken)
- baseUrl se define en la collection Postman como variable compartida.

Reglas
- Todo endpoint que requiera userId debe tomarlo desde el header X-IND-AxUserId.
- Los endpoints de negocio CRM deben exigir companyId por header X-IND-Company.
- Los endpoints que derivan de BaseCrmController validan ademas el contexto firmado de companias usando X-IND-EntraOid, X-IND-Context-Version, X-IND-Permissions-Revision y X-IND-Context-Token.
- Swagger debe documentar resumen, parametros, respuestas y errores.
- Regla obligatoria de fechas en tickets y hojas de gastos: request admite `DDMMYYYY` o `DD.MM.YYYY`; response devuelve siempre `DD.MM.YYYY` para `transDate`, `createdDateFrom`, `createdDateTo` y `createdDate`.

Endpoints

## Auth
- POST /api/auth/login (AllowAnonymous)
  Body: { "Username": "...", "Password": "..." }
  Response: IndApiResponse con Data.token y Data.expires.
- POST /api/auth/refresh (Authorize)
  Headers: Authorization
- POST /api/auth/entra/context (Authorize)
  Body: { "entraOid": "GUID", "appCode": "APP" }
  Response context fields include: ContextToken, ContextVersion, PermissionsRevision, ContextIssuedUtc, ContextExpiresUtc, Header.DefaultCurrencyCode, Companies[].CurrencyCode, Companies[].AllowSelfManagement, Companies[].CrmUserId

## Health
- GET /api/health/ping (AllowAnonymous)
- GET /api/health/health (Authorize)

## System
- GET /api/system/getEnvironmentName (Authorize)
- GET /api/system/getCompanyName (Authorize)
- GET /api/system/exchange-rate?baseCurrency=EUR&targetCurrency=USD&date=2026-02-16 (Authorize)
  Query required: baseCurrency, targetCurrency (ISO 4217, 3 letras)
  Query optional: date (yyyy-MM-dd; si no se envia usa latest)
  Response: IndApiResponse<ExchangeRateDto> con BaseCurrency, TargetCurrency, Rate, Date, Source natural por proveedor
  Source posibles:
  - Banco Central Europeo (ECB)
  - Frankfurter API (fallback nivel 2)
  - Open ER API (fallback nivel 3)
  Comportamiento interno: ECB es proveedor primario; fallback nivel 2 a Frankfurter y fallback nivel 3 a OpenErApi.
  Frankfurter endpoint: https://api.frankfurter.app/latest?from={BASE}&to={TARGET}
  OpenErApi endpoint: https://open.er-api.com/v6/latest/{BASE}
  Nota OpenErApi: solo soporta latest; si se solicita fecha distinta a hoy, se usa latest igualmente.
  Contrato externo: sin cambios en ruta, envelope ni estructura publica (no se expone el fallback).
  Cache: MemoryCache 24h por clave base|target|date (solo resultados exitosos).
  ErrorCode: VALIDATION_ERROR (422), EXCHANGE_RATE_NOT_FOUND (404, legacy), RATE_UNAVAILABLE (404), INTERNAL_ERROR (500)
- GET /api/system/exchange-rate/public-direct?baseCurrency=USD&targetCurrency=EUR&date=2026-02-18 (AllowAnonymous)
  Query required: baseCurrency, targetCurrency (ISO 4217, 3 letras)
  Query optional: date (yyyy-MM-dd; si no se envia usa latest)
  Endpoint de consumo recomendado: {{baseUrl}}/api/system/exchange-rate/public-direct?baseCurrency=AED&targetCurrency=EUR
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
  Headers adicionales cuando persistTicket=true: X-IND-Company, X-IND-AxUserId, X-IND-EntraOid, X-IND-Context-Version, X-IND-Permissions-Revision, X-IND-Context-Token.
  Draft IA incluye `gastoType` (tipo de gasto de cabecera), `ticketDate`, `ticketTime` y mantiene `lines[].typeValue`. `transDate` se conserva por compatibilidad y debe coincidir con `ticketDate` cuando la IA detecta fecha del ticket.
  Si `persistTicket=true`, `Data.TicketCreation.ProcessedByAI` retorna `true` y el ticket queda marcado en AX como procesado por IA.
- POST /api/ia/service/expensesheets/ask (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `question`.
  Body optional: `answerInstructions`, `listRequest`, `sourceJson`.
  `listRequest` reutiliza los filtros de `POST /api/crm/expensesheets/list`: `filter`, `billedMode`, `createdDateFrom`, `createdDateTo`, `projId`, `currencyCode`, `expenseSheetStatus`, `reimbursableExpense`, `includeSubordinates`.
  Compatibilidad: `page` y `pageSize` pueden enviarse dentro de `listRequest`, pero este endpoint los ignora.
  `sourceJson` acepta el JSON completo devuelto por `POST /api/crm/expensesheets/list` o un array directo de registros (`Items`).
  Limites defensivos para `sourceJson`: maximo 4 MB por request y maximo 6000 registros inline.
  Si llega `sourceJson`, el endpoint analiza ese JSON directamente y omite la carga server-side.
  Si no llega `sourceJson`, el backend carga todos los registros filtrados server-side y decide si responde en modo `direct` o `chunked`.
  Response data: `Answer`, `Model`, `SourceKey`, `FiltersApplied`, `TotalSourceRecords`, `RecordsSentToModel`, `RetrievalMode`, `Truncated`, `Warnings`.
  Errores relevantes: 422 validacion, 429 rate limit IA, 500 error interno.

## Expense Sheets
- GET /api/crm/expensesheets/currencies (Authorize + X-IND-Company)
  Response items: CurrencyCode, CurrencyCodeISO
- GET /api/crm/expensesheets/subordinates (Authorize + X-IND-Company + X-IND-AxUserId)
  Response items: UserId (CRM), AxUserId, Name
- POST /api/crm/expensesheets (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required by mode:
  mode 0 (default): description, currencyCode, lines[] (con lines[].price)
  mode 1: description, currencyCode (sin lines)
  mode 2: existingHojaGastosId y lines[] (con lines[].price)
  Optional: mode (0|1|2), existingHojaGastosId, projId, exchRate, expenseSheetStatus, exchangeRateMode, reimbursableExpense (0 No, 1 Yes, 2 Both), lines[].projId, lines[].internacional, lines[].fileId, lines[].reimbursableExpense, lines[].currencyCode, lines[].amountMST, lines[].exchRate
- GET /api/crm/expensesheets/fuel-price-km?transDate=2026-02-18 (Authorize + X-IND-Company + X-IND-AxUserId)
  Query optional: transDate (DDMMYYYY o DD.MM.YYYY; si no se envia usa hoy)
  Response: IndApiResponse con PriceKm, Source y TransDate
- GET /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Response header fields include: expenseSheetStatus, estadoComentarios, exchangeRateMode, createdDate, reimbursableExpense
  Response line fields include: price, qty, amount, projId, reimbursableExpense, currencyCode, amountMST, exchRate
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId` para evitar colision con `/api/crm/expensesheets/tickets`.
- PUT /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: description, currencyCode (projId optional, exchRate optional, expenseSheetStatus optional, exchangeRateMode optional, estadoComentarios optional, reimbursableExpense optional)
  Nota: si se envia `estadoComentarios`, tambien se deben enviar `expenseSheetStatus` y `exchangeRateMode`.
  Nota: actualizar cabecera no propaga cambios a lineas existentes. La propagacion se ejecuta con endpoints explicitos.
  Nota: si una linea guardada usa otra divisa, AX marca la cabecera con `INDDefaultParameters.CRMCurrencyVarios`; si una linea guardada usa otro proyecto (`projId`/`projIdHornos`), AX marca la cabecera con `INDDefaultParameters.CRMProjIdVarios`; si una linea guardada usa otro `reimbursableExpense`, AX marca la cabecera con `INDReimbursableExpense::Both`.
- POST /api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate?recalculateAmountMST=true (Authorize + X-IND-Company + X-IND-AxUserId)
  Propaga la `currencyCode` y `exchRate` actuales de cabecera a todas las lineas existentes.
  Query optional: `recalculateAmountMST` (default true).
  AX bloquea la operacion si la hoja tiene lineas multimoneda, si `currencyCode` de cabecera es `INDDefaultParameters.CRMCurrencyVarios` o si esta bloqueada por Voucher.
  Response data: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId`.
- POST /api/crm/expensesheets/{hojaGastosId}/reimbursable-expense/propagate (Authorize + X-IND-Company + X-IND-AxUserId)
  Propaga el `reimbursableExpense` actual de cabecera a todas las lineas existentes.
  AX bloquea la operacion si `reimbursableExpense` de cabecera es `Both` o si la hoja esta bloqueada por Voucher.
  Response data: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId`.
- PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: transDate (DDMMYYYY o DD.MM.YYYY), typeValue, description, qty, price
  Optional: fileId (INDFileId), internacional, projId, reimbursableExpense (0 No, 1 Yes, 2 Both), currencyCode, amountMST, exchRate
  Nota: si `currencyCode` de linea difiere de cabecera, enviar `exchRate` o `amountMST`; AX no reutiliza la tasa de cabecera para otra divisa.
  Nota: si `reimbursableExpense` de linea difiere de cabecera, AX marca cabecera como `Both`.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas manuales temporales.
- DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}?deleteMode=0|1|2 (Authorize + X-IND-Company + X-IND-AxUserId)
  deleteMode: 0=LineOnly, 1=HeaderOnly (alias de WholeSheet), 2=WholeSheet.
  Legacy: deleteWholeSheet=0|1 sigue soportado si no se envia deleteMode. AX procesa 1 y 2 como deleteWholeSheet.
  Nota: si deleteMode es LineOnly, `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas manuales temporales.
  Nota: si deleteMode no es LineOnly, lineRecId puede ser 0.
- POST /api/crm/expensesheets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize
  Body optional: filter, billedMode, createdDateFrom (DDMMYYYY o DD.MM.YYYY), createdDateTo (DDMMYYYY o DD.MM.YYYY), projId, currencyCode, expenseSheetStatus (0 Draft, 1 InReview, 2 Approved, 3 Rejected, 4 Paid), reimbursableExpense (0 No, 1 Yes, 2 Both), includeSubordinates (bool; true = subordinados directos del usuario de header)
  Response list fields include: expenseSheetStatus, estadoComentarios, exchangeRateMode, userId, userName, exchRate, createdDate y reimbursableExpense
  billedMode: 0=no facturado, 1=facturado, 2=ambos (default 0).

## Expense Sheet Tickets
- POST /api/crm/expensesheets/tickets (Authorize + X-IND-Company + X-IND-AxUserId)
  mode 0: crea cabecera + lineas + DocuRef.
  mode 1: crea solo cabecera + DocuRef.
  mode 2: agrega lineas a `existingFileId`.
  Body mode 0|1: description, currencyCode, transDate (DDMMYYYY o DD.MM.YYYY), urlFile.
  Body mode 0|2: lines[] con description, qty, price (totalAmount opcional). Las lineas de ticket pueden informar `price`/`totalAmount` negativos; `qty` no puede ser negativo y `qty = 0` solo se acepta con total de linea negativo.
  Optional: totalAmount, comentario, fileExtension, existingFileId, gastoType (0,1,2,3,4,5,6,7,8,14), ocrJson, normalizedJson, ticketDate (DDMMYYYY o DD.MM.YYYY), ticketTime (HH:mm, HH:mm:ss o segundos 0..86399).
- POST /api/crm/expensesheets/tickets/quick-create (Authorize + X-IND-Company + X-IND-AxUserId)
  Content-Type required: multipart/form-data.
  Body required: ticketImage (jpg/jpeg/png/webp, max 50 MB).
  Body optional: currencyCode, description, comentario, existingHojaGastosId, projId (legacy alias: projectId).
  Flujo: crea ticket provisional, sube archivo, extrae draft IA, finaliza ticket y opcionalmente lo vincula a una hoja de gastos existente.
  Response data: `FileId`, `UrlFile`, `FileName`, `ProcessedByAI`, `LinkedToSheet`, `HojaGastosId`, `CompletedStage`, `FailedStage`, `RollbackAttempted`, `RollbackSucceeded`, `RollbackMessage`, `StepTraceIds.{TicketCreate,FileUpload,DraftExtract,TicketFinalize,SheetLink}`.
  En errores tras crear `FileId`, el endpoint intenta rollback interno del blob y del ticket AX; el error original se conserva y el resultado del rollback viaja en los campos `Rollback*`.
  Errores relevantes: 422 validacion, 429 rate limit IA, 503 servicio IA no disponible, 500 error interno.
- GET /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Devuelve cabecera + lineas del ticket.
  Cabecera incluye `processedByAI` (bool), `gastoType` (int), `hojaGastosIdDisplay` (string), `ocrJson` (string), `normalizedJson` (string), `ticketDate` y `ticketTime`.
- POST /api/crm/expensesheets/tickets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize.
  Body optional: searchKey (compat: `filter`), status (0 Pending, 1 Assigned), createdDateFrom (DDMMYYYY o DD.MM.YYYY), createdDateTo (DDMMYYYY o DD.MM.YYYY), currencyCode, gastoType (0,1,2,3,4,5,6,7,8,14), processedByAI (bool).
  Nota: `createdDateFrom/createdDateTo` son opcionales; si ambos llegan, se valida `from <= to`. La fecha de referencia de filtros y respuesta es `ticketHeader.createdDate`.
  Response items: `FileId`, `Description`, `Status`, `ProcessedByAI`, `CurrencyCode`, `TotalAmount`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `GastoType`.
- POST /api/crm/expensesheets/tickets/link/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize.
  Body optional: searchKey (compat: `filter`), createdDateFrom (DDMMYYYY o DD.MM.YYYY), createdDateTo (DDMMYYYY o DD.MM.YYYY), currencyCode, gastoType (0,1,2,3,4,5,6,7,8,14), processedByAI (bool).
  Prefiltros fijos en origen: `status = Pending` y `totalAmount != 0`.
  Nota: la fecha de referencia de filtros y respuesta es `ticketHeader.createdDate`.
  Response items: `FileId`, `Description`, `CurrencyCode`, `TotalAmount`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `ProcessedByAI`, `GastoType`.
- POST /api/crm/expensesheets/tickets/link/bulk (Authorize + X-IND-Company + X-IND-AxUserId)
  Body legacy soportado: `expenseSheetId`, `ticketIds[]` (equivale a `selectionMode = selected`).
  Body ampliado:
  - `expenseSheetId` obligatorio
  - `selectionMode` opcional (`selected` por defecto, `filtered`)
  - `ticketIds[]` obligatorio en `selected`
  - `filters` obligatorio en `filtered`: `searchKey` (compat: `filter`), `createdDateFrom`, `createdDateTo`, `currencyCode`, `gastoType`, `processedByAI`
  - `excludedIds[]` opcional en `filtered`
  En `filtered` reutiliza la misma resolucion server-side que `tickets/link/list`, con prefiltros base `status = Pending` y `totalAmount != 0`.
  Reutiliza `createExpenseSheet` en modo `2` para anadir una linea por ticket a una hoja existente, usando el `projId` de la hoja destino para la linea generada.
  Valida hoja destino, permisos, editabilidad y deduplicacion, y soporta resultado parcial.
  Response data: `expenseSheetId`, `requestedCount`, `linkedCount`, `skippedCount`, `failedCount`, `linkedTicketIds`, `skipped[]`, `failed[]`.
- PUT /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza cabecera y DocuRef (description, currencyCode, gastoType, totalAmount, status, transDate (DDMMYYYY o DD.MM.YYYY), ticketDate (DDMMYYYY o DD.MM.YYYY), ticketTime (HH:mm, HH:mm:ss o segundos 0..86399), comentario, urlFile, fileName, fileExtension, processedByAI, ocrJson, normalizedJson).
- POST /api/crm/expensesheets/tickets/{fileId}/ia (Authorize + X-IND-Company + X-IND-AxUserId)
  Reemplaza cabecera + lineas del ticket con datos de IA.
  Reglas:
  - Reemplazo total de lineas (delete + insert).
  - Marca `processedByAI=true`.
  - Usa metodo AX atomico `updateExpenseSheetTicketFromIA`.
  - Compatibilidad de entrada: si llega envelope tipo `expensefromticket` (`{ Success, Message, Data, TraceId }`), el backend adapta automaticamente `Data` al contrato esperado.
  Body: `description`, `currencyCode`, `gastoType` (opcional), `totalAmount` (opcional), `transDate` (DDMMYYYY o DD.MM.YYYY), `ticketDate` (DDMMYYYY o DD.MM.YYYY, opcional), `ticketTime` (HH:mm, HH:mm:ss o segundos 0..86399, opcional), `comentario` (opcional), `urlFile`, `fileName` (opcional), `ocrJson` (opcional), `normalizedJson` (opcional), `fileExtension` (opcional), `lines[]`. Las lineas pueden llevar importes negativos como descuento; si `qty = 0`, el total de linea debe ser negativo.
- POST /api/crm/expensesheets/tickets/{fileId}/file?extension=jpg (Authorize + X-IND-Company + X-IND-AxUserId)
  Content-Type: multipart/form-data (primer archivo del payload).
  Carga/reemplaza imagen en Azure Blob y actualiza `INDURLFile` + `INDFilename` en AX.
  Formato de nombre aplicado: `yyyyMMddHHmmss_{axUserId}_{fileId}.{ext}`.
- DELETE /api/crm/expensesheets/tickets/{fileId}/file (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina blob asociado y limpia `INDURLFile` + `INDFilename` del ticket en AX.
- DELETE /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina ticket completo.
  Query opcional: lineRecId (si se envia, elimina solo esa linea granular usando el metodo unificado de AX).
  Nota: si se envia `lineRecId`, debe ser distinto de 0 y puede ser negativo para lineas temporales.
- POST /api/crm/expensesheets/tickets/{fileId}/lines (Authorize + X-IND-Company + X-IND-AxUserId)
  Crea una linea granular en `INDTicketInfoLine`.
  Body: `description`, `qty`, `price`, `totalAmount` opcional.
- PUT /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza una linea granular de `INDTicketInfoLine`.
  Body: `description`, `qty`, `price`, `totalAmount` opcional.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas temporales.
- DELETE /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina una linea granular de `INDTicketInfoLine`.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas temporales.

## Activities / Visits
- POST /api/crm/activities/create (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `accountNum`, `visitType`, `description`, `transDate` (yyyyMMdd o yyyy-MM-dd).
  Body optional: `contactMethod` (`0` InPerson, `1` PhoneCall, `2` OnlineMeeting), `comentarios`, `antecedentes`, `conclusiones`.
  Si `contactMethod` no se envia, AX recibe `0` (InPerson).
- POST /api/crm/activities/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `fromDate`, `toDate` (yyyyMMdd o yyyy-MM-dd), `page`, `pageSize`.
  Body optional: `accountNum`.
  Response rows incluyen `ActividadId`, `RecId`, `Name`, `AccountNum`, `TransDate`, `ActividadType`, `TipoVisita`, `ContactMethod` y `Description`.
- GET /api/crm/activities/{recId} (Authorize + X-IND-Company)
  Response rows incluyen el detalle de visita con `ContactMethod`.
- GET /api/crm/activities/by-code/{code} (Authorize + X-IND-Company)
  Response `Items[0]` es `ActivityDetailDto` e incluye `ContactMethod`.
- PUT /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `accountNum`, `visitType`, `description`, `transDate` (yyyyMMdd o yyyy-MM-dd).
  Body optional: `contactMethod` (`0` InPerson, `1` PhoneCall, `2` OnlineMeeting), `comentarios`, `antecedentes`, `conclusiones`.
- DELETE /api/crm/activities/{recId} (Authorize + X-IND-Company)

## Projects
- GET /api/crm/projects/list?filter=...&page=1&pageSize=50 (Authorize + X-IND-Company)
  Nota: page y pageSize son obligatorios. Si no hay filtro, AX devuelve lista vacia.
