# AX INDControlDataVisibility jobs changes - 2026-06-23

## Objetivo
Evitar validar el caso ABI con jobs o clases AX antiguas. Las capturas anteriores no muestran los marcadores `build` actuales ni el `utilityBuild` de `INDCRMUtilityService`, por lo que no prueban todavia el comportamiento de la clase compilada con `ownonly-guard`.

## Jobs actualizados
- `Job_INDDVUsersDiag.xpo`
- `Job_INDDVUsers.xpo`
- `Job_Test_ctrlGetVisibleUsers.xpo`
- `Job_INDDV_ABI_OwnOnlySmoke.xpo`
- `Job_INDDVUsersCases.xpo`

## Cambios
- Se actualizo el marcador de los jobs a `2026.06.23-runtime-version-gate`.
- Si `ctrlGetVisibleUsers` no devuelve `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard`, el job ahora ejecuta `throw error(...)` y no lista usuarios.
- Si el header no trae los campos extendidos de fila efectiva, el job ahora ejecuta `throw error(...)` y no continua.
- `Job_INDDVUsersDiag.xpo` sigue siendo texto X++ plano para copiar y pegar en el editor de jobs de Axapta.
- `Job_INDDVUsersCases.xpo` valida A/B/C/D solo despues de comprobar `utilityBuild` y el header efectivo extendido.

## Validacion esperada
- Una ejecucion valida debe mostrar primero `runtime-version-gate`.
- Debe mostrar `utilityBuild=INDCRMUtilityService build=2026.06.22-ownonly-guard`.
- Para `VISITAS_GESTION` configurado como `Solo propios`, el header efectivo debe mostrar `dataVis=0/OwnOnly`.
- Si esos marcadores no aparecen, hay que reimportar y compilar `INDCRMUtilityService` antes de interpretar si ABI aparece o no.

## Pendiente
- Reejecutar en AX el smoke `Job_INDDV_ABI_OwnOnlySmoke` o el texto plano `Job_INDDVUsersDiag`.
- Capturar las lineas `runtime-version-gate`, `utilityBuild`, `header effective` y `lineCount`.
- Ejecutar `Job_INDDVUsersCases` para validar el caso activo A/B/C/D despues de importar y compilar la clase actual.
