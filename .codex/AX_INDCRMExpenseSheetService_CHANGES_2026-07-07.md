# AX Change Log - INDCRMExpenseSheetService - 2026-07-07

## Objetivo

Preparar los XPO de Axapta para que los metodos usados por la API devuelvan siempre dos totales claros:

- `TotalAmountCurrency`: total en la divisa del documento.
- `TotalAmountMST`: total reembolsable en MST.

El campo nuevo se agrega siempre al final de la salida para reducir el impacto en consumidores existentes. La API C# queda alineada en el mismo cambio manteniendo aliases legacy.

## Archivo afectado

- `.codex/Axapta/INDCRMExpenseSheetService.xpo`

## Cambios aplicados

- `buildExpenseSheetListRow(CRMHojaGastosTable _hoja)` mantiene la columna existente de total con `_hoja.TotalAmount()` como `TotalAmountCurrency` y agrega `_hoja.TotalAmountMST()` al final como `TotalAmountMST`.
- `getExpenseSheet(container _data)` mantiene la columna existente de total con `header.TotalAmount()` como `TotalAmountCurrency` y agrega `header.TotalAmountMST()` al final como `TotalAmountMST`.
- `buildExpenseSheetTicketListRow(INDTicketInfoTable _ticketHeader, DocuRef _docuRef)` mantiene `INDTicketInfoTable.TotalAmount` como `TotalAmountCurrency` y agrega `INDTicketInfoTable.AmountMST` al final como `TotalAmountMST`.
- `buildExpenseSheetTicketLinkListRow(INDTicketInfoTable _ticketHeader, DocuRef _docuRef)` mantiene `INDTicketInfoTable.TotalAmount` como `TotalAmountCurrency` y agrega `INDTicketInfoTable.AmountMST` al final como `TotalAmountMST`.
- Los metodos de ticket que devuelven `INDTicketInfoTable.TotalAmount` tras crear, actualizar, eliminar o ajustar lineas agregan `INDTicketInfoTable.AmountMST` al final de los extras existentes como `TotalAmountMST`:
  - `createExpenseSheetTicket`
  - `createExpenseSheetTicket_backup`
  - `createExpenseSheetTicketLine`
  - `deleteExpenseSheetTicket`
  - `deleteExpenseSheetTicketLine`
  - `updateExpenseSheetTicketLine`
  - `updateExpenseSheetTicketFromIA`
  - `adjustExpenseSheetTicketTotalAmount`
- `adjustExpenseSheetTicketTotalAmount` relee `INDTicketInfoTable` despues de `adjustTotalAmount(...)` para devolver el `AmountMST` actualizado y no el buffer anterior.

## Compatibilidad

- Los cambios son aditivos: `TotalAmountMST` se agrega al final para no desplazar indices existentes.
- En hoja de gastos se conserva la fuente de la columna historica de total con `CRMHojaGastosTable.TotalAmount()` y se agrega el reembolsable MST con `CRMHojaGastosTable.TotalAmountMST()`.
- En tickets se conserva la fuente de la columna historica de total con `INDTicketInfoTable.TotalAmount` y se agrega el reembolsable MST con `INDTicketInfoTable.AmountMST`.
- La API C# expone `TotalAmountCurrency` y `TotalAmountMST` en los DTOs y respuestas afectadas. `TotalAmount` y `AmountMST` se mantienen como aliases legacy donde ya existian.

## Validacion

- Importar y compilar `.codex/Axapta/INDCRMExpenseSheetService.xpo` en Axapta.
- Compilar `IND_CRM_API.sln` y desplegar API tras importar AX.
