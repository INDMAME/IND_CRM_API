# Cambios Axapta - INDEmailTemplates - 2026-05-29

## Objetivo

Ajustar la busqueda de plantillas vigentes para que `ToDate` vacio se trate de forma explicita como una plantilla sin vencimiento.

## Metodo tocado

- `INDEmailTemplates::findValid`

## Detalle

- Se mantiene la busqueda por `TargetModule`, `LanguageId` y `FromDate <= fecha de referencia`.
- Dentro del recorrido ordenado por `FromDate desc`, una plantilla es valida si:
  - `ToDate` esta vacio, o
  - `ToDate >= fecha de referencia`.
- Se elimina la variable local `toDate`, porque ya no hace falta sustituir el vacio por una fecha maxima artificial.

## Compatibilidad

El contrato del metodo no cambia. La llamada actual desde `INDCRMExpenseSheetService` sigue usando los mismos parametros y recibe el mismo tipo de retorno.

## Riesgo residual

Despues de importar el XPO en Axapta, compilar la tabla y validar un envio con una plantilla que tenga `FromDate` informado y `ToDate` vacio.
