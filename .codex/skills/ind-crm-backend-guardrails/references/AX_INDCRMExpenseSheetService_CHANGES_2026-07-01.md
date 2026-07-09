# INDCRMExpenseSheetService - 2026-07-01

## Objetivo
- Alinear los endpoints de lineas de ticket con la misma regla de ajuste manual usada por la tabla `INDTicketInfoTable`.

## Cambios
- Las rutas AX que crean, actualizan o eliminan lineas de ticket ya no asignan `ticketHeader.TotalAmount` mediante suma directa.
- Ahora llaman a `ticketHeader.calcTotalAmount()` para respetar tickets con linea `Adjustment`.

## Impacto
- Sin linea de ajuste, el total se sigue recalculando como suma de lineas.
- Con linea de ajuste, el total manual se mantiene y se recalcula la diferencia.
- No cambia el contrato API ni los indices del container de respuesta.

## XPO a importar
- `.codex/Axapta/INDCRMExpenseSheetService.xpo`
