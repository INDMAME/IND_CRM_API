# Cambios Axapta - CRMHojaGastosLine - 2026-07-02

## Objetivo

Ajustar la normalizacion de divisa para que una linea no recalcule `ExchRate` cuando la divisa de la linea coincide con la divisa de reembolso de la hoja.

## XPO tocados

- `.codex/Axapta/CRMHojaGastosLine.xpo`

## Metodos tocados

- `calcExchRateFromAmountMST`
- `LineAmountMST`
- `normalizeCurrencyAmounts`
- `validateCurrencyAmounts`

## Regla funcional vigente

- Si `Currency` de linea coincide con `CRMHojaGastosTable.CurrencyCode`, no hay conversion de divisa.
- En esa situacion, editar `AmountMST` conserva el importe informado por el usuario y no recalcula `ExchRate`.
- Si falta `ExchRate` y la divisa es la misma, se inicializa con `100`.
- Si `Currency` de linea es distinta a la divisa de reembolso, se mantiene el calculo inverso:
  `ExchRate = Amount * 100 / AmountMST`.
- `validateCurrencyAmounts` solo exige `ExchRate > 0` o `AmountMST > 0` cuando existe conversion real de divisa.

## Pendiente

- Importar y compilar el XPO en Axapta.
- Probar linea en misma divisa con `AmountMST` manual distinto de `Amount`: debe conservar `ExchRate`.
- Probar linea en divisa extranjera con `AmountMST` manual: debe recalcular `ExchRate`.
