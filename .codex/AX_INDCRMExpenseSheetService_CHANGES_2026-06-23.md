# Cambios Axapta - INDCRMExpenseSheetService - 2026-06-23

## Objetivo

Reforzar el comportamiento best-effort de las notificaciones de hojas de gastos y dejar trazas mas diagnosticas cuando falle el envio de email.

## Metodos tocados

- `INDCRMExpenseSheetService::updateExpenseSheetHeader`
- `INDCRMExpenseSheetService::sendExpenseSheetStatusNotification`

## Cambios

- `updateExpenseSheetHeader` mantiene el `ttscommit` antes de lanzar el email.
- La llamada de email posterior al commit queda envuelta en `try/catch` propio para que los fallos catchables de notificacion no cambien la respuesta funcional del cambio de estado.
- El envio best-effort captura `Exception::DDEerror`, `Exception::Error` y `Exception::Internal` porque el fallo observado de COM no quedo contenido con los catches anteriores.
- Se agregan trazas de intento, resultado no aceptado y resultado aceptado con:
  - `company`
  - `hojaGastosId`
  - estado anterior y nuevo
  - actor de notificacion
  - `eventType`
  - `correlationId`
  - `idempotencyKey`
- No se registran secretos, cuerpos HTML/texto ni direcciones de email.

## Hallazgo relacionado

El error observado `Se ha llamado al metodo con un numero no valido de parametros` no viene del container enviado por `IND_CRM_API`. En la revision inicial, el ProgID COM x86 `IND.InternalApiClient` apuntaba a una DLL registrada de 2026-05-26 cuyo `SendMailEx` exponia 22 parametros y no incluia `attachmentFilePaths`.

La XPO actual y el codigo fuente de `C:\INDProjects\IND_INTERNAL_API\INDInternalApiClient` esperan 23 parametros, con `attachmentFilePaths` despues de `textBody`.

## Validacion de despliegue

El 2026-06-23 se ejecuto:

```powershell
.\scripts\deploy-indinternalapiclient-com.ps1 -Configuration Release
```

Resultado:

- Build `Release|x86` correcto, sin warnings ni errores.
- DLL copiada y registrada desde `C:\INDAxaptaConfigAPI\API Internal Client\INDInternalApiClient.dll`.
- Type library regenerada en `C:\INDAxaptaConfigAPI\API Internal Client\INDInternalApiClient.tlb`.
- PowerShell x86 confirma `SendMailEx` con 23 parametros y `attachmentFilePaths` en posicion 13.
- SHA256 de la DLL registrada: `B2760CB88E0B8DA351A518C4F93E0619991DD68FB9535638AB8CCA13503881EE`.
- Health de `https://dev.service.insertec.eu:2087/api/health/status` devuelve HTTP 200.

## Compatibilidad

- No cambia la firma de `updateExpenseSheetHeader`.
- No cambia el container recibido desde `IND_CRM_API`.
- No cambia el contrato HTTP ni el envelope `IndApiResponse<T>`.
- Los cambios solo agregan logs y aislamiento best-effort del envio de email despues del commit.

## Validacion pendiente en Axapta

- Importar y compilar `INDCRMExpenseSheetService.xpo` en Axapta.
- Repetir aprobacion y rechazo de hoja con una DLL COM registrada que exponga `SendMailEx` de 23 parametros.
- Confirmar que, si el transporte de email devuelve `false`, el cambio de estado devuelve exito y queda warning en infolog/logs.
- Confirmar que un error AX de negocio antes del `ttscommit` sigue devolviendo error y no queda oculto por el best-effort.

## Ajuste adicional: multiples jefaturas en solicitud de aprobacion

### Objetivo

Permitir que una hoja enviada a `InReview` notifique por email a todas las jefaturas directas configuradas para el propietario en `CRMUsuarioSubordinadoTable`, manteniendo el idioma de plantilla de cada destinatario.

### Metodos tocados

- `INDCRMExpenseSheetService::getExpenseSheetNotificationRecipients`
- `INDCRMExpenseSheetService::resolveExpenseSheetManagerCrmUserIds`
- `INDCRMExpenseSheetService::resolveExpenseSheetManagerCrmUserId`
- `INDCRMExpenseSheetService::sendExpenseSheetNotificationToRecipient`
- `INDCRMExpenseSheetService::sendExpenseSheetStatusNotification`

### Cambios

- Se agrega `resolveExpenseSheetManagerCrmUserIds` para devolver todas las jefaturas directas del propietario, deduplicadas y sin incluir al propio usuario.
- `resolveExpenseSheetManagerCrmUserId` queda como wrapper legacy para preservar compatibilidad con cualquier llamador singular.
- `getExpenseSheetNotificationRecipients` devuelve una fila `Approver` por cada jefatura con email en solicitudes de aprobacion.
- `sendExpenseSheetStatusNotification` envia la solicitud de aprobacion a cada jefatura en envios secuenciales best-effort, no en paralelo contra COM.
- El render de plantilla e idioma se mueve a `sendExpenseSheetNotificationToRecipient`, de forma que cada jefatura usa su propio `LanguageId`.
- La `idempotencyKey` solo incorpora `recipientCrmUserId` en solicitudes de aprobacion con posible multi-destinatario; los eventos de destinatario unico conservan el formato anterior.
- No se registran tokens, cuerpos HTML/texto ni direcciones de email. Las trazas agregan `recipientCrmUserId` para diagnostico sin exponer el correo.

### Compatibilidad

- No cambia la firma publica de `sendExpenseSheetStatusNotification`.
- No cambia el contrato del container de `getExpenseSheetNotificationRecipients`; solo puede devolver mas filas `Approver` para el mismo evento.
- No cambia el flujo de `Approved`, `Rejected` ni `Paid` salvo reutilizar el helper comun de envio por destinatario.
- El envio sigue ejecutandose despues del `ttscommit` y es best-effort.

### Validacion pendiente en Axapta

- Importar y compilar `INDCRMExpenseSheetService.xpo`.
- Crear o localizar un subordinado con dos registros en `CRMUsuarioSubordinadoTable`.
- Pasar una hoja de `Draft` a `InReview` y confirmar dos intentos/aceptaciones de email con distinta `idempotencyKey`.
- Confirmar que cada jefatura recibe su template en el idioma configurado en `SysUserInfo.Language`.
- Confirmar que ambas jefaturas ven la hoja y que cualquiera puede aprobar/rechazar si la hoja sigue en estado pendiente.
- Confirmar que si una jefatura no tiene email, se intenta la otra y queda warning solo para la que no puede enviarse.
