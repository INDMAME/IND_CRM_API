# AX INDEmailTemplatesForm changes - 2026-06-01

## Objetivo
- Ajustar el envio de prueba CRM para usar el endpoint extendido de email y enviar `importance = high`.
- Inicializar en el dialog los estados anterior/nuevo segun el `TargetModule` de la plantilla seleccionada.
- Dejar preparado un boton opcional para pruebas directas de `EmailTest` contra `IND_INTERNAL_API` sin resolver usuarios ni hojas CRM.

## Cambios
- DataSource `INDEmailTemplates.active()`:
  - Mantiene `SendTestEmail` habilitado solo para plantillas vigentes.
  - Si existe un control llamado `SendDirectInternalApiMailExTest`, lo muestra y habilita solo cuando `TargetModule == EmailTest` y la plantilla esta vigente.
- Boton CRM `SendTestEmail.clicked()`:
  - Agrega defaults de transicion por `TargetModule`:
    - `CRMInReview`: `Draft -> InReview`.
    - `CRMApproved`: `InReview -> Approved`.
    - `CRMRejected`: `InReview -> Rejected`.
    - `CRMPaid`: `Approved -> Paid`.
  - Cambia el transporte de `sendInternalApiMail` a `sendInternalApiMailEx` con `importance = high`.
- El boton directo queda integrado directamente en `.codex/Axapta/INDEmailTemplatesForm.xpo`.
- No se mantiene un `.xpp` separado para evitar duplicidad con el formulario importable.

## Notas
- En el XPO actual de `INDEmailTemplates` no existe un campo `INDExpenseSheetStatus`; la inicializacion se resuelve desde `TargetModule`, que es el campo disponible y alineado con el tipo de plantilla.
- El boton directo debe llamarse `SendDirectInternalApiMailExTest` para aprovechar la visibilidad automatica desde `active()`.
- Si los emails enviados por `sendInternalApiMailEx` siguen sin mostrar importancia alta, el siguiente punto de revision es que AOS tenga registrada la DLL actualizada y que `IND_INTERNAL_API` desplegada propague `importance` al payload de Microsoft Graph.

## Ajuste adicional
- `SendTestEmail` queda como boton de prueba CRM:
  - Visible solo para `TargetModule` distinto de `Empty` y `EmailTest`.
  - Habilitado solo si la plantilla esta vigente y el modulo es CRM.
- `SendDirectInternalApiMailExTest` queda como boton directo de transporte:
  - Visible y habilitado solo para `TargetModule == EmailTest` y plantilla vigente.

## Ajuste enum importancia
- El dialog directo usa `typeid(INDEmailImportance)` y valor por defecto `enum2value(INDEmailImportance::high)`.
- Antes de llamar a la API interna, el valor seleccionado se convierte con `INDInternalApiClientServer::normalizeInternalApiMailImportance`.
- El boton CRM tambien envia `strFmt("%1", enum2value(INDEmailImportance::high))`, dejando la conversion final en `INDInternalApiClientServer`.
- Motivo: `enum2value` devuelve `0/1/2`; la API interna necesita `low/normal/high`.

## Ajuste link opcional
- En `SendDirectInternalApiMailExTest`, el campo `Link de prueba` pasa a ser opcional.
- Si no se informa, el codigo usa `#` solo para resolver el placeholder `%6` de la plantilla HTML sin bloquear el envio.
- El cuerpo de texto plano omite el link cuando se usa ese fallback tecnico.
- No se modifica `email-test.html`: la plantilla sigue siendo valida porque `%6` siempre recibe un valor.
