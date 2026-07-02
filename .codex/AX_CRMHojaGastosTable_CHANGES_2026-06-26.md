# Cambios Axapta - CRMHojaGastosTable - 2026-06-26

## Objetivo

Eliminar el uso funcional de la cabecera multidivisa/`CRMCurrencyVarios` en hojas de gasto. La cabecera pasa a representar siempre la divisa local de reembolso con `ExchRate = 100`.

## XPO tocados

- `.codex/Axapta/CRMHojaGastosTable.xpo`
- `.codex/Axapta/CRMHojaGastosTableForm.xpo`

## Metodos tocados

- `initValue`
- `normalizeReimbursementCurrencyDefaults`
- `insert`
- `update`
- `validateWrite`
- `markHeaderVariousFromLine`
- `updateCurrencyDefaultsInLines`
- `UpdateDivisaEnLineas`
- `canChangeCurrencyDefaults`
- `validateCanChangeCurrencyDefaults`

## Reglas funcionales

- `CurrencyCode` de cabecera se fuerza a `CompanyInfo::standardCurrency()`.
- `ExchRate` de cabecera se fuerza a `100`.
- `ExchangeRateMode` de cabecera se fuerza a `Manual`.
- La cabecera ya no se marca con divisa "varios" cuando una linea usa otra divisa.
- La propagacion de divisa de cabecera a lineas queda obsoleta y no modifica lineas.

## Pendiente

- Importar y compilar el XPO en Axapta.
- Verificar que crear/editar cabecera desde form y API mantiene siempre divisa local y `ExchRate = 100`.
