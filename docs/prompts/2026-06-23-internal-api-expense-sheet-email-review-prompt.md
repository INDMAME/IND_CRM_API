# Prompt para revision Codex: error AX_COM_ERROR al cambiar estado de hoja de gastos

Actua como senior engineer en el proyecto `IND_CRM_API` y revisa el flujo de cambio de estado de hojas de gastos. Necesito que investigues y propongas una correccion segura para un error que afecta a jefatura al aprobar/rechazar hojas.

## Contexto

Endpoint afectado:

```text
PUT /api/crm/expensesheets/{hojaGastosId}
```

Controlador principal:

```text
Controllers/CRM/CrmExpenseSheetsController.cs
```

Metodo C#:

```text
UpdateExpenseSheetHeader
```

Metodo AX llamado:

```text
INDCRMExpenseSheetService.updateExpenseSheetHeader
```

Caso observado:

```text
hojaGastosId=HG000062
company=ISE
axUserId=MAME
actorAxUserId=DM
currencyCode=EUR
projId=VARIOS
errorCode=AX_COM_ERROR
httpStatus=500
```

Trazas de logs:

```text
138af2648ef544a0b8c66c80bfd55b59
80702a6b1be744329168a597660d0d34
```

Logs revisados:

```text
C:\INDAxaptaLogs\AxaptaAudit_20260623.log
C:\inetpub\wwwroot\IND_CRM_APP\Logs\indpersonasapp-20260623.log
```

Stack AX observado:

```text
\Classes\COM\SendMailEx
\Classes\INDInternalApiClientServer\sendInternalApiMailEx - line 56
\Classes\INDCRMUtilityService\sendInternalApiMailEx - line 25
\Classes\INDCRMExpenseSheetService\sendExpenseSheetStatusNotification - line 157
\Classes\INDCRMExpenseSheetService\updateExpenseSheetHeader - line 214
```

Mensaje COM:

```text
Error al ejecutar codigo: Se ha llamado al metodo con un numero no valido de parametros.
```

## Hipotesis principal

El fallo se produce al enviar la notificacion de email del cambio de estado. Parece una desalineacion de firma o version entre Axapta y el metodo COM/DLL `IND.InternalApiClient.SendMailEx`.

La documentacion del proyecto indica que el contrato actual de `SendMailEx` incluye `attachmentFilePaths` despues de `textBody` y antes de `saveToSentItems`. Incluso cuando no hay adjuntos, Axapta debe pasar un valor vacio para no desplazar argumentos.

## Objetivos de la revision

1. Confirmar si el contenedor construido por `UpdateExpenseSheetHeader` en C# es compatible con `INDCRMExpenseSheetService.updateExpenseSheetHeader`.
2. Confirmar si la excepcion se origina realmente en `sendExpenseSheetStatusNotification` y no en el payload enviado desde C#.
3. Revisar la documentacion:

```text
docs/plans/2026-05-22-expense-sheet-email-deeplinks-design_API.md
docs/email-templates/expense-sheets/README.md
```

4. Revisar si el envio de email esta definido como best-effort y si la implementacion AX cumple esa regla.
5. Proponer la correccion minima para que un fallo de email no bloquee el cambio de estado.
6. Validar que la API interna devuelva un error suficientemente diagnosticable sin exponer stack traces ni secretos.

## Restricciones

- No romper el contrato `IndApiResponse<T>`.
- Mantener compatibilidad con `IND_CRM_APP`.
- No ocultar errores de Axapta que puedan indicar rollback o estado incierto.
- No implementar reintentos desde C# si no se puede confirmar idempotencia.
- No devolver stack traces completos al cliente.
- No introducir dependencias nuevas salvo justificacion fuerte.

## Correccion local ya aplicada en C#

Se agrego diagnostico estructurado en `UpdateExpenseSheetHeader` para que el envelope de error incluya:

```text
Message mas especifico
ErrorCode=AX_COM_ERROR
Errors[].Field/Message con operacion AX, hoja, actor, HRESULT, mensaje COM y causa probable
TraceId
```

Esto mejora la investigacion, pero no corrige la causa funcional dentro de Axapta/DLL.

## Entregable esperado

Devuelve:

1. Diagnostico tecnico con evidencia.
2. Causa raiz mas probable.
3. Lista de archivos/clases AX/DLL que deben revisarse.
4. Correccion propuesta en AX o DLL.
5. Riesgos transaccionales.
6. Plan de pruebas manuales:
   - aprobar hoja sin adjuntos
   - aprobar hoja con adjuntos
   - rechazar hoja
   - cambio hecho por jefatura sobre subordinado
   - fallo simulado del servicio de email
7. Recomendacion sobre si la notificacion debe ser best-effort y como loguearla.
