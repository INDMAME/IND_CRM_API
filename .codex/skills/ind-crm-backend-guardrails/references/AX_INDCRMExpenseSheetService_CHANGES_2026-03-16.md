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
