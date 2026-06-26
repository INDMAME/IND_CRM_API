# Cambios Axapta - INDCRMExpenseSheetService - 2026-06-26

## Objetivo

Exponer a la API el ajuste atomico de importe total de ticket implementado en `INDTicketInfoTable.adjustTotalAmount`.

## Metodo nuevo

- `INDCRMExpenseSheetService::adjustExpenseSheetTicketTotalAmount(container _data)`

## Contrato de entrada

```text
_data[1] = DataAreaId
_data[2] = AxUserId
_data[3] = FileId
_data[4] = NewTotalAmount
```

## Contrato de salida

Header compatible con los metodos existentes mediante `INDCRMUtilityService::buildHeader`:

```text
[success, message, fileId, previousTotalAmount, newTotalAmount, differenceAmount, adjustmentLineRecId, adjustmentLineCreated, adjustmentDescription, adjustmentAmount]
```

## Codigo X++ propuesto

```xpp
static server container adjustExpenseSheetTicketTotalAmount(container _data)
{
    DataAreaId         companyId;
    str 20             axUserId;
    CRMUserId          crmUserId;
    INDFileId          fileId;
    Amount             newTotalAmount;

    INDTicketInfoTable ticketHeader;
    container          adjustResult;
    boolean            lineCreated;
    Amount             previousTotalAmount;
    Amount             savedTotalAmount;
    Amount             differenceAmount;
    RecId              adjustmentLineRecId;
    int                startLine;
    str                msg;
    ;

    companyId = INDCRMUtilityService::getCompany(_data);

    if (!companyId)
        return INDCRMUtilityService::buildHeader(false, 'CompanyId es obligatorio.', conNull());

    if (conLen(_data) < 4)
        return INDCRMUtilityService::buildHeader(false, 'Input container invalido. Se esperan 4 campos.', conNull());

    axUserId       = conPeek(_data, 2);
    fileId         = conPeek(_data, 3);
    newTotalAmount = conPeek(_data, 4);

    if (!axUserId)
        return INDCRMUtilityService::buildHeader(false, 'AxUserId es obligatorio.', conNull());

    if (!fileId)
        return INDCRMUtilityService::buildHeader(false, 'FileId es obligatorio.', conNull());

    if (newTotalAmount < 0)
        return INDCRMUtilityService::buildHeader(false, 'TotalAmount no puede ser negativo.', conNull());

    startLine = infolog.line();

    try
    {
        changecompany(companyId)
        {
            crmUserId = INDCRMExpenseSheetService::resolveCrmUserIdFromAny(axUserId);

            if (!crmUserId)
                return INDCRMUtilityService::buildHeader(false, 'crmUserId es obligatorio.', conNull());

            select firstonly ticketHeader
                where ticketHeader.FileId          == fileId
                   && ticketHeader.CreatedByUserId == axUserId;

            if (!ticketHeader.RecId)
                return INDCRMUtilityService::buildHeader(false, 'Ticket no encontrado.', conNull());

            adjustResult = ticketHeader.adjustTotalAmount(newTotalAmount, axUserId);

            lineCreated         = conPeek(adjustResult, 1);
            previousTotalAmount = conPeek(adjustResult, 2);
            savedTotalAmount    = conPeek(adjustResult, 3);
            differenceAmount    = conPeek(adjustResult, 4);
            adjustmentLineRecId = conPeek(adjustResult, 5);

            return INDCRMUtilityService::buildHeader(true, 'Importe total ajustado correctamente.',
                                                     [ticketHeader.FileId,
                                                      strFmt('%1', previousTotalAmount),
                                                      strFmt('%1', savedTotalAmount),
                                                      strFmt('%1', differenceAmount),
                                                      strFmt('%1', adjustmentLineRecId),
                                                      lineCreated ? '1' : '0',
                                                      'AJUSTE DE IMPORTE TOTAL',
                                                      lineCreated ? '1' : '0']);
        }
    }

    return INDCRMUtilityService::buildHeader(false, 'Error no controlado.', conNull());
}
```

## Ajuste recomendado en detalle de ticket

Para que la API pueda diferenciar lineas normales de lineas de ajuste al consultar el ticket, ampliar la fila de `getExpenseSheetTicket` para agregar `INDTicketInfoLine.Adjustment` al final de cada linea.

Contrato actual compatible:

```text
[RecId, Description, Qty, Price, TotalAmount, RefRecIdTable, CreatedByUserId, Adjustment]
```

La API expone ese ultimo campo como `AdjustmentAmount` y lo trata como opcional; si AX no lo envia, el campo queda `null`.

## Validacion pendiente en Axapta

- Importar y compilar `INDTicketInfoTable` y `INDCRMExpenseSheetService`.
- Ejecutar el endpoint con un nuevo total mayor y confirmar una linea positiva con `Adjustment = Yes` en AX y `AdjustmentAmount = true` en API.
- Ejecutar el endpoint con un nuevo total menor y confirmar una linea negativa con `Adjustment = Yes` en AX y `AdjustmentAmount = true` en API.
- Ejecutar el endpoint con el mismo total y confirmar que no se crea linea adicional.
- Confirmar que `getExpenseSheetTicket` devuelve el flag `Adjustment` para las lineas de ajuste.

## Actualizacion adicional: cabecera de hoja siempre local

Objetivo: retirar el uso funcional de la cabecera multidivisa en hojas de gasto y evitar que los endpoints de cabecera sobrescriban divisas reales de linea.

Metodos tocados:

- `createExpenseSheet`
- `updateExpenseSheetHeader`
- `propagateExpenseSheetCurrencyDefaults`
- `updateExpenseSheetLine`

Reglas funcionales:

- `createExpenseSheet` mantiene `CurrencyCode/ExchRate` de entrada solo como compatibilidad para la divisa por defecto de nuevas lineas.
- La cabecera se normaliza siempre con `CRMHojaGastosTable.normalizeReimbursementCurrencyDefaults()`.
- `updateExpenseSheetHeader` acepta campos legacy de divisa, pero no los aplica a cabecera.
- `propagateExpenseSheetCurrencyDefaults` queda como no-op compatible y devuelve `updatedLines = 0`.
- `updateExpenseSheetLine` ya no consulta cabecera "varios"; si la linea no trae divisa usa la divisa local de cabecera.

Pendiente:

- Importar y compilar `CRMHojaGastosTable`, `CRMHojaGastosLine` e `INDCRMExpenseSheetService`.
- Probar que el endpoint legacy de propagacion no modifica lineas existentes.
