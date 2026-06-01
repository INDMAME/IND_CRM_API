# AX INDCRMExpenseSheetService changes - 2026-06-01

## Objetivo
- Alinear el envio real de notificaciones de hojas de gastos con el nuevo enum `INDEmailImportance`.

## Cambios
- `sendExpenseSheetStatusNotification` deja de pasar el literal `high` y usa `strFmt('%1', enum2value(INDEmailImportance::high))`.
- La conversion de `2` a `high` queda centralizada en `INDInternalApiClientServer::normalizeInternalApiMailImportance`.

## Nota
- Importar/compilar `INDEmailImportance` e `INDInternalApiClientServer` antes de compilar esta clase.

## Ajuste adicional: detalle de hoja de gastos
- `getExpenseSheet` agrega al final de los datos de cabecera el nombre CRM del propietario de la hoja.
- Fuente AX: `CRMUsuarioTable::Find(header.UserId).Name`.
- Nuevo indice AX de cabecera: `[14] UserName`.
- Los indices anteriores `[1]..[13]` no cambian para mantener compatibilidad con la API y clientes existentes.

## Pendientes API/frontend
- Hecho en API: `ExpenseSheetDetailDto` expone `UserName`.
- Hecho en API: `CrmExpenseSheetsController.MapExpenseSheetDetail` mapea el indice `[14]` si AX lo devuelve y usa cadena vacia con contratos AX antiguos.
- Pendiente en frontend: consumir `UserName` en el detalle y mostrar `UserId + ' ' + UserName` solo cuando el usuario actual no sea el propietario CRM de la hoja.
