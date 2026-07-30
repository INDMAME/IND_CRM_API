# AX Change Log - INDCRMExpenseSheetService (2026-07-30)

## Objetivo

Exponer `ReimbursableExpense` y `ReimbursableAmount` en cada linea devuelta por
`GET /api/crm/expensesheets/tickets/{fileId}` para que la UI pueda mostrar el
estado y el importe reembolsable de la linea de gasto vinculada.

## Diseno funcional

Un ticket puede contener varias `INDTicketInfoLine`, pero se vincula de forma
canonica a una unica `CRMHojaGastosLine` mediante `FileId`. Los valores se
resuelven una sola vez con `CRMHojaGastosLine::FindByFileId()` y se anexan como
metadatos a cada fila del detalle del ticket.

El importe se repite en todas las lineas del ticket y no representa un reparto
por `INDTicketInfoLine`. Los consumidores no deben sumar
`Lines[*].ReimbursableAmount`.

## Cambio AX

Objeto:

- `INDCRMExpenseSheetService.xpo`.

Metodo:

- `getExpenseSheetTicket(container _data)`.

La fila conserva las posiciones 1-8 y agrega al final:

- 9: `ReimbursableExpense`, enum numerico `INDReimbursableExpenseLines`.
- 10: `ReimbursableAmount`, importe reembolsable MST persistido en
  `CRMHojaGastosLine`.

No se modifica `INDTicketInfoLine` ni se crean campos o reglas de reparto
nuevas.

## Compatibilidad

- AX nuevo y API anterior: las posiciones 9-10 adicionales se ignoran.
- API nueva y AX anterior: ambos campos se devuelven como `null`.
- Ticket sin linea de gasto vinculada: ambos campos se devuelven como `null`.
- `FileId` ambiguo en datos heredados: ambos campos se devuelven como `null`.
- `ReimbursableExpense=No`: se conservan los valores funcionales `1` y
  `ReimbursableAmount=0`.
- No existe fallback desde `TotalAmount`, `AmountMST`,
  `TotalAmountMST` ni `VisaEmpresa`.

## Contrato API

`ExpenseSheetTicketLineDto` agrega propiedades opcionales en PascalCase:

- `ReimbursableExpense: int?`, limitado a `0|1`.
- `ReimbursableAmount: decimal?`.

El mapper acepta filas antiguas de 7-8 posiciones y solo lee los nuevos valores
cuando AX devuelve las posiciones 9-10.

Swagger se actualiza mediante el DTO tipado usado por
`ResponseType(typeof(IndPagedResponse<ExpenseSheetTicketDetailDto>))`.

## Revision de routing

No cambian el verbo, la ruta ni los headers:

- `GET /api/crm/expensesheets/tickets/{fileId}`.
- Requiere `Authorization`, `X-IND-Company` y `X-IND-AxUserId`.
- No se introducen nuevas rutas ni colisiones con rutas literales hermanas.

## Importacion y validacion AX

Prerequisito AX:

1. Confirmar que `CRMHojaGastosLine::FindByFileId()` ya existe y compila.
2. Si no existe en el entorno, importar y compilar primero
   `CRMHojaGastosLine.xpo`.

Importar y compilar despues:

1. `INDCRMExpenseSheetService.xpo`.

Despues de importar:

1. Compilar la clase en Axapta.
2. Consultar un ticket vinculado reembolsable y comprobar `0` + importe.
3. Consultar un ticket vinculado no reembolsable y comprobar `1` + `0`.
4. Consultar un ticket sin vinculo y comprobar valores nulos en la API.
5. Comprobar un ticket multilinea y verificar que el importe repetido no se
   suma en la UI.
