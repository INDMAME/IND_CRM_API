# Prompt frontend - Totales de hojas de gastos y tickets

Contexto: `IND_CRM_API` actualiza los contratos de hojas de gastos y tickets para exponer siempre dos importes de cabecera:

- `TotalAmountCurrency`: total en la divisa del documento/ticket.
- `TotalAmountMST`: total reembolsable en MST.

La API mantiene aliases legacy por compatibilidad (`TotalAmount` y, en detalle de ticket, `AmountMST`), pero el frontend debe migrar a los campos nuevos. La serializacion actual de la API usa nombres PascalCase.

## Objetivo

Actualizar `IND_CRM_APP` para que las tarjetas, listados y detalles de hojas de gastos/tickets muestren el importe reembolsable con `TotalAmountMST` siempre que exista.

## Endpoints y campos

1. `POST /api/crm/expensesheets/list`
   - Cada item devuelve ahora `TotalAmountCurrency` y `TotalAmountMST`.
   - `TotalAmount` queda como alias legacy de `TotalAmountCurrency`.
   - En tarjetas de cabecera/listado de hojas, mostrar `TotalAmountMST`.

2. `GET /api/crm/expensesheets/{hojaGastosId}`
   - Cabecera devuelve `TotalAmountCurrency` y `TotalAmountMST`.
   - Lineas devuelven `Amount`, `AmountMST`, `TotalAmountCurrency` y `TotalAmountMST`.
   - En resumen/cabecera de la hoja, mostrar `Header.TotalAmountMST`.
   - En tarjetas o resumenes de lineas, mostrar `Line.TotalAmountMST` con fallback a `Line.AmountMST`.

3. `POST /api/crm/expensesheets/tickets/list`
   - Cada item devuelve `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.
   - En tarjetas/listados de tickets, mostrar `TotalAmountMST`.

4. `POST /api/crm/expensesheets/tickets/link/list`
   - Cada item devuelve `TotalAmount`, `TotalAmountCurrency` y `TotalAmountMST`.
   - En tarjetas/listados de tickets vinculables, mostrar `TotalAmountMST`.

5. `GET /api/crm/expensesheets/tickets/{fileId}`
   - Cabecera devuelve `TotalAmount`, `TotalAmountCurrency`, `AmountMST` y `TotalAmountMST`.
   - Mostrar `TotalAmountMST`; usar `AmountMST` solo como fallback legacy.

6. Mutaciones de tickets que devuelven totales recalculados:
   - `POST /api/crm/expensesheets/tickets`
   - `PUT /api/crm/expensesheets/tickets/{fileId}`
   - `POST /api/crm/expensesheets/tickets/{fileId}/ia`
   - `POST /api/crm/expensesheets/tickets/{fileId}/total-adjustment`
   - `POST /api/crm/expensesheets/tickets/{fileId}/lines`
   - `PUT /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId}`
   - `DELETE /api/crm/expensesheets/tickets/{fileId}/lines/{lineRecId}`
   - Usar `TotalAmountMST` para refrescar UI de totales tras guardar.

## Regla de visualizacion

Implementar un helper unico para elegir el importe visible:

```ts
function getVisibleReimbursableTotal(row: {
  TotalAmountMST?: number | null;
  AmountMST?: number | null;
  TotalAmountCurrency?: number | null;
  TotalAmount?: number | null;
  Amount?: number | null;
}) {
  return row.TotalAmountMST
    ?? row.AmountMST
    ?? row.TotalAmountCurrency
    ?? row.TotalAmount
    ?? row.Amount
    ?? null;
}
```

Usar este helper en:

- Tarjetas de cabecera de hojas de gastos.
- Tarjetas/listados de lineas de hojas de gastos.
- Tarjetas/listados de tickets.
- Resumenes tras crear, editar, eliminar o ajustar tickets/lineas.

## No hacer

- No seguir usando `TotalAmount` como importe principal visible.
- No calcular MST en frontend.
- No mezclar `TotalAmountCurrency` con reembolso visual salvo fallback temporal.
- No cambiar payloads de entrada; este ajuste es de respuesta/visualizacion.

## Validacion frontend esperada

- En un listado de hojas, el importe visible coincide con `Items[*].TotalAmountMST`.
- En el detalle de una hoja, el total de cabecera visible coincide con `Items[0].TotalAmountMST`.
- En tarjetas de lineas de hoja, el importe visible usa `Line.TotalAmountMST` o `Line.AmountMST`.
- En listados/detalles de tickets, el importe visible coincide con `TotalAmountMST`.
- Tras crear/editar/eliminar linea de ticket o aplicar ajuste de total, el total refrescado usa `Data.TotalAmountMST`.
