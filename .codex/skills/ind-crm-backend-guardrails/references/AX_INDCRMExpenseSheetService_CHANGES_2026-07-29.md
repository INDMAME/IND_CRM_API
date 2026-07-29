# AX Change Log - INDCRMExpenseSheetService - 2026-07-29

## Objetivo

Ampliar de forma aditiva el contrato AX de hojas de gastos para separar los totales contables legacy, el bruto company/MST y el reembolso explicito company/MST, sin desplazar indices existentes. La regla funcional queda fijada como `ReimbursableExpense=Yes` incluido y `ReimbursableExpense=No` excluido.

Tambien se restaura la proteccion que impide borrar un ticket completo mientras siga vinculado a una linea de gastos y se conserva la divisa extranjera informada al insertar tickets.

## Contrato de salida

### Listado de hojas

`buildExpenseSheetListRow(CRMHojaGastosTable _hoja)` conserva las posiciones 1-18 y agrega:

- 19: `TotalGrossAmountMST`, obtenido desde `_hoja.TotalBrutoAmountMST()`.
- 20: `TotalReimbursableAmount`, obtenido desde `_hoja.TotalReimbursableAmount()`.

### Detalle de hoja - cabecera

`getExpenseSheet(container _data)` conserva las posiciones 1-18 y agrega:

- 19: `TotalGrossAmountMST`, obtenido desde `header.TotalBrutoAmountMST()`.
- 20: `TotalReimbursableAmount`, obtenido desde `header.TotalReimbursableAmount()`.

### Detalle de hoja - linea

La linea conserva las posiciones 1-14 y agrega:

- 15: `ReimbursableAmount`, obtenido desde `line.ReimbursableAmount`.

## Compatibilidad API

- `TotalAmountCurrency`, `TotalAmountMST` y `AxCreatedDate` conservan sus indices y su semantica legacy.
- La API trata los campos nuevos como opcionales para poder convivir con una version AX anterior.
- Si AX no devuelve la posicion 20, `TotalReimbursableAmount` usa `TotalAmountMST` como fallback.
- Si AX no devuelve la posicion 19, `TotalGrossAmountMST` queda nulo.
- Si una linea AX no devuelve la posicion 15, `ReimbursableAmount` queda nulo.
- No cambian rutas, request containers, envelopes ni contratos de tickets.

## Borrado de tickets

`deleteExpenseSheetTicket(container _data)` vuelve a comprobar `CRMHojaGastosLine.FileId` antes del borrado completo. Si existe una linea vinculada, devuelve error y exige desvincularla primero. El borrado granular de una linea de ticket mantiene su comportamiento actual.

## Orden de importacion AX

1. `INDReimbursableAmount.xpo`.
2. `CRMHojaGastosLine.xpo`.
3. `CRMHojaGastosTable.xpo`.
4. `INDTicketInfoTable.xpo`.
5. `INDTicketExpenseSheetLink.xpo`.
6. `INDCRMExpenseSheetService.xpo`.
7. `INDRecalcularAmountMST_HojasGastos.xpo`, `INDRecalAmountMSTExchange_HojasGastosV2.xpo` e `INDRecalcularAmountMST_Tickets.xpo`.
8. `CRMHojaGastosLineForm.xpo` y `CRMHojaGastosTableForm.xpo`.

Despues de importar, sincronizar el diccionario cuando corresponda y compilar las tablas antes de compilar la clase de servicio.

## Objetos AX relacionados

- `CRMHojaGastosLine` centraliza `ReimbursableAmount = AmountMST` cuando `ReimbursableExpense=Yes`; con `No` guarda cero. El nuevo indicador es la unica fuente funcional y actualiza `VisaEmpresa` como espejo legacy inverso (`Yes -> Visa No`, `No -> Visa Yes`).
- `CRMHojaGastosTable` recompone el estado de cabecera `No`/`Yes`/`Both`, conserva un `No`/`Yes` explicito cuando no hay lineas, usa `Yes` como valor incluido por defecto, elimina Visa de los filtros de reembolso/pago y replica `hasTicket()` en consultas agregadas complementarias para los conjuntos Ticket/NoTicket sin consultas N+1. La propagacion de cabecera recalcula y refresca tambien el origen contable de cada linea. `TotalAmount_SoloVisa()` queda solo como desglose bruto legacy usando `AmountMST` y no interviene en pagos.
- Las rutas contables `Aprobar_DesaprobarHojaDeGastos`, `Aprobar_DesaprobarHojaDeGastos_MEX`, `mapDescripcionDiaHojaGastos`, `MapLedgerDimension_Amount`, `MapLedgerDimension_Amount_Line` y `PostInvoice` incluyen solo `ReimbursableExpense=Yes` y consumen `ReimbursableAmount`; `LineAmountMST()` queda reservado al calculo bruto company/MST.
- `INDTicketInfoTable` conserva la divisa extranjera al insertar y, junto con `INDTicketExpenseSheetLink`, mantiene divisa, cambio, `AmountMST` y el derivado de reembolso al sincronizar o vincular tickets.
- Los jobs se ejecutan solo en la compania AX actual para no fijar ni recorrer empresas sin autorizacion; el operador debe repetirlos explicitamente en cada compania aprobada. Recalculan los campos derivados para datos historicos y refrescan el origen contable cuando cambia el importe efectivo.
- El job V2 migra primero el criterio legacy inverso (`VisaEmpresa=Yes -> ReimbursableExpense=No`, `VisaEmpresa=No -> ReimbursableExpense=Yes`), recalcula `AmountMST`, recompone las cabeceras y solo entonces actualiza una vez la contabilidad de cada linea afectada.
- `INDRecalcularAmountMST_Tickets` completa `AmountMST` y, solo para la divisa de empresa, `ExchRate`, sin modificar `TotalAmount` ni `CurrencyCode` del ticket.
- El job V2 informa cuantas banderas cambio y cuantas lineas estaban excluidas por Visa. Se ejecuta como migracion controlada; con la tabla actualizada puede repetirse sin invertir decisiones nuevas porque toda edicion de `ReimbursableExpense` mantiene el espejo Visa coherente.
- Los formularios refrescan los importes derivados, mantienen Visa visible pero bloqueado y bloquean el marcador de cabecera `Both`.

## Alineacion API y documentacion

- DTOs de detalle, linea y listado ampliados de forma nullable.
- Mapeadores de detalle, listado y dataset IA alineados con los nuevos indices; el filtro de cabecera queda limitado a `0..2` en ambos endpoints.
- Swagger/XML, `ENDPOINTS.md`, MCP y `POSTMAN.md` actualizados sin crear una nueva coleccion Postman.
- Los esquemas MCP y los ejemplos de las colecciones Postman activas usan `DDMMYYYY`/`DD.MM.YYYY`, colocan `includeSubordinates` solo en el listado y mantienen `currencyCode` opcional en la actualizacion de cabecera.
- La ruta legacy de propagacion de divisa queda documentada como `no-op`; no modifica las lineas.

## Validacion local

- Los quince XPO afectados o reexportados se comprobaron en Windows-1252, CRLF completo y sin BOM.
- Bloques `SOURCE/ENDSOURCE`, `METHODS/ENDMETHODS` y `CONTROL/ENDCONTROL` equilibrados; `INDCRMExpenseSheetService` conserva 65/65 bloques `SOURCE`.
- `.codex/MCP_TOOLS.json` validado como JSON.
- `git diff --check` sin errores.
- Compilacion Debug|x86 de `IND_CRM_API.sln` completada con `scripts/build-api.ps1`; solo se mantienen warnings XML preexistentes.

## Validacion AX pendiente

La evidencia local no sustituye la validacion de runtime AX. Queda pendiente:

1. Importar los XPO en el orden indicado.
2. Sincronizar el diccionario de datos.
3. Compilar EDT, tablas, formularios afectados y `INDCRMExpenseSheetService`.
4. Ejecutar `getExpenseSheet` y `getExpenseSheetsList` y confirmar longitudes 20/20/15.
5. Verificar que los indices 1-18 y 1-14 conservan los valores anteriores.
6. Verificar el fallback desde una API nueva contra una clase AX anterior.
7. Confirmar la matriz funcional: `ReimbursableExpense=Yes` copia `AmountMST` y deja Visa en `No`; `ReimbursableExpense=No` deja cero y Visa en `Yes`. Visa permanece bloqueado y no participa en calculos.
8. Confirmar que un ticket vinculado no puede borrarse completo y que el borrado granular sigue permitido.
9. Crear un ticket en divisa extranjera, confirmar que `CurrencyCode` no se sustituye por la divisa company y validar la sincronizacion hacia la linea.
10. Antes de ejecutar el job V2, revisar en AX los recuentos por combinacion `VisaEmpresa/ReimbursableExpense` y confirmar que Visa es la fuente historica que debe invertirse una sola vez.
11. Ejecutar el job V2 una sola vez por cada compania aprobada en un entorno controlado, confirmar que transforma `Visa Yes -> Reimbursable No` y `Visa No -> Reimbursable Yes` antes de recalcular, refresca una sola vez los efectos contables y sanea cabeceras historicas `Both` sin alterar cabeceras vacias que ya sean `No` o `Yes`.
12. Ejecutar `INDRecalcularAmountMST_Tickets` por cada compania aprobada y comprobar que conserva el importe y la divisa originales del ticket.
