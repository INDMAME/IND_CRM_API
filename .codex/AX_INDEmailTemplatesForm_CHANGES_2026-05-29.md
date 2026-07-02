# Cambios Axapta - INDEmailTemplatesForm - 2026-05-29

## Objetivo

Completar el boton `Enviar prueba` del formulario de plantillas para probar el email con el registro actual de `INDEmailTemplates`.

## Cambios

- `INDEmailTemplates_ds.active`
  - Habilita `SendTestEmail` solo cuando la plantilla actual esta vigente hoy.
  - Una plantilla es vigente si:
    - tiene `FromDate`,
    - `FromDate <= today()`,
    - `ToDate` esta vacio o `ToDate >= today()`.

- `SendTestEmail.clicked`
  - Usa el registro actual del datasource `INDEmailTemplates`.
  - Pide en dialogo:
    - hoja de gastos,
    - estado anterior,
    - estado nuevo,
    - usuario actor.
  - Si el estado nuevo es `Paid`, abre un segundo dialogo para pedir `Usuario pagador`.
  - No pide `From` ni `To`; resuelve los correos con la logica real de hojas de gastos y `INDPersonaTable`.
  - No pide `Source`; usa internamente `axapta-template-form-test` para construir la URL.
  - Fuerza el render con la plantilla seleccionada, validando que su `TargetModule` coincide con la transicion indicada. `EmailTest` se permite como plantilla tecnica.

## Motivo del segundo dialogo

El `Dialog` simple de Axapta no ofrece un cambio reactivo fiable para ocultar o mostrar campos al modificar otro campo. Para mantenerlo simple, el formulario solo pide `Usuario pagador` despues de que el usuario haya seleccionado `Paid`.

## Riesgo residual

Compilar el formulario en Axapta y validar:

- que `SendTestEmail` queda deshabilitado en plantillas no vigentes,
- que una plantilla vigente puede enviar prueba,
- que el caso `Paid` solicita el usuario pagador,
- que la DLL/API interna esta registrada en el cliente/AOS que ejecuta el formulario.
