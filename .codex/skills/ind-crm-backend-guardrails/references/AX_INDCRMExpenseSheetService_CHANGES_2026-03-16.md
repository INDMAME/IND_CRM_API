## Objetivo

Extender los metodos AX de tickets para aceptar y persistir dos campos JSON grandes en `DocuRef`:

- `INDOCRJson`
- `INDNormalizedJson`

El cambio debe ser retrocompatible y no debe mover indices existentes del contrato `container`.

## Clase AX

- `INDCRMExpenseSheetService`

## Metodos impactados

### `createExpenseSheetTicket`

Contrato ajustado para `headerIn` en modos `0|1`:

- Antes:
  - `[axUserId, descr, currencyCode, totalAmount, transDateStr, comentario, urlFile, fileName, gastoType(optional)]`
- Ahora:
  - `[axUserId, descr, currencyCode, totalAmount, transDateStr, comentario, urlFile, fileName, gastoType(optional), ocrJson(optional), normalizedJson(optional)]`

Persistencia nueva en `DocuRef`:

- `DocuRef.INDOCRJson`
- `DocuRef.INDNormalizedJson`

### `updateExpenseSheetTicketFromIA`

Contrato ajustado para `headerIn`:

- Antes:
  - `[axUserId, fileId, descr, currencyCode, totalAmount, transDateStr, comentario, urlFile, fileName, gastoType(optional)]`
- Ahora:
  - `[axUserId, fileId, descr, currencyCode, totalAmount, transDateStr, comentario, urlFile, fileName, gastoType(optional), ocrJson(optional), normalizedJson(optional)]`

Persistencia nueva en `DocuRef`:

- `DocuRef.INDOCRJson`
- `DocuRef.INDNormalizedJson`

### `updateExpenseSheetTicket`

Contrato ajustado:

- Antes:
  - `_data[13] = gastoType (optional)`
- Ahora:
  - `_data[13] = gastoType (optional)`
  - `_data[14] = ocrJson (optional)`
  - `_data[15] = normalizedJson (optional)`

Persistencia nueva en `DocuRef`:

- `DocuRef.INDOCRJson`
- `DocuRef.INDNormalizedJson`

## Compatibilidad

- Los indices existentes no cambian.
- Los nuevos campos son opcionales.
- Los consumidores actuales siguen funcionando sin enviar los nuevos valores.
- `updateExpenseSheetTicketFromIA` sigue aceptando el header antiguo sin JSON.
- No hay cambios de rutas ni de contratos HTTP obligatorios para clientes existentes.

## Riesgos

- En `updateExpenseSheetTicket`, si API no recupera los valores actuales, un update parcial podria limpiar uno de los JSON por accidente.
- Para evitarlo, la API debe poder leer y reenviar ambos valores cuando haga merge parcial.

## Pendientes API

- Hecho: extender request models de create/update ticket.
- Hecho: extender request model de `UpdateExpenseSheetTicketFromIA`.
- Hecho: extender builders `container` en endpoints que llaman a AX.
- Hecho: merge seguro de `ocrJson` y `normalizedJson` tambien en `UpdateExpenseSheetTicketFromIA` y en el helper usado por `quick-create`.
- Hecho: mantener merge parcial seguro en `UpdateExpenseSheetTicket` leyendo y reenviando ambos JSON.
- Hecho: exponer ambos valores en `getExpenseSheetTicket` y en `ExpenseSheetTicketDetailDto` para soportar merges parciales seguros.

## Verificacion

- Compilacion `MSBuild /t:Compile` correcta.
- Sin cambios de indices existentes.
- Los nuevos campos siguen siendo opcionales.
- `updateExpenseSheetTicketFromIA` ya persiste ambos JSON en la misma llamada atomica que reemplaza lineas.

## Integracion Azure OCR -> OpenAI

### Objetivo aplicado

El flujo activo de IA para tickets ya no usa vision directa de OpenAI en los endpoints publicados.

Ahora el pipeline activo es:

- Blob URL
- Azure Document Intelligence (`prebuilt-receipt`)
- JSON OCR estructurado
- OpenAI normaliza ese JSON al contrato CRM
- AX persiste `INDOCRJson` y `INDNormalizedJson` en `DocuRef`

### Servicios nuevos en API

- `AzureReceiptAnalyzerService`
  - Llama a Azure Document Intelligence por URI (`urlSource`).
  - Devuelve:
    - `RawJson`
    - `PromptJson`
    - metadatos resumidos del receipt

- `OpenAITicketNormalizationService`
  - Usa el mismo modelo OpenAI ya configurado.
  - Ya no envia `input_image` al flujo activo.
  - Normaliza el OCR JSON al contrato existente del draft.

- `TicketAIProcessingService`
  - Orquesta:
    - blob SAS de lectura
    - OCR Azure
    - normalizacion OpenAI
  - Implementa `IND_IExpenseTicketDraftService` para que los endpoints activos usen este pipeline sin romper contrato.

### Endpoints activos ajustados

- `POST /api/crm/expensesheets/tickets/quick-create`
  - Ya no usa vision directa de OpenAI.
  - Usa blob ya subido -> Azure OCR -> OpenAI normalization.
  - Persiste `ocrJson` y `normalizedJson` en `updateExpenseSheetTicketFromIA`.

- `POST /api/ia/service/expensefromticket`
  - Ya no usa vision directa de OpenAI.
  - Sube un blob temporal para OCR por URI.
  - Si `persistTicket=true`, reenvia `ocrJson` y `normalizedJson` tambien en create/update ticket.

### Compatibilidad

- No cambia ninguna ruta publica.
- No cambia el multipart esperado.
- No cambia el response esperado.
- Se mantiene la semantica sincronica de `quick-create`.

### Logging nuevo

Se agregan logs por etapa para ver el pipeline real en produccion:

- `[QUICKCREATE-AI-ARCH]`
- `[QUICKCREATE-DRAFT-START]`
- `[QUICKCREATE-DRAFT-RESULT]`
- `[TICKET-IA-JSON]`
- `[IA-DRAFT-ARCH]`
- `[AZDOCS]`
- `[OPENAI-NORMALIZE]`
- `[TICKET-AI]`

### Verificacion funcional aplicada

- Compilacion correcta con:
  - `C:\Program Files\Microsoft Visual Studio\18\Insiders\MSBuild\Current\Bin\MSBuild.exe`
  - target `Compile`
- La busqueda en controladores y DI ya no muestra uso activo de `input_image`.
- El codigo legacy de vision OpenAI permanece solo como implementacion interna reutilizable, no como pipeline activo de endpoints.
