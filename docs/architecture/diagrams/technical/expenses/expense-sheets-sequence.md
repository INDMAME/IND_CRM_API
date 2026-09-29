# Secuencia de hojas de gastos

Las operaciones pasan por el mismo proxy web y cliente API. Las lecturas
devuelven listas paginadas o detalles; las modificaciones pueden cambiar la
cabecera o las líneas en Axapta.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as Interfaz de gastos React/Razor
  participant Proxy as Rutas MVC de gastos
  participant Client as ApiClientService
  participant Api as CrmExpenseSheetsController
  participant Guard as Protecciones base del CRM
  participant Bc as Business Connector COM
  participant Ax as INDCRMExpenseSheetService
  participant Blob as Proxy o almacenamiento Blob

  Note over Ui,Ax: Operaciones de lectura
    Ui->>Proxy: POST /api/crm/expensesheets/list<br/>Filtros, página y tamaño
    Proxy->>Client: Lista hojas de gastos
    Client->>Api: POST /api/crm/expensesheets/list<br/>Authorization + cabeceras de contexto
    Api->>Guard: Valida token, empresa, usuario AX y contexto
    Guard->>Bc: Abre la sesión AX de la petición
    Bc->>Ax: getExpenseSheetsList
    Ax-->>Bc: Resultado de lista
    Bc-->>Api: DTO mapeados
    Api-->>Client: IndPagedResponse(ExpenseSheetListItemDto)
    Client-->>Proxy: Envoltorio
    Proxy-->>Ui: Respuesta JSON

    Ui->>Proxy: GET /api/crm/expensesheets/{hojaGastosId}
    Proxy->>Client: Obtiene el detalle
    Client->>Api: GET /api/crm/expensesheets/{hojaGastosId}
    Api->>Guard: Valida el contexto de la petición
    Guard->>Bc: Abre la sesión AX de la petición
    Bc->>Ax: getExpenseSheet
    Ax-->>Api: IndPagedResponse(ExpenseSheetDetailDto)
    Api-->>Ui: Detalle mediante cliente y proxy

  Note over Ui,Ax: Modificaciones
    Ui->>Proxy: POST /api/crm/expensesheets
    Proxy->>Client: DTO de creación
    Client->>Api: POST /api/crm/expensesheets
    Api->>Guard: Valida token, empresa, usuario AX y contexto
    Guard->>Bc: Abre la sesión AX de la petición
    Bc->>Ax: createExpenseSheet
    Ax-->>Api: Resultado de creación o validación
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: PUT /api/crm/expensesheets/{hojaGastosId}
    Proxy->>Client: DTO de cambio de cabecera
    Client->>Api: PUT /api/crm/expensesheets/{hojaGastosId}
    Api->>Bc: Ejecuta updateExpenseSheetHeader
    Bc->>Ax: updateExpenseSheetHeader
    Ax-->>Api: Resultado de la actualización
    Api-->>Ui: IndApiResponse(object)

    Ui->>Proxy: PUT o DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}
    Proxy->>Client: DTO de cambio de línea
    Client->>Api: Endpoint de modificación de línea
    Api->>Bc: Ejecuta updateExpenseSheetLine o deleteExpenseSheetLine
    Bc->>Ax: Actualiza o borra la línea AX
    Ax-->>Api: Resultado de la modificación
    Api-->>Ui: IndApiResponse(object)

  opt Borrar hoja con archivos de ticket relacionados
    Ui->>Proxy: Borra la hoja de gastos
    Proxy->>Blob: Borra vista previa o archivo vinculado si existe
    Proxy->>Client: Borra o actualiza datos AX
    Note over Proxy,Blob: La ruta de producción para borrar<br/>archivos vinculados no está confirmada.
  end
```

## Endpoints relacionados

- `POST /api/crm/expensesheets/list`
- `GET /api/crm/expensesheets/{hojaGastosId}`
- `POST /api/crm/expensesheets`
- `PUT /api/crm/expensesheets/{hojaGastosId}`
- `PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- `DELETE /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
- `GET /api/crm/expensesheets/currencies`
- `GET /api/crm/expensesheets/subordinates`
- `GET /api/crm/expensesheets/fuel-price-km`

Los controladores revisados también exponen endpoints de tickets bajo el mismo
árbol de rutas. Su flujo se documenta por separado.
