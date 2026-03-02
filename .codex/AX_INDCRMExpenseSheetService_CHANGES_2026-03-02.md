# AX Change Log - INDCRMExpenseSheetService (2026-03-02)

## Objetivo
Corregir discrepancias de contrato AX/API en gastos y tickets, priorizando:
- filtros opcionales robustos en listados de tickets,
- opcionales de cabecera en update de hoja de gastos,
- consistencia de mapeo nullable en endpoints de tickets.

## Alcance (fase AX)
- Clase AX: `INDCRMExpenseSheetService`
- Metodos AX impactados:
  - `getExpenseSheetTicketsList(container _data)`
  - `updateExpenseSheetHeader(container _data)`
  - Comentario de contrato en `getSubordinatesByUser(container _data)`
- Endpoints/API relacionados:
  - `CrmExpenseSheetTicketsController.GetExpenseSheetTicketsList`
  - `CrmExpenseSheetsController.UpdateExpenseSheetHeader`
  - `CrmExpenseSheetTicketsController.UpdateExpenseSheetTicket`

## Contrato de entrada/salida relevante
- `getExpenseSheetTicketsList`:
  - `_data[4]` = `statusFilter` (opcional, `0|1` efectivo).
  - `_data[9]` = `processedByAI` (opcional, `0|1` efectivo).
  - Valores distintos de `0|1` (incluyendo vacio y token `null`) se tratan como "sin filtro".
- `updateExpenseSheetHeader`:
  - `_data[8]` = `ExpenseSheetStatus` (opcional, entero >= 0).
  - `_data[9]` = `ExchangeRateMode` (opcional, entero >= 0).
  - Valores no numericos/vacios en `_data[8]` y `_data[9]` no activan actualizacion de esos campos.
- `getSubordinatesByUser`:
  - Comentario de salida corregido a `[[subordinateUserId, subordinateName], ...]`.

## Cambios aplicados por metodo
### getExpenseSheetTicketsList
- Se agrego `statusFilterRaw` para parsear `_data[4]` como texto.
- Se cambio el parseo de `_data[4]` para activar filtro solo con `0` o `1`.
- Se cambio el parseo de `_data[9]` para activar filtro solo con `0` o `1`.
- Se mantiene estable la logica principal del `select` y el formato de salida.

### updateExpenseSheetHeader
- Se agregaron `expenseSheetStatusRaw` y `exchangeRateModeRaw`.
- Se cambio parseo de `_data[8]` y `_data[9]` para activar update solo con enteros validos (>= 0).
- Se evita que placeholders de API activen actualizaciones no deseadas.

### getSubordinatesByUser
- Se corrigio el comentario de contrato de salida para reflejar el orden real de columnas.

## Ajuste de integracion API (pendiente de fase AX->API cubierto en este turno)
- `CrmExpenseSheetTicketsController.GetExpenseSheetTicketsList`:
  - cuando `status` o `processedByAI` no vienen en request, se envia token `null` al container AX.
- `CrmExpenseSheetsController.UpdateExpenseSheetHeader`:
  - se removieron restricciones artificiales entre `expenseSheetStatus`, `exchangeRateMode` y `estadoComentarios`.
  - se usa append de opcionales con posiciones estables y placeholders (`null`) para preservar indices AX.
- `CrmExpenseSheetTicketsController` mapeos/respuesta:
  - `MapExpenseSheetTicketDetail` y `MapExpenseSheetTicketList` preservan `null` en `ProcessedByAI` y `GastoType`.
  - `UpdateExpenseSheetTicket` usa `ProcessedByAI` retornado por AX (extras) en la respuesta final.

## Riesgos y mitigaciones
- Riesgo: consumidores legacy que dependian de defaults `false/0` en respuestas de tickets.
- Mitigacion: se preserva semantica nullable de DTO y se alinea con datos reales de AX.
- Riesgo: payloads legacy con valores no numericos en `_data[8]/_data[9]` para update header.
- Mitigacion: ahora se ignoran de forma defensiva en lugar de forzar actualizacion incorrecta.

## Checklist de salida
- [x] Plan por clase AX definido.
- [x] Metodo AX documentado con contrato de indices.
- [x] Archivo temporal de cambios actualizado.
- [x] Pendiente AX->API reflejado y aplicado en controladores/mappers.
