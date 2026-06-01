# AX INDCRMExpenseSheetService changes - 2026-06-01

## Objetivo
- Alinear el envio real de notificaciones de hojas de gastos con el nuevo enum `INDEmailImportance`.

## Cambios
- `sendExpenseSheetStatusNotification` deja de pasar el literal `high` y usa `strFmt('%1', enum2value(INDEmailImportance::high))`.
- La conversion de `2` a `high` queda centralizada en `INDInternalApiClientServer::normalizeInternalApiMailImportance`.

## Nota
- Importar/compilar `INDEmailImportance` e `INDInternalApiClientServer` antes de compilar esta clase.
