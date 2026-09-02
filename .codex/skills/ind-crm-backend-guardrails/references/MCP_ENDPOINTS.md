# Endpoints MCP de IND_CRM_API

Precedencia: `.codex/ENDPOINTS.md` define el contrato HTTP y `.codex/MCP_TOOLS.json`, las herramientas y sus esquemas JSON. Este documento es una guía humana: resume ambos contratos, pero no redefine ninguno.
Objetivo: documentación detallada para exponer la API mediante herramientas MCP con esquemas JSON.

## Convenciones globales

- URL base: `{{baseUrl}}`. Las URLs vigentes por entorno se mantienen en `docs/operations/configuracion-entornos.md`, ruta relativa a la raiz del repositorio.
- Variables: `baseUrl`, `tokenId`, `companyId`, `axUserId`, `entraOid`, `contextVersion`, `permissionsRevision`, `contextToken`.
- Autenticación: `Authorization: Bearer {{tokenId}}`.
- Cabecera de empresa: `X-IND-Company: {{companyId}}` (obligatoria en endpoints CRM).
- Contexto firmado: toda ruta `/api/crm/*` que ejecuta `RequireCompanyOrReturn422` exige además `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision` y `X-IND-Context-Token`.
- Cabecera de usuario AX: `X-IND-AxUserId: {{axUserId}}` solo cuando la herramienta lo declara. Puede identificar al sujeto funcional o propietario enviado a AX, pero no reemplaza al actor del contexto firmado.
- `axUserId` y los campos del contexto firmado se obtienen de `/api/auth/entra/context`; cada herramienta declara cuáles necesita.
- Fechas en tickets y hojas de gastos: la petición acepta `DDMMYYYY` o `DD.MM.YYYY`; la respuesta devuelve `DD.MM.YYYY`.

## Catálogo MCP

- Archivo canónico de herramientas: `.codex/MCP_TOOLS.json`.
- Incluye `inputSchema` por herramienta y el mapeo HTTP en `x-http`.

## Endpoints

## Autenticación

### Tool: auth_login
- HTTP: POST `/api/auth/login`
- Autenticación: `AllowAnonymous`.
- Cabeceras: `Content-Type: application/json`.
- Cuerpo: `Username`, `Password`.
- Respuesta: `IndApiResponse<{ token, expires }>`

### Tool: auth_refresh
- HTTP: POST `/api/auth/refresh`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.
- Respuesta: `IndApiResponse<{ token, expires }>`

### Tool: auth_entra_context
- HTTP: POST `/api/auth/entra/context`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `Content-Type: application/json`.
- Cuerpo: `entraOid`, `appCode`.
- Respuesta: `IndApiResponse<{ header, items }>`
- Notas: de aquí se obtienen `companyId`, `axUserId`, `header.defaultCurrencyCode`, `companies[].currencyCode`, `companies[].allowSelfManagement` y `companies[].crmUserId`.

## Salud

### Tool: health_ping
- HTTP: GET `/api/health/ping`
- Autenticación: `AllowAnonymous`.

### Tool: health_health
- HTTP: GET `/api/health/health`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.

## Sistema

### Tool: system_get_environment_name
- HTTP: GET `/api/system/getEnvironmentName`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.

### Tool: system_get_company_name
- HTTP: GET `/api/system/getCompanyName`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.

### Tool: system_exchange_rate
- HTTP: GET `/api/system/exchange-rate`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.
- Consulta: `baseCurrency`, `targetCurrency`, `date` (opcional).
- Códigos de error: `VALIDATION_ERROR`, `EXCHANGE_RATE_NOT_FOUND` (heredado), `RATE_UNAVAILABLE`, `INTERNAL_ERROR`.
- Notas internas:
  - Proveedor primario: ECB.
  - Alternativa de nivel 2: Frankfurter (`https://api.frankfurter.app/latest?from={BASE}&to={TARGET}`).
  - Alternativa de nivel 3: OpenErApi (`https://open.er-api.com/v6/latest/{BASE}`).
  - Valores de respuesta en `Source` (texto de interfaz):
    - `Banco Central Europeo (ECB)`
    - `Frankfurter API (fallback nivel 2)`
    - `Open ER API (fallback nivel 3)`
  - OpenErApi usa solo el último valor disponible; si se solicita una fecha distinta de hoy, consulta igualmente ese valor.
  - Contrato MCP/público sin cambios: se mantienen el mismo envoltorio y la misma estructura de respuesta.
  - Caché: `MemoryCache` durante 24 h por `base|target|date` (solo resultados correctos).

### Tool: system_exchange_rate_public_direct
- HTTP: GET `/api/system/exchange-rate/public-direct`
- Autenticación: `AllowAnonymous`.
- Consulta: `baseCurrency`, `targetCurrency`, `date` (opcional).
- Endpoint de consumo recomendado: `{{baseUrl}}/api/system/exchange-rate/public-direct?baseCurrency=AED&targetCurrency=EUR`
- Códigos de error: `VALIDATION_ERROR`, `EXCHANGE_RATE_NOT_FOUND` (heredado), `RATE_UNAVAILABLE`, `INTERNAL_ERROR`.

## MCP

### Tool: mcp_tools
- HTTP: GET `/api/mcp/tools`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.

## Servicios de IA

### Tool: speech_transcribe
- HTTP: POST `/api/ia/service/speech`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.
- Cuerpo multipart: `languageId`, `audioFile`, `temperature` (opcional), `prompt` (opcional).

### Tool: expensefromticket_draft
- HTTP: POST `/api/ia/service/expensefromticket`
- Autenticación: token bearer.
- Cabeceras: `Authorization`.
- Cuando `persistTicket=true`, además de `Authorization`, son obligatorias `X-IND-Company` y `X-IND-AxUserId`, junto con las cuatro cabeceras de contexto firmado: `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision` y `X-IND-Context-Token`.
- Cuerpo multipart: `ticketImage`, `persistTicket` (opcional), `ticketUrlFile` (opcional; si no viene, se usa una URL temporal).
- Respuesta: `Data` incluye `gastoType`, `ticketDate`, `ticketTime` y `lines[].typeValue` por línea. `transDate` se mantiene por compatibilidad y coincide con `ticketDate` cuando se detecta la fecha del ticket.
- Respuesta: con `persistTicket=true`, `Data.TicketCreation.ProcessedByAI` debe ser `true`.

## Hojas de gastos

### Tool: crm_expensesheets_currencies
- HTTP: GET `/api/crm/expensesheets/currencies`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`.

### Tool: crm_expensesheets_create
- HTTP: POST `/api/crm/expensesheets`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo:
  - `mode` (0|1|2)
  - `existingHojaGastosId` (requerido cuando `mode=2`)
  - `description` (requerido cuando `mode=0|1`); `currencyCode` es opcional en los tres modos y solo actúa como valor heredado predeterminado para líneas nuevas.
  - `lines` (requerido cuando `mode=0|2`)
  - `lines[].transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `typeValue`, `description`, `qty`, `price`
  - Opcionales: `projId`, `exchRate`, `expenseSheetStatus`, `exchangeRateMode`, `reimbursableExpense`, `lines[].projId`, `lines[].projIdProvided`, `lines[].internacional`, `lines[].fileId`, `lines[].reimbursableExpense`, `lines[].currencyCode`, `lines[].amountMST`, `lines[].exchRate`
  - `lines[].projIdProvided=true` conserva un proyecto explícito, incluido `""`; `false` u omitido sin `lines[].projId` delega en `defaultProjectForNewLine` de AX. Ese valor predeterminado usa solo el proyecto elegible de cabecera; una cabecera vacía, con `PurchParameters.INDProjIdVarious` o con un proyecto inelegible deja la línea sin proyecto. Si no se envía el indicador, la presencia de `lines[].projId` mantiene compatibilidad con clientes anteriores.
  - `reimbursableExpense`: en escritura de cabecera, el enum AX `INDReimbursableExpense` solo admite `0 Yes` y `1 No`; `2 Both` se deriva de líneas mixtas y solo se conserva en respuestas y filtros. En `lines[]`, `INDReimbursableExpenseLines` admite solo `0 Yes` y `1 No`; el valor predeterminado es `Yes`.

### Tool: crm_expensesheets_fuel_price_km
- HTTP: GET `/api/crm/expensesheets/fuel-price-km`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Consulta: `transDate` (`DDMMYYYY` o `DD.MM.YYYY`, opcional).

### Tool: crm_expensesheets_get
- HTTP: GET `/api/crm/expensesheets/{hojaGastosId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`.
- Autorización: el usuario AX que consulta se obtiene del contexto firmado. La herramienta no acepta `X-IND-AxUserId` como identidad del solicitante.
- La cabecera de respuesta incluye: `userId`, `userName`, `expenseSheetStatus`, `estadoComentarios`, `exchangeRateMode`, `createdDate`, `axCreatedDate`, `reimbursableExpense`, `totalAmountCurrency`, `totalAmountMST`, `totalGrossAmountMST`, `totalReimbursableAmount`, `defaultLineProjId`.
- `defaultLineProjId` es el proyecto elegible de cabecera para una nueva línea. Queda vacío con una cabecera vacía, con `PurchParameters.INDProjIdVarious` o con un proyecto inelegible, y queda `null` con contratos AX anteriores a la posición 21.
- Nota sobre totales: `totalAmountCurrency`/`totalAmount` y `totalAmountMST` conservan los totales contables heredados. `totalGrossAmountMST` es el bruto de empresa/MST y no se filtra por reembolso ni Visa; `totalReimbursableAmount` es el reembolso de empresa/MST, incluye solo `ReimbursableExpense=Yes`, no consulta Visa y usa `totalAmountMST` como alternativa con AX heredado.
- Nota JSON: Web API serializa las propiedades en PascalCase; los consumidores JavaScript deben usar `TotalGrossAmountMST`, `TotalReimbursableAmount` y `ReimbursableAmount`.
- Nota: `userName` es `CRMUsuarioTable.Name` del propietario CRM de la hoja.
- Nota AX: `axCreatedDate` expone la fecha final adicional devuelta por AX y normalmente refleja `createdDate`.
- La respuesta de líneas incluye: `price`, `qty`, `amount`, `projId`, `reimbursableExpense`, `currencyCode`, `amountMST`, `reimbursableAmount`, `exchRate`.
- Nota sobre líneas: `amount` es el total en divisa original, `amountMST` es el total de empresa/MST y `reimbursableAmount` es el importe reembolsable de empresa/MST; copia `amountMST` con `ReimbursableExpense=Yes` y vale cero con `ReimbursableExpense=No`, independientemente de Visa; queda nulo con AX heredado. Visa queda bloqueado como espejo inverso de compatibilidad.
- Nota de enrutamiento: `hojaGastosId` excluye el literal `tickets` para evitar una colisión con `/api/crm/expensesheets/tickets`.

### Tool: crm_expensesheets_update_header
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `description`; opcionales: `currencyCode` (compatibilidad heredada), `projId`, `projIdProvided`, `exchRate`, `expenseSheetStatus`, `exchangeRateMode`, `estadoComentarios`, `reimbursableExpense` (`0 Yes` incluye o `1 No` excluye; `2 Both` no se admite en escritura).
- Proyecto: `projIdProvided=false` conserva el proyecto bajo el bloqueo de cabecera de AX; `true` aplica `projId`, incluido `""`. Todo valor no vacío debe ser un proyecto elegible: AX rechaza `PurchParameters.INDProjIdVarious`, proyectos inexistentes, cerrados o no imputables. Si se omite el indicador, un `projId` no nulo se considera explícito para mantener clientes anteriores.
- Regla: si se envía `estadoComentarios`, también se deben enviar `expenseSheetStatus` y `exchangeRateMode`.
- Nota: no propaga cambios a líneas; usar los endpoints explícitos de propagación.
- Nota: la divisa de cabecera permanece local y no se convierte en un marcador multimoneda. AX agrega `ProjIdHornos` y el estado de reembolso de todas las líneas: si coinciden, adopta el valor común; si difieren, incluso por un proyecto vacío, usa `PurchParameters.INDProjIdVarious` o `INDReimbursableExpense::Both`. Una diferencia aislada entre `ProjId` y `ProjIdHornos` no activa el marcador de proyecto.

### Tool: crm_expensesheets_propagate_currency_defaults
- HTTP: POST `/api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Consulta: `recalculateAmountMST` opcional, valor predeterminado `true`; `force` opcional, valor predeterminado `false`.
- Compatibilidad sin efecto: conserva la ruta y su respuesta, pero AX no modifica la divisa, el tipo de cambio ni `amountMST` de las líneas.
- `recalculateAmountMST` y `force` se conservan como parámetros compatibles; AX devuelve cero líneas actualizadas.
- Enrutamiento: `hojaGastosId` excluye el literal `tickets`.

### Tool: crm_expensesheets_propagate_project_default
- HTTP: POST `/api/crm/expensesheets/{hojaGastosId}/project-default/propagate`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo opcional: `{ "projId": "PROYECTO", "projIdProvided": true }`. Un objetivo explícito, incluido `""`, se aplica atómicamente a cabecera y líneas. Un `projId` presente sin indicador también es explícito por compatibilidad; sin objetivo o con `projIdProvided=false`, se usa el proyecto ya guardado en cabecera.
- AX bloquea el marcador `PurchParameters.INDProjIdVarious`, la ausencia de proyecto en modo heredado o una hoja con `Voucher`.
- Enrutamiento: `hojaGastosId` excluye el literal `tickets`.

### Tool: crm_expensesheets_propagate_reimbursable_expense
- HTTP: POST `/api/crm/expensesheets/{hojaGastosId}/reimbursable-expense/propagate`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Propaga `reimbursableExpense` de cabecera a líneas existentes (`Yes` incluye y copia `AmountMST`; `No` excluye y deja el importe en cero).
- AX bloquea si `reimbursableExpense` de cabecera es `Both` o si la hoja tiene `Voucher`.
- Enrutamiento: `hojaGastosId` excluye el literal `tickets`.

### Tool: crm_expensesheets_update_line
- HTTP: PUT `/api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `typeValue`, `description`, `qty`, `price`, `internacional` (opcional), `fileId` (opcional), `projId` (opcional), `projIdProvided` (opcional), `reimbursableExpense` (opcional), `currencyCode` (opcional), `amountMST` (opcional), `exchRate` (opcional).
- Proyecto: `projIdProvided=false` conserva el proyecto actual de la línea; `true` aplica `projId`, incluido `""`. Si se omite el indicador, se conserva la semántica heredada: un `projId` no vacío es explícito; sin valor usa solo el proyecto elegible de cabecera. Una cabecera vacía, con `PurchParameters.INDProjIdVarious` o inelegible conserva el proyecto actual de la línea.
- Nota: si `currencyCode` de línea difiere de la divisa de reembolso de cabecera, enviar `exchRate` o `amountMST`; AX no reutiliza la tasa de cabecera para otra divisa. Si ambas divisas coinciden, editar `amountMST` no recalcula `exchRate`.
- Nota: `reimbursableExpense` de línea admite `0 Yes` para incluir `AmountMST` y `1 No` para excluirlo. AX adopta el valor común cuando todas las líneas coinciden y marca la cabecera como `Both` cuando existen líneas `Yes` y `No`.

### Tool: crm_expensesheets_delete_line
- HTTP: DELETE `/api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Consulta:
  - `deleteMode` (0=LineOnly, 1=HeaderOnly alias de WholeSheet, 2=WholeSheet)
  - `deleteWholeSheet` (heredado)
- Nota: AX usa `deleteExpenseSheetLine` con el indicador `deleteWholeSheet`; `HeaderOnly` y `WholeSheet` se procesan igual.

### Tool: crm_expensesheets_list
- HTTP: POST `/api/crm/expensesheets/list`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `page`, `pageSize`, `filter` (opcional), `billedMode` (opcional), `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`, opcional), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`, opcional), `projId` (opcional), `currencyCode` (opcional), `expenseSheetStatus` (opcional), `includeSubordinates` (booleano opcional; `true` = usuario de cabecera y subordinados directos).
- El cuerpo también acepta `reimbursableExpense` (opcional, valor numérico AX de `INDReimbursableExpense`: `0 Yes` incluye, `1 No` excluye, `2 Both` mezcla).
- La respuesta por elemento incluye: `expenseSheetStatus`, `estadoComentarios`, `exchangeRateMode`, `userId`, `userName`, `exchRate`, `createdDate`, `axCreatedDate`, `reimbursableExpense`, `totalAmountCurrency`, `totalAmountMST`, `totalGrossAmountMST` y `totalReimbursableAmount`.
- Nota sobre totales: los dos primeros totales conservan la semántica contable heredada; el bruto es de empresa/MST y no se filtra por reembolso ni Visa, mientras el reembolso explícito incluye solo `ReimbursableExpense=Yes` y no consulta Visa. Con AX heredado, el reembolso usa `totalAmountMST` y el bruto queda nulo.
- Nota JSON: Web API serializa las propiedades en PascalCase; los consumidores JavaScript deben usar `TotalGrossAmountMST` y `TotalReimbursableAmount`.

## Tickets de hojas de gastos

### Tool: crm_expensesheets_tickets_create
- HTTP: POST `/api/crm/expensesheets/tickets`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo:
  - `mode` (0|1|2)
  - `existingFileId` (requerido cuando `mode=2`)
  - `description`, `currencyCode`, `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `urlFile` (requeridos cuando `mode=0|1`)
  - `totalAmount`, `comentario`, `fileExtension`, `gastoType`, `ocrJson`, `normalizedJson`, `ticketDate`, `ticketTime` (opcionales; `gastoType` permitido: 0,1,2,3,4,5,6,7,8,14; `ticketTime` acepta HH:mm, HH:mm:ss o segundos 0..86399)
  - `lines[]` con `description`, `qty`, `price`, `totalAmount` (líneas requeridas cuando `mode=0|2`; `price`/`totalAmount` pueden ser negativos; `qty` no puede ser negativo y `qty = 0` solo se acepta con un total negativo).

### Endpoint no expuesto como tool MCP: crm_expensesheets_tickets_quick_create
- HTTP: POST `/api/crm/expensesheets/tickets/quick-create`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: multipart/form-data`.
- Cuerpo multipart:
  - Requerido: `ticketImage` (jpg/jpeg/png/webp, máximo 50 MB).
  - Opcional: `currencyCode`, `description`, `comentario`, `existingHojaGastosId`, `projId` (alias heredado: `projectId`)
- Flujo: crea un ticket provisional, sube el archivo, ejecuta OCR y normalización IA, finaliza el ticket y, opcionalmente, lo vincula a una hoja existente.
- Si se produce un error después de crear `FileId`, intenta la reversión interna del blob y del ticket AX.
- Datos de respuesta: `FileId`, `UrlFile`, `FileName`, `ProcessedByAI`, `LinkedToSheet`, `HojaGastosId`, `CompletedStage`, `FailedStage`, `RollbackAttempted`, `RollbackSucceeded`, `RollbackMessage`, `StepTraceIds`.
- Errores relevantes: 422 por validación, 429 por límite de IA, 503 por indisponibilidad del servicio de IA y 500 por error interno.

### Tool: crm_expensesheets_tickets_get
- HTTP: GET `/api/crm/expensesheets/tickets/{fileId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Respuesta: cabecera incluye `processedByAI`, `gastoType`, `hojaGastosIdDisplay`, `ocrJson`, `normalizedJson`, `ticketDate` y `ticketTime`.
- Respuesta `Lines[*]`: incluye `ReimbursableExpense` (`int?`, `0=Yes`, `1=No`) y `ReimbursableAmount` (`decimal?`, divisa de la empresa), procedentes de la `CRMHojaGastosLine` vinculada. Ambos son `null` con AX heredado o sin una vinculación única. Se repiten en todas las líneas como metadatos no sumables de la misma línea de hoja; no representan importes individuales del ticket.

### Tool: crm_expensesheets_tickets_list
- HTTP: POST `/api/crm/expensesheets/tickets/list`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo:
  - Requerido: `page`, `pageSize`
  - Opcional: `searchKey` (compatibilidad: `filter`), `status` (0|1), `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`), `currencyCode`, `gastoType` (0,1,2,3,4,5,6,7,8,14), `processedByAI` (bool)
  - Regla: para ejecutar la consulta siempre deben viajar `X-IND-Company` y `X-IND-AxUserId`; el rango de fechas es opcional y usa `ticketHeader.createdDate`.
- Respuesta: cada item incluye `FileId`, `Description`, `Status`, `ProcessedByAI`, `CurrencyCode`, `TotalAmount`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `GastoType`.

### Endpoint no expuesto como tool MCP: crm_expensesheets_tickets_link_list
- HTTP: POST `/api/crm/expensesheets/tickets/link/list`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo:
  - Requerido: `page`, `pageSize`
  - Opcional: `searchKey` (compatibilidad: `filter`), `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`), `currencyCode`, `gastoType` (0,1,2,3,4,5,6,7,8,14), `processedByAI` (bool)
  - Regla: el origen sale prefiltrado con `status = Pending` y `totalAmount != 0`; la fecha de referencia es `ticketHeader.createdDate`.
- Respuesta: cada item incluye `FileId`, `Description`, `CurrencyCode`, `TotalAmount`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `ProcessedByAI`, `GastoType`.

### Endpoint no expuesto como tool MCP: crm_expensesheets_tickets_link_bulk
- HTTP: POST `/api/crm/expensesheets/tickets/link/bulk`
  - Autenticación: token bearer.
  - Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
  - Cuerpo:
    - Contrato heredado admitido: `expenseSheetId`, `ticketIds[]` (`selectionMode = selected`).
    - Requerido: `expenseSheetId`
    - Opcional: `selectionMode` (`selected` por defecto, `filtered`)
    - En `selected`: `ticketIds[]` obligatorio
    - En `filtered`: `filters` obligatorio (`searchKey`, `filter`, `createdDateFrom`, `createdDateTo`, `currencyCode`, `gastoType`, `processedByAI`) y `excludedIds[]` opcional
    - Regla: en `filtered` reutiliza la misma resolución en el servidor que `tickets/link/list`; la vinculación final reutiliza `createExpenseSheet` en modo `2`, usa el proyecto de cabecera solo cuando es elegible (en otro caso deja la línea sin proyecto) y admite un resultado parcial.
  - Datos de respuesta: `expenseSheetId`, `requestedCount`, `linkedCount`, `skippedCount`, `failedCount`, `linkedTicketIds`, `skipped[]`, `failed[]`.

### Tool: crm_expensesheets_tickets_update
- HTTP: PUT `/api/crm/expensesheets/tickets/{fileId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo opcional: `description`, `currencyCode`, `gastoType`, `totalAmount`, `amountMST`, `exchRate`, `status` (0|1), `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `ticketDate` (`DDMMYYYY` o `DD.MM.YYYY`), `ticketTime` (`HH:mm`, `HH:mm:ss` o segundos 0..86399), `comentario`, `urlFile`, `fileName`, `fileExtension`, `processedByAI`, `ocrJson`, `normalizedJson`.
- Nota: si el ticket vinculado está en la misma divisa de reembolso de la hoja, editar `amountMST` conserva `exchRate`; si la divisa difiere, AX recalcula `exchRate` con `totalAmount * 100 / amountMST`.

### Endpoint no expuesto como tool MCP: crm_expensesheets_tickets_apply_ia
- HTTP: POST `/api/crm/expensesheets/tickets/{fileId}/ia`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo:
  - `description`, `currencyCode`, `gastoType`, `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `ticketDate` (`DDMMYYYY` o `DD.MM.YYYY`, opcional), `ticketTime` (`HH:mm`, `HH:mm:ss` o segundos 0..86399, opcional), `urlFile` (se completan desde el ticket actual si no se envían).
  - `totalAmount` (opcional)
  - `comentario` (opcional)
  - `fileName` (opcional), `ocrJson` (opcional), `normalizedJson` (opcional), `fileExtension` (opcional si no hay `fileName`)
  - `lines[]` obligatorio con `description`, `qty`, `price`, `totalAmount` (opcional). Las líneas pueden llevar importes negativos como descuento; si `qty = 0`, el total de línea debe ser negativo.
- Regla: reemplazo total del detalle de líneas (borrado e inserción) y `processedByAI=true`.
- Compatibilidad: acepta el cuerpo directo del contrato IA o el envoltorio de `expensefromticket` (`Success/Message/Data/TraceId`) y mapea `Data` de forma interna.

### Tool: crm_expensesheets_tickets_upload_file
- HTTP: POST `/api/crm/expensesheets/tickets/{fileId}/file`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: multipart/form-data`.
- Consulta opcional: `extension` (si no viene, usa la extensión del archivo y `jpg` como alternativa).
- Cuerpo multipart: un archivo (primer archivo del cuerpo).
- Regla: nombre final `yyyyMMddHHmmss_{axUserId}_{fileId}.{ext}`

### Tool: crm_expensesheets_tickets_delete_file
- HTTP: DELETE `/api/crm/expensesheets/tickets/{fileId}/file`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.

### Tool: crm_expensesheets_tickets_delete
- HTTP: DELETE `/api/crm/expensesheets/tickets/{fileId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Consulta opcional: `lineRecId` (si se envía, elimina solo esa línea mediante el método unificado).

### Tool: crm_expensesheets_tickets_create_line
- HTTP: POST `/api/crm/expensesheets/tickets/{fileId}/lines`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `description`, `qty`, `price`, `totalAmount` (opcional).

### Tool: crm_expensesheets_tickets_update_line
- HTTP: PUT `/api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `description`, `qty`, `price`, `totalAmount` (opcional).

### Tool: crm_expensesheets_tickets_delete_line
- HTTP: DELETE `/api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.

## Actividades / Visitas

### Endpoint no expuesto como tool MCP: crm_data_visibility_visible_users
- HTTP: GET `/api/crm/data-visibility/visible-users`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Consulta: `appCode` opcional (valor predeterminado `CRM`), `moduleCode` opcional (valor predeterminado `VISITAS_GESTION`), `asOfDate` opcional (`yyyyMMdd` o `yyyy-MM-dd`), `includeCrmUserId` opcional (valor predeterminado `true`).
- Uso: lista los usuarios AX visibles para el usuario actual mediante `INDControlDataVisibility`.
- Nota: las personas visibles sin `INDPersonaTable.UserId` no se devuelven. No usa la jerarquía heredada de Hojas de gastos.
- Respuesta: filas con `Alias`, `AxUserId`, `CrmUserId`, `Name`, `Source`, `MutationPolicy`, `MutationPolicyInt`, `MutationPolicyLabel` y `CanMutate`.
- `CanMutate` se aplica a la actualización o eliminación de registros del propietario visible. Las visitas no se crean en nombre de subordinados.

### Endpoint no expuesto como tool MCP: crm_activities_create
- HTTP: POST `/api/crm/activities/create`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `accountNum`, `visitType`, `description`, `transDate` (`yyyyMMdd` o `yyyy-MM-dd`), `contactMethod` opcional (`0` InPerson, `1` PhoneCall, `2` OnlineMeeting), `comentarios`, `antecedentes`, `conclusiones`.

### Endpoint no expuesto como tool MCP: crm_activities_list
- HTTP: POST `/api/crm/activities/list`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `fromDate`, `toDate`, `accountNum` opcional, `ownerAxUserId` opcional, `page`, `pageSize`.
- Visibilidad: AX filtra por el conjunto visible de `CRM / VISITAS_GESTION`; si se envía `ownerAxUserId`, solo reduce el resultado a ese usuario visible.
- Respuesta: filas con `ActividadId`, `RecId`, `Name`, `AccountNum`, `TransDate`, `ActividadType`, `TipoVisita`, `ContactMethod` y `Description`.

### Endpoint no expuesto como tool MCP: crm_activities_get_by_recid
- HTTP: GET `/api/crm/activities/{recId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Visibilidad: AX valida lectura con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- Respuesta: `ActivityDetailDto` dentro de `Items[0]`, incluido `ContactMethod`.

### Endpoint no expuesto como tool MCP: crm_activities_get_by_code
- HTTP: GET `/api/crm/activities/by-code/{code}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Visibilidad: AX valida lectura con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- Respuesta: `ActivityDetailDto` dentro de `Items[0]`, incluido `ContactMethod`.

### Endpoint no expuesto como tool MCP: crm_activities_update
- HTTP: PUT `/api/crm/activities/{recId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `accountNum`, `visitType`, `description`, `transDate`, `contactMethod` opcional (`0` InPerson, `1` PhoneCall, `2` OnlineMeeting), `comentarios`, `antecedentes`, `conclusiones`.
- Visibilidad: AX valida la modificación con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.

### Endpoint no expuesto como tool MCP: crm_activities_delete
- HTTP: DELETE `/api/crm/activities/{recId}`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`.
- Visibilidad: AX valida la modificación con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.

### Endpoint no expuesto como tool MCP: crm_visits_create_assistant
- HTTP: POST `/api/crm/visits/createVisitaAsistente`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `refRecIdActividad`, `asistenteTipo`, `asistenteId`, `contactoRecId`.
- Visibilidad: AX valida la modificación de la actividad principal con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.

### Endpoint no expuesto como tool MCP: crm_visits_delete_assistant
- HTTP: DELETE `/api/crm/visits/deleteVisitaAsistente`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `X-IND-AxUserId`, `Content-Type: application/json`.
- Cuerpo: `refRecIdActividad`, `asistenteId`.
- Visibilidad: AX valida la modificación de la actividad principal con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.

## Proyectos

### Tool: crm_projects_list
- HTTP: GET `/api/crm/projects/list`
- Autenticación: token bearer.
- Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`.
- Consulta: `filter` (opcional), `page`, `pageSize`.
