# Endpoints de IND_CRM_API

URL base: `{{baseUrl}}`. Las URLs vigentes de DEV y PROD se mantienen en `docs/operations/configuracion-entornos.md`, ruta relativa a la raiz del repositorio.

## Autenticación y cabeceras comunes

- `Authorization: Bearer {{tokenId}}` (requerido en todo endpoint salvo login, `health/ping` y `/api/system/exchange-rate/public-direct`).
- `X-IND-Company: {{companyId}}` (requerido en endpoints CRM: `/api/crm/*`).
- `X-IND-AxUserId: {{axUserId}}` (solo cuando el endpoint lo documenta expresamente; puede identificar al sujeto funcional o propietario enviado a AX, pero no sustituye al actor del contexto firmado).
- `X-IND-EntraOid: {{entraOid}}` (requerido en endpoints que validan el contexto firmado de compañías).
- `X-IND-Context-Version: {{contextVersion}}` (requerido en endpoints que validan el contexto firmado de compañías).
- `X-IND-Permissions-Revision: {{permissionsRevision}}` (requerido en endpoints que validan el contexto firmado de compañías).
- `X-IND-Context-Token: {{contextToken}}` (requerido en endpoints que validan el contexto firmado de compañías).
- `companyId` se obtiene de `/api/auth/entra/context` (`Items[0].Header.DefaultCompany` o `Items[0].Companies[*].CompanyId`).
- `contextVersion`, `permissionsRevision` y `contextToken` se obtienen de `/api/auth/entra/context` (`Items[0].ContextVersion`, `Items[0].PermissionsRevision`, `Items[0].ContextToken`).
- `baseUrl` se define en la colección Postman como variable compartida.

## Reglas

- Cada endpoint documenta si necesita `X-IND-AxUserId`. En contratos heredados puede aportar el usuario funcional enviado a AX; en rutas migradas, la identidad del actor autorizado procede del contexto firmado y el servidor no confía en una cabecera manipulable para decidir visibilidad o permisos.
- Los endpoints de negocio CRM deben exigir `companyId` mediante la cabecera `X-IND-Company`.
- Los endpoints que invocan `RequireCompanyOrReturn422` validan el contexto firmado de compañías mediante `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision` y `X-IND-Context-Token`. En `/api/ia/service/expensefromticket`, esta validación solo se ejecuta cuando `persistTicket=true`.
- Swagger debe documentar resumen, parámetros, respuestas y errores.
- Regla obligatoria de fechas en tickets y hojas de gastos: la petición admite `DDMMYYYY` o `DD.MM.YYYY`; la respuesta devuelve siempre `DD.MM.YYYY` para `transDate`, `createdDateFrom`, `createdDateTo` y `createdDate`.

## Endpoints

## Autenticación
- POST /api/auth/login (AllowAnonymous)
  Cuerpo: `{ "Username": "...", "Password": "..." }`.
  Respuesta: `IndApiResponse` con `Data.token` y `Data.expires`.
- POST /api/auth/refresh (Authorize)
  Cabeceras: `Authorization`.
- POST /api/auth/entra/context (Authorize)
  Cuerpo: `{ "entraOid": "GUID", "appCode": "APP" }`.
  Campos de la respuesta de contexto: `ContextToken`, `ContextVersion`, `PermissionsRevision`, `ContextIssuedUtc`, `ContextExpiresUtc`, `Header.DefaultCurrencyCode`, `Header.UserName`, `Companies[].CurrencyCode`, `Companies[].AllowSelfManagement`, `Companies[].CrmUserId`.

## Salud
- GET /api/health/ping (AllowAnonymous)
- GET /api/health/health (Authorize)

## Sistema
- GET /api/system/getEnvironmentName (Authorize)
- GET /api/system/getCompanyName (Authorize)
- GET /api/system/exchange-rate?baseCurrency=EUR&targetCurrency=USD&date=2026-02-16 (Authorize)
  Consulta obligatoria: `baseCurrency`, `targetCurrency` (ISO 4217, 3 letras).
  Consulta opcional: `date` (`yyyy-MM-dd`; si no se envía, usa el último valor disponible).
  Respuesta: `IndApiResponse<ExchangeRateDto>` con `BaseCurrency`, `TargetCurrency`, `Rate`, `Date` y el nombre natural del proveedor en `Source`.
  Valores posibles de `Source`:
  - Banco Central Europeo (ECB)
  - Frankfurter API (fallback nivel 2)
  - Open ER API (fallback nivel 3)
  Comportamiento interno: ECB es el proveedor principal; Frankfurter es la alternativa de nivel 2 y OpenErApi, la de nivel 3.
  Endpoint de Frankfurter: `https://api.frankfurter.app/latest?from={BASE}&to={TARGET}`.
  Endpoint de OpenErApi: `https://open.er-api.com/v6/latest/{BASE}`.
  Nota de OpenErApi: solo admite el último valor disponible; si se solicita una fecha distinta de hoy, se usa igualmente ese último valor.
  Contrato externo: no cambia la ruta, el envoltorio ni la estructura pública; no se expone el proveedor alternativo.
  Caché: `MemoryCache` durante 24 h por la clave `base|target|date` (solo resultados correctos).
  Códigos de error: `VALIDATION_ERROR` (422), `EXCHANGE_RATE_NOT_FOUND` (404, heredado), `RATE_UNAVAILABLE` (404), `INTERNAL_ERROR` (500).
- GET /api/system/exchange-rate/public-direct?baseCurrency=USD&targetCurrency=EUR&date=2026-02-18 (AllowAnonymous)
  Consulta obligatoria: `baseCurrency`, `targetCurrency` (ISO 4217, 3 letras).
  Consulta opcional: `date` (`yyyy-MM-dd`; si no se envía, usa el último valor disponible).
  Endpoint de consumo recomendado: {{baseUrl}}/api/system/exchange-rate/public-direct?baseCurrency=AED&targetCurrency=EUR
  Respuesta: `IndApiResponse<ExchangeRateDto>` con el mismo contrato que `/api/system/exchange-rate`.
  Códigos de error: `VALIDATION_ERROR` (422), `EXCHANGE_RATE_NOT_FOUND` (404, heredado), `RATE_UNAVAILABLE` (404), `INTERNAL_ERROR` (500).

## MCP
- GET /api/mcp/tools (Authorize)
  Respuesta: catálogo MCP (`MCP_TOOLS.json`).

## Enums CRM
- GET /api/crm/enums/by-name?appCode=CRM&axEnumNames=CRMGastoType,INDExpenseSheetStatus,INDReimbursableExpense,INDReimbursableExpenseLines (Authorize + X-IND-Company)
  Consulta opcional: `appCode` con valor predeterminado `CRM`; `axEnumNames` es una lista separada por comas.
  Si `axEnumNames` se omite o llega vacío, devuelve todos los enums activos configurados para la aplicación y la empresa.
  Respuesta: `IndPagedResponse<CrmEnumCatalogDto>`.
  Cada elemento incluye `Company`, `AppCode`, `AxEnumName`, `AxEnumId`, `Found` y `Options[]`.
  Cada opción incluye `Value` (compatibilidad), `EnumIndex` (valor numérico AX que deben enviar los endpoints de negocio cuando exista), `Label`, `Description`, `Active`, `SortOrder` y `AxEnumsTableRefRecId`.
  Nota: `SortOrder = 0` es válido y no significa vacío.
  Códigos de error: `VALIDATION_ERROR` (422), `AX_COM_ERROR`/`AX_SESSION_ERROR` (500).
- GET /api/crm/enums/by-id?appCode=CRM&axEnumIds=61472,61523 (Authorize + X-IND-Company)
  Consulta opcional: `appCode` con valor predeterminado `CRM`; `axEnumIds` es una lista separada por comas.
  Si `axEnumIds` se omite o llega vacío, devuelve todos los enums activos configurados para la aplicación y la empresa.
  Respuesta y semántica iguales a `/api/crm/enums/by-name`.

## Servicios de IA
- POST /api/ia/service/text/format (Authorize)
  Tipo de contenido: `application/json`.
  Cuerpo obligatorio: `text` (máximo configurable de 20.000 caracteres por defecto).
  Cuerpo opcional: `languageId` (`auto` por defecto o un identificador de idioma BCP 47 válido).
  Comportamiento: corrige ortografía, gramática, puntuación y disposición legible del texto plano, conservando el idioma, significado y datos de origen. No traduce, resume, responde, persiste ni actualiza contenido. El consumidor no puede reemplazar las instrucciones definidas por el servidor.
  Datos de respuesta: `formattedText` (resultado completo), `hasChanges` (calculado por la API) y `warnings[]` con `fragment` y `reason`.
  Errores: 422 por validación o rechazo de moderación, 429 por límite o concurrencia del usuario y 503 por indisponibilidad del proveedor de IA.
- POST /api/ia/service/speech (Authorize)
  Tipo de contenido: `multipart/form-data`.
  Campos: `languageId` (obligatorio), `audioFile` (obligatorio), `temperature` (opcional, 0-1), `prompt`/`context` (opcionales).
- POST /api/ia/service/expensefromticket (Authorize)
  Tipo de contenido: `multipart/form-data`.
  Campos: `ticketImage` (obligatorio), `persistTicket` (opcional, `true|false`), `ticketUrlFile` (opcional; si `persistTicket=true` y no se envía, se usa una URL temporal).
  Cabeceras adicionales cuando `persistTicket=true`: `X-IND-Company`, `X-IND-AxUserId`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`.
  El borrador de IA incluye `gastoType` (tipo de gasto de cabecera), `ticketDate`, `ticketTime`, `totalAmount` (total bruto OCR) y mantiene `lines[].typeValue`. `transDate` se conserva por compatibilidad y debe coincidir con `ticketDate` cuando la IA detecta la fecha del ticket.
  El total bruto se contrasta con etiquetas OCR de pago (`TOTAL A PAGAR`, `amount due`, etc.) y la suma de líneas se reconcilia antes de persistir; base imponible, subtotal, impuestos, descuentos, ahorro, importe entregado y cambio no se aceptan como total pagadero.
  Si `persistTicket=true`, `Data.TicketCreation.ProcessedByAI` retorna `true` y el ticket queda marcado en AX como procesado por IA.
- POST /api/ia/service/expensesheets/ask (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `question`.
  Cuerpo opcional: `answerInstructions`, `listRequest`, `sourceJson`.
  `listRequest` reutiliza los filtros de `POST /api/crm/expensesheets/list`: `filter`, `billedMode`, `createdDateFrom`, `createdDateTo`, `projId`, `currencyCode`, `expenseSheetStatus`, `reimbursableExpense`, `includeSubordinates`.
  Compatibilidad: `page` y `pageSize` pueden enviarse dentro de `listRequest`, pero este endpoint los ignora.
  `sourceJson` acepta el JSON completo devuelto por `POST /api/crm/expensesheets/list` o un array directo de registros (`Items`).
  Límites defensivos para `sourceJson`: máximo 4 MB por petición y máximo 6.000 registros incluidos directamente.
  Si llega `sourceJson`, el endpoint analiza ese JSON directamente y omite la carga en el servidor.
  Si no llega `sourceJson`, el backend carga todos los registros filtrados en el servidor y decide si responde en modo `direct` o `chunked`.
  Datos de respuesta: `Answer`, `Model`, `SourceKey`, `FiltersApplied`, `TotalSourceRecords`, `RecordsSentToModel`, `RetrievalMode`, `Truncated`, `Warnings`.
  Errores relevantes: 422 por validación, 429 por límite de IA y 500 por error interno.
  Límite de consultas predeterminado: 30 peticiones por usuario y 900 segundos. Usa la política `AssistantQueries:*`, independiente de OCR, voz, tickets, formateo y del indicador global `OpenAI:RateLimitEnabled`. Al agotar esta cuota local responde `429 ASSISTANT_QUERY_RATE_LIMIT_EXCEEDED`; otros 429 conservan su código y espera propios.

## Asistente de ayuda CRM
- GET /api/help/catalog?responseLocale=es-ES (Authorize)
  Devuelve `KnowledgeVersion`, `DefaultLocale`, `ResponseLocale` y módulos/temas ordenados. No llama a OpenAI.
  `responseLocale` admite `es-ES`, `eu-ES`, `en`, `pt`, `it`, `zh-Hans` por compatibilidad con la interfaz. El paquete publicado contiene solo español: todas esas peticiones devuelven el catálogo completo con `DefaultLocale=es-ES` y `ResponseLocale=es-ES`.
  Incluye ETag privado por cultura efectiva y admite `If-None-Match`.
- GET /api/help/topics/{topicId}?responseLocale=es-ES (Authorize)
  Devuelve `Title`, `Summary`, `Chunks` y `QuickAnswers` sin llamar a OpenAI. Mientras solo esté publicado el contenido en español, cualquier cultura admitida recibe la proyección completa `es-ES` y `ResponseLocale=es-ES`.
  Datos de respuesta: `Id`, `ModuleId`, `Title`, `Summary`, `RouteKey`, `PrerequisiteTopicIds`, `RelatedTopicIds`, `Chunks`, `QuickAnswers`, `KnowledgeVersion`, `ResponseLocale`.
  `RouteKey` pertenece a una lista permitida (`home`, `visits.history`, `expenses.sheets`, `expenses.tickets`); nunca es una URL.
- POST /api/ia/service/help/ask (Authorize)
  Cuerpo obligatorio: `question` (máximo 1.200) y `responseLocale` (`es-ES`, `eu-ES`, `en`, `pt`, `it`, `zh-Hans`).
  Cuerpo opcional: `selectedModuleId`, `selectedTopicId`, `answerInstructions` (máximo 2.000), `history` (máximo 8 mensajes `user|assistant`, 1.600 caracteres cada uno), `clientInteractionId` (UUID).
  `selectedModuleId` debe ser un ID visible del chatbot y actúa como ámbito estricto. `troubleshooting` y `glossary` permanecen en `GET /api/help/catalog` para el Manual, pero `POST /ask` nunca los acepta ni los devuelve como temas, candidatos o fuentes primarias. Sin `selectedTopicId`, la API entrega a una única llamada de OpenAI todos los temas y fragmentos publicados del módulo visible para que interprete la intención y seleccione la evidencia relevante; la clasificación léxica queda solo como diagnóstico y ya no descarta ni preselecciona la pregunta, incluso cuando un seguimiento no contiene tokens independientes. Además incorpora internamente los fragmentos de `troubleshooting` como contexto diagnóstico: no cambia los temas, candidatos, fuentes visibles ni acciones del módulo seleccionado, y el modelo no puede ofrecerlo como sección del chatbot. Este modo elimina primero el historial antiguo si necesita liberar presupuesto y falla de forma segura si el contexto completo no cabe, sin responder desde evidencia parcial. Un módulo inexistente, reservado al manual, sin temas o un `selectedTopicId` que no pertenece al módulo seleccionado devuelve `notDocumented` sin llamar a OpenAI.
  `answerInstructions` solo puede ajustar tono, claridad, longitud, formato y organización. Este campo no puede anular las reglas fijas del servidor sobre fundamentación, seguridad, idioma, citas y rutas. Cuando se envía, incluso una respuesta rápida canónica pasa por la reescritura del modelo.
  Datos de respuesta: `InteractionId`, `Resolution`, `Answer`, `Candidates`, `Sources`, `Actions`, `KnowledgeVersion`, `ResponseLocale`, `FeedbackToken`, `QuickAnswerUsed`, `Model`.
  `Resolution`: `answered`, `needsSelection` o `notDocumented`. Sin módulo, las selecciones ambiguas y los descartes léxicos no llaman a OpenAI. Con un módulo válido, OpenAI decide entre `answered` y `notDocumented`; este último conserva una explicación generada, pero no admite citas ni acciones. Una respuesta `answered` exige al menos una cita primaria visible; las citas diagnósticas solo pueden complementar esa evidencia y cada acción debe pertenecer a uno de los temas primarios citados.
  OpenAI recibe texto redactado, contexto local recuperado, `store:false`, sin herramientas y sin identidad. Las citas y acciones se validan contra los fragmentos y `RouteKey` permitidos. Las instrucciones fijas obligan a interpretar primero la intención del usuario y a explicitar una interpretación prudente cuando la redacción sea ambigua, además de sintetizar y parafrasear solo la evidencia relevante. La API detecta solapamientos literales largos con fragmentos o respuestas rápidas y reintenta una reescritura una vez. Si el rechazo de calidad persiste, responde `422 HELP_ANSWER_REWRITE_REQUIRED`, separado de una indisponibilidad real del proveedor. Las etiquetas de interfaz, nombres de campos y valores cortos de `RouteKey` pueden conservarse literalmente para mantener precisión.
  Límite independiente predeterminado: 30 peticiones por usuario y 900 segundos, incluso si `OpenAI:RateLimitEnabled=false`. Usa la misma configuración `AssistantQueries:*` que el chat de Gastos, aunque cada endpoint conserva su contador propio. Al superarlo, responde `429 ASSISTANT_QUERY_RATE_LIMIT_EXCEEDED`, conserva `Retry-After` con la espera restante y explica que se ha alcanzado un límite establecido de consultas. Los 429 de concurrencia o del proveedor mantienen sus códigos y esperas propios.
- POST /api/help/feedback (Authorize)
  Cuerpo obligatorio: `feedbackToken`, `helpful`.
  Si `helpful=false`, `reason` es obligatorio: `incorrect`, `outdated`, `unclear`, `incomplete`, `permissions`, `other`. `comment` es opcional (máximo 1.000).
  El token HMAC está ligado al usuario y a `InteractionId`, caduca en 60 minutos por defecto y se consume una sola vez por proceso API; una repetición devuelve 403.

### Runtime y configuración

- Indicador de función: `HelpAssistant:Enabled` / `INDCRM_HELP_ENABLED`; está desactivado en `App.config` hasta desplegar el paquete validado.
- Paquete: `HelpAssistant:KnowledgeBundlePath` / `INDCRM_HELP_KNOWLEDGE_BUNDLE_PATH`; valor predeterminado `Knowledge\crm-help.bundle.json` relativo al ejecutable. El proyecto lo copia con `PreserveNewest` cuando existe; la fuente generada se integra desde `IND_CRM_APP\docs\crm-help\generated\crm-help.bundle.json` y no se admite un paquete vacío.
- Esquemas de paquete admitidos: `1.0` y `1.1`. `1.1` añade `module.localizations[locale]={title,description}` y `topic.localizations[locale]={title,summary,chunks,quickAnswers}` sin retirar los campos escalares canónicos. El paquete actual declara únicamente `es-ES`. Los mapas son opcionales por compatibilidad con paquetes antiguos; cada entrada presente debe usar una cultura declarada en `supportedResponseLocales`, y sus ID de fragmentos y respuestas rápidas deben coincidir exactamente con los conjuntos canónicos. El runtime aplica los mismos límites de texto, recursos e ID relacionados. Los `title`, `summary`, `chunks` y `quickAnswers` canónicos siguen siendo la única fuente de contenido para recuperación y fundamentación; los alias y las preguntas localizadas conservan su función de búsqueda.
- Modelo/presupuesto: `HelpAssistant:Model`, `ReasoningEffort`, `PromptCacheKey`, `TimeoutSeconds`, `MaxInputTokens`, `MinDocumentTokens`, `MaxDocumentTokens`, `MinOutputTokens`, `MaxOutputTokens`, `MaxHistoryMessages`. Valores efectivos predeterminados: `gpt-5.4-mini`, `low`, 90 s, 18k de entrada total, 4k-12k de documentación y 1,6k-3,2k de salida.
- Límite común de consultas: `AssistantQueries:RateLimitEnabled`, `RateLimitMaxRequests`, `RateLimitWindowSeconds`, `RateLimitValidationMultiplier`. Valores predeterminados: `true`, `30`, `900`, `1`; las tres claves numéricas heredadas `HelpAssistant:RateLimit*` se conservan como alternativa si no existe la nueva configuración común. `HelpAssistant:RateLimitEnabled` sigue siendo el interruptor adicional exclusivo de Ayuda CRM.
- Variables de máquina equivalentes: `INDCRM_ASSISTANT_QUERY_RATE_LIMIT_ENABLED`, `INDCRM_ASSISTANT_QUERY_RATE_LIMIT_MAX_REQUESTS`, `INDCRM_ASSISTANT_QUERY_RATE_LIMIT_WINDOW_SECONDS`, `INDCRM_ASSISTANT_QUERY_RATE_LIMIT_VALIDATION_MULTIPLIER`.
- Verificación local tras compilar x86: `powershell.exe -File scripts\test-assistant-query-rate-limit.ps1`; valida ambos contadores, solicitudes 1-30, rechazo de la 31, `Retry-After: 900`, el código de error exclusivo y el mensaje de 15 minutos.
- Feedback: `HelpAssistant:FeedbackHmacSecret` (mínimo 32 caracteres) y `FeedbackTokenMinutes`.
- Analítica: `AnalyticsPath` (valor predeterminado `C:\INDData\CRMHelpAnalytics`), `AnalyticsHmacSecret`, `AnalyticsTextCaptureEnabled`, `AnalyticsAclReady`, `AnalyticsVolumeEncrypted`, muestreo y retenciones.
- Las métricas NDJSON no contienen pregunta, respuesta, historial, IP, email, empresa, OID ni identidad directa. El texto redactado solo se escribe en `review` cuando los tres interruptores de seguridad están activos y existe un secreto HMAC.
- Retenciones predeterminadas: 90 días para revisión, 180 días para métricas y 730 días para agregados; purga local diaria con el mejor esfuerzo.
- `scripts/setup-help-analytics-acl.ps1` prepara la ACL solo para el destino exacto y exige `-AllowExisting` si ya contiene datos; `scripts/export-help-analytics-report.ps1 -IncludeReviewQueue` genera HTML/CSV privados semanales o mensuales y una cola editorial redactada separada, sin endpoint público.
- `scripts/test-help-retrieval.ps1` ejecuta el recuperador real sobre el paquete y `evals/retrieval-cases.json`, calcula Top1/Recall@5 sobre la clasificación interna, exige `MenuExact` para todos los temas más un ID inexistente, valida que las seis culturas admitidas reciban la proyección española completa y comprueba compatibilidad `1.0` mediante un recurso temporal.
- `scripts/test-help-feedback-token.ps1` comprueba, sin imprimir secretos ni tokens, que el primer consumo se acepta y que las repeticiones o entradas mal formadas se rechazan.
- `scripts/test-help-answer-evals.ps1` valida `answer-cases.json` y, fuera de `-ValidateOnly`, llama secuencialmente al endpoint autenticado directo `/api/ia/service/help/ask`. Requiere `-ApiBaseUrl`, `-CasesPath` y `-OutputDirectory`; lee el bearer exclusivamente de la variable de entorno de proceso indicada por `-TokenEnvironmentVariable` y no lo acepta como parámetro ni lo incluye en la salida.
- El ejecutor comprueba HTTP/envoltorio, `expectedResolution`, configuración regional y temas mediante `Sources` y `requiredSourceChunkIds`; genera JSON/HTML escapado y devuelve un código distinto de cero ante fallos estructurales. `-CaseId` limita la ejecución a un caso y cada petición usa un `clientInteractionId` nuevo.
- `-ValidateOnly` valida parámetros y corpus, crea los informes sin respuestas y no lee el token ni accede a la red. `requiredFacts`, `forbiddenClaims`, exactitud semántica y calidad de traducción se exportan para revisión humana; nunca se marcan como aprobadas automáticamente.

## Hojas de gastos
- GET /api/crm/expensesheets/currencies (Authorize + X-IND-Company)
  Elementos de respuesta: `CurrencyCode`, `CurrencyCodeISO`.
- GET /api/crm/expensesheets/subordinates (Authorize + X-IND-Company + X-IND-AxUserId)
  Elementos de respuesta: `UserId` (CRM), `AxUserId`, `Name`.
- POST /api/crm/expensesheets (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio según `mode`:
  `mode=0` (predeterminado): `description`, `lines[]` (con `lines[].price`).
  `mode=1`: `description` (sin `lines`).
  `mode=2`: `existingHojaGastosId` y `lines[]` (con `lines[].price`).
  Opcionales: `mode` (0|1|2), `existingHojaGastosId`, `projId`, `currencyCode`/`exchRate` heredados como valores predeterminados de líneas nuevas, `expenseSheetStatus`, `exchangeRateMode`, `reimbursableExpense` (`INDReimbursableExpense`, solo `0=Yes` o `1=No`; predeterminado `Yes`), `lines[].projId`, `lines[].projIdProvided`, `lines[].internacional`, `lines[].fileId`, `lines[].reimbursableExpense` (`INDReimbursableExpenseLines`, valor heredado o predeterminado `Yes`), `lines[].currencyCode`, `lines[].amountMST`, `lines[].exchRate`. `currencyCode` de cabecera es opcional en los tres modos.
  Nota sobre el proyecto de línea: `projIdProvided=true` conserva `lines[].projId` como valor explícito, incluido `""`; `false` u omitido sin `projId` delega en `defaultProjectForNewLine` de AX. Ese valor predeterminado usa exclusivamente el proyecto elegible de cabecera; una cabecera vacía, con `PurchParameters.INDProjIdVarious` o con un proyecto inelegible deja la línea sin proyecto. Por compatibilidad, un `projId` presente sin indicador se considera explícito.
  Nota: la cabecera AX mantiene siempre la divisa local de reembolso y `ExchRate=100`; la divisa real se informa en cada línea.
  Nota sobre enums AX: `expenseSheetStatus`, `exchangeRateMode`, `reimbursableExpense` (`INDReimbursableExpense`) y `lines[].reimbursableExpense` (`INDReimbursableExpenseLines`) deben enviarse como valores numéricos obtenidos desde `/api/crm/enums/by-name`. En escritura de cabecera solo se admiten `Yes=0` y `No=1`; `Both=2` es un valor derivado de líneas mixtas y queda reservado para respuestas y filtros. En líneas, `Yes=0` incluye `AmountMST` y `No=1` lo excluye, dejando `ReimbursableAmount=0`.
  Datos de respuesta: `HojaGastosId` y `LineRecIds` (`number[]`, identificadores `RecId` numéricos de AX).
- GET /api/crm/expensesheets/fuel-price-km?transDate=2026-02-18 (Authorize + X-IND-Company + X-IND-AxUserId)
  Consulta opcional: `transDate` (`DDMMYYYY` o `DD.MM.YYYY`; si no se envía, usa hoy).
  Respuesta: `IndApiResponse` con `PriceKm`, `Source` y `TransDate`.
- GET /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + contexto firmado)
  Autorización: el usuario que consulta se obtiene del contexto firmado (`RequireValidatedSnapshotAxUserIdOrReturn403`). `X-IND-AxUserId` no es obligatorio para esta lectura y no decide la visibilidad.
  Campos de cabecera de la respuesta: `userName`, `expenseSheetStatus`, `estadoComentarios`, `exchangeRateMode`, `createdDate`, `axCreatedDate`, `reimbursableExpense`, `totalAmountCurrency`, `totalAmountMST`, `totalGrossAmountMST`, `totalReimbursableAmount`, `defaultLineProjId`.
  Nota sobre el proyecto predeterminado: `defaultLineProjId` es el proyecto elegible de cabecera que debe usar una nueva línea. Con el contrato AX actual queda vacío cuando la cabecera está vacía, contiene `PurchParameters.INDProjIdVarious` o apunta a un proyecto inelegible; queda `null` con contratos AX anteriores que no incluyen la posición 21.
  Nota sobre totales: `totalAmountCurrency` y su alias `totalAmount` conservan, por compatibilidad nominal, el total contable heredado calculado desde el importe reembolsable; `totalAmountMST` conserva el total contable heredado en divisa de empresa/MST. `totalGrossAmountMST` es el total bruto de empresa/MST y no se filtra por reembolso ni por Visa. `totalReimbursableAmount` es el total explícito de reembolso de empresa/MST e incluye únicamente las líneas con `ReimbursableExpense=Yes`; `VisaEmpresa` no interviene en el cálculo. Durante un despliegue AX anterior, `totalReimbursableAmount` usa `totalAmountMST` como alternativa y `totalGrossAmountMST` queda nulo.
  Nota JSON: Web API serializa las propiedades en PascalCase; en JavaScript usar `TotalGrossAmountMST`, `TotalReimbursableAmount` y `ReimbursableAmount`.
  Nota AX: `axCreatedDate` expone la fecha final adicional devuelta por el contrato AX y se normaliza a `DD.MM.YYYY`; actualmente refleja la misma fecha de creación que `createdDate`.
  Nota: `userName` es `CRMUsuarioTable.Name` del propietario CRM de la hoja (`userId`). Se agrega al contrato de detalle como campo adicional compatible con clientes anteriores.
  Campos de línea de la respuesta: `price`, `qty`, `amount`, `projId`, `reimbursableExpense`, `currencyCode`, `amountMST`, `reimbursableAmount`, `exchRate`, `totalAmountCurrency`, `totalAmountMST`.
  Nota sobre líneas: `amount` y su alias `totalAmountCurrency` expresan el total en la divisa original de la línea; `amountMST` y su alias `totalAmountMST` expresan el total de empresa/MST; `reimbursableAmount` expresa la parte reembolsable de empresa/MST, copia `amountMST` con `ReimbursableExpense=Yes` y vale cero con `ReimbursableExpense=No`, independientemente de `VisaEmpresa`; queda nulo con contratos AX heredados. AX conserva `VisaEmpresa` bloqueado como espejo inverso de compatibilidad (`Yes` reembolsable -> Visa `No`; `No` reembolsable -> Visa `Yes`).
  Nota de enrutamiento: el literal `tickets` queda excluido de `hojaGastosId` para evitar colisión con `/api/crm/expensesheets/tickets`.
- PUT /api/crm/expensesheets/{hojaGastosId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `description`. Opcionales: `projId`/`projIdProvided`, `currencyCode`/`exchRate` heredados e ignorados por cabecera, `expenseSheetStatus`, `exchangeRateMode`, `estadoComentarios` y `reimbursableExpense` con enum `INDReimbursableExpense`; solo se admiten `0=Yes` o `1=No`.
  Nota sobre el proyecto: `projIdProvided=false` conserva el proyecto bajo el bloqueo de cabecera de AX; `true` aplica `projId`, incluido `""`. Todo valor no vacío debe ser un proyecto elegible: AX rechaza `PurchParameters.INDProjIdVarious`, proyectos inexistentes, cerrados o no imputables. Si se omite el indicador, un `projId` no nulo se considera explícito para mantener clientes anteriores.
  Nota: `Both=2` no se admite en escritura de cabecera; AX lo deriva cuando existen líneas mixtas y la API lo conserva en respuestas y filtros.
  Nota: si se envía `estadoComentarios`, también se deben enviar `expenseSheetStatus` y `exchangeRateMode`.
  Nota: actualizar la cabecera no propaga divisa, proyecto ni reembolso a líneas existentes. La cabecera queda siempre en divisa local de reembolso (`ExchRate=100`); proyecto y reembolso solo se propagan mediante sus endpoints dedicados después de la confirmación del usuario.
  Nota sobre agregados: AX compara el proyecto operativo `ProjIdHornos` de todas las líneas guardadas. Si todas coinciden, incluido el valor vacío, la cabecera adopta el valor común; si difieren, incluido el valor vacío, usa `PurchParameters.INDProjIdVarious`. Una diferencia aislada entre el campo oculto `ProjId` y `ProjIdHornos` no activa el marcador. Para reembolso, todas `Yes` o todas `No` producen el valor común y una mezcla produce `INDReimbursableExpense::Both`.
  Notificaciones de estado: la API no envía emails directamente. `INDCRMExpenseSheetService.updateExpenseSheetHeader` en Axapta captura el estado anterior y posterior, y lanza el email con el mejor esfuerzo fuera de `tts` cuando corresponde. Eventos AX admitidos: `ExpenseSheetApprovalRequested`, `ExpenseSheetApproved`, `ExpenseSheetRejected`, `ExpenseSheetRejectionCancelled` y `ExpenseSheetPaid`. Si emisor y destinatario se resuelven como el mismo usuario CRM, se omite el email. El transporte AX/DLL actual usa exclusivamente `SendMailEx`; el parámetro opcional `attachmentFilePaths` va después de `textBody` y antes de `saveToSentItems`. Para estas notificaciones se envía vacío porque no adjuntan ficheros.
- POST /api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate?recalculateAmountMST=true&force=false (Authorize + X-IND-Company + X-IND-AxUserId)
  Compatibilidad sin efecto: se conserva la ruta, pero AX ya no propaga la divisa de cabecera a las líneas.
  Consulta opcional: `recalculateAmountMST` (valor predeterminado `true`, solo por compatibilidad de respuesta), `force` (valor predeterminado `false`, ignorado).
  Datos de respuesta: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de enrutamiento: el literal `tickets` queda excluido de `hojaGastosId`.
- POST /api/crm/expensesheets/{hojaGastosId}/project-default/propagate (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo opcional: `{ "projId": "PROYECTO", "projIdProvided": true }`. Con `projIdProvided=true`, AX aplica el objetivo, incluido `""`, a cabecera y líneas en una sola transacción. Un `projId` presente sin indicador también se considera explícito por compatibilidad; sin objetivo o con `projIdProvided=false`, usa el `projId` ya guardado en cabecera.
  AX bloquea la operación si el objetivo es `PurchParameters.INDProjIdVarious`, si el modo heredado no tiene `projId` de cabecera o si la hoja está bloqueada por `Voucher`.
  Datos de respuesta: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de enrutamiento: el literal `tickets` queda excluido de `hojaGastosId`.
- POST /api/crm/expensesheets/{hojaGastosId}/reimbursable-expense/propagate (Authorize + X-IND-Company + X-IND-AxUserId)
  Propaga el `reimbursableExpense` actual de cabecera a todas las líneas existentes.
  Usar después de modificar la cabecera solo cuando el usuario confirme que desea actualizar todas las líneas.
  AX bloquea la operación si `reimbursableExpense` de cabecera es `INDReimbursableExpense::Both` o si la hoja está bloqueada por `Voucher`.
  Datos de respuesta: `hojaGastosId`, `propagationType`, `updatedLines`, `recalculateAmountMST`.
  Nota de enrutamiento: el literal `tickets` queda excluido de `hojaGastosId`.
- PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `typeValue`, `description`, `qty`, `price`.
  Opcionales: `fileId` (`INDFileId`), `internacional`, `projId`, `projIdProvided`, `reimbursableExpense` (`INDReimbursableExpenseLines`), `currencyCode`, `amountMST`, `exchRate`.
  Nota sobre el proyecto: `projIdProvided=false` conserva el proyecto actual de la línea; `true` aplica `projId`, incluido `""`. Si se omite el indicador, el PUT conserva el comportamiento heredado: un `projId` no vacío es explícito; sin valor usa solo el proyecto elegible de cabecera. Si la cabecera está vacía, contiene `PurchParameters.INDProjIdVarious` o apunta a un proyecto inelegible, conserva el proyecto actual de la línea.
  Nota sobre `fileId`: este PUT solo acepta el mismo valor ya persistido; no permite alta, baja ni sustitución. Para cambiar la asociación deben usarse los endpoints dedicados `/ticket`.
  Nota sobre enums AX: `typeValue` y `reimbursableExpense` deben enviarse como valores numéricos obtenidos desde `/api/crm/enums/by-name`; las líneas solo aceptan `INDReimbursableExpenseLines` No/Yes, no Both.
  Nota: si `currencyCode` de línea no es la divisa de reembolso de la hoja, enviar `exchRate` o `amountMST`; AX no reutiliza la tasa de cabecera para divisas extranjeras. Si la divisa de línea y reembolso coinciden, editar `amountMST` no recalcula `exchRate`.
  Nota: AX recalcula el agregado tras guardar. Si todas las líneas coinciden en `reimbursableExpense`, la cabecera adopta ese valor; si existen líneas `Yes` y `No`, la cabecera pasa a `INDReimbursableExpense::Both`.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para líneas manuales temporales.
- PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}/ticket (Authorize + X-IND-Company + X-IND-AxUserId)
  Asocia un ticket existente a una línea manual ya persistida sin reemplazar el resto de campos de la línea.
  Cuerpo obligatorio: `fileId` (`INDFileId`).
  Identidad: `X-IND-AxUserId` identifica al propietario de la hoja; el actor que consulta se toma exclusivamente del `AxUserId` del contexto firmado ya validado, nunca de otra cabecera. Si el contexto no contiene actor, devuelve 403 `AUTH_CONTEXT_STALE`; si no coincide con el propietario, devuelve 403 `AUTH_FORBIDDEN`.
  Reglas: `lineRecId` debe ser distinto de 0 y puede ser negativo; la hoja debe estar en estado `Draft`; la línea debe ser manual y no tener marcados `Ticket` ni `Factura`; solo el propietario firmado puede asociar y debe tener `Edit` sobre `GASTOS_HOJA_GASTO` y `View` sobre `GASTOS_TICKETS`. AX valida además la propiedad del ticket, su elegibilidad y la ausencia de otra asociación incompatible.
  Idempotencia: repetir la petición con el mismo `fileId` devuelve 200; normalmente `Changed=false`, salvo que AX repare el estado derivado del ticket. Una asociación nueva devuelve `Changed=true`.
  Datos de respuesta: `HojaGastosId`, `LineRecId`, `FileId`, `TicketStatus`, `Changed`.
  Errores AX: `FORBIDDEN` devuelve 403 `AUTH_FORBIDDEN`; `NOT_FOUND` devuelve 404; `CONFLICT` e `INVALID_STATE` devuelven 409; `INVALID_TICKET` y otras reglas de negocio devuelven 422; `ERROR` devuelve 500.
  Nota de enrutamiento: el sufijo literal `/ticket` y la restricción `lineRecId:long` separan esta ruta de la actualización completa de línea; no existe una combinación equivalente de método y plantilla bajo `/api/crm/expensesheets/tickets`.
- DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}/ticket (Authorize + X-IND-Company + X-IND-AxUserId)
  Desvincula el ticket actual de la línea; no elimina la línea de gasto, el ticket ni su imagen.
  No admite cuerpo ni parámetros de consulta. `lineRecId` debe ser distinto de 0 y puede ser negativo; la hoja debe estar en estado `Draft`.
  Identidad: `X-IND-AxUserId` identifica al propietario y el actor que consulta procede del `AxUserId` del contexto firmado validado; solo se permite cuando ambos coinciden y el actor tiene `Edit` sobre `GASTOS_HOJA_GASTO` y `View` sobre `GASTOS_TICKETS`. Un contexto sin actor devuelve 403 `AUTH_CONTEXT_STALE`; un actor no propietario o sin permisos devuelve 403 `AUTH_FORBIDDEN`.
  Idempotencia: si la línea ya está desvinculada, devuelve 200 y `Changed=false`; cuando elimina la asociación, devuelve `Changed=true`.
  Datos de respuesta: `HojaGastosId`, `LineRecId`, `FileId`, `TicketStatus`, `Changed`.
  Errores AX: `NOT_FOUND` devuelve 404; `CONFLICT` e `INVALID_STATE` devuelven 409; otras reglas de negocio devuelven 422; `ERROR` devuelve 500.
- DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}?deleteMode=0|1|2 (Authorize + X-IND-Company + X-IND-AxUserId)
  `deleteMode`: `0=LineOnly`, `1=HeaderOnly` (alias de `WholeSheet`), `2=WholeSheet`.
  Compatibilidad heredada: `deleteWholeSheet=0|1` sigue admitido si no se envía `deleteMode`. AX procesa 1 y 2 como `deleteWholeSheet`.
  Nota: si `deleteMode` es `LineOnly`, `lineRecId` debe ser distinto de 0 y puede ser negativo para líneas manuales temporales.
  Nota: si `deleteMode` no es `LineOnly`, `lineRecId` puede ser 0.
- POST /api/crm/expensesheets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `page`, `pageSize`.
  Cuerpo opcional: `filter`, `billedMode`, `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`), `projId`, `currencyCode`, `expenseSheetStatus`, `reimbursableExpense` (`INDReimbursableExpense`: `Yes` incluye, `No` excluye, `Both` mezcla), `includeSubordinates` (booleano; `true` = usuario de la cabecera y subordinados directos).
  Nota sobre enums AX: `expenseSheetStatus` y `reimbursableExpense` deben enviarse como valores numéricos obtenidos desde `/api/crm/enums/by-name`.
  Campos de lista de la respuesta: `expenseSheetStatus`, `estadoComentarios`, `exchangeRateMode`, `userId`, `userName`, `exchRate`, `createdDate`, `axCreatedDate`, `reimbursableExpense`, `totalAmountCurrency`, `totalAmountMST`, `totalGrossAmountMST` y `totalReimbursableAmount`.
  Nota sobre totales: `totalAmountCurrency`/`totalAmount` y `totalAmountMST` conservan los totales contables heredados; `totalGrossAmountMST` es el bruto de empresa/MST y no se filtra por reembolso ni Visa; `totalReimbursableAmount` es el reembolso de empresa/MST e incluye solo `ReimbursableExpense=Yes`, sin consultar Visa. Con AX heredado, el reembolso usa `totalAmountMST` como alternativa y el bruto queda nulo.
  Nota JSON: Web API serializa las propiedades en PascalCase; en JavaScript usar `TotalGrossAmountMST` y `TotalReimbursableAmount`.
  Nota AX: `axCreatedDate` expone la fecha final adicional devuelta por el contrato AX y se normaliza a `DD.MM.YYYY`; actualmente refleja la misma fecha de creación que `createdDate`.
  Orden: `createdDate` descendente, `HojaGastosId` descendente como desempate; las filas sin `createdDate` válida quedan al final.
  `billedMode`: `0` = no facturado, `1` = facturado, `2` = ambos (valor predeterminado `0`).

## Tickets de hojas de gastos
- POST /api/crm/expensesheets/tickets (Authorize + X-IND-Company + X-IND-AxUserId)
  `mode=0`: crea cabecera, líneas y `DocuRef`.
  `mode=1`: crea solo cabecera y `DocuRef`.
  `mode=2`: agrega líneas a `existingFileId`.
  Cuerpo para `mode=0|1`: `description`, `currencyCode`, `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `urlFile`.
  Cuerpo para `mode=0|2`: `lines[]` con `description`, `qty`, `price` (`totalAmount` opcional). Las líneas de ticket pueden informar `price`/`totalAmount` negativos; `qty` no puede ser negativo y `qty = 0` solo se acepta con un total de línea negativo.
  Opcionales: `totalAmount`, `comentario`, `fileExtension`, `existingFileId`, `gastoType`, `ocrJson`, `normalizedJson`, `ticketDate` (`DDMMYYYY` o `DD.MM.YYYY`), `ticketTime` (`HH:mm`, `HH:mm:ss` o segundos 0..86399).
  Nota sobre enums AX: `gastoType` debe enviarse como valor numérico obtenido desde `/api/crm/enums`.
  Los datos de respuesta incluyen `TotalAmountCurrency` y `TotalAmountMST` cuando AX devuelve los campos adicionales de cabecera. `TotalAmount` se mantiene como alias heredado de `TotalAmountCurrency`.
  Duplicidad: si el mismo usuario ya tiene otro ticket con igual `ticketDate` y una `ticketTime` válida, responde 409 con `CRM_EXPENSESHEET_TICKET_DUPLICATE`. Una hora ausente o `0` no participa en esta validación; los usuarios distintos no colisionan.
- POST /api/crm/expensesheets/tickets/quick-create (Authorize + X-IND-Company + X-IND-AxUserId)
  Tipo de contenido obligatorio: `multipart/form-data`.
  Cuerpo obligatorio: `ticketImage` (jpg/jpeg/png/webp, máximo 50 MB).
  Cuerpo opcional: `currencyCode`, `description`, `comentario`, `existingHojaGastosId`, `projId` (alias heredado: `projectId`).
  Flujo: crea un ticket provisional, sube el archivo, extrae el borrador de IA, finaliza el ticket y, opcionalmente, lo vincula a una hoja de gastos existente.
  Al vincularlo, vuelve a leer el ticket persistido como fuente monetaria. EUR mantiene el contrato local; una divisa extranjera exige importe original, `TotalAmountMST`/`AmountMST` y `ExchRate` positivos. Si el estado final queda incompleto, responde 422 antes de llamar a AX y aplica la reversión de la creación rápida.
  El alta provisional conserva `TicketDate` vacío hasta terminar el OCR; la fecha de hoy se usa solo como `DocuRef.INDTransDate` obligatorio y no participa en la duplicidad del ticket.
  Datos de respuesta: `FileId`, `UrlFile`, `FileName`, `ProcessedByAI`, `LinkedToSheet`, `HojaGastosId`, `TotalAmountCurrency`, `TotalAmountMST`, `CompletedStage`, `FailedStage`, `RollbackAttempted`, `RollbackSucceeded`, `RollbackMessage`, `StepTraceIds.{TicketCreate,FileUpload,DraftExtract,TicketFinalize,SheetLink}`.
  Si se produce un error después de crear `FileId`, el endpoint intenta revertir internamente el blob y el ticket AX; se conserva el error original y el resultado de la reversión viaja en los campos `Rollback*`.
  Errores relevantes: 409 por duplicidad (`CRM_EXPENSESHEET_TICKET_DUPLICATE`), 422 por validación, 429 por límite de IA, 503 por indisponibilidad del servicio de IA y 500 por error interno.
- GET /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Devuelve la cabecera y las líneas del ticket.
  La cabecera incluye `processedByAI` (booleano), `gastoType` (entero), `hojaGastosIdDisplay` (cadena), `ocrJson` (cadena), `normalizedJson` (cadena), `ticketDate`, `ticketTime`, `totalAmountCurrency` y `totalAmountMST`.
  Nota sobre totales: `totalAmountCurrency` viene de `INDTicketInfoTable.TotalAmount`; `totalAmountMST` viene de `INDTicketInfoTable.AmountMST`. `totalAmount` y `amountMST` se mantienen como alias heredados.
  Las líneas incluyen `AdjustmentAmount` cuando AX devuelve el indicador `INDTicketInfoLine.Adjustment`.
  Cada elemento de `Lines[*]` incluye también `ReimbursableExpense` (`int?`, `0=Yes`, `1=No`) y `ReimbursableAmount` (`decimal?`, importe en divisa de la empresa), obtenidos de la `CRMHojaGastosLine` vinculada al ticket.
  Compatibilidad: ambos campos son `null` cuando AX devuelve el contrato heredado o cuando no existe una vinculación única con `CRMHojaGastosLine`. Si existen varias líneas de ticket, los valores se repiten como metadatos de la misma línea de hoja vinculada; no son importes propios de cada línea de ticket y no deben sumarse.
- POST /api/crm/expensesheets/tickets/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `page`, `pageSize`.
  Cuerpo opcional: `searchKey` (compatibilidad: `filter`), `status`, `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`), `currencyCode`, `gastoType`, `processedByAI` (booleano).
  Nota sobre enums AX: `status` y `gastoType` deben enviarse como valores numéricos obtenidos desde `/api/crm/enums`.
  Nota: `createdDateFrom`/`createdDateTo` son opcionales; si ambos llegan, se valida `from <= to`. La fecha de referencia de los filtros y la respuesta es `ticketHeader.createdDate`.
  Elementos de respuesta: `FileId`, `Description`, `Status`, `ProcessedByAI`, `CurrencyCode`, `TotalAmount`, `TotalAmountCurrency`, `TotalAmountMST`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `GastoType`.
  Nota: `TotalAmount` se mantiene como alias heredado de `TotalAmountCurrency`.
- POST /api/crm/expensesheets/tickets/link/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `page`, `pageSize`.
  Cuerpo opcional: `searchKey` (compatibilidad: `filter`), `createdDateFrom` (`DDMMYYYY` o `DD.MM.YYYY`), `createdDateTo` (`DDMMYYYY` o `DD.MM.YYYY`), `currencyCode`, `gastoType`, `processedByAI` (booleano).
  Nota sobre enums AX: `gastoType` debe enviarse como valor numérico obtenido desde `/api/crm/enums`.
  Prefiltros fijos en origen: estado pendiente interno de AX y `totalAmount != 0`.
  Nota: la fecha de referencia de filtros y respuesta es `ticketHeader.createdDate`.
  Elementos de respuesta: `FileId`, `Description`, `CurrencyCode`, `TotalAmount`, `TotalAmountCurrency`, `TotalAmountMST`, `TransDate`, `TicketDate`, `TicketTime`, `FileName`, `ProcessedByAI`, `GastoType`.
  Nota: `TotalAmount` se mantiene como alias heredado de `TotalAmountCurrency`.
- POST /api/crm/expensesheets/tickets/link/bulk (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo heredado admitido: `expenseSheetId`, `ticketIds[]` (equivale a `selectionMode = selected`).
  Cuerpo ampliado:
  - `expenseSheetId` obligatorio
  - `selectionMode` opcional (`selected` por defecto, `filtered`)
  - `ticketIds[]` obligatorio en `selected`
  - `filters` obligatorio en `filtered`: `searchKey` (compat: `filter`), `createdDateFrom`, `createdDateTo`, `currencyCode`, `gastoType`, `processedByAI`
  - `excludedIds[]` opcional en `filtered`
  En `filtered` reutiliza la misma resolución en el servidor que `tickets/link/list`, con prefiltros base de estado pendiente interno de AX y `totalAmount != 0`.
  Reutiliza `createExpenseSheet` en modo `2` para añadir una línea por ticket a una hoja existente. La API vuelve a leer cada ticket en el servidor y no acepta importes monetarios autoritativos desde el navegador. EUR conserva valores opcionales vacíos; una divisa extranjera exige `CurrencyCode`, importe original, `TotalAmountMST`/`AmountMST` y `ExchRate` positivos antes de llamar a AX. La línea generada usa el proyecto de cabecera solo cuando es elegible; si la cabecera está vacía, contiene el marcador VARIOS o su proyecto ya no es elegible, queda sin proyecto.
  Valida la hoja de destino, los permisos, la editabilidad y la deduplicación, y admite un resultado parcial.
  Un estado monetario incompleto se informa por ticket en `failed[]`; no se crea su contenedor AX y el resto del lote puede continuar. Datos de respuesta: `expenseSheetId`, `requestedCount`, `linkedCount`, `skippedCount`, `failedCount`, `linkedTicketIds`, `skipped[]`, `failed[]`.
- PUT /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza la cabecera y `DocuRef` (`description`, `currencyCode`, `gastoType`, `totalAmount`, `amountMST`, `exchRate`, `status`, `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `ticketDate` (`DDMMYYYY` o `DD.MM.YYYY`), `ticketTime` (`HH:mm`, `HH:mm:ss` o segundos 0..86399), `comentario`, `urlFile`, `fileName`, `fileExtension`, `processedByAI`, `ocrJson`, `normalizedJson`).
  Puede responder 409 con `CRM_EXPENSESHEET_TICKET_DUPLICATE` si la fecha y hora informadas ya existen para otro ticket del mismo usuario.
  Nota: si el ticket vinculado está en la misma divisa de reembolso de la hoja, editar `amountMST` conserva `exchRate`; si la divisa difiere, AX recalcula `exchRate` con `totalAmount * 100 / amountMST`.
  Los datos de respuesta incluyen `TotalAmount`, `TotalAmountCurrency`, `AmountMST`, `TotalAmountMST` y `ExchRate`; `TotalAmount`/`AmountMST` quedan como alias heredados.
- POST /api/crm/expensesheets/tickets/{fileId}/total-adjustment (Authorize + X-IND-Company + X-IND-AxUserId)
  Ajusta `INDTicketInfoTable.TotalAmount` y crea una línea diferencial en `INDTicketInfoLine` cuando cambia el importe.
  Body required: `totalAmount` (nuevo total de cabecera, mayor o igual que 0).
  AX calcula `differenceAmount = totalAmount nuevo - TotalAmount anterior`; la diferencia puede ser positiva, negativa o cero.
  Si hay diferencia, la línea se crea o recalcula con `description` fija `AJUSTE DE IMPORTE TOTAL`, `Qty = 1`, `Price = differenceAmount`, `TotalAmount = differenceAmount` y `Adjustment = Yes` en AX, expuesto como `AdjustmentAmount`.
  Si ya existía una línea de ajuste y un cambio posterior deja en `0` la diferencia entre la cabecera y la suma de líneas normales, AX elimina la línea `Adjustment`; en ese caso `AdjustmentLineRecId` vuelve vacío/0 y `AdjustmentAmount` se devuelve como `false`.
  Datos de respuesta: `FileId`, `PreviousTotalAmount`, `NewTotalAmount`, `TotalAmountCurrency`, `TotalAmountMST`, `DifferenceAmount`, `AdjustmentLineRecId`, `AdjustmentLineCreated`, `AdjustmentDescription`, `AdjustmentAmount`.
- POST /api/crm/expensesheets/tickets/{fileId}/ia (Authorize + X-IND-Company + X-IND-AxUserId)
  Reemplaza la cabecera y las líneas del ticket con datos de IA.
  Puede responder 409 con `CRM_EXPENSESHEET_TICKET_DUPLICATE` si la fecha y hora detectadas ya existen para otro ticket del mismo usuario.
  Reglas:
  - Reemplazo total de líneas (borrado e inserción).
  - Marca `processedByAI=true`.
  - Usa el método AX atómico `updateExpenseSheetTicketFromIA`.
  - Compatibilidad de entrada: si llega un envoltorio de tipo `expensefromticket` (`{ Success, Message, Data, TraceId }`), el backend adapta automáticamente `Data` al contrato esperado.
  Cuerpo: `description`, `currencyCode`, `gastoType` (opcional), `totalAmount` (opcional), `amountMST` (opcional), `exchRate` (opcional), `transDate` (`DDMMYYYY` o `DD.MM.YYYY`), `ticketDate` (`DDMMYYYY` o `DD.MM.YYYY`, opcional), `ticketTime` (`HH:mm`, `HH:mm:ss` o segundos 0..86399, opcional), `comentario` (opcional), `urlFile`, `fileName` (opcional), `ocrJson` (opcional), `normalizedJson` (opcional), `fileExtension` (opcional), `lines[]`. Las líneas pueden llevar importes negativos como descuento; si `qty = 0`, el total de línea debe ser negativo. Si `currencyCode` no es EUR y no llegan `amountMST` ni `exchRate`, la API calcula automáticamente `amountMST` con el tipo `currencyCode -> EUR` para la fecha del ticket.
  Los datos de respuesta incluyen `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`; `TotalAmount` se mantiene como alias heredado de `TotalAmountCurrency`.
- POST /api/crm/expensesheets/tickets/{fileId}/file?extension=jpg (Authorize + X-IND-Company + X-IND-AxUserId)
  Tipo de contenido: `multipart/form-data` (primer archivo del cuerpo).
  Carga o reemplaza la imagen en Azure Blob y actualiza `INDURLFile` e `INDFilename` en AX.
  Formato de nombre aplicado: `yyyyMMddHHmmss_{axUserId}_{fileId}.{ext}`.
- DELETE /api/crm/expensesheets/tickets/{fileId}/file (Authorize + X-IND-Company + X-IND-AxUserId)
  Primero limpia de forma protegida `INDURLFile` e `INDFilename` en AX y solo después intenta eliminar el blob.
  Protección: si el ticket está en estado `Assigned`, la limpieza AX se rechaza y no se toca el blob; devuelve 409 con `CRM_EXPENSESHEET_TICKET_ASSIGNED`.
  Datos de respuesta: `FileId`, `BlobDeleted`. `BlobDeleted=false` es posible si AX se limpió correctamente pero el blob no existía, la URL no se podía resolver o Azure no confirmó la eliminación; el fallo se registra y no revierte la limpieza AX.
- DELETE /api/crm/expensesheets/tickets/{fileId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina ticket completo.
  Consulta opcional: `lineRecId` (si se envía, elimina solo esa línea granular mediante el método unificado de AX).
  Nota: si se envía `lineRecId`, debe ser distinto de 0 y puede ser negativo para líneas temporales.
  Protección: sin `lineRecId`, un ticket vinculado a una línea no se elimina y devuelve 409 con `CRM_EXPENSESHEET_TICKET_ASSIGNED`; primero debe desvincularse. La eliminación granular conserva su comportamiento actual.
- POST /api/crm/expensesheets/tickets/{fileId}/lines (Authorize + X-IND-Company + X-IND-AxUserId)
  Crea una línea granular en `INDTicketInfoLine`.
  Cuerpo: `description`, `qty`, `price`, `totalAmount` opcional.
  Los datos de respuesta incluyen el total de cabecera recalculado como `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.
- PUT /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Actualiza una línea granular de `INDTicketInfoLine`.
  Cuerpo: `description`, `qty`, `price`, `totalAmount` opcional.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para líneas temporales.
  Los datos de respuesta incluyen el total de cabecera recalculado como `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.
- DELETE /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Elimina una línea granular de `INDTicketInfoLine`.
  Nota: `lineRecId` debe ser distinto de 0 y puede ser negativo para líneas temporales.
  Los datos de respuesta incluyen el total de cabecera recalculado como `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.

## Cuentas y contactos
- POST /api/crm/accounts/listContacts (Authorize + X-IND-Company + contexto firmado)
  Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `Content-Type: application/json`.
  Cuerpo obligatorio: `accountNum`, `page` y `pageSize`; la página y su tamaño deben ser mayores que cero y `pageSize` no puede superar 50.
  Respuesta: `IndPagedResponse<object>` con `Total`, `Page`, `PageSize` e `Items` obtenidos de `INDCRMVisitsService.getContactoContainer`.
- POST /api/crm/accounts/listAccounts (Authorize + X-IND-Company + contexto firmado)
  Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision`, `X-IND-Context-Token`, `Content-Type: application/json`.
  Cuerpo obligatorio: `page` y `pageSize`; `accountNum` es opcional. La página y su tamaño deben ser mayores que cero y `pageSize` no puede superar 50.
  Respuesta: `IndPagedResponse<object>` con `Total`, `Page`, `PageSize` e `Items` obtenidos de `INDCRMVisitsService.getAccountContainer`.

## Actividades / Visitas
- GET /api/crm/data-visibility/visible-users?appCode=CRM&moduleCode=VISITAS_GESTION&includeCrmUserId=true (Authorize + X-IND-Company + X-IND-AxUserId)
  Consulta opcional: `appCode` con valor predeterminado `CRM`, `moduleCode` con valor predeterminado `VISITAS_GESTION`, `asOfDate` (`yyyyMMdd` o `yyyy-MM-dd`), `includeCrmUserId` con valor predeterminado `true`.
  AX resuelve usuarios visibles con `INDControlDataVisibility`; no usa subordinados legacy de Hojas de gastos.
  Personas visibles sin usuario AX no se devuelven en la lista.
  Las filas de respuesta incluyen `Alias`, `AxUserId`, `CrmUserId`, `Name`, `Source`, `MutationPolicy`, `MutationPolicyInt`, `MutationPolicyLabel` y `CanMutate`.
  `CanMutate` gobierna la actualización y eliminación de registros del propietario visible; la creación sigue usando siempre el `X-IND-AxUserId` del actor y no debe crear en nombre de subordinados.
- POST /api/crm/activities/create (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `accountNum`, `visitType` (valor numérico AX de `CRMTipoVisita`), `description`, `transDate` (`yyyyMMdd` o `yyyy-MM-dd`).
  Cuerpo opcional: `contactMethod` (valor numérico AX de `INDContactMethod`), `comentarios`, `antecedentes`, `conclusiones`, `userId`, `createdByUserId`.
  Nota: `userId` y `createdByUserId` del cuerpo no gobiernan el actor; la API usa siempre `X-IND-AxUserId`.
  Si `contactMethod` no se envía, AX recibe el valor numérico predeterminado histórico `0`.
  Los datos de respuesta incluyen `RecId`, `OwnerAxUserId`, `INDCreatedByUserId`, `CreatedByUserId` y `UserId`; todos los campos de propietario se derivan del usuario AX de la cabecera cuando la creación es correcta.
- POST /api/crm/activities/list (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `fromDate`, `toDate` (`yyyyMMdd` o `yyyy-MM-dd`), `page`, `pageSize`.
  Cuerpo opcional: `accountNum`, `ownerAxUserId`.
  AX filtra los propietarios visibles con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
  Si `ownerAxUserId` se envía, AX devuelve solo visitas de ese propietario siempre que esté dentro del conjunto visible del usuario de la cabecera.
  Las filas de respuesta incluyen `ActividadId`, `RecId`, `Name`, `AccountNum`, `TransDate`, `ActividadType`, `TipoVisita`, `ContactMethod` y `Description`.
- GET /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  AX valida lectura con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
  Las filas de respuesta incluyen el detalle de visita con `ContactMethod`, `OwnerAxUserId`, `OwnerName` y los alias compatibles `INDCreatedByUserId`, `CreatedByUserId`, `UserId`.
  Nota: `OwnerAxUserId` es el propietario funcional AX canónico de la actividad.
- GET /api/crm/activities/by-code/{code} (Authorize + X-IND-Company + X-IND-AxUserId)
  AX valida lectura con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
  La respuesta `Items[0]` es `ActivityDetailDto` e incluye `ContactMethod`, `OwnerAxUserId`, `OwnerName` y los alias compatibles `INDCreatedByUserId`, `CreatedByUserId`, `UserId`.
- PUT /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `accountNum`, `visitType` (valor numérico AX de `CRMTipoVisita`), `description`, `transDate` (`yyyyMMdd` o `yyyy-MM-dd`).
  Cuerpo opcional: `contactMethod` (valor numérico AX de `INDContactMethod`), `comentarios`, `antecedentes`, `conclusiones`, `userId`.
  Nota: `userId` del cuerpo no gobierna el actor; la API usa siempre `X-IND-AxUserId`.
  AX valida la modificación con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- DELETE /api/crm/activities/{recId} (Authorize + X-IND-Company + X-IND-AxUserId)
  AX valida la modificación con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- POST /api/crm/visits/createVisitaAsistente (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `refRecIdActividad`, `asistenteTipo` (valor numérico AX de `CRMCustVendVisitaAsistente`), `asistenteId`, `contactoRecId`.
  Cuerpo opcional: `createdByUserId`; si se envía distinto de la cabecera, la API lo ignora y usa `X-IND-AxUserId`.
  AX valida la modificación de la visita con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.
- DELETE /api/crm/visits/deleteVisitaAsistente (Authorize + X-IND-Company + X-IND-AxUserId)
  Cuerpo obligatorio: `refRecIdActividad`, `asistenteId`.
  AX valida la modificación de la visita con `INDControlDataVisibility` para `CRM / VISITAS_GESTION`.

## Proyectos
- GET /api/crm/projects/list?filter=...&page=1&pageSize=50 (Authorize + X-IND-Company)
  Nota: `page` y `pageSize` son obligatorios. Si no hay filtro, AX devuelve una lista vacía.

## Plantilla interna
- GET /api/crm/template/sample (Authorize + X-IND-Company + contexto firmado)
  Cabeceras: `Authorization`, `X-IND-Company`, `X-IND-EntraOid`, `X-IND-Context-Version`, `X-IND-Permissions-Revision` y `X-IND-Context-Token`.
  No recibe cuerpo ni parámetros de consulta. La respuesta prevista es `IndPagedResponse<object>`.
  Es una plantilla interna oculta de Swagger mediante `ApiExplorerSettings(IgnoreApi = true)` y llama al método de ejemplo `INDCRMVisitsService.sampleMethod`; no debe tratarse como un contrato funcional ni exponerse como herramienta MCP.
