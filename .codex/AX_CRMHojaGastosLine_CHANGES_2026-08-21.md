# CRMHojaGastosLine - 2026-08-21

## Objetivo

Evitar proyectos heredados invalidos y mantener el proyecto agregado de la
cabecera despues de altas, cambios y borrados de lineas.

## Metodos modificados

- `resolveRealProjectId`: devuelve el identificador canonico o vacio para un
  marcador reservado, un valor vacio o un proyecto inexistente.
- `resolveEligibleProjectId`: ademas exige `INDPermitirImputarGastos` y una
  `INDTransDateCierreCostes` vacia, igual que el catalogo de gastos.
- `InitFromHojaGastosTable` e `InitFromPreviousLine`: usan el default estricto
  de la ultima linea persistida.
- `insert`, `update` y `delete`: bloquean primero la cabecera y recalculan sus
  agregados dentro de la misma transaccion.
- `validateField` y `validateWrite`: rechazan proyectos nuevos cerrados, no
  imputables, inexistentes o reservados, pero permiten editar otros campos sin
  sustituir un proyecto historico que se haya cerrado despues.

## Compatibilidad

- No cambia el esquema de tabla.
- Una linea puede conservar o recibir explicitamente un proyecto vacio.
- El marcador de varios proyectos nunca se persiste en una linea nueva.

## Validacion pendiente en AX

- Importar y compilar la tabla en Axapta.
- Probar alta, cambio y borrado concurrentes en AOS.
- Confirmar la sincronizacion de asignaciones y costes por proyecto.
