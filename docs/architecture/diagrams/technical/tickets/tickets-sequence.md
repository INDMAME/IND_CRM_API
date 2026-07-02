# Tickets sequence

Tickets are handled as CRM expense-sheet artifacts with optional file upload,
AI extraction, line creation, and link operations. Side effects can include
Blob writes, temporary OCR files, OpenAI calls, and Axapta mutations.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as React ticket UI
  participant Proxy as MVC ticket routes
  participant Client as ApiClientService
  participant Api as CrmExpenseSheetTicketsController
  participant Guard as Base CRM guards
  participant Bc as Business Connector COM
  participant Ax as INDCRMExpenseSheetService
  participant Blob as Azure Blob Storage
  participant Ocr as Document Intelligence
  participant Ai as OpenAI

  Note over Ui,Ax: List and detail
    Ui->>Proxy: POST /api/crm/expensesheets/tickets/list
    Proxy->>Client: Ticket list request
    Client->>Api: POST /api/crm/expensesheets/tickets/list<br/>Authorization + context headers
    Api->>Guard: Validate token, company, AX user, context
    Guard->>Bc: Open request AX session
    Bc->>Ax: getExpenseSheetTicketsList
    Ax-->>Api: Ticket list DTOs
    Api-->>Ui: IndPagedResponse(ticket list)

    Ui->>Proxy: GET /api/crm/expensesheets/tickets/{fileId}
    Proxy->>Client: Ticket detail request
    Client->>Api: GET /api/crm/expensesheets/tickets/{fileId}
    Api->>Bc: Execute getExpenseSheetTicket
    Bc->>Ax: getExpenseSheetTicket
    Ax-->>Api: Ticket detail DTO
    Api-->>Ui: IndPagedResponse(ticket detail)

  Note over Ui,Ax: Create, update, and line changes
    Ui->>Proxy: POST /api/crm/expensesheets/tickets
    Proxy->>Client: Create ticket DTO
    Client->>Api: POST /api/crm/expensesheets/tickets
    Api->>Bc: Execute createExpenseSheetTicket
    Bc->>Ax: createExpenseSheetTicket
    Ax-->>Api: Created ticket data
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: PUT /api/crm/expensesheets/tickets/{fileId}
    Proxy->>Client: Update ticket DTO
    Client->>Api: PUT /api/crm/expensesheets/tickets/{fileId}
    Api->>Bc: Execute updateExpenseSheetTicket
    Bc->>Ax: updateExpenseSheetTicket
    Ax-->>Api: Update result
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: POST, PUT, or DELETE ticket lines
    Proxy->>Client: Ticket line DTO
    Client->>Api: /tickets/{fileId}/lines...
    Api->>Bc: Execute line mutation
    Bc->>Ax: create, update, or delete ticket line
    Ax-->>Api: Mutation result
    Api-->>Ui: IndApiResponse(object)

  Note over Ui,Ai: Quick create and AI extraction
    Ui->>Proxy: POST /api/crm/expensesheets/tickets/quick-create<br/>multipart file + optional sheet link
    Proxy->>Client: Forward multipart request
    Client->>Api: POST quick-create
    Api->>Guard: Validate token, company, AX user, context
    Api->>Bc: Create provisional ticket
    Bc->>Ax: createExpenseSheetTicket
    Api->>Blob: Upload ticket file
    Api->>Blob: Create temporary OCR access
    Api->>Ocr: Analyze receipt image
    Ocr-->>Api: Extracted receipt fields
    Api->>Ai: Normalize expense draft
    Ai-->>Api: Draft expense lines and header
    Api->>Bc: Apply AI result
    Bc->>Ax: updateExpenseSheetTicketFromIA
    opt Link to an existing expense sheet
      Api->>Bc: Link selected tickets to sheet
      Bc->>Ax: createExpenseSheet or link operation
    end
    Api-->>Ui: IndApiResponse(quick-create result with step trace ids)

  Note over Ui,Ax: Explicit AI and linking
    Ui->>Proxy: POST /api/crm/expensesheets/tickets/{fileId}/ia
    Proxy->>Client: Apply AI request
    Client->>Api: POST apply AI
    Api->>Blob: Read ticket file or URL reference
    Api->>Ocr: Analyze receipt
    Api->>Ai: Normalize extraction
    Api->>Bc: updateExpenseSheetTicketFromIA
    Bc->>Ax: Update ticket
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: POST /api/crm/expensesheets/tickets/link/list or /link/bulk
    Proxy->>Client: Link request
    Client->>Api: Link endpoints
    Api->>Bc: Query or link tickets
    Bc->>Ax: Ticket link operation
    Api-->>Ui: IndPagedResponse or IndApiResponse
```

## Side effects

- Ticket file upload writes to Blob Storage.
- OCR and AI flows may create temporary blob access and external-service
  requests.
- Applying AI mutates ticket header/line data in Axapta.
- Bulk linking may create or update expense-sheet relationships in Axapta.
- `quick-create` returns step trace identifiers when available.

The exact Axapta method used for every link variant is pendiente de validar
from X++ implementation details.

## Related endpoints

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
- `POST`, `PUT`, and `DELETE` line endpoints under
  `/api/crm/expensesheets/tickets/{fileId}/lines`.
- `POST /api/ia/service/speech`
- `POST /api/ia/service/expensefromticket`
- `POST /api/ia/service/expensesheets/ask`
