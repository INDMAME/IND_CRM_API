# Cambios Axapta - INDTicketInfoTable - 2026-06-26

## Objetivo

Agregar un metodo atomico en la cabecera de ticket para ajustar `TotalAmount` y crear una linea diferencial en `INDTicketInfoLine` cuando el nuevo total sea distinto al total anterior.

## Metodo nuevo

- `INDTicketInfoTable.adjustTotalAmount(Amount _newTotalAmount, str 20 _createdByUserId)`

## Reglas funcionales

- Lee el `TotalAmount` anterior de la cabecera.
- Rechaza `newTotalAmount < 0`.
- Calcula `differenceAmount = _newTotalAmount - oldTotalAmount`.
- Actualiza `INDTicketInfoTable.TotalAmount` con el nuevo importe.
- Si `differenceAmount != 0`, crea una linea en `INDTicketInfoLine` con:
  - `FileId = INDTicketInfoTable.FileId`
  - `RefRecIdTable = INDTicketInfoTable.RecId`
  - `Description = "AJUSTE DE IMPORTE TOTAL"`
  - `Qty = 1`
  - `Price = differenceAmount`
  - `TotalAmount = differenceAmount`
  - `Adjustment = NoYes::Yes` en AX, expuesto como `AdjustmentAmount` en la API
  - `CreatedByUserId = _createdByUserId`
- La diferencia puede ser positiva o negativa.
- Si la diferencia es cero, solo confirma la cabecera y no crea linea.

## Contrato de retorno

El metodo devuelve:

```text
[lineCreated, oldTotalAmount, newTotalAmount, differenceAmount, adjustmentLineRecId]
```

## Nota de implementacion

La implementacion final esta en `.codex/Axapta/INDTicketInfoTable.xpo`.

- `adjustTotalAmount` actualiza la cabecera, normaliza divisa/reembolso y sincroniza la linea de hoja de gasto vinculada.
- La linea diferencial se inserta con `doInsert()` para no recalcular la cabecera desde una linea parcial de ajuste.
- El sumatorio de lineas usa el buffer declarado de `INDTicketInfoLine`.

## Riesgos y pendientes

- Importar y compilar el metodo en Axapta.
- Importar y compilar tambien `INDTicketInfoLine`, porque el XPO usa el campo real `Adjustment`.
- Confirmar si `TaxPercent` necesita valor por defecto funcional en lineas de ajuste.
- Confirmar que las lineas con `Adjustment = Yes` no se editen como lineas normales en formularios o servicios AX.

## Actualizacion adicional: divisa y reembolso del ticket

Objetivo: alinear `INDTicketInfoTable` con la logica de divisa/reembolso usada en `CRMHojaGastosLine`.

XPO tocados:

- `.codex/Axapta/INDTicketInfoTable.xpo`
- `.codex/Axapta/INDTicketInfoLine.xpo`
- `.codex/Axapta/INDTicketInfoTableForm.xpo`

Metodos agregados o modificados en `INDTicketInfoTable`:

- `adjustTotalAmount`
- `initValue`
- `calcExchRateFromAmountMST`
- `calcTotalAmount`
- `modifiedField`
- `normalizeCurrencyAmounts`
- `ticketAmountMST`
- `validateCurrencyAmounts`
- `insert`
- `update`
- `validateWrite`
- `syncHojaGastoLine`

Reglas funcionales:

- `CurrencyCode` inicializa con `CompanyInfo::standardCurrency()`.
- `ExchRate` inicializa con `100`.
- Si cambia `TotalAmount`, `CurrencyCode` o `ExchRate`, se recalcula `AmountMST`.
- Si cambia `AmountMST`, desde 2026-07-02 solo se recalcula `ExchRate` cuando el ticket esta en divisa distinta a la divisa de reembolso; si coincide, se conserva `ExchRate`.
- Si la divisa no coincide con la divisa de reembolso, debe existir `ExchRate` o `AmountMST`.
- Al sincronizar un ticket ya vinculado a `CRMHojaGastosLine`, se propagan `Currency`, `ExchRate`, `Amount` y `AmountMST`.
- `adjustTotalAmount` crea una linea diferencial con `INDTicketInfoLine.Adjustment = Yes`.

Pendiente:

- `INDTicketInfoTable` exige `ExchRate` para divisa no local. Los flujos API que creen tickets no locales deben informar el tipo de cambio o derivarlo antes de guardar.
