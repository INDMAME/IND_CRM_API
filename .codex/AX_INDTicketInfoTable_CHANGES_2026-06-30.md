# Cambios Axapta - INDTicketInfoTable - 2026-06-30

## Objetivo

Corregir el recalculo de cabecera cuando se modifica una linea normal de ticket.

## Causa raiz

`calcTotalAmount()` habia quedado llamando a `syncAdjustmentLineToTotal()` cuando existia una linea marcada como `Adjustment = Yes`.
Ese comportamiento preservaba el `TotalAmount` de cabecera y ajustaba la linea diferencial para compensar el cambio de una linea normal.

Ese flujo solo es correcto para el ajuste manual de cabecera expuesto por `adjustTotalAmount()`. Para cambios de lineas, la cabecera debe recalcularse desde las lineas actuales.

## Cambios

- `calcTotalAmount()` vuelve a sumar las lineas del ticket para recalcular `TotalAmount`.
- `syncAdjustmentLineToTotal()` se mantiene como flujo explicito de ajuste manual de cabecera, llamado desde `adjustTotalAmount()`.
- `syncAdjustmentLineToTotal()` suma las lineas por `FileId` y repara la referencia `RefRecIdTable` de lineas de ajuste antiguas si faltaba.

## Verificacion de propagacion a linea de gasto

- `adjustTotalAmount()` actualiza `TotalAmount`, guarda con `doUpdate()` y llama despues a `syncHojaGastoLine()`.
- `INDTicketInfoTable.update()` recalcula `TotalAmount`, guarda la cabecera y llama despues a `syncHojaGastoLine()`.
- `syncHojaGastoLine()` usa `CRMHojaGastosLine::FindByFileId()` para localizar una unica linea canonica asociada.
- Si existe una linea canonica y puede sincronizarse, copia `TotalAmount` hacia `CRMHojaGastosLine.Amount`, recalcula divisa y actualiza la hoja.
- La propagacion se omite si no hay linea asociada unica por `FileId`, si la hoja tiene `Voucher`, esta aprobada o su estado no permite edicion.

## Impacto

- Al editar `Qty`, `Price` o `TotalAmount` de una linea normal de ticket, la cabecera vuelve a reflejar la suma de lineas.
- Si el ticket tiene una linea de gasto asociada y editable, el nuevo total de cabecera se propaga a `CRMHojaGastosLine.Amount`.
- No cambia el contrato API ni el shape de respuesta.
- El cambio requiere importar y compilar `INDTicketInfoTable.xpo` en AX.
