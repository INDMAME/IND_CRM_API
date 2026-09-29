# Secuencia de ticket desde imagen con IA

El flujo de `quick-create` conserva la imagen, analiza el justificante con
Azure Document Intelligence, normaliza los datos con OpenAI, los adapta al
contrato del ticket y los persiste en Axapta.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as Interfaz React de tickets
  participant Proxy as Proxy MVC de tickets
  participant Limit as Control de límites de OpenAI
  participant Api as CrmExpenseSheetTicketsController
  participant Guard as Protecciones base del CRM
  participant Ax as Axapta mediante COM
  participant BlobSvc as Servicio de archivos de tickets
  participant Blob as Azure Blob Storage
  participant Pipe as Servicio de procesamiento de IA
  participant Ocr as Azure Document Intelligence
  participant Norm as Normalizador OpenAI de tickets
  participant OpenAI as OpenAI Responses API

  Ui->>Proxy: POST /api/crm/expensesheets/tickets/quick-create<br/>Imagen multipart + metadatos
  Proxy->>Limit: Reenvía autenticación y contexto
  Limit->>Limit: Comprueba el límite de IA por usuario<br/>y la concurrencia

  alt Límite de uso o concurrencia superado
    Limit-->>Proxy: 429 IndApiResponse<br/>Error de límite de IA
    Proxy-->>Ui: Reintenta o muestra el límite
  else Petición permitida
    Limit->>Api: Continúa con quick-create
    Api->>Guard: Valida JWT, empresa,<br/>usuario AX y contexto CRM
    Guard-->>Api: Petición permitida
    Api->>Api: Lee multipart y valida la imagen<br/>Extensión, tipo y máximo de 50 MB
    Api->>Ax: createExpenseSheetTicket<br/>Ticket provisional solo con cabecera
    Ax-->>Api: fileId y datos provisionales

    Api->>BlobSvc: UploadTicketFile(company, axUser,<br/>fileId, finalFileName, image)
    BlobSvc->>Blob: Guarda la imagen original
    Blob-->>BlobSvc: blobUrl y blobName
    BlobSvc-->>Api: Resultado de carga
    Api->>Ax: updateExpenseSheetTicket<br/>Sincroniza urlFile y fileName
    Ax-->>Api: Metadatos del archivo persistidos

    Api->>Pipe: ProcessFromStoredBlobAsync(blobUrl,<br/>fileName, perfil QuickCreate)
    Pipe->>BlobSvc: CreateReadOnlyBlobUrl(blobUrl)
    BlobSvc-->>Pipe: URL de lectura de corta duración
    Pipe->>Ocr: Analiza el justificante desde Blob<br/>Carga urlSource
    Ocr-->>Pipe: AzureReceiptAnalysisResult<br/>RawJson + PromptJson + campos
    Pipe->>Norm: NormalizeReceiptAsync(resultado OCR,<br/>fileName, perfil)
    Norm->>OpenAI: Petición a Responses API<br/>Prompt + JSON OCR compacto
    OpenAI-->>Norm: JSON de borrador estructurado
    Norm-->>Pipe: ExpenseSheetDraftResponse<br/>normalizedJson + attempts
    Pipe-->>Api: Borrador + ocrJson + normalizedJson

    Api->>Api: Convierte a UpdateExpenseSheetTicketFromIARequest<br/>Cabecera, líneas, ocrJson y normalizedJson

    alt Líneas de ticket válidas
      Api->>Ax: updateExpenseSheetTicket<br/>Reemplaza cabecera y líneas desde IA
      Ax-->>Api: processedByAI, fileName e ids de línea
      Api->>Api: completedStage = ticket-finalized
    else Alternativa solo con cabecera
      Api->>Ax: updateExpenseSheetTicket<br/>Solo la cabecera y el JSON de DocuRef
      Ax-->>Api: processedByAI y fileName
      Api->>Api: Devuelve el ticket para revisión manual
    end

    opt Se proporcionó una hoja existente
      Api->>Ax: getExpenseSheetTicket
      Ax-->>Api: Detalle final del ticket
      Api->>Ax: createExpenseSheet modo 2<br/>Vincula una línea a la hoja existente
      Ax-->>Api: Resultado de la vinculación
      Api->>Api: completedStage = sheet-linked
    end

    Api-->>Proxy: 201 IndApiResponse(QuickCreateResult)<br/>fileId, urlFile, fileName,<br/>processedByAI, linkedToSheet,<br/>completedStage, stepTraceIds
    Proxy-->>Ui: Resultado del ticket creado
  end

  Note over Api,Pipe: El endpoint solo de borrador<br/>/api/ia/service/expensefromticket<br/>usa el mismo flujo IA.<br/>Llama a ProcessFromImageAsync,<br/>escribe un Blob temporal,<br/>lo borra al limpiar<br/>y solo persiste el ticket<br/>cuando persistTicket=true.
```

## Contratos observados

Endpoint principal de creación:

- `POST /api/crm/expensesheets/tickets/quick-create`
- Entrada: imagen multipart y metadatos opcionales.
- Cabeceras de negocio obligatorias: `Authorization`, `X-IND-Company`,
  `X-IND-AxUserId` y las cabeceras de contexto CRM.
- Respuesta correcta: `IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>`.
- Campos observados: `FileId`, `UrlFile`, `FileName`,
  `ProcessedByAI`, `LinkedToSheet`, `HojaGastosId`, `CompletedStage`,
  `StepTraceIds`.

Endpoint de solo borrador con el mismo flujo IA:

- `POST /api/ia/service/expensefromticket`
- Entrada: `ticketImage`; `persistTicket`, `ticketUrlFile` o `urlFile` son
  opcionales.
- Respuesta correcta: `IndApiResponse<ExpenseSheetDraftResponse>`.
- El borrador hereda la forma de alta de gastos y añade `gastoType`,
  `transDate`, `Confidence`, `Warnings`, `RawCurrency`, `Merchant` y el
  `TicketCreation` opcional.

## Mapeo interno de contratos

Azure Document Intelligence devuelve `AzureReceiptAnalysisResult`. Sus campos
relevantes en el servidor son:

- `RawJson`: respuesta OCR original conservada para auditoría o persistencia.
- `PromptJson`: JSON OCR compacto enviado a OpenAI.
- `MerchantName`, `TransactionDate`, `CurrencyCode`, `RawCurrency`,
  `TotalAmount`, `ItemCount`, `Warnings` y `CurrencyHints`.

OpenAI convierte el JSON OCR en `ExpenseSheetDraftResponse` y `normalizedJson`.
Después, `quick-create` genera `UpdateExpenseSheetTicketFromIARequest` con
cabecera, líneas, `ocrJson`, `normalizedJson`, URL, nombre y extensión.

## Efectos laterales

- `quick-create` crea un ticket provisional antes de subir la imagen.
- La imagen se guarda en Azure Blob y sus metadatos se sincronizan con Axapta.
- Se genera una URL de lectura temporal para Azure Document Intelligence.
- OpenAI recibe el JSON OCR compacto, no el archivo original del navegador.
- La actualización final puede reemplazar líneas o limitarse a la cabecera si
  las líneas no son válidas.
- Si se indica una hoja existente, Axapta puede vincular el ticket añadiendo
  una línea.

## Límites vigentes

- La lista exacta de campos multipart debe comprobarse contra el cliente React
  y la colección Postman vigentes antes de publicar o modificar el contrato.
- Los índices de contenedor de Axapta son un detalle del contrato AOT/X++ y no
  se duplican aquí.
