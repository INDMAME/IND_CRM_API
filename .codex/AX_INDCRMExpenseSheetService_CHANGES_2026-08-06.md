# Cambios AX - INDCRMExpenseSheetService - 2026-08-06

## Objetivo

Al vincular un ticket digitalizado a una linea manual existente, aplicar el ticket como fuente del bloque monetario sin sustituir fecha, tipo, descripcion, proyecto, indicador internacional ni la opcion reembolsable de la linea.

## Metodo modificado

- `linkExpenseSheetLineTicket`: conserva el contrato de entrada y respuesta. Para vinculos nuevos exige ticket `Pending`, documento e importe positivo. Copia `Qty`, `Price`, `Amount`, `Currency`, `ExchRate` y `AmountMST`; recalcula los campos derivados de reembolso y persiste con `doUpdate()` para evitar la sincronizacion inversa hacia el ticket.
- `linkExpenseSheetLineTicket`: repetir el mismo `FileId` ya no retorna antes de aplicar el bloque monetario. Reconciliara la linea con el estado actual del ticket y devolvera `changed = 0` cuando no exista ninguna diferencia real.
- `refreshTicketStatusByFileId`: actualiza `Status` y `SearchKey` con `doUpdate()`. No ejecuta `INDTicketInfoTable.update()` y, por tanto, no vuelve a copiar descripcion u otros campos del ticket a la linea.

## Reglas de persistencia

- Se mantiene el orden de bloqueo cabecera, linea y ticket dentro de una unica transaccion `tts`.
- El bloque monetario de la linea queda como `Qty = 1`, `Price = Amount = ticket.TotalAmount`, `Currency = ticket.CurrencyCode`, `ExchRate = ticket.ExchRate` y `AmountMST = ticket.AmountMST`, seguido de la normalizacion de divisa existente.
- `ReimbursableExpense` no se modifica. `ReimbursableAmount` y el espejo heredado `VisaEmpresa` se recalculan desde ese indicador y el nuevo `AmountMST`.
- Se conservan `TransDate`, `Type`, `Description`, `ProjId`, `ProjIdHornos` e `Internacional`.
- La desvinculacion conserva los ultimos importes persistidos en la linea.
- Un ticket con `TotalAmount <= 0` no es vinculable.

## Compatibilidad

- No cambian rutas HTTP, request, response, headers, indices de `container` ni codigos funcionales.
- No hay cambios de tablas, indices, enums ni sincronizacion de diccionario.
- El XPO a importar y compilar es `.codex/Axapta/INDCRMExpenseSheetService.xpo`.
- El espejo versionado en `IND_CRM_APP/.codex/Axapta/INDCRMExpenseSheetService.xpo` debe mantenerse alineado.

## Validacion y pendiente AX

- `npm run test:expense-ticket-flow`: 21 de 21 regresiones correctas. Cubren copia monetaria, campos preservados, importe positivo, idempotencia y ausencia de sincronizacion inversa.
- `scripts/build-api.ps1 -Configuration Debug -Platform x86`: compilacion .NET Framework correcta. Esta compilacion no compila el XPO.
- Los espejos XPO conservan Windows-1252, CRLF y 67 bloques `SOURCE`/`ENDSOURCE` equilibrados.
- Sigue pendiente importar y compilar `INDCRMExpenseSheetService` en AX, sincronizar si el entorno lo solicita y validar en runtime: vinculo nuevo, repeticion sin cambios, reconciliacion tras cambiar el ticket, reembolsable Si/No, divisa extranjera, desvinculo y rollback ante validacion.
