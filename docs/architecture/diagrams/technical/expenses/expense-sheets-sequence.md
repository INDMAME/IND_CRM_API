# Expense sheets sequence

Expense sheet operations pass through the same web-app proxy and API client
path. Read operations return paged data or detail envelopes. Mutations return
command envelopes and may update header or line data in Axapta.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as React/Razor expense UI
  participant Proxy as MVC expense routes
  participant Client as ApiClientService
  participant Api as CrmExpenseSheetsController
  participant Guard as Base CRM guards
  participant Bc as Business Connector COM
  participant Ax as INDCRMExpenseSheetService
  participant Blob as Blob proxy or storage

  Note over Ui,Ax: Read operations
    Ui->>Proxy: POST /api/crm/expensesheets/list<br/>filters, page, size
    Proxy->>Client: List expense sheets
    Client->>Api: POST /api/crm/expensesheets/list<br/>Authorization + context headers
    Api->>Guard: Validate token, company, AX user, context
    Guard->>Bc: Open request AX session
    Bc->>Ax: getExpenseSheetsList
    Ax-->>Bc: List result
    Bc-->>Api: Mapped DTOs
    Api-->>Client: IndPagedResponse(ExpenseSheetListItemDto)
    Client-->>Proxy: Envelope
    Proxy-->>Ui: JSON response

    Ui->>Proxy: GET /api/crm/expensesheets/{hojaGastosId}
    Proxy->>Client: Get detail
    Client->>Api: GET /api/crm/expensesheets/{hojaGastosId}
    Api->>Guard: Validate request context
    Guard->>Bc: Open request AX session
    Bc->>Ax: getExpenseSheet
    Ax-->>Api: IndPagedResponse(ExpenseSheetDetailDto)
    Api-->>Ui: Detail envelope through client and proxy

  Note over Ui,Ax: Mutations
    Ui->>Proxy: POST /api/crm/expensesheets
    Proxy->>Client: Create request DTO
    Client->>Api: POST /api/crm/expensesheets
    Api->>Guard: Validate token, company, AX user, context
    Guard->>Bc: Open request AX session
    Bc->>Ax: createExpenseSheet
    Ax-->>Api: Created or validation result
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: PUT /api/crm/expensesheets/{hojaGastosId}
    Proxy->>Client: Header update DTO
    Client->>Api: PUT /api/crm/expensesheets/{hojaGastosId}
    Api->>Bc: Execute updateExpenseSheetHeader
    Bc->>Ax: updateExpenseSheetHeader
    Ax-->>Api: Update result
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: PUT or DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}
    Proxy->>Client: Line mutation DTO
    Client->>Api: Line mutation endpoint
    Api->>Bc: Execute updateExpenseSheetLine or deleteExpenseSheetLine
    Bc->>Ax: Update or delete AX line
    Ax-->>Api: Mutation result
    Api-->>Ui: IndApiResponse(object)

  opt Delete sheet with related ticket files
    Ui->>Proxy: Delete expense sheet
    Proxy->>Blob: Delete linked preview or blob file if available
    Proxy->>Client: Delete or update AX data
    Note over Proxy,Blob: Exact production route is pendiente de validar.
  end
```

## Related endpoints

- `POST /api/crm/expensesheets/list`
- `GET /api/crm/expensesheets/{hojaGastosId}`
- `POST /api/crm/expensesheets`
- `PUT /api/crm/expensesheets/{hojaGastosId}`
- `PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- `DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- `GET /api/crm/expensesheets/currencies`
- `GET /api/crm/expensesheets/subordinates`
- `GET /api/crm/expensesheets/fuel-price-km`

The reviewed controllers also expose ticket endpoints under the same
expense-sheet route tree. Ticket-specific flow is documented separately.
