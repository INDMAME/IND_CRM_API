# Cambios Axapta - INDTicketInfoLine - 2026-06-26

## Objetivo

Mantener el desglose de ticket alineado con la cabecera `INDTicketInfoTable`, para que el importe en divisa y el reembolso se recalculen cuando cambian las lineas.

## XPO tocados

- `.codex/Axapta/INDTicketInfoLine.xpo`
- `.codex/Axapta/INDTicketInfoTable.xpo`
- `.codex/Axapta/INDTicketInfoTableForm.xpo`

## Metodos tocados

- `calcTotalAmount`
- `modifiedField`
- `insert`
- `update`
- `delete`
- `syncTicketHeader`

## Reglas funcionales

- `Qty` o `Price` recalculan `TotalAmount` en la linea.
- Insertar, actualizar o borrar una linea recalcula el `TotalAmount` de cabecera.
- La cabecera normaliza despues `AmountMST` y `ExchRate` segun su `CurrencyCode`.

## Riesgos y pendientes

- Importar y compilar los XPO en Axapta.
- Validar manualmente en formulario que el detalle refresca la cabecera y la linea de hoja de gasto vinculada.
- `INDTicketInfoLine` no tiene campos propios `CurrencyCode`, `ExchRate` ni `AmountMST`; la divisa se mantiene en la cabecera del ticket porque el ticket se transforma en una unica `CRMHojaGastosLine`.
