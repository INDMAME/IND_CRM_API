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

- Linea `ReimbursableExpense=Yes` (`0`): la linea se incluye en el pago y `ReimbursableAmount=AmountMST`.
- Linea `ReimbursableExpense=No` (`1`): la linea se excluye del pago y `ReimbursableAmount=0`.
- Cabecera `Both` (`2`): marcador calculado por AX cuando existen lineas con ambos valores; no debe enviarse a las lineas.
- `VisaEmpresa` se mantiene visible pero bloqueado. AX lo conserva como espejo inverso legacy (`ReimbursableExpense=Yes` -> Visa `No`; `ReimbursableExpense=No` -> Visa `Yes`), pero nunca lo usa para calcular o filtrar el reembolso.

La serializacion real de Web API usa nombres PascalCase. En TypeScript/JavaScript se deben leer exactamente `TotalGrossAmountMST`, `TotalReimbursableAmount` y `ReimbursableAmount`.

## Objetivo

Actualizar `IND_CRM_APP` para mostrar por separado el total bruto company/MST, el total reembolsable y los importes en divisa original. No reutilizar un unico helper entre hojas y tickets porque sus contratos mantienen semanticas distintas.

Antes de modificar:

- Leer las instrucciones y skills locales de `IND_CRM_APP`.
- Revisar `git status` y conservar todos los cambios pendientes del frontend.
- Usar `Web/wwwroot` como fuente canonica. No editar manualmente bundles, chunks ni la copia generada raiz `wwwroot`.
- No agregar dependencias, credenciales ni URLs de entorno al codigo.

## Frontera proxy C# del frontend

No basta con actualizar TypeScript. `IND_CRM_APP` deserializa y reconstruye la respuesta del backend antes de entregarla a React.

Actualizar tambien:

- `App/Models/CRM/ExpenseSheetTicketModels.cs`: agregar a `ExpenseSheetTicketLineDto` las propiedades nullable `int? ReimbursableExpense` y `decimal? ReimbursableAmount`, con sus nombres JSON PascalCase.
- `Web/Controllers/Gastos/GastosController.cs`: propagar ambos valores en `ToExpenseSheetTicketApiDetailLine`. Este mapper actualmente enumera los campos de salida y descartaria cualquier propiedad no incluida expresamente.

No cambiar rutas, envelopes, autorizacion ni headers.

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

Alias de linea permitidos:

- Importe original: `TotalAmountCurrency ?? Amount`, mostrado con `CurrencyCode`.
- Bruto company: `AmountMST ?? TotalAmountMST`, mostrado con la divisa de la empresa.
- Reembolso: exclusivamente `ReimbursableAmount`, mostrado con la divisa de la empresa.

Usar siempre `??` y no `||`, porque cero es un valor funcional valido.

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

## Caso de auditoria obligatorio

Usar como fixture de contrato esta linea observada en `GET /api/crm/expensesheets/{hojaGastosId}`:

```json
{
  "ReimbursableExpense": 1,
  "CurrencyCode": "USD",
  "AmountMST": 108.11,
  "ReimbursableAmount": 0.00,
  "TotalAmountCurrency": 100.00,
  "TotalAmountMST": 108.11
}
```

La UI debe interpretarla sin recalcular ni ocultar diferencias:

- Original: `100.00 USD`.
- Bruto company: `108.11` en la divisa de la empresa.
- Reembolso: `0.00` en la divisa de la empresa.
- Estado: no reembolsable (`1=No`).

El ejemplo es coherente con el contrato actual: `ReimbursableExpense=1` significa `No`, por lo que `ReimbursableAmount=0`. El frontend debe mostrar siempre el valor fisico recibido y nunca corregirlo usando `AmountMST` como fallback.

## Tickets: detalle ampliado

Los totales de ticket mantienen su semantica: seguir usando `TotalAmountMST` para el total convertido y `TotalAmountCurrency` para el total en divisa del ticket en:

- `POST /api/crm/expensesheets/tickets/list`
- `POST /api/crm/expensesheets/tickets/link/list`
- `GET /api/crm/expensesheets/tickets/{fileId}`
- Respuestas de crear, editar, IA, ajuste, alta/edicion/borrado de lineas de ticket.

El detalle `GET /api/crm/expensesheets/tickets/{fileId}` agrega en cada `Lines[*]`:

- `ReimbursableExpense` (`int?`; TypeScript `number | null`): enum de la `CRMHojaGastosLine` vinculada (`0=Yes`, `1=No`).
- `ReimbursableAmount` (`decimal?`; TypeScript `number | null`): importe reembolsable de esa linea vinculada en divisa de la empresa.

Ambos valores quedan `null` cuando AX devuelve el contrato legacy o cuando no existe una vinculacion unica con `CRMHojaGastosLine`. Si el ticket contiene varias lineas, el API repite los mismos valores en todas ellas como metadatos de la unica linea de hoja vinculada. No sumarlos ni tratarlos como importes individuales de las lineas del ticket.

Actualizar en React, como minimo:

- `Web/wwwroot/react/src/pages/gastos/expenseTypes.ts`.
- `Web/wwwroot/react/src/pages/gastos/utils/expenseApiResponseNormalizers.ts`.
- `Web/wwwroot/react/src/pages/gastos/tickets/detail/expenseTicketDetailTypes.ts`.
- Los mappers y estados de detalle de ticket que convierten la linea API al modelo UI.
- `ExpenseTicketLinesList.tsx` y `ExpenseTicketLineDetailForm.tsx` si son las superficies que muestran la linea.

El normalizador debe aceptar PascalCase y camelCase solo en el limite de entrada, conservar `null` y normalizar un unico modelo interno. Rechazar `2=Both` como valor de linea. Mostrar ambos valores como solo lectura; cero debe verse como `0.00`, mientras que `null` debe verse como no disponible.

`AmountMST` permanece como fallback legacy solo en detalle de ticket. No aplicar ese fallback a `ReimbursableAmount` de una linea de hoja.

## No hacer

- No presentar `TotalAmountMST` como el nuevo total bruto: conserva semantica contable legacy.
- No usar `AmountMST` de una linea como si fuera `ReimbursableAmount`.
- No filtrar el bruto company/MST por Visa ni por `ReimbursableExpense`.
- No usar Visa para decidir el reembolso; la unica bandera funcional es `ReimbursableExpense`.
- No sumar `Lines[*].ReimbursableAmount` del detalle de ticket: puede estar repetido como metadato de la linea de hoja vinculada.
- No sumar ni convertir importes en frontend.
- No cambiar payloads de entrada.

## Validacion frontend esperada

- En datos AX coherentes, una linea con `ReimbursableExpense=Yes` (`0`) muestra `ReimbursableAmount=AmountMST`, suma en `TotalReimbursableAmount` y mantiene `VisaEmpresa=No` como espejo legacy. Si el servidor devuelve una diferencia, conservar los valores fisicos y reportarla sin recalcular en frontend.
- Una linea con `ReimbursableExpense=No` (`1`) muestra `ReimbursableAmount=0`, queda fuera de `TotalReimbursableAmount`, conserva su `AmountMST` bruto y mantiene `VisaEmpresa=Yes` como espejo legacy.
- Las tarjetas de hoja distinguen total bruto y reembolso con etiquetas claras.
- El importe original de cada linea se obtiene de `Amount` y conserva su `CurrencyCode`.
- Los listados y mutaciones de tickets siguen refrescando desde `TotalAmountMST`.
- El proxy C# conserva los dos campos nuevos hasta la respuesta entregada a React.
- Las lineas de ticket repetidas no generan ninguna suma de `ReimbursableAmount`.
- PascalCase y camelCase se normalizan una sola vez y el modelo UI conserva ceros y nulos.

Ejecutar, segun los scripts disponibles del proyecto:

- `npm run test:gastos:currency`.
- `npm run check:types`.
- `npm run check:localization:keys`.
- `npm run check:resx:encoding`.
- `npm run build`.
- `dotnet build`.
- `npm run check:react-doctor`.

No hacer pruebas visuales automatizadas. Entregar un checklist manual de escritorio y movil, detallar archivos modificados y declarar cualquier prueba live pendiente por falta de autenticacion.
