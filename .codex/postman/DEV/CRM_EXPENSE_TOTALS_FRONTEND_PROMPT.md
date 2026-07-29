# Prompt frontend - Totales de hojas de gastos y tickets

Contexto: `IND_CRM_API` separa ahora tres conceptos en las hojas de gastos:

- `TotalAmountCurrency` y `TotalAmountMST`: totales contables legacy; conservan su semantica actual.
- `TotalGrossAmountMST`: total bruto en divisa de la empresa. No se filtra por `ReimbursableExpense` ni por Visa.
- `TotalReimbursableAmount`: total que se reembolsa al empleado en divisa de la empresa. Incluye solo lineas con `ReimbursableExpense=Yes`; Visa no participa en el calculo.

En cada linea de gasto:

- `Amount`: importe en la divisa propia de la linea.
- `AmountMST`: importe bruto convertido a la divisa de la empresa.
- `ReimbursableAmount`: importe reembolsable en divisa de la empresa; copia `AmountMST` con `ReimbursableExpense=Yes` y vale cero con `ReimbursableExpense=No`, independientemente de Visa.

Semantica funcional del enum:

- Linea `ReimbursableExpense=Yes` (`1`): la linea se incluye en el pago y `ReimbursableAmount=AmountMST`.
- Linea `ReimbursableExpense=No` (`0`): la linea se excluye del pago y `ReimbursableAmount=0`.
- Cabecera `Both` (`2`): marcador calculado por AX cuando existen lineas con ambos valores; no debe enviarse a las lineas.
- `VisaEmpresa` se mantiene visible pero bloqueado. AX lo conserva como espejo inverso legacy (`ReimbursableExpense=Yes` -> Visa `No`; `ReimbursableExpense=No` -> Visa `Yes`), pero nunca lo usa para calcular o filtrar el reembolso.

La serializacion real de Web API usa nombres PascalCase. En TypeScript/JavaScript se deben leer exactamente `TotalGrossAmountMST`, `TotalReimbursableAmount` y `ReimbursableAmount`.

## Objetivo

Actualizar `IND_CRM_APP` para mostrar por separado el total bruto company/MST, el total reembolsable y los importes en divisa original. No reutilizar un unico helper entre hojas y tickets porque sus contratos mantienen semanticas distintas.

## Endpoints de hojas y campos

1. `POST /api/crm/expensesheets/list`
   - Cada item agrega `TotalGrossAmountMST` y `TotalReimbursableAmount`.
   - `TotalAmountCurrency`, `TotalAmount` y `TotalAmountMST` siguen siendo campos contables legacy.
   - Mostrar `TotalReimbursableAmount` como reembolso y `TotalGrossAmountMST` como bruto company/MST.

2. `GET /api/crm/expensesheets/{hojaGastosId}`
   - La cabecera agrega `TotalGrossAmountMST` y `TotalReimbursableAmount`.
   - Las lineas agregan `ReimbursableAmount` y mantienen `Amount` y `AmountMST`.
   - En cabecera, mostrar por separado `Header.TotalReimbursableAmount` y `Header.TotalGrossAmountMST`.
   - En cada linea, usar `Line.ReimbursableAmount` para reembolso, `Line.AmountMST` para bruto company y `Line.Amount` para importe original.

## Compatibilidad durante el despliegue AX

- Si la clase AX aun no devuelve la posicion nueva de cabecera, la API entrega `TotalReimbursableAmount` usando `TotalAmountMST` como fallback.
- Si AX aun no devuelve el bruto nuevo, `TotalGrossAmountMST` queda `null`.
- Si AX aun no devuelve el campo nuevo de linea, `ReimbursableAmount` queda `null`; no sustituirlo por `AmountMST`, porque eso trataria lineas con `ReimbursableExpense=No` como reembolso.

```ts
function getExpenseSheetTotals(row: {
  TotalReimbursableAmount?: number | null;
  TotalAmountMST?: number | null;
  TotalGrossAmountMST?: number | null;
}) {
  return {
    reimbursable: row.TotalReimbursableAmount ?? row.TotalAmountMST ?? null,
    grossCompany: row.TotalGrossAmountMST ?? null,
  };
}
```

## Tickets: comportamiento existente

Los contratos de tickets no cambian con este ajuste. Seguir usando `TotalAmountMST` para su total convertido y `TotalAmountCurrency` para el total en divisa del ticket en:

- `POST /api/crm/expensesheets/tickets/list`
- `POST /api/crm/expensesheets/tickets/link/list`
- `GET /api/crm/expensesheets/tickets/{fileId}`
- Respuestas de crear, editar, IA, ajuste, alta/edicion/borrado de lineas de ticket.

`AmountMST` permanece como fallback legacy solo en detalle de ticket. No aplicar ese fallback a `ReimbursableAmount` de una linea de hoja.

## No hacer

- No presentar `TotalAmountMST` como el nuevo total bruto: conserva semantica contable legacy.
- No usar `AmountMST` de una linea como si fuera `ReimbursableAmount`.
- No filtrar el bruto company/MST por Visa ni por `ReimbursableExpense`.
- No usar Visa para decidir el reembolso; la unica bandera funcional es `ReimbursableExpense`.
- No sumar ni convertir importes en frontend.
- No cambiar payloads de entrada.

## Validacion frontend esperada

- Una linea con `ReimbursableExpense=Yes` muestra `ReimbursableAmount=AmountMST`, suma en `TotalReimbursableAmount` y mantiene `VisaEmpresa=No` como espejo legacy.
- Una linea con `ReimbursableExpense=No` muestra `ReimbursableAmount=0`, queda fuera de `TotalReimbursableAmount`, conserva su `AmountMST` bruto y mantiene `VisaEmpresa=Yes` como espejo legacy.
- Las tarjetas de hoja distinguen total bruto y reembolso con etiquetas claras.
- El importe original de cada linea se obtiene de `Amount` y conserva su `CurrencyCode`.
- Los listados y mutaciones de tickets siguen refrescando desde `TotalAmountMST`.
