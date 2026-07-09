# INDTicketInfoTableForm - 2026-07-01

## Objetivo
- Evitar el error de concurrencia del formulario al modificar importes de lineas de ticket.
- Mantener sincronizado el buffer de cabecera despues de que una linea recalcula `INDTicketInfoTable`.

## Causa raiz
- `INDTicketInfoLine.update()` recalcula y guarda la cabecera del ticket por tabla.
- El datasource padre `INDTicketInfoTable` del formulario conservaba un `orig()` anterior.
- Al guardar/refrescar, AX intentaba escribir la cabecera antigua y mostraba el mensaje de "otro usuario ha modificado" para `TotalAmount` y `AmountMST`.

## Cambios
- Se agrego `ticketLineSyncInProgress` en el formulario.
- `INDTicketInfoTable.write()` no ejecuta `super()` cuando el cambio viene del guardado de lineas; solo recarga cabecera y lineas.
- Se agrego `refreshTicketAfterLineSync()` para centralizar `reRead`, `refresh` y `executeQuery`.
- El datasource `INDTicketInfoLine` ahora refresca la cabecera despues de `write()` y `delete()`.

## XPO a importar
- `.codex/Axapta/INDTicketInfoTableForm.xpo`
