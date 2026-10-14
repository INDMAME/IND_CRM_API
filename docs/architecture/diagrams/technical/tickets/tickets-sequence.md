# Secuencia de tickets

Los tickets se tratan como justificantes de gastos con carga opcional de
archivo, extracción por IA, líneas y vinculación. El flujo puede escribir en
Blob, crear accesos temporales para OCR, llamar a OpenAI y modificar Axapta.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as Interfaz React de tickets
  participant Proxy as Rutas MVC de tickets
  participant Client as ApiClientService
  participant Api as CrmExpenseSheetTicketsController
  participant Guard as Protecciones base del CRM
  participant Bc as Business Connector COM
  participant Ax as INDCRMExpenseSheetService
  participant Blob as Azure Blob Storage
  participant Ocr as Document Intelligence
  participant Ai as OpenAI

  Note over Ui,Ax: Lista y detalle
    Ui->>Proxy: POST /api/crm/expensesheets/tickets/list
    Proxy->>Client: Solicita la lista de tickets
    Client->>Api: POST /api/crm/expensesheets/tickets/list<br/>Authorization + cabeceras de contexto
    Api->>Guard: Valida token, empresa, usuario AX y contexto
    Guard->>Bc: Abre la sesión AX de la petición
    Bc->>Ax: getExpenseSheetTicketsList
    Ax-->>Api: DTO de la lista de tickets
    Api-->>Ui: IndPagedResponse(lista de tickets)

    Ui->>Proxy: GET /api/crm/expensesheets/tickets/{fileId}
    Proxy->>Client: Solicita el detalle del ticket
    Client->>Api: GET /api/crm/expensesheets/tickets/{fileId}
    Api->>Bc: Ejecuta getExpenseSheetTicket
    Bc->>Ax: getExpenseSheetTicket
    Ax-->>Api: DTO de detalle del ticket
    Api-->>Ui: IndPagedResponse(detalle del ticket)

  Note over Ui,Ax: Alta, actualización y cambios de líneas
    Ui->>Proxy: POST /api/crm/expensesheets/tickets
    Proxy->>Client: DTO de alta del ticket
    Client->>Api: POST /api/crm/expensesheets/tickets
    Api->>Bc: Ejecuta createExpenseSheetTicket
    Bc->>Ax: createExpenseSheetTicket
    Ax-->>Api: Datos del ticket creado
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: PUT /api/crm/expensesheets/tickets/{fileId}
    Proxy->>Client: DTO de actualización del ticket
    Client->>Api: PUT /api/crm/expensesheets/tickets/{fileId}
    Api->>Bc: Ejecuta updateExpenseSheetTicket
    Bc->>Ax: updateExpenseSheetTicket
    Ax-->>Api: Resultado de la actualización
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: POST, PUT o DELETE de líneas del ticket
    Proxy->>Client: DTO de línea del ticket
    Client->>Api: /tickets/{fileId}/lines...
    Api->>Bc: Ejecuta la modificación de línea
    Bc->>Ax: Crea, actualiza o borra una línea
    Ax-->>Api: Resultado de la modificación
    Api-->>Ui: IndApiResponse(object)

  Note over Ui,Ai: Alta rápida y extracción con IA
    Ui->>Proxy: POST /api/crm/expensesheets/tickets/quick-create<br/>Archivo multipart + vinculación opcional
    Proxy->>Client: Reenvía la petición multipart
    Client->>Api: POST quick-create
    Api->>Guard: Valida token, empresa, usuario AX y contexto
    Api->>Bc: Crea un ticket provisional
    Bc->>Ax: createExpenseSheetTicket
    Api->>Blob: Sube el archivo del ticket
    Api->>Blob: Crea acceso temporal para OCR
    Api->>Ocr: Analiza la imagen del justificante
    Ocr-->>Api: Campos extraídos
    Api->>Ai: Normaliza el borrador de gasto
    Ai-->>Api: Cabecera y líneas propuestas
    Api->>Bc: Aplica el resultado de IA
    Bc->>Ax: updateExpenseSheetTicketFromIA
    opt Vincular a una hoja de gastos existente
      Api->>Bc: Vincula los tickets seleccionados
      Bc->>Ax: createExpenseSheet u operación de vinculación
    end
    Api-->>Ui: IndApiResponse(resultado con trazas por paso)

  Note over Ui,Ax: IA y vinculación explícitas
    Ui->>Proxy: POST /api/crm/expensesheets/tickets/{fileId}/ia
    Proxy->>Client: Solicita aplicar IA
    Client->>Api: POST para aplicar IA
    Api->>Blob: Lee el archivo o su referencia URL
    Api->>Ocr: Analiza el justificante
    Api->>Ai: Normaliza la extracción
    Api->>Bc: updateExpenseSheetTicketFromIA
    Bc->>Ax: Actualiza el ticket
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: POST /api/crm/expensesheets/tickets/link/list o /link/bulk
    Proxy->>Client: Solicita la vinculación
    Client->>Api: Endpoints de vinculación
    Api->>Bc: Consulta o vincula tickets
    Bc->>Ax: Operación de vinculación
    Api-->>Ui: IndPagedResponse o IndApiResponse
```

## Efectos laterales

- La carga del archivo escribe en Blob Storage.
- OCR e IA pueden crear acceso Blob temporal y peticiones externas.
- Aplicar IA modifica la cabecera o las líneas del ticket en Axapta.
- La vinculación masiva puede crear o actualizar relaciones con hojas.
- `quick-create` devuelve identificadores de traza por paso si existen.

El método exacto de Axapta usado por cada variante de vinculación no está
confirmado en este documento; debe verificarse en la implementación X++ antes
de cambiar o publicar ese contrato.

## Endpoints relacionados

- `POST /api/crm/expensesheets/tickets`
- `POST /api/crm/expensesheets/tickets/quick-create`
- `POST /api/crm/expensesheets/tickets/list`
- `POST /api/crm/expensesheets/tickets/link/list`
- `POST /api/crm/expensesheets/tickets/link/bulk`
- `GET /api/crm/expensesheets/tickets/{fileId}`
- `PUT /api/crm/expensesheets/tickets/{fileId}`
- `DELETE /api/crm/expensesheets/tickets/{fileId}`
- `POST /api/crm/expensesheets/tickets/{fileId}/ia`
- `POST /api/crm/expensesheets/tickets/{fileId}/file`
- `DELETE /api/crm/expensesheets/tickets/{fileId}/file`
- Endpoints `POST`, `PUT` y `DELETE` de líneas bajo
  `/api/crm/expensesheets/tickets/{fileId}/lines`.
- `POST /api/ia/service/speech`
- `POST /api/ia/service/expensefromticket`
- `POST /api/ia/service/expensesheets/ask`
