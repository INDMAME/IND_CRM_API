# Plantillas de correo electrónico para hojas de gastos

Estas plantillas se usan como contenido del campo `INDEmailTemplates.HtmlTemplate`.
El asunto recomendado se guarda en `INDEmailTemplates.SubjectTemplate`.

La lógica vigente se valida contra `INDCRMExpenseSheetService` y
`INDCRMUtilityService`. Los HTML de esta carpeta son los artefactos mantenibles;
no debe crearse una segunda plantilla genérica fuera de esta ruta.

## Mapeo por TargetModule

| TargetModule | Archivo | SubjectTemplate | Uso |
| --- | --- | --- | --- |
| `CRMInReview` | `crm-in-review.html` | `Hoja de gastos %1 pendiente de aprobación` | Solicitud o reenvío a los responsables. También se envía cuando una decisión se deshace y la hoja vuelve a revisión. |
| `CRMApprovalRequestCancelled` | `crm-approval-request-undone.html` | `Solicitud de Aprobación Deshecha - hoja de gastos %1` | Aviso a los responsables cuando el propietario deshace la solicitud (`InReview -> Draft`). |
| `CRMApprovalUndone` | `crm-approval-undone.html` | `Aprobación deshecha - hoja de gastos %1` | Aviso al propietario cuando un responsable deshace una aprobación (`Approved -> InReview`). |
| `CRMRejectionUndone` | `crm-rejection-undone.html` | `Rechazo deshecho - hoja de gastos %1` | Aviso al propietario cuando un responsable deshace un rechazo (`Rejected -> InReview`). |
| `CRMApproved` | `crm-approved.html` | `Hoja de gastos %1 aprobada` | Aviso cuando una hoja pasa a aprobada. |
| `CRMRejected` | `crm-rejected.html` | `Hoja de gastos %1 rechazada` | Aviso cuando una hoja pasa a rechazada. |
| `CRMPaid` | `crm-paid.html` | `Hoja de gastos %1 pagada` | Aviso cuando se contabiliza el pago/remesa. |
| `EmailTest` | `email-test.html` | `Prueba plantilla email CRM %1` | Prueba técnica de renderizado y envío. |

No se genera plantilla para `Empty`; ese valor es técnico y no debe usarse para envíos reales. Los módulos nuevos usan los valores numéricos `5`, `7` y `8` del enum de Axapta; los valores existentes se conservan. `CRMApprovalRequestCancelled` y `ExpenseSheetApprovalRequestCancelled` siguen siendo los identificadores técnicos del evento; su texto visible es «Solicitud de Aprobación Deshecha».

## Placeholders

`INDCRMExpenseSheetService::renderExpenseSheetTemplate` aplica `strFmt` con este orden:

| Placeholder | Valor |
| --- | --- |
| `%1` | `HojaGastosId` |
| `%2` | Estado/evento visible |
| `%3` | Importe total con divisa |
| `%4` | Fecha `DD.MM.YYYY` |
| `%5` | Descripción |
| `%6` | Enlace de detalle CRM |
| `%7` | Texto del botón |
| `%8` | Año |
| `%9` | Mes abreviado |
| `%10` | Día con dos dígitos |
| `%11` | Comentarios de estado |
| `%12` | `src` del logo |

## Logo

El campo `INDEmailTemplates.Logo` guarda el Base64 del logo.
El servicio construye `%12` como `data:image/png;base64,<Logo>` si el valor no viene ya con prefijo `data:`.
En los HTML el logo siempre debe quedar como:

```html
<img src="%12" alt="insertec" width="180" height="48" style="display:block;width:180px;max-width:180px;height:auto;border:0;outline:none;text-decoration:none;" />
```

## Vigencia

La plantilla vigente se resuelve por:

- `TargetModule`
- `LanguageId`, tomado desde `SysUserInfo.Language`
- `FromDate <= today()`
- `ToDate >= today()` o `ToDate` vacío

La tabla valida que no existan rangos solapados para el mismo `TargetModule` + `LanguageId`.

## Transporte

El envío real lo decide Axapta desde `INDCRMExpenseSheetService` y sale por `INDCRMUtilityService::sendInternalApiMailEx` hacia `INDInternalApiClientServer::sendInternalApiMailEx`.

El único método COM/DLL admitido es `SendMailEx`. Su contrato incluye `attachmentFilePaths` después de `textBody` y antes de `saveToSentItems`.

- Para notificaciones de hojas de gastos se envía `attachmentFilePaths` vacío.
- Para estas notificaciones se envía `saveToSentItems=true` para solicitar a Graph una copia en Elementos enviados del buzón emisor.
- Cualquier flujo que adjunte ficheros debe pasar rutas absolutas ya preparadas y separadas por `;`.
- Esas rutas deben apuntar a ficheros copiados en la carpeta configurada en Axapta con `INDDefaultParameters.FilePathEmails`.
- `IND_CRM_API` no recibe ni almacena Base64 para este flujo; solo transporta las rutas preparadas o las deja vacías.
- La DLL lee los ficheros desde AOS, infiere el tipo de contenido y aplica los límites vigentes: máximo 10 adjuntos, 25 MB por fichero y 50 MB total antes de Base64.

## Registro de envíos en Axapta

`INDCRMExpenseSheetService` crea una fila en `INDMailLogTable` por destinatario cuando `SendMailEx` devuelve éxito para un aviso de cambio de estado. Usa la empresa de la hoja, `DocumentTypes=Crm_HojaGasto`, `IdDocumento=HojaGastosId`, el remitente y destinatario reales, el asunto, el cuerpo en texto plano y `TipoDocumento=eventType`. La fecha y hora de creación las aporta Axapta. La tabla no distingue fallos de envíos aceptados: los intentos rechazados siguen en los avisos de Axapta y los logs del transporte. Un fallo al insertar el registro no cambia el resultado del envío ni revierte el estado de la hoja.

La clase requiere que existan en el AOT `INDMailLogTable` y el valor `INDMailDocumentTypes::Crm_HojaGasto` creado en DEV. No se modifica la tabla ni se utiliza su método heredado `EnviarMail`.

## Checklist de importación en Axapta DEV

Si ya se importó la versión anterior de este evento, actualizar `INDEmailTemplateTargetModule` e `INDCRMExpenseSheetService`, el asunto de la fila `CRMApprovalRequestCancelled` y su HTML con `crm-approval-request-undone.html`. `INDEmailTemplatesForm` no cambia con este ajuste de texto. Para una primera instalación, seguir el checklist completo.

Si el flujo anterior ya está instalado y solo se quiere activar la copia en Elementos enviados, basta con reimportar y compilar `INDCRMExpenseSheetService.xpo`.

- [ ] Confirmar que el cliente está conectado a **DEV** y a la empresa donde se harán las pruebas. No usar una hoja real en trámite para provocar las transiciones.
- [ ] Antes de compilar la clase, verificar en el AOT que existen `INDMailLogTable` y `INDMailDocumentTypes::Crm_HojaGasto`. Para trasladar la integración a otra instalación, llevar primero ese valor del enum y la tabla si allí no existe.
- [ ] Exportar desde el AOT los objetos activos `INDEmailTemplateTargetModule`, `INDCRMExpenseSheetService` e `INDEmailTemplatesForm` como copia recuperable. Comparar los métodos activos con los XPO de esta entrega para preservar cambios de Axapta aún no versionados.
- [ ] Importar `.codex/Axapta/INDEmailTemplateTargetModule.xpo` sobre el enum existente y compilarlo. Comprobar que los valores `0` a `6` conservan sus números y que `CRMApprovalRequestCancelled=5`, `CRMApprovalUndone=7` y `CRMRejectionUndone=8` aparecen una sola vez.
- [ ] Importar `.codex/Axapta/INDCRMExpenseSheetService.xpo` sobre la clase existente. Compilar la clase y revisar errores, advertencias e Infolog.
- [ ] Importar `.codex/Axapta/INDEmailTemplatesForm.xpo` sobre el formulario existente. Compilar el formulario y verificar que la prueba de plantilla propone las transiciones correctas.
- [ ] Compilar las dependencias que Axapta señale. Estos tres XPO no cambian tablas, campos, índices ni relaciones: no se requiere sincronización de base de datos por este cambio. Si el cliente conserva metadatos antiguos, cerrar y reabrir el cliente antes de probar.
- [ ] En `INDEmailTemplates`, crear o actualizar una fila vigente por **TargetModule e idioma del destinatario** para cada uno de los tres eventos de la tabla anterior. Informar un `TemplateId` único de hasta 25 caracteres, el `LanguageId` real del destinatario y el `SubjectTemplate` de la tabla. Desde `Gestión HTML > Importar`, seleccionar el archivo HTML correspondiente y después usar `Previsualizar`. Asignar `Logo`, `FromDate` y `ToDate`; comprobar que no existan periodos solapados.
- [ ] Usar la prueba del formulario para `CRMApprovalRequestCancelled` con `InReview -> Draft` y un responsable destinatario. Verificar asunto, logo, enlace, estado, remitente propietario y que se recibe una sola vez.
- [ ] Usar la prueba del formulario para `CRMApprovalUndone` con `Approved -> InReview` y para `CRMRejectionUndone` con `Rejected -> InReview`. Indicar como remitente a un responsable distinto del propietario y verificar que el propietario recibe el mensaje correcto.
- [ ] Ejecutar las tres transiciones reales con hojas de prueba: solicitud deshecha por el propietario y ambas reversiones por un responsable. Confirmar el estado guardado, el aviso a todos los responsables cuando la hoja vuelve a revisión y el aviso adicional al propietario en cada reversión. Revisar `correlationId` e `idempotencyKey` en los registros del transporte y descartar duplicados.
- [ ] Comprobar que el mensaje de prueba aparece en Elementos enviados del buzón usado como remitente.
- [ ] En la empresa de la hoja de prueba, comprobar en `INDMailLogTable` una fila por destinatario aceptado, con `DocumentTypes=Crm_HojaGasto`, `IdDocumento=HojaGastosId`, remitente, destinatario, asunto y `TipoDocumento` del evento. Un envío rechazado no debe crear fila.
- [ ] Comprobar casos negativos: cambio sin transición, `Approved -> Draft` de autogestión y `Rejected -> Draft` no deben generar estos tres avisos. Confirmar que un fallo de correo no revierte el estado ya guardado.

Si falta una plantilla vigente, el servicio intenta enviar texto plano. El correo HTML solo quedará activo cuando exista la fila de `INDEmailTemplates` para el idioma del destinatario.

## Notas de las plantillas

- No fijar URLs en el código; el enlace lo entrega Axapta en `%6`.
- No pegar Base64 dentro del HTML; usar el campo `Logo`.
- Los HTML usan `Arial, Helvetica, sans-serif` como fuente estándar de correo electrónico.
- Los HTML usan estilos en línea, tablas, `bgcolor`, `align` y `valign`; no dependen de CSS externo, Google Fonts, `position:absolute`, sombras ni `border-radius`.
- El logo se limita con el atributo HTML `width="180"` y un estilo en línea para evitar que los clientes de correo ignoren el ancho CSS.
- Mantener los asuntos por debajo de 100 caracteres, que es el tamaño actual de `SubjectTemplate`.
- Tras importar en Axapta, probar un envío real para confirmar que `strFmt` resuelve correctamente `%10`, `%11` y `%12` en Axapta 3.0.
