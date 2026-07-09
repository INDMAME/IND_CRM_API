# Cambios Axapta - INDTicketInfoTable - 2026-07-02

## Objetivo

Alinear la cabecera de ticket con la nueva regla de divisas: si el ticket esta en la misma divisa de reembolso de la hoja vinculada, editar `AmountMST` no debe recalcular `ExchRate`.

## XPO tocados

- `.codex/Axapta/INDTicketInfoTable.xpo`

## Metodos tocados

- `calcExchRateFromAmountMST`
- `calcTotalAmount`
- `normalizeCurrencyAmounts`
- `reimbursementCurrencyCode`
- `syncAdjustmentLineToTotal`
- `ticketAmountMST`
- `validateCurrencyAmounts`

## Regla funcional vigente

- Si el ticket esta vinculado a una linea de hoja de gastos, la divisa de reembolso se toma de `CRMHojaGastosTable.CurrencyCode`.
- Si no hay linea vinculada o el vinculo es ambiguo, se usa `CompanyInfo::standardCurrency()` como fallback.
- Si `CurrencyCode` coincide con la divisa de reembolso resuelta, no hay conversion de divisa.
- En esa situacion, editar `AmountMST` conserva el importe informado y no recalcula `ExchRate`.
- Si falta `ExchRate` y la divisa es la misma que la de reembolso, se usa `100`.
- Si `CurrencyCode` es distinta a la divisa de reembolso, se mantiene el calculo inverso:
  `ExchRate = TotalAmount * 100 / AmountMST`.
- Cuando el importe de reembolso se recalcula automaticamente en misma divisa, `AmountMST = TotalAmount`.
- Si existe una linea `INDTicketInfoLine.Adjustment = Yes` y un cambio posterior en cabecera o detalle hace que la diferencia entre cabecera y suma de lineas normales sea `0`, AX elimina la linea de ajuste.
- `calcTotalAmount()` vuelve a delegar en `syncAdjustmentLineToTotal('', false)` cuando ya existe ajuste, para que altas, modificaciones y bajas de detalle apliquen la misma regla.
- Cuando no existe ajuste, `calcTotalAmount()` mantiene el comportamiento de suma normal de lineas.

## Pendiente

- Importar y compilar el XPO en Axapta.
- Probar ticket vinculado a una hoja donde `CurrencyCode` coincide con la divisa de reembolso y `AmountMST` manual es distinto de `TotalAmount`: debe conservar `ExchRate`.
- Probar ticket en divisa extranjera con `AmountMST` manual: debe recalcular `ExchRate`.
- Probar ticket con ajuste manual donde una alta, modificacion o baja de linea deja la diferencia en `0`: la linea `AJUSTE DE IMPORTE TOTAL` debe desaparecer.
