# AX INDCRMExpenseSheetService changes - 2026-05-27

## Objetivo

Centralizar en `INDCRMExpenseSheetService` el envio de mejor esfuerzo de emails de hojas de gasto para todas las transiciones soportadas.

## Metodos principales

- `resolveExpenseSheetNotificationEvent(fromStatus, toStatus)`
  - Resuelve el evento desde la transicion de estado.
- `sendExpenseSheetStatusNotification(expenseSheet, fromStatus, toStatus, actorUserId, userPagador, source, correlationId)`
  - Metodo global de negocio para montar evento, usuarios, URL, asunto/cuerpo y llamar al envio generico.
- `sendExpenseSheetPaidNotification(expenseSheet, userPagador)`
  - Wrapper de pago que delega en el metodo global con destino `Paid`.
- `resolveCrmUserIdFromAny(userId)`
  - Convierte usuario AX/CRM al `CRMUsuarioTable.UserId` usado para comparar emisor y destinatario.
- `buildExpenseSheetNotificationSubject` / `buildExpenseSheetNotificationMessage`
  - Mensajes simples en espanol para el email.

## Eventos soportados

- `Draft -> InReview`: `ExpenseSheetApprovalRequested`
- `InReview -> Approved`: `ExpenseSheetApproved`
- `InReview -> Rejected`: `ExpenseSheetRejected`
- `Rejected -> InReview`: `ExpenseSheetRejectionCancelled`
- `Any -> Paid`: `ExpenseSheetPaid`

Si `fromStatus` y `toStatus` son iguales, el metodo no envia email.

## Reglas de usuarios

- `ExpenseSheetApprovalRequested`: From propietario de la hoja, To actor recibido.
- `ExpenseSheetApproved`, `ExpenseSheetRejected`, `ExpenseSheetRejectionCancelled`: From actor, To propietario de la hoja.
- `ExpenseSheetPaid`: From `userPagador` si se informa, si no actor/current user; To `INDCreatedByUserId` si existe, si no propietario.
- Si emisor y destinatario resuelven al mismo usuario CRM, se omite el email.

## Cambios de tipo

Las variables/parametros AX de usuario tocados en esta clase pasan de `UserId` a `str 20` para evitar recortes de IDs.

## Pendiente conocido

El punto exacto de remesa/pago queda pendiente de confirmacion funcional. El helper de pago ya acepta `userPagador` para integrarlo cuando se confirme el metodo definitivo.
