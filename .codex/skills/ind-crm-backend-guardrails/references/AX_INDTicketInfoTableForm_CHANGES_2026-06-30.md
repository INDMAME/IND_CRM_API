# Cambios Axapta - INDTicketInfoTableForm - 2026-06-30

## Objetivo

Permitir que la edicion manual de `INDTicketInfoTable.TotalAmount` desde el formulario recalcule la linea `Adjustment`.

## Causa raiz

El campo `Importe divisa` del formulario esta enlazado directamente a `INDTicketInfoTable.TotalAmount`.
Al guardar, el datasource ejecutaba el `update()` normal de la tabla, que recalcula el total desde las lineas y por eso el importe manual volvia al valor anterior.

## Cambio

- Se agrego `write()` al datasource `INDTicketInfoTable`.
- Si el usuario cambia `TotalAmount`, se guarda la cabecera normal y despues se llama a `adjustTotalAmount(manualTotalAmount, curUserId())`.
- Tras el ajuste se refrescan la cabecera y las lineas para mostrar la linea `AJUSTE DE IMPORTE TOTAL` recalculada.

## Impacto

- Editar `Importe divisa` en `INDTicketInfoTableForm` actualiza o crea la linea marcada con `Adjustment = Yes`.
- El recalc normal por cambios en lineas sigue perteneciendo a `INDTicketInfoLine.update()` y `INDTicketInfoTable.calcTotalAmount()`.
- No cambia el contrato API.
