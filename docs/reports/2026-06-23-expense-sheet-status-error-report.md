# Reporte: error al cambiar estado de hoja de gastos HG000062

Fecha: 2026-06-23
Proyecto revisado: IND_CRM_API
Flujo afectado: `PUT /api/crm/expensesheets/{hojaGastosId}`

## Resumen

Un usuario de jefatura intenta aprobar o cambiar el estado de la hoja de gastos `HG000062` y la aplicacion muestra `Error interno del servidor.`. La respuesta de la API interna devuelve `Success=false`, `ErrorCode=AX_COM_ERROR` y HTTP 500.

La evidencia apunta a un fallo dentro de Axapta durante la actualizacion de estado. La actualizacion llega hasta `INDCRMExpenseSheetService.updateExpenseSheetHeader`, pero al ejecutar la notificacion de email Axapta lanza una excepcion COM por numero incorrecto de parametros.

## Evidencia de logs

Logs revisados:

- `C:\inetpub\wwwroot\IND_CRM_APP\Logs\indpersonasapp-20260623.log`
- `C:\INDAxaptaLogs\AxaptaAudit_20260623.log`

Trazas afectadas:

- `138af2648ef544a0b8c66c80bfd55b59`
- `80702a6b1be744329168a597660d0d34`

Datos relevantes observados:

- Hoja: `HG000062`
- Empresa: `ISE`
- Usuario de hoja: `MAME`
- Usuario actor/jefatura: `DM`
- Moneda: `EUR`
- Proyecto: `VARIOS`
- Cambio de estado observado: `2 -> 1` y despues `1 -> 2`
- API interna responde HTTP 500 con `AX_COM_ERROR`
- La web encapsula la respuesta y devuelve JSON con `Success=false`

Stack AX observado en el log:

```text
\Classes\COM\SendMailEx
\Classes\INDInternalApiClientServer\sendInternalApiMailEx - line 56
\Classes\INDCRMUtilityService\sendInternalApiMailEx - line 25
\Classes\INDCRMExpenseSheetService\sendExpenseSheetStatusNotification - line 157
\Classes\INDCRMExpenseSheetService\updateExpenseSheetHeader - line 214
```

Mensaje COM observado:

```text
Error al ejecutar codigo: Se ha llamado al metodo con un numero no valido de parametros.
```

## Causa probable

La causa probable no esta en el payload de la web ni en la llamada HTTP del frontend. El contenedor llega a Axapta y el fallo aparece despues, dentro del flujo de notificacion.

El punto mas probable es una desalineacion entre:

- La firma esperada por el metodo COM/DLL `IND.InternalApiClient.SendMailEx`.
- La llamada desde `INDInternalApiClientServer::sendInternalApiMailEx`.
- La version registrada/cargada del componente COM en el servidor AOS.

La documentacion vigente indica que `SendMailEx` debe recibir `attachmentFilePaths` despues de `textBody` y antes de `saveToSentItems`. Si Axapta llama a una version antigua o con el orden anterior, el COM puede devolver exactamente este tipo de error por numero incorrecto de parametros.

Validacion local adicional:

- El ProgID COM x86 `IND.InternalApiClient` resuelve a `C:\INDAxaptaConfigAPI\API Internal Client\INDInternalApiClient.DLL`.
- La DLL registrada tiene `LastWriteTime=2026-05-26 10:33:52` y SHA256 `CABA956559F3B728C680016D7CE376A312C88077BF2FD3B9DE523468813E699F`.
- La reflexion en PowerShell de 32 bits muestra que esa DLL expone `SendMailEx` con 22 parametros.
- El codigo fuente vigente de `C:\INDProjects\IND_INTERNAL_API\INDInternalApiClient` declara `SendMailEx` con 23 parametros, incluyendo `attachmentFilePaths`.
- El binario encontrado en `IND_INTERNAL_API_SP3` tambien expone 22 parametros y no representa el contrato de correo vigente.

## Impacto

- El cambio de estado de hoja de gastos queda bloqueado para usuarios de jefatura.
- El error de email afecta a la estabilidad de una operacion principal.
- La UI solo muestra un mensaje generico si la API interna no devuelve diagnostico suficiente.
- No se puede asumir desde C# si Axapta hizo rollback o si dejo efectos parciales, porque la excepcion ocurre dentro del metodo AX.

## Correccion local aplicada en API interna

Se mejoro el manejo de excepciones en `CrmExpenseSheetsController.UpdateExpenseSheetHeader`:

- Mantiene el contrato existente `IndApiResponse<object>`.
- Mantiene `ErrorCode=AX_COM_ERROR` para no romper consumidores actuales.
- Cambia el `Message` generico por un mensaje tecnico mas util cuando el fallo es COM.
- Agrega `Errors` con diagnostico estructurado:
  - operacion AX
  - hoja de gastos
  - usuario AX
  - usuario actor
  - estado solicitado
  - tipo de excepcion
  - HRESULT
  - mensaje COM saneado
  - causa probable cuando encaja con `SendMailEx` o parametros invalidos

Esta correccion no intenta reintentar ni saltarse el envio de email desde C#, porque eso seria inseguro sin conocer el estado transaccional dentro de Axapta.

## Correccion local preparada en Axapta

Se preparo ajuste en XPO para que el envio de email de estado sea realmente best-effort:

- `INDCRMExpenseSheetService::updateExpenseSheetHeader` mantiene el `ttscommit` antes del email.
- La llamada posterior al commit queda envuelta en `try/catch` propio.
- `sendExpenseSheetStatusNotification` y `INDInternalApiClientServer::sendInternalApiMailEx` capturan `Exception::DDEerror`, `Exception::Error` y `Exception::Internal`.
- Se agregan trazas sin secretos ni cuerpos de email para intento, resultado `false`, aceptacion y fallos COM/DDE.

Esta parte queda pendiente de importar y compilar en Axapta.

## Recomendacion funcional

La correccion definitiva debe hacerse en Axapta/DLL:

1. Revisar `INDInternalApiClientServer::sendInternalApiMailEx` en la linea 56.
2. Validar la firma real de `IND.InternalApiClient.SendMailEx` registrada en el servidor.
3. Confirmar que `attachmentFilePaths` se pasa como argumento aunque no haya adjuntos.
4. Confirmar el orden exacto:

```text
htmlBody, textBody, attachmentFilePaths, saveToSentItems, importance
```

5. Hacer que `sendExpenseSheetStatusNotification` trate el envio de email como best-effort: loguear el fallo, pero no fallar la actualizacion de estado.
6. Compilar `IND_INTERNAL_API\INDInternalApiClient` en x86 Release, reemplazar/registrar la DLL COM usada por AOS y confirmar que `SendMailEx` expone 23 parametros.

## Riesgos pendientes

- La API interna puede mejorar el diagnostico, pero no puede garantizar la estabilidad funcional mientras Axapta siga ejecutando una DLL COM registrada con firma antigua.
- Los XPO preparados no corrigen runtime hasta importarse y compilarse en Axapta/AOS.
- Se necesita una prueba manual contra AX/AOS para confirmar si el cambio de estado se conserva o si la transaccion hace rollback.
- Si existen varios AOS o varias instalaciones COM, hay que validar que todos tengan la misma version registrada.
