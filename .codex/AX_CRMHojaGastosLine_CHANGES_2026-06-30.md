# CRMHojaGastosLine - 2026-06-30

## Objetivo
- Robustecer la sincronizacion inversa desde una linea de hoja de gastos hacia el ticket asociado.
- Evitar que un cambio de importe en la hoja de gastos deje la cabecera del ticket descuadrada frente a sus lineas.

## Cambios
- `FindByFileId()` ya no devuelve un `firstOnly` arbitrario: primero exige una unica linea con `Ticket = Yes`; si no existe, solo hace fallback legacy cuando hay exactamente una linea con ese `FileId`.
- Si un `FileId` apunta a varias lineas y no hay una unica linea de ticket, la busqueda devuelve vacio para evitar actualizar una linea equivocada.
- `canSyncLinkedTicket()` solo permite sincronizar desde la linea canonica devuelta por `FindByFileId()`.
- `syncLinkedTicket()` conserva el comportamiento anterior cuando el ticket tiene una unica linea normal: actualiza esa linea con cantidad, precio e importe de la hoja de gasto.
- Si el ticket tiene varias lineas, no tiene lineas, o la unica linea es de ajuste, se recalcula o crea la linea `Adjustment` con `syncAdjustmentLineToTotal(...)`.
- El total, divisa, cambio e importe MST de la cabecera del ticket se validan despues de sincronizar la linea normal o la linea de ajuste.

## Flujo esperado
1. Cambio en linea de ticket: `INDTicketInfoLine` recalcula la cabecera y `INDTicketInfoTable.syncHojaGastoLine()` actualiza la linea de hoja de gastos.
2. Cambio manual en cabecera de ticket desde formulario: `INDTicketInfoTableForm` llama a `adjustTotalAmount(...)`, recalcula la linea de ajuste y refresca las lineas.
3. Cambio en linea de hoja de gastos asociada: `CRMHojaGastosLine.syncLinkedTicket()` actualiza cabecera y mantiene cuadradas las lineas del ticket.

## XPO a importar
- `.codex/Axapta/CRMHojaGastosLine.xpo`
