# Cambios AX - INDTicketInfoTable - 2026-07-29

## Objetivo

Evitar que dos tickets sin hora informada (`TicketTime == 0`) se consideren duplicados solo por compartir fecha, conservar las validaciones funcionales por tipo de gasto y mantener sincronizado el importe reembolsable de la linea vinculada.

## Metodos modificados

- `validateUniqueTicketDateTime()`:
  - omite la comprobacion de duplicidad si falta `TicketDate` o si `TicketTime == 0`;
  - mantiene la comprobacion existente por fecha y hora cuando el ticket tiene una hora valida;
  - muestra `ticketInfoTable.FileId`, correspondiente al registro conflictivo, en el mensaje de error.
- `validateField()`:
  - evalua `FieldNum(INDTicketInfoTable, GastoType)` para que la validacion se dispare sobre el campo correcto de la tabla de tickets;
  - conserva las restricciones de tipos de gasto no habilitados fuera de Mexico;
  - valida que los tickets de kilometraje de ISI tengan un precio por kilometro configurado para la fecha del ticket.
- `syncHojaGastoLine()`:
  - recalcula `ReimbursableAmount` antes del `doUpdate()` de la linea vinculada;
  - mantiene `ReimbursableExpense` como criterio de reembolso y `VisaEmpresa` como espejo heredado inverso.

## Compatibilidad y riesgos

- No cambia los contratos de API.
- No permite duplicados cuando ambos tickets comparten la misma fecha y una misma hora valida.
- La validacion de tipo de gasto requiere importar y compilar la tabla para confirmar la disponibilidad de `priceKmAFecha()` en el entorno AX objetivo.
- Requiere importar y compilar la tabla en Axapta para validar el comportamiento en runtime.

## Casos de prueba manual

1. Misma fecha y ambos `TicketTime == 0`: el segundo ticket se guarda sin error de duplicidad.
2. Misma fecha y misma hora valida: el segundo ticket se rechaza y el error muestra el `FileId` del primero.
3. Misma fecha y horas validas distintas: ambos tickets se guardan.
4. Fechas distintas y misma hora valida: ambos tickets se guardan.
5. Edicion del mismo registro sin cambiar fecha/hora: no se detecta a si mismo como duplicado.
6. En ISI, un ticket de kilometraje sin precio por kilometro para la fecha se rechaza.
7. Al sincronizar un ticket vinculado, `ReimbursableAmount` queda alineado con `AmountMST` y `ReimbursableExpense`.
