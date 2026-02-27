# AX ExpenseSheet Tickets: GastoType y Filtros (2026-02-27)

## Objetivo
Registrar los cambios aplicados en `INDCRMExpenseSheetService.xpo` para luego ajustar los endpoints C# que consumen estos contratos.

## Cambios AX aplicados
Archivo actualizado:
- `.codex/Axapta/INDCRMExpenseSheetService.xpo`

### 1) Integracion de `GastoType` en `INDTicketInfoTable`
Se integro `GastoType` en metodos de ticket que crean, actualizan y devuelven cabecera de `INDTicketInfoTable`.

#### `createExpenseSheetTicket(container _data)`
- Header mode 0/1 ahora soporta campo opcional:
  - `headerIn[9] = gastoType` (opcional)
- Validacion:
  - Usa `isValidGastoType(...)`.
- Persistencia:
  - Al crear ticket: `ticketHeader.GastoType = gastoType` (si no viene, queda `0`).

#### `createExpenseSheetTicket_backup(container _data)`
- Misma integracion que metodo principal:
  - `headerIn[9] = gastoType` (opcional)
  - validacion con `isValidGastoType(...)`
  - persistencia en `ticketHeader.GastoType`

#### `updateExpenseSheetTicket(container _data)`
- Nuevo campo opcional:
  - `_data[13] = gastoType`
- Validacion:
  - Usa `isValidGastoType(...)`.
- Persistencia:
  - Si se informa, actualiza `ticketHeader.GastoType`.

#### `updateExpenseSheetTicketFromIA(container _data)`
- Header ahora soporta:
  - `headerIn[10] = gastoType` (opcional)
- Validacion:
  - Usa `isValidGastoType(...)`.
- Persistencia:
  - Si se informa, actualiza `ticketHeader.GastoType`.

### 2) Salidas AX ampliadas con `GastoType`
#### `getExpenseSheetTicket(container _data)`
- Header extras agrega al final:
  - `... , ProcessedByAI, GastoType`
- Nota compatibilidad:
  - Se agrego al final para no desplazar indices existentes.

#### `getExpenseSheetTicketsList(container _data)`
- Cada row ahora retorna:
  - `[FileId, Description, Status, CurrencyCode, TotalAmount, CreatedByUserId, TransDate, UrlFile, FileName, ProcessedByAI, GastoType]`
- Nota compatibilidad:
  - `GastoType` agregado al final del row.

### 3) `getExpenseSheetTicketsList` con filtros nuevos
Se ampliaron filtros opcionales, replicando el patron de `getExpenseSheetsList`.

Entrada actual:
- `_data[1] = companyId`
- `_data[2] = axUserId`
- `_data[3] = searchKey/filterTxt` (opcional)
- `_data[4] = statusFilter` (opcional, 0|1)
- `_data[5] = createdDateFrom` (requerido, formato yyyymmdd)
- `_data[6] = createdDateTo` (requerido, formato yyyymmdd)
- `_data[7] = currencyCode` (opcional)
- `_data[8] = gastoType` (opcional)

Reglas aplicadas:
- `companyId`, `axUserId`, `createdDateFrom` y `createdDateTo` son obligatorios para ejecutar la consulta.
- Si falta algun requerido o el rango es invalido (`from > to`), AX retorna `Sin datos`.
- `statusFilter` valido: `0..1`.
- `currencyCode` se normaliza en mayusculas.
- `gastoType` opcional se valida con `isValidGastoType(...)`.
- `searchKey/filterTxt` usa `INDSearchKey` y conserva busqueda por `Description` y `FileId` para compatibilidad.
- Rango de fechas obligatorio aplica sobre `DocuRef.INDTransDate`.

## Cambios endpoints C# aplicados (fase AX->API)
Archivos actualizados:
- `Contracts/Requests/GetExpenseSheetTicketsListRequest.cs`
- `Contracts/Requests/CreateExpenseSheetTicketRequest.cs`
- `Contracts/Requests/UpdateExpenseSheetTicketRequest.cs`
- `Contracts/Requests/UpdateExpenseSheetTicketFromIARequest.cs`
- `Contracts/Responses/ExpenseSheetTicketDtos.cs`
- `Controllers/CRM/CrmExpenseSheetTicketsController.cs`

### Contratos request actualizados
- `GetExpenseSheetTicketsListRequest`:
  - Nuevos campos: `searchKey`, `createdDateFrom`, `createdDateTo`, `currencyCode`, `gastoType`.
  - Compatibilidad: `filter` se mantiene y se mapea como fallback de `searchKey`.
- Create/Update/IA:
  - Se agrego `gastoType` para crear/actualizar en AX.

### Validaciones y container AX alineados
- `GetExpenseSheetTicketsList`:
  - Requiere `createdDateFrom` y `createdDateTo` (ademas de headers `X-IND-Company` y `X-IND-AxUserId`).
  - Valida formato fecha (`yyyyMMdd` o `yyyy-MM-dd`) y rango (`from <= to`).
  - Valida `status` (0|1) y `gastoType` (0,1,2,3,4,5,6,7,8,14).
  - Construye container AX en orden fijo:
    - `[company, axUserId, searchKey, status|empty, createdDateFrom, createdDateTo, currencyCode|empty, gastoType|empty]`
- `createExpenseSheetTicket`:
  - En mode 0/1 envia `headerIn[9]=gastoType` cuando aplica.
- `updateExpenseSheetTicket`:
  - Envia `gastoType` en `_data[13]`.
- `updateExpenseSheetTicketFromIA`:
  - Envia `headerIn[10]=gastoType`.

### Mapeos de salida API
- `ExpenseSheetTicketDetailDto` y `ExpenseSheetTicketListItemDto` ahora incluyen `GastoType`.
- `MapExpenseSheetTicketDetail(...)` lee `GastoType` desde extras de cabecera.
- `MapExpenseSheetTicketList(...)` lee `GastoType` desde row de listado.

### Routing y compatibilidad
- No se cambiaron `RoutePrefix` ni plantillas de ruta.
- Se mantiene compatibilidad de `tickets/list` aceptando `filter` como alias de `searchKey`.
- No se detectaron colisiones nuevas entre rutas literales y parametrizadas.

## Cambio adicional aplicado en endpoint de tipo de cambio
Archivo actualizado:
- `Controllers/System/SystemController.cs`

Cambio:
- `Source` ahora retorna codigo fijo por nivel de obtencion:
  - `Banco Central Europeo (ECB)` = proveedor primario
  - `Frankfurter API (fallback nivel 2)` = fallback nivel 2
  - `Open ER API (fallback nivel 3)` = fallback nivel 3

Nota:
- Se mantiene ruta, envelope y estructura de respuesta.
