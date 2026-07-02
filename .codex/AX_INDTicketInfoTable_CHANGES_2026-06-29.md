# INDTicketInfoTable - cambios 2026-06-29

## Objetivo
- Sincronizar cambios de ticket hacia la linea de hoja de gastos vinculada por `FileId`.
- Evitar que el ticket modifique lineas de hojas aceptadas, pagadas o fuera de estados editables.

## Cambios
- `syncHojaGastoLine()` valida `CRMHojaGastosLine.canSyncLinkedTicket()` antes de actualizar.
- La actualizacion de la linea usa `doUpdate()` controlado para evitar recursion `ticket -> linea -> ticket`.
- Despues del `doUpdate()` se conservan los efectos de proyecto y coste que antes ejecutaba `CRMHojaGastosLine.update()`.
- La hoja toma siempre `TotalAmount` de la cabecera del ticket como `Amount`.
- `Qty` y `Price` solo se copian desde `INDTicketInfoLine` cuando existe una unica linea y su `TotalAmount` coincide con la cabecera.
- Se mantiene la normalizacion con `AmountMST = Amount * 100 / ExchRate` mediante la logica AX existente.

## Validacion esperada
- Cambios en cabecera o lineas de ticket actualizan la linea de hoja si la hoja sigue editable.
- En tickets multi-linea se sincroniza el total, sin inventar cantidad/precio agregados.
- En hojas bloqueadas no se actualiza la linea desde ticket.
