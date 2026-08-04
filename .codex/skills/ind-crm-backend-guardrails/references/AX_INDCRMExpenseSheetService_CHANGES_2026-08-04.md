# Cambios AX - INDCRMExpenseSheetService - 2026-08-04

## Objetivo

Permitir vincular y desvincular un ticket digitalizado en una linea manual ya persistida, limitado a hojas de gastos en estado `Draft`, sin aprobar y sin `Voucher`.

## Metodos nuevos

- `buildExpenseSheetLineTicketResult`: normaliza la respuesta `buildHeader` con extras estables: `[reasonCode, hojaId, lineRecId, fileId, ticketStatus, changed]`.
- `linkExpenseSheetLineTicket`: recibe `[companyId, ownerAxUserId, viewerAxUserId, hojaGastosId, lineRecId, fileId]`, bloquea cabecera, linea y ticket en ese orden, valida propietario, autorizacion y elegibilidad, vincula el `FileId` y sincroniza el estado del ticket.
- `unlinkExpenseSheetLineTicket`: recibe `[companyId, ownerAxUserId, viewerAxUserId, hojaGastosId, lineRecId]`, mantiene la misma frontera de estado, propietario y autorizacion y limpia solo `FileId`. Si el ticket existe para el propietario, recalcula su estado; si falta, permite reparar el vinculo huerfano.

Los codigos funcionales estables son `NOT_FOUND`, `FORBIDDEN`, `INVALID_STATE`, `CONFLICT` e `INVALID_TICKET`. Un ticket inexistente al vincular devuelve `NOT_FOUND`; `FORBIDDEN` indica que el viewer firmado no puede modificar al propietario; `INVALID_TICKET` queda reservado para estado, importe o documento no validos. `INVALID_INPUT` y `ERROR` cubren entradas invalidas y fallos no funcionales. En exito `reasonCode` queda vacio. `ticketStatus` queda vacio cuando no existe un ticket aplicable y `changed` se devuelve como `0` o `1`.

## Metodos modificados

- `updateExpenseSheetLine`: el PUT generico solo admite el mismo `FileId` ya persistido. Cualquier alta, baja o sustitucion se rechaza y debe pasar por `linkExpenseSheetLineTicket` o `unlinkExpenseSheetLineTicket`, que reciben el viewer firmado.
- `updateExpenseSheetTicket`: si la actualizacion dejaria vacios `INDURLFile` e `INDFilename`, comprueba bajo el lock del ticket si existe una `CRMHojaGastosLine` asociada y rechaza la limpieza para preservar la imagen asignada.
- `deleteExpenseSheetTicket`: se restaura la guarda que impide eliminar el ticket completo mientras su `FileId` este vinculado a una linea de gasto. La eliminacion granular de una linea interna del ticket conserva su comportamiento.

## Reglas de transaccion e idempotencia

- Cada operacion nueva usa una sola transaccion `tts`.
- Link y unlink validan `ctrlCanMutateOwnerAxUserId(viewerAxUserId, companyId, 'CRM', 'GASTOS_HOJA_GASTO', ownerAxUserId)` antes de abrir la transaccion.
- La vinculacion no sustituye otro `FileId` ni acepta los indicadores legacy `Ticket` o `Factura`.
- Repetir la misma vinculacion devuelve exito y repara el estado derivado si fuera necesario.
- Desvincular una linea sin `FileId` devuelve exito sin cambios.
- La desvinculacion es correctiva: no queda bloqueada por un ticket ausente ni por asignaciones duplicadas heredadas.
- Desvincular preserva la linea de gasto, la cabecera del ticket, sus lineas y `DocuRef`.
- `line.update()` mantiene los hooks existentes: en la vinculacion prevalecen los importes de la linea manual y despues se recalcula el estado del ticket.

## Impacto y pendientes

- No hay cambios de tablas, indices, enums ni sincronizacion de diccionario.
- La API consumidora debe mapear los extras del `buildHeader` sin alterar los contratos AX existentes; la exposicion HTTP queda fuera de este XPO.
- No se ha anadido una guarda en `INDTicketInfoTable.delete()`: `CRMHojaGastosLine` mantiene un `DeleteAction Cascade` hacia esa tabla y una guarda directa podria bloquear borrados legitimos de lineas. Corregir ese ciclo requiere un cambio separado y prueba AX.
- Pendiente de importar y compilar `INDCRMExpenseSheetService` en AX y validar manualmente los escenarios Draft, autorizacion denegada, Ticket/Factura legacy, idempotencia, conflicto, propietario, ticket sin documento, importe cero, rechazo de transiciones `FileId` por PUT, vincular, desvincular, limpiar una imagen asignada y borrar un ticket vinculado.
- Por alcance solicitado no se ejecutaron build, compilacion ni pruebas de runtime.
