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
- `buildExpenseSheetWebLink`
  - Construye el enlace del resolver web con la base CRM web, no con la URL de `IND_INTERNAL_API`.
  - Resuelve la base desde `INDDefaultParameters::find().CRMAppUrl`.

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

## Correccion de URL web

El enlace de email debe apuntar a la web CRM:

```text
DEV: https://dev.insertec.biz:2053/Gastos/ExpenseSheetLink?...
```

No debe usar `INDDefaultParameters.InternalAPIUrlService`, porque esa clave apunta al transporte interno:

```text
DEV: https://dev.service.insertec.eu:2087/
```

Si `CRMAppUrl` no esta configurado, `buildExpenseSheetWebLink` devuelve vacio para que el envio de email se omita antes de emitir un enlace roto.

## Diagnostico de permisos de detalle

`getExpenseSheet` mantiene la regla existente:

```text
header.UserId == crmUserId && header.HojaGastosId == hojaId
```

Cuando esa regla no encuentra la hoja, la respuesta AX incluye extras de diagnostico para que `IND_CRM_API` los escriba en logs con el tag `[EXPENSE-AUTHZ-DETAIL]`:

```text
stage=detail-access
rule=header.UserId==crmUserId&&header.HojaGastosId==hojaId
companyId={company}
axUserId={session AX user}
crmUserId={CRMUsuarioTable::Find(axUserId).UserId}
hojaGastosId={sheet id}
sheetExists={0|1}
sheetUserId={CRMHojaGastosTable.UserId}
sheetCreatedBy={CRMHojaGastosTable.INDCreatedByUserId}
sheetStatus={CRMHojaGastosTable.ExpenseSheetStatus}
```

Esto no relaja permisos. Si `INDCreatedByUserId` debe permitir gestion, hay que definirlo como regla explicita en AX/API.

## Pendiente conocido

El punto exacto de remesa/pago queda pendiente de confirmacion funcional. El helper de pago ya acepta `userPagador` para integrarlo cuando se confirme el metodo definitivo.
