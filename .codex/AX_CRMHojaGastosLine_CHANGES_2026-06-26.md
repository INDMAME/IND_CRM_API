# Cambios Axapta - CRMHojaGastosLine - 2026-06-26

## Objetivo

Mantener la divisa real en cada linea de hoja de gasto y eliminar la dependencia del marcador de cabecera multidivisa.

## XPO tocados

- `.codex/Axapta/CRMHojaGastosLine.xpo`

## Metodos tocados

- `applyHeaderCurrencyDefaults`
- `calcExchRateFromAmountMST`
- `InitFromHojaGastosTable`
- `LineAmountMST`
- `modifiedField`
- `normalizeCurrencyAmounts`
- `validateCurrencyAmounts`
- `validateWrite`

## Reglas funcionales

- La cabecera solo aporta la divisa local como valor inicial de linea.
- Si una linea cambia `Amount`, `Currency` o `ExchRate`, se recalcula `AmountMST`.
- Si una linea cambia `AmountMST`, desde 2026-07-02 solo se recalcula `ExchRate` cuando `Currency` difiere de la divisa de reembolso; si coincide, se conserva `ExchRate`.
- Divisas no locales requieren `ExchRate > 0`.
- `Qty` y `Price` recalculan `Amount` y pasan por la misma normalizacion de divisa.

## Pendiente

- Importar y compilar el XPO en Axapta.
- Probar lineas EUR/local, divisa extranjera con `ExchRate` y divisa extranjera con `AmountMST`.
