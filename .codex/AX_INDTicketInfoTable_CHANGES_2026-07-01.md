# INDTicketInfoTable - 2026-07-01

## Objetivo
- Alinear el recalculo de tickets con la regla funcional de ajuste manual de importe total.
- Si ya existe una linea `Adjustment`, el total de cabecera se mantiene como importe total informado manualmente.

## Causa raiz
- `calcTotalAmount()` estaba sumando siempre todas las lineas.
- Eso era correcto para tickets sin ajuste, pero incorrecto despues de informar un total manual.
- En tickets con ajuste, modificar, crear o borrar una linea normal debe recalcular la linea de ajuste, no cambiar el total manual de cabecera.

## Cambios
- `calcTotalAmount()` detecta si existe una linea con `Adjustment = Yes`.
- Si existe, llama a `syncAdjustmentLineToTotal('', false)` para recalcular la diferencia contra el total de cabecera actual.
- Si no existe ajuste, mantiene el comportamiento de suma normal de lineas.
- `update()` de cabecera deja de recalcular lineas; solo normaliza importes/divisa y guarda la cabecera.
- Los cambios de lineas siguen recalculando desde `INDTicketInfoLine.syncTicketHeader()` dentro de su propio `ttsbegin`.
- El ajuste manual sigue recalculando desde `adjustTotalAmount()` dentro de su propio `ttsbegin`.

## Flujo esperado
1. El usuario informa un total reembolsable manual.
2. `adjustTotalAmount()` crea o actualiza `AJUSTE DE IMPORTE TOTAL` con la diferencia.
3. Si se edita, crea o borra una linea normal, `calcTotalAmount()` conserva el total manual y recalcula la linea de ajuste.
4. La linea de hoja de gasto asociada queda sincronizada con el total manual de cabecera.

## XPO a importar
- `.codex/Axapta/INDTicketInfoTable.xpo`
