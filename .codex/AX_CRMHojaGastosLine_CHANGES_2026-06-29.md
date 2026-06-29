# CRMHojaGastosLine - cambios 2026-06-29

## Objetivo
- Mantener los importes de divisa y reembolso sincronizados con el ticket asociado por `FileId`.
- Respetar hojas bloqueadas por `Voucher`, `Aprobado` o estado no editable.

## Cambios
- `ReimbursableExpense` queda tipado con `INDReimbursableExpenseLines` para exponer solo `No/Yes` en lineas.
- `initValue()` inicializa `ReimbursableExpense` en `Yes`.
- `applyHeaderCurrencyDefaults()` e `InitFromHojaGastosTable()` heredan `No/Yes` desde cabecera y no copian el marcador `Both`.
- Se agrego `canSyncLinkedTicket()` para centralizar la barrera de sincronizacion.
- Se agrego `syncLinkedTicket()` para propagar `Description`, `Currency`, `Amount`, `ExchRate` y `AmountMST` hacia `INDTicketInfoTable`.
- Si el ticket tiene una unica linea de detalle, tambien se sincronizan `Qty`, `Price` y `TotalAmount` hacia `INDTicketInfoLine`.
- `insert()` y `update()` invocan la sincronizacion despues de normalizar y guardar la linea.

## Validacion esperada
- `Qty` o `Price` recalculan `Amount` y despues `AmountMST`.
- `ExchRate` recalcula `AmountMST`.
- `AmountMST` manual recalcula `ExchRate` usando la formula AX en base 100.
- En hojas aprobadas, pagadas o no editables no se propagan cambios hacia el ticket.
