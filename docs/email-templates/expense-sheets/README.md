# Plantillas de email para hojas de gastos

Estas plantillas se usan como contenido del campo `INDEmailTemplates.HtmlTemplate`.
El asunto recomendado se guarda en `INDEmailTemplates.SubjectTemplate`.

## Mapeo por TargetModule

| TargetModule | Archivo | SubjectTemplate | Uso |
| --- | --- | --- | --- |
| `CRMInReview` | `crm-in-review.html` | `Hoja de gastos %1 pendiente de aprobacion` | Solicitud o reenvio a aprobacion. Tambien cubre el flujo `Rejected -> InReview`; no se crea `CRMRejectionCancelled`. |
| `CRMApproved` | `crm-approved.html` | `Hoja de gastos %1 aprobada` | Aviso cuando una hoja pasa a aprobada. |
| `CRMRejected` | `crm-rejected.html` | `Hoja de gastos %1 rechazada` | Aviso cuando una hoja pasa a rechazada. |
| `CRMPaid` | `crm-paid.html` | `Hoja de gastos %1 pagada` | Aviso cuando se contabiliza el pago/remesa. |
| `EmailTest` | `email-test.html` | `Prueba plantilla email CRM %1` | Prueba tecnica de renderizado y envio. |

No se genera plantilla para `Empty`; ese valor es tecnico y no debe usarse para envios reales.

## Placeholders

`INDCRMExpenseSheetService::renderExpenseSheetTemplate` aplica `strFmt` con este orden:

| Placeholder | Valor |
| --- | --- |
| `%1` | `HojaGastosId` |
| `%2` | Estado/evento visible |
| `%3` | Importe total con divisa |
| `%4` | Fecha `DD.MM.YYYY` |
| `%5` | Descripcion |
| `%6` | Link de detalle CRM |
| `%7` | Texto del boton |
| `%8` | Anio |
| `%9` | Mes abreviado |
| `%10` | Dia con dos digitos |
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
- `ToDate >= today()` o `ToDate` vacio

La tabla valida que no existan rangos solapados para el mismo `TargetModule` + `LanguageId`.

## Transporte

El envio real lo decide Axapta desde `INDCRMExpenseSheetService` y sale por `INDCRMUtilityService::sendInternalApiMailEx` hacia `INDInternalApiClientServer::sendInternalApiMailEx`.

Desde 2026-06-09 el unico metodo COM/DLL soportado es `SendMailEx`. Su contrato incluye `attachmentFilePaths` despues de `textBody` y antes de `saveToSentItems`.

- Para notificaciones de hojas de gastos se envia `attachmentFilePaths` vacio.
- Si un flujo futuro adjunta ficheros, debe pasar rutas absolutas ya staged y separadas por `;`.
- Esas rutas deben apuntar a ficheros copiados en la carpeta configurada en Axapta con `INDDefaultParameters.FilePathEmails`.
- `IND_CRM_API` no recibe ni almacena Base64 para este flujo; solo transporta o deja vacias las rutas staged.
- La DLL lee los ficheros desde AOS, infiere content type y aplica los limites vigentes: maximo 10 adjuntos, 25 MB por fichero y 50 MB total antes de Base64.

## Notas de importacion

- No hardcodear URLs; el enlace lo entrega Axapta en `%6`.
- No pegar Base64 dentro del HTML; usar el campo `Logo`.
- Los HTML usan `Arial, Helvetica, sans-serif` como fuente estandar de email.
- Los HTML usan estilos inline, tablas, `bgcolor`, `align` y `valign`; no dependen de CSS externo, Google Fonts, `position:absolute`, sombras ni `border-radius`.
- El logo se limita con atributo HTML `width="180"` y estilo inline para evitar que clientes de correo ignoren el ancho CSS.
- Mantener los asuntos por debajo de 100 caracteres, que es el tamano actual de `SubjectTemplate`.
- Tras importar en Axapta, probar un envio real para confirmar que `strFmt` resuelve correctamente `%10`, `%11` y `%12` en Axapta 3.0.
