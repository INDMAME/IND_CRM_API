# IND_CRM_API Endpoints (actualizado 2026-06-19)

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
  Response context fields include: ContextToken, ContextVersion, PermissionsRevision, ContextIssuedUtc, ContextExpiresUtc, Header.DefaultCurrencyCode, Header.UserName, Companies[].CurrencyCode, Companies[].AllowSelfManagement, Companies[].CrmUserId

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

## CRM Enums
- GET /api/crm/enums/by-name?appCode=CRM&axEnumNames=CRMGastoType,INDExpenseSheetStatus,INDReimbursableExpense,INDReimbursableExpenseLines (Authorize + X-IND-Company)
  Query optional: `appCode` default `CRM`, `axEnumNames` lista separada por comas.
  Si `axEnumNames` se omite o llega vacio, devuelve todos los enums activos configurados para el aplicativo y company.
  Response: `IndPagedResponse<CrmEnumCatalogDto>`.
  Cada item incluye `Company`, `AppCode`, `AxEnumName`, `AxEnumId`, `Found` y `Options[]`.
  Cada opcion incluye `Value` (compatibilidad), `EnumIndex` (valor numerico AX que deben enviar los endpoints de negocio cuando exista), `Label`, `Description`, `Active`, `SortOrder` y `AxEnumsTableRefRecId`.
  Nota: `SortOrder = 0` es valido y no significa vacio.
  ErrorCode: VALIDATION_ERROR (422), AX_COM_ERROR/AX_SESSION_ERROR (500).
- GET /api/crm/enums/by-id?appCode=CRM&axEnumIds=61472,61523 (Authorize + X-IND-Company)
  Query optional: `appCode` default `CRM`, `axEnumIds` lista separada por comas.
  Si `axEnumIds` se omite o llega vacio, devuelve todos los enums activos configurados para el aplicativo y company.
  Response y semantica iguales a `/api/crm/enums/by-name`.

## IA Services
- POST /api/ia/service/text/format (Authorize)
  Content-Type: application/json.
  Body required: `text` (max configurable, 20,000 characters by default).
  Body optional: `languageId` (`auto` by default or a valid BCP 47 language identifier).
  Behavior: corrects spelling, grammar, punctuation and readable plain-text layout while preserving the source language, meaning and data. It does not translate, summarize, answer, persist or update content. Server-owned instructions cannot be overridden by the consumer.
  Response data: `formattedText` (complete result), `hasChanges` (calculated by the API) and `warnings[]` with `fragment` and `reason`.
  Errors: 422 validation or moderation rejection, 429 per-user rate/concurrency limit, 503 AI provider unavailable.
- POST /api/ia/service/speech (Authorize)
  Content-Type: multipart/form-data
  Fields: languageId (required), audioFile (required), temperature (optional 0-1), prompt/context (optional)
- POST /api/ia/service/expensefromticket (Authorize)
  Content-Type: multipart/form-data
  Fields: ticketImage (required), persistTicket (optional true|false), ticketUrlFile (optional; si persistTicket=true y no se envia, se usa URL temporal)
  Headers adicionales cuando persistTicket=true: X-IND-Company, X-IND-AxUserId, X-IND-EntraOid, X-IND-Context-Version, X-IND-Permissions-Revision, X-IND-Context-Token.
  Draft IA incluye `gastoType` (tipo de gasto de cabecera), `ticketDate`, `ticketTime`, `totalAmount` (total bruto OCR) y mantiene `lines[].typeValue`. `transDate` se conserva por compatibilidad y debe coincidir con `ticketDate` cuando la IA detecta fecha del ticket.
  El total bruto se contrasta con etiquetas OCR de pago (`TOTAL A PAGAR`, `amount due`, etc.) y la suma de lineas se reconcilia antes de persistir; base imponible, subtotal, impuestos, descuentos, ahorro, importe entregado y cambio no se aceptan como total pagadero.
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
  mode 0 (default): description, lines[] (con lines[].price)
  mode 1: description (sin lines)
  mode 2: existingHojaGastosId y lines[] (con lines[].price)
  Optional: mode (0|1|2), existingHojaGastosId, projId, currencyCode/exchRate legacy como defaults de lineas nuevas, expenseSheetStatus, exchangeRateMode, reimbursableExpense (INDReimbursableExpense, default Yes), lines[].projId, lines[].internacional, lines[].fileId, lines[].reimbursableExpense (INDReimbursableExpenseLines, default heredado/default Yes), lines[].currencyCode, lines[].amountMST, lines[].exchRate
  Nota: la cabecera AX mantiene siempre la divisa local de reembolso y ExchRate=100; la divisa real se informa en cada linea.
  Nota enums AX: `expenseSheetStatus`, `exchangeRateMode`, `reimbursableExpense` (`INDReimbursableExpense`) y `lines[].reimbursableExpense` (`INDReimbursableExpenseLines`) deben enviarse como valores numericos obtenidos desde `/api/crm/enums/by-name`. En reembolso, `Yes=0` incluye el `AmountMST`; `No=1` excluye y deja `ReimbursableAmount=0`; `Both=2` solo representa una cabecera con lineas mixtas.
  Response data: `HojaGastosId` y `LineRecIds` (`number[]`, RecIds AX numericos).
- GET /api/crm/expensesheets/fuel-price-km?transDate=2026-02-18 (Authorize + X-IND-Company + X-IND-AxUserId)
  Query optional: transDate (DDMMYYYY o DD.MM.YYYY; si no se envia usa hoy)
  Response: IndApiResponse con PriceKm, Source y TransDate
- GET /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Response header fields include: userName, expenseSheetStatus, estadoComentarios, exchangeRateMode, createdDate, axCreatedDate, reimbursableExpense, totalAmountCurrency, totalAmountMST, totalGrossAmountMST, totalReimbursableAmount
  Nota totales: `totalAmountCurrency` y su alias `totalAmount` conservan, por compatibilidad nominal, el total contable legacy calculado desde el importe reembolsable; `totalAmountMST` conserva el total contable legacy en divisa company/MST. `totalGrossAmountMST` es el total bruto company/MST y no se filtra por reembolso ni por Visa. `totalReimbursableAmount` es el total explicito de reembolso company/MST e incluye unicamente las lineas con `ReimbursableExpense=Yes`; `VisaEmpresa` no interviene en el calculo. Durante un despliegue AX anterior, `totalReimbursableAmount` usa `totalAmountMST` como fallback y `totalGrossAmountMST` queda nulo.
  Nota JSON: Web API serializa las propiedades en PascalCase; en JavaScript usar `TotalGrossAmountMST`, `TotalReimbursableAmount` y `ReimbursableAmount`.
  Nota AX: `axCreatedDate` expone la fecha final adicional devuelta por el contrato AX y se normaliza a `DD.MM.YYYY`; actualmente refleja la misma fecha de creacion que `createdDate`.
  Nota: `userName` es `CRMUsuarioTable.Name` del propietario CRM de la hoja (`userId`). Se agrega al contrato de detalle como campo adicional compatible con clientes anteriores.
  Response line fields include: price, qty, amount, projId, reimbursableExpense, currencyCode, amountMST, reimbursableAmount, exchRate, totalAmountCurrency, totalAmountMST
  Nota lineas: `amount` y su alias `totalAmountCurrency` expresan el total en la divisa original de la linea; `amountMST` y su alias `totalAmountMST` expresan el total company/MST; `reimbursableAmount` expresa la parte reembolsable company/MST, copia `amountMST` con `ReimbursableExpense=Yes` y vale cero con `ReimbursableExpense=No`, independientemente de `VisaEmpresa`; queda nulo contra contratos AX legacy. AX conserva `VisaEmpresa` bloqueado como espejo inverso de compatibilidad (`Yes` reembolsable -> Visa `No`; `No` reembolsable -> Visa `Yes`).
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId` para evitar colision con `/api/crm/expensesheets/tickets`.
- PUT /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: description (projId optional, currencyCode/exchRate legacy ignorados por cabecera, expenseSheetStatus optional, exchangeRateMode optional, estadoComentarios optional, reimbursableExpense optional con enum INDReimbursableExpense; Yes incluye, No excluye y Both representa mezcla)
  Nota: si se envia `estadoComentarios`, tambien se deben enviar `expenseSheetStatus` y `exchangeRateMode`.
  Nota: actualizar cabecera no propaga divisa a lineas existentes. La cabecera queda siempre en divisa local de reembolso (`ExchRate=100`).
  Nota: si una linea guardada usa otro proyecto (`projId`/`projIdHornos`), AX marca la cabecera con `PurchParameters.INDProjIdVarious`; si una linea guardada usa otro `reimbursableExpense`, AX marca la cabecera con el valor agrupador de reembolso configurado en AX.
  Notificaciones de estado: el API no envia emails directamente. `INDCRMExpenseSheetService.updateExpenseSheetHeader` en Axapta captura estado anterior/posterior y lanza el email best-effort fuera del `tts` cuando aplica. Eventos AX soportados: `ExpenseSheetApprovalRequested`, `ExpenseSheetApproved`, `ExpenseSheetRejected`, `ExpenseSheetRejectionCancelled` y `ExpenseSheetPaid`. Si emisor y destinatario resuelven al mismo usuario CRM, se omite el email. Desde 2026-06-09 el transporte AX/DLL usa exclusivamente `SendMailEx`; el parametro opcional `attachmentFilePaths` va despues de `textBody` y antes de `saveToSentItems`. Para estas notificaciones se envia vacio, porque no adjuntan ficheros.
- POST /api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate?recalculateAmountMST=true&force=false (Authorize + X-IND-Company + X-IND-AxUserId)
  Legacy/no-op: se conserva por compatibilidad, pero AX ya no propaga divisa de cabecera a lineas.
  Query optional: `recalculateAmountMST` (default true, solo compatibilidad de respuesta), `force` (default false, ignorado).
  Response data: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId`.
- POST /api/crm/expensesheets/{hojaGastosId}/project-default/propagate (Authorize + X-IND-Company + X-IND-AxUserId)
  Propaga el `projId` actual de cabecera a `projId` y `projIdHornos` de todas las lineas existentes y rehace la asignacion de proyecto de cada linea.
  AX bloquea la operacion si `projId` de cabecera es `PurchParameters.INDProjIdVarious`, si no hay `projId` de cabecera o si la hoja esta bloqueada por Voucher.
  Response data: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId`.
- POST /api/crm/expensesheets/{hojaGastosId}/reimbursable-expense/propagate (Authorize + X-IND-Company + X-IND-AxUserId)
  Propaga el `reimbursableExpense` actual de cabecera a todas las lineas existentes.
  Usar despues de modificar cabecera solo cuando el usuario confirme que desea actualizar todas las lineas.
  AX bloquea la operacion si `reimbursableExpense` de cabecera es el valor agrupador de reembolso configurado en AX o si la hoja esta bloqueada por Voucher.
  Response data: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de routing: el literal `tickets` queda excluido de `hojaGastosId`.
- PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: transDate (DDMMYYYY o DD.MM.YYYY), typeValue, description, qty, price
  Optional: fileId (INDFileId), internacional, projId, reimbursableExpense (INDReimbursableExpenseLines), currencyCode, amountMST, exchRate
  Nota enums AX: `typeValue` y `reimbursableExpense` deben enviarse como valores numericos obtenidos desde `/api/crm/enums/by-name`; las lineas solo aceptan `INDReimbursableExpenseLines` No/Yes, no Both.
  Nota: si `currencyCode` de linea no es la divisa de reembolso de la hoja, enviar `exchRate` o `amountMST`; AX no reutiliza tasa de cabecera para divisas extranjeras. Si la divisa de linea y reembolso coinciden, editar `amountMST` no recalcula `exchRate`.
  Nota: si `reimbursableExpense` de linea difiere de cabecera, AX marca cabecera con el valor agrupador de reembolso configurado en AX.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas manuales temporales.
- DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}?deleteMode=0|1|2 (Authorize + X-IND-Company + X-IND-AxUserId)
  deleteMode: 0=LineOnly, 1=HeaderOnly (alias de WholeSheet), 2=WholeSheet.
  Legacy: deleteWholeSheet=0|1 sigue soportado si no se envia deleteMode. AX procesa 1 y 2 como deleteWholeSheet.
  Nota: si deleteMode es LineOnly, `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas manuales temporales.
  Nota: si deleteMode no es LineOnly, lineRecId puede ser 0.
- POST /api/crm/expensesheets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize
  Body optional: filter, billedMode, createdDateFrom (DDMMYYYY o DD.MM.YYYY), createdDateTo (DDMMYYYY o DD.MM.YYYY), projId, currencyCode, expenseSheetStatus, reimbursableExpense (INDReimbursableExpense: Yes incluye, No excluye, Both mezcla), includeSubordinates (bool; true = usuario de header + subordinados directos)
  Nota enums AX: `expenseSheetStatus` y `reimbursableExpense` deben enviarse como valores numericos obtenidos desde `/api/crm/enums/by-name`.
  Response list fields include: expenseSheetStatus, estadoComentarios, exchangeRateMode, userId, userName, exchRate, createdDate, axCreatedDate, reimbursableExpense, totalAmountCurrency, totalAmountMST, totalGrossAmountMST y totalReimbursableAmount
  Nota totales: `totalAmountCurrency`/`totalAmount` y `totalAmountMST` conservan los totales contables legacy; `totalGrossAmountMST` es el bruto company/MST y no se filtra por reembolso ni Visa; `totalReimbursableAmount` es el reembolso company/MST e incluye solo `ReimbursableExpense=Yes`, sin consultar Visa. Con AX legacy, el reembolso usa `totalAmountMST` como fallback y el bruto queda nulo.
  Nota JSON: Web API serializa las propiedades en PascalCase; en JavaScript usar `TotalGrossAmountMST` y `TotalReimbursableAmount`.
  Nota AX: `axCreatedDate` expone la fecha final adicional devuelta por el contrato AX y se normaliza a `DD.MM.YYYY`; actualmente refleja la misma fecha de creacion que `createdDate`.
  Orden: createdDate descendente, HojaGastosId descendente como desempate; filas sin createdDate valida al final.
  billedMode: 0=no facturado, 1=facturado, 2=ambos (default 0).

## Expense Sheet Tickets
- POST /api/crm/expensesheets/tickets (Authorize + X-IND-Company + X-IND-AxUserId)
  mode 0: crea cabecera + lineas + DocuRef.
  mode 1: crea solo cabecera + DocuRef.
  mode 2: agrega lineas a `existingFileId`.
  Body mode 0|1: description, currencyCode, transDate (DDMMYYYY o DD.MM.YYYY), urlFile.
  Body mode 0|2: lines[] con description, qty, price (totalAmount opcional). Las lineas de ticket pueden informar `price`/`totalAmount` negativos; `qty` no puede ser negativo y `qty = 0` solo se acepta con total de linea negativo.
  Optional: totalAmount, comentario, fileExtension, existingFileId, gastoType, ocrJson, normalizedJson, ticketDate (DDMMYYYY o DD.MM.YYYY), ticketTime (HH:mm, HH:mm:ss o segundos 0..86399).
  Nota enums AX: `gastoType` debe enviarse como valor numerico obtenido desde `/api/crm/enums`.
  Response data incluye `TotalAmountCurrency` y `TotalAmountMST` cuando AX devuelve los extras de cabecera. `TotalAmount` se mantiene como alias legacy de `TotalAmountCurrency`.
  Duplicidad: si el mismo usuario ya tiene otro ticket con igual `ticketDate` y una `ticketTime` valida, responde 409 con `CRM_EXPENSESHEET_TICKET_DUPLICATE`. Una hora ausente o `0` no participa en esta validacion; usuarios distintos no colisionan.
- POST /api/crm/expensesheets/tickets/quick-create (Authorize + X-IND-Company + X-IND-AxUserId)
  Content-Type required: multipart/form-data.
  Body required: ticketImage (jpg/jpeg/png/webp, max 50 MB).
  Body optional: currencyCode, description, comentario, existingHojaGastosId, projId (legacy alias: projectId).
  Flujo: crea ticket provisional, sube archivo, extrae draft IA, finaliza ticket y opcionalmente lo vincula a una hoja de gastos existente.
  El alta provisional conserva `TicketDate` vacio hasta terminar el OCR; la fecha de hoy se usa solo como `DocuRef.INDTransDate` obligatorio y no participa en la duplicidad del ticket.
  Response data: `FileId`, `UrlFile`, `FileName`, `ProcessedByAI`, `LinkedToSheet`, `HojaGastosId`, `TotalAmountCurrency`, `TotalAmountMST`, `CompletedStage`, `FailedStage`, `RollbackAttempted`, `RollbackSucceeded`, `RollbackMessage`, `StepTraceIds.{TicketCreate,FileUpload,DraftExtract,TicketFinalize,SheetLink}`.
  En errores tras crear `FileId`, el endpoint intenta rollback interno del blob y del ticket AX; el error original se conserva y el resultado del rollback viaja en los campos `Rollback*`.
  Errores relevantes: 409 duplicidad (`CRM_EXPENSESHEET_TICKET_DUPLICATE`), 422 validacion, 429 rate limit IA, 503 servicio IA no disponible, 500 error interno.
- GET /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Devuelve cabecera + lineas del ticket.
  Cabecera incluye `processedByAI` (bool), `gastoType` (int), `hojaGastosIdDisplay` (string), `ocrJson` (string), `normalizedJson` (string), `ticketDate`, `ticketTime`, `totalAmountCurrency` y `totalAmountMST`.
  Nota totales: `totalAmountCurrency` viene de `INDTicketInfoTable.TotalAmount`; `totalAmountMST` viene de `INDTicketInfoTable.AmountMST`. `totalAmount` y `amountMST` se mantienen como aliases legacy.
  Lineas incluyen `AdjustmentAmount` cuando AX devuelve el flag `INDTicketInfoLine.Adjustment`.
  Cada elemento de `Lines[*]` incluye tambien `ReimbursableExpense` (`int?`, `0=Yes`, `1=No`) y `ReimbursableAmount` (`decimal?`, importe en divisa de la empresa), obtenidos de la `CRMHojaGastosLine` vinculada al ticket.
  Compatibilidad: ambos campos son `null` cuando AX devuelve el contrato legacy o cuando no existe una vinculacion unica con `CRMHojaGastosLine`. Si existen varias lineas de ticket, los valores se repiten como metadatos de la misma linea de hoja vinculada; no son importes propios de cada linea de ticket y no deben sumarse.
- POST /api/crm/expensesheets/tickets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize.
  Body optional: searchKey (compat: `filter`), status, createdDateFrom (DDMMYYYY o DD.MM.YYYY), createdDateTo (DDMMYYYY o DD.MM.YYYY), currencyCode, gastoType, processedByAI (bool).
  Nota enums AX: `status` y `gastoType` deben enviarse como valores numericos obtenidos desde `/api/crm/enums`.
  Nota: `createdDateFrom/createdDateTo` son opcionales; si ambos llegan, se valida `from <= to`. La fecha de referencia de filtros y respuesta es `ticketHeader.createdDate`.
  Response items: `FileId`, `Description`, `Status`, `ProcessedByAI`, `CurrencyCode`, `TotalAmount`, `TotalAmountCurrency`, `TotalAmountMST`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `GastoType`.
  Nota: `TotalAmount` se mantiene como alias legacy de `TotalAmountCurrency`.
- POST /api/crm/expensesheets/tickets/link/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: page, pageSize.
  Body optional: searchKey (compat: `filter`), createdDateFrom (DDMMYYYY o DD.MM.YYYY), createdDateTo (DDMMYYYY o DD.MM.YYYY), currencyCode, gastoType, processedByAI (bool).
  Nota enums AX: `gastoType` debe enviarse como valor numerico obtenido desde `/api/crm/enums`.
  Prefiltros fijos en origen: estado pendiente interno de AX y `totalAmount != 0`.
  Nota: la fecha de referencia de filtros y respuesta es `ticketHeader.createdDate`.
  Response items: `FileId`, `Description`, `CurrencyCode`, `TotalAmount`, `TotalAmountCurrency`, `TotalAmountMST`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `ProcessedByAI`, `GastoType`.
  Nota: `TotalAmount` se mantiene como alias legacy de `TotalAmountCurrency`.
- POST /api/crm/expensesheets/tickets/link/bulk (Authorize + X-IND-Company + X-IND-AxUserId)
  Body legacy soportado: `expenseSheetId`, `ticketIds[]` (equivale a `selectionMode = selected`).
  Body ampliado:
  - `expenseSheetId` obligatorio
  - `selectionMode` opcional (`selected` por defecto, `filtered`)
  - `ticketIds[]` obligatorio en `selected`
  - `filters` obligatorio en `filtered`: `searchKey` (compat: `filter`), `createdDateFrom`, `createdDateTo`, `currencyCode`, `gastoType`, `processedByAI`
  - `excludedIds[]` opcional en `filtered`
  En `filtered` reutiliza la misma resolucion server-side que `tickets/link/list`, con prefiltros base de estado pendiente interno de AX y `totalAmount != 0`.
  Reutiliza `createExpenseSheet` en modo `2` para anadir una linea por ticket a una hoja existente, usando el `projId` de la hoja destino para la linea generada.
  Valida hoja destino, permisos, editabilidad y deduplicacion, y soporta resultado parcial.
  Response data: `expenseSheetId`, `requestedCount`, `linkedCount`, `skippedCount`, `failedCount`, `linkedTicketIds`, `skipped[]`, `failed[]`.
- PUT /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza cabecera y DocuRef (description, currencyCode, gastoType, totalAmount, amountMST, exchRate, status, transDate (DDMMYYYY o DD.MM.YYYY), ticketDate (DDMMYYYY o DD.MM.YYYY), ticketTime (HH:mm, HH:mm:ss o segundos 0..86399), comentario, urlFile, fileName, fileExtension, processedByAI, ocrJson, normalizedJson).
  Puede responder 409 con `CRM_EXPENSESHEET_TICKET_DUPLICATE` si la fecha y hora informadas ya existen para otro ticket del mismo usuario.
  Nota: si el ticket vinculado esta en la misma divisa de reembolso de la hoja, editar `amountMST` conserva `exchRate`; si la divisa difiere, AX recalcula `exchRate` con `totalAmount * 100 / amountMST`.
  Response data incluye `TotalAmount`, `TotalAmountCurrency`, `AmountMST`, `TotalAmountMST` y `ExchRate`; `TotalAmount`/`AmountMST` quedan como aliases legacy.
- POST /api/crm/expensesheets/tickets/{fileId}/total-adjustment (Authorize + X-IND-Company + X-IND-AxUserId)
  Ajusta `INDTicketInfoTable.TotalAmount` y crea una linea diferencial en `INDTicketInfoLine` cuando cambia el importe.
  Body required: `totalAmount` (nuevo total de cabecera, mayor o igual que 0).
  AX calcula `differenceAmount = totalAmount nuevo - TotalAmount anterior`; la diferencia puede ser positiva, negativa o cero.
  Si hay diferencia, la linea se crea o recalcula con description fija `AJUSTE DE IMPORTE TOTAL`, `Qty = 1`, `Price = differenceAmount`, `TotalAmount = differenceAmount` y `Adjustment = Yes` en AX, expuesto como `AdjustmentAmount`.
  Si ya existia linea de ajuste y un cambio posterior deja la diferencia entre cabecera y suma de lineas normales en `0`, AX elimina la linea `Adjustment`; en ese caso `AdjustmentLineRecId` vuelve vacio/0 y `AdjustmentAmount` se devuelve como `false`.
  Response data: `FileId`, `PreviousTotalAmount`, `NewTotalAmount`, `TotalAmountCurrency`, `TotalAmountMST`, `DifferenceAmount`, `AdjustmentLineRecId`, `AdjustmentLineCreated`, `AdjustmentDescription`, `AdjustmentAmount`.
- POST /api/crm/expensesheets/tickets/{fileId}/ia (Authorize + X-IND-Company + X-IND-AxUserId)
  Reemplaza cabecera + lineas del ticket con datos de IA.
  Puede responder 409 con `CRM_EXPENSESHEET_TICKET_DUPLICATE` si la fecha y hora detectadas ya existen para otro ticket del mismo usuario.
  Reglas:
  - Reemplazo total de lineas (delete + insert).
  - Marca `processedByAI=true`.
  - Usa metodo AX atomico `updateExpenseSheetTicketFromIA`.
  - Compatibilidad de entrada: si llega envelope tipo `expensefromticket` (`{ Success, Message, Data, TraceId }`), el backend adapta automaticamente `Data` al contrato esperado.
  Body: `description`, `currencyCode`, `gastoType` (opcional), `totalAmount` (opcional), `amountMST` (opcional), `exchRate` (opcional), `transDate` (DDMMYYYY o DD.MM.YYYY), `ticketDate` (DDMMYYYY o DD.MM.YYYY, opcional), `ticketTime` (HH:mm, HH:mm:ss o segundos 0..86399, opcional), `comentario` (opcional), `urlFile`, `fileName` (opcional), `ocrJson` (opcional), `normalizedJson` (opcional), `fileExtension` (opcional), `lines[]`. Las lineas pueden llevar importes negativos como descuento; si `qty = 0`, el total de linea debe ser negativo. Si `currencyCode` no es EUR y no llegan `amountMST` ni `exchRate`, la API calcula automaticamente `amountMST` con el tipo `currencyCode -> EUR` para la fecha del ticket.
  Response data incluye `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`; `TotalAmount` se mantiene como alias legacy de `TotalAmountCurrency`.
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
  Response data incluye el total recalculado de cabecera como `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.
- PUT /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza una linea granular de `INDTicketInfoLine`.
  Body: `description`, `qty`, `price`, `totalAmount` opcional.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas temporales.
  Response data incluye el total recalculado de cabecera como `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.
- DELETE /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina una linea granular de `INDTicketInfoLine`.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para lineas temporales.
  Response data incluye el total recalculado de cabecera como `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.

## Activities / Visits
- GET /api/crm/data-visibility/visible-users?appCode=CRM&moduleCode=VISITAS_GESTION&includeCrmUserId=true (Authorize + X-IND-Company + X-IND-AxUserId)
  Query optional: `appCode` default `CRM`, `moduleCode` default `VISITAS_GESTION`, `asOfDate` (yyyyMMdd o yyyy-MM-dd), `includeCrmUserId` default `true`.
  AX resuelve usuarios visibles con `INDControlDataVisibility`; no usa subordinados legacy de Hojas de gastos.
  Personas visibles sin usuario AX no se devuelven en la lista.
  Response rows incluyen `Alias`, `AxUserId`, `CrmUserId`, `Name`, `Source`, `MutationPolicy`, `MutationPolicyInt`, `MutationPolicyLabel` y `CanMutate`.
  `CanMutate` gobierna update/delete sobre registros del propietario visible; create sigue usando siempre el `X-IND-AxUserId` del actor y no debe crear en nombre de subordinados.
- POST /api/crm/activities/create (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `accountNum`, `visitType` (valor numerico AX de `CRMTipoVisita`), `description`, `transDate` (yyyyMMdd o yyyy-MM-dd).
  Body optional: `contactMethod` (valor numerico AX de `INDContactMethod`), `comentarios`, `antecedentes`, `conclusiones`, `userId`, `createdByUserId`.
  Nota: `userId` y `createdByUserId` del body no gobiernan el actor; API usa siempre `X-IND-AxUserId`.
  Si `contactMethod` no se envia, AX recibe el valor numerico por defecto historico `0`.
  Response data incluye `RecId`, `OwnerAxUserId`, `INDCreatedByUserId`, `CreatedByUserId` y `UserId`; todos los campos de owner se derivan del usuario AX del header cuando la creacion es exitosa.
- POST /api/crm/activities/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `fromDate`, `toDate` (yyyyMMdd o yyyy-MM-dd), `page`, `pageSize`.
  Body optional: `accountNum`, `ownerAxUserId`.
  AX filtra los propietarios visibles con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
  Si `ownerAxUserId` se envia, AX devuelve solo visitas de ese propietario siempre que este dentro del set visible del usuario de header.
  Response rows incluyen `ActividadId`, `RecId`, `Name`, `AccountNum`, `TransDate`, `ActividadType`, `TipoVisita`, `ContactMethod` y `Description`.
- GET /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  AX valida lectura con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
  Response rows incluyen el detalle de visita con `ContactMethod`, `OwnerAxUserId`, `OwnerName` y alias compatibles `INDCreatedByUserId`, `CreatedByUserId`, `UserId`.
  Nota: `OwnerAxUserId` es el propietario funcional AX canonico de la actividad.
- GET /api/crm/activities/by-code/{code} (Authorize + X-IND-Company + X-IND-AxUserId)
  AX valida lectura con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
  Response `Items[0]` es `ActivityDetailDto` e incluye `ContactMethod`, `OwnerAxUserId`, `OwnerName` y alias compatibles `INDCreatedByUserId`, `CreatedByUserId`, `UserId`.
- PUT /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `accountNum`, `visitType` (valor numerico AX de `CRMTipoVisita`), `description`, `transDate` (yyyyMMdd o yyyy-MM-dd).
  Body optional: `contactMethod` (valor numerico AX de `INDContactMethod`), `comentarios`, `antecedentes`, `conclusiones`, `userId`.
  Nota: `userId` del body no gobierna el actor; API usa siempre `X-IND-AxUserId`.
  AX valida modificacion con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- DELETE /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  AX valida modificacion con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- POST /api/crm/visits/createVisitaAsistente (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `refRecIdActividad`, `asistenteTipo` (valor numerico AX de `CRMCustVendVisitaAsistente`), `asistenteId`, `contactoRecId`.
  Body optional: `createdByUserId`; si se envia distinto del header, API lo ignora y usa `X-IND-AxUserId`.
  AX valida modificacion de la visita con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- DELETE /api/crm/visits/deleteVisitaAsistente (Authorize + X-IND-Company + X-IND-AxUserId)
  Body required: `refRecIdActividad`, `asistenteId`.
  AX valida modificacion de la visita con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.

## Projects
- GET /api/crm/projects/list?filter=...&page=1&pageSize=50 (Authorize + X-IND-Company)
  Nota: page y pageSize son obligatorios. Si no hay filtro, AX devuelve lista vacia.
