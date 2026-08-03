# Cambios Axapta - CRMHojaGastosLine - 2026-08-03

## Objetivo

Consolidar `ReimbursableExpense` como unico criterio de inclusion en el reembolso y conservar `VisaEmpresa` solo como espejo legacy inverso.

## XPO tocados

- `.codex/Axapta/CRMHojaGastosLine.xpo`
- `.codex/Axapta/INDRecalAmountMSTExchange_HojasGastosV2.xpo`
- `.codex/Axapta/INDRecalcularAmountMST_Tickets.xpo`

## Metodos tocados

- `CRMHojaGastosLine.recalculateReimbursableAmount`
- `INDRecalAmountMSTExchange_HojasGastosV2.updateLines`
- `INDRecalAmountMSTExchange_HojasGastosV2.updateReimbursableAmounts`
- `INDRecalAmountMSTExchange_HojasGastosV2` (recorrido multiempresa)
- `INDRecalcularAmountMST_Tickets.UpdateTickets`
- `INDRecalcularAmountMST_Tickets` (recorrido multiempresa)

## Regla funcional vigente

- `ReimbursableExpense = Yes` incluye la linea, asigna `ReimbursableAmount = AmountMST` y deja `VisaEmpresa = No`.
- `ReimbursableExpense = No` excluye la linea, asigna `ReimbursableAmount = 0` y deja `VisaEmpresa = Yes`.
- Cualquier valor de linea distinto de `Yes` se trata de forma segura como excluido.
- La migracion legacy transforma `VisaEmpresa = Yes` en `ReimbursableExpense = No` y `VisaEmpresa = No` en `ReimbursableExpense = Yes`.
- El job acepta un `AmountMST` calculado de cero cuando el importe de origen tambien es cero, para no conservar importes antiguos.

## API y tickets

- No cambia el contrato API ni la posicion de los contenedores AX.
- La API ya expone `ReimbursableExpense`, `ReimbursableAmount`, `TotalGrossAmountMST` y `TotalReimbursableAmount`.
- Los campos reembolsables no se duplican en la tabla de tickets; el endpoint de tickets los obtiene de la linea de gasto vinculada.
- Los cambios ordinarios de ticket y el job de recalculo sincronizan `AmountMST` cuando existe una vinculacion canonica y la hoja permite edicion; despues recalculan el importe reembolsable de la linea.
- El job V2 no fuerza una escritura directa sobre tickets. El job de tickets se ejecuta despues y restablece la coherencia desde el ticket cuando la vinculacion es segura.

## Ejecucion y validacion pendiente

- Importar y compilar los XPO en Axapta.
- Sincronizar el diccionario tras confirmar en AOT los enums `Yes=0`, `No=1` y, en cabecera, `Both=2`.
- Sin permitir escrituras intermedias, ejecutar primero `INDRecalAmountMSTExchange_HojasGastosV2` una sola vez. El propio job recorre `LAN`, `IHI`, `RSI`, `SET`, `REF`, `ISI`, `ISM`, `AZM`, `CUM`, `IST` y `RIS`, y reinicia la cola contable dentro de cada `changeCompany`.
- `TAZ`, `ITA` e `ISE` permanecen excluidas del conjunto heredado de V2. La politica anterior de ejecutar los jobs solo en la compania actual queda reemplazada por recorridos explicitos.
- Ejecutar despues `INDRecalcularAmountMST_Tickets` una sola vez; su propio conjunto recorre `LAN`, `IHI`, `RSI`, `SET`, `REF`, `TAZ`, `ISI`, `ISM`, `AZM`, `CUM`, `IST`, `ITA` e `ISE`. Nunca debe ejecutarse antes del job V2, porque la sincronizacion de tickets recalcula la linea y sustituye el valor legacy de Visa.
- Los conjuntos heredados no son identicos: V2 excluye `TAZ`, `ITA` e `ISE`, mientras el job de tickets si las incluye. Validar expresamente esas tres companias antes de ejecutar la carga en Axapta.
- Verificar una linea legacy con Visa activa y otra sin Visa, una linea nueva incluida y otra excluida, y una linea de importe cero.
- Confirmar que `TotalReimbursableAmount` suma solo lineas `Yes` y que `TotalGrossAmountMST` mantiene todas las lineas validas.
