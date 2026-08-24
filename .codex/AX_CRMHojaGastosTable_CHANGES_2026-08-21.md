# CRMHojaGastosTable - 2026-08-21

## Objetivo

Representar correctamente el proyecto agregado de una hoja y proporcionar un
default valido para la siguiente linea.

## Metodos modificados

- `defaultProjectForNewLine`: obtiene la ultima linea por fecha, hora y `RecId`,
  filtrada por hoja y usuario. La ultima linea vacia produce vacio; sin lineas se
  usa la cabecera solo si su proyecto sigue siendo elegible.
- `recalculateProjectFromLines`: aplica 0 lineas o todas vacias igual a vacio;
  un unico `ProjIdHornos` real igual a ese proyecto; cualquier mezcla, incluso
  con vacio, marcador reservado o un `ProjIdHornos` inexistente, igual al
  marcador configurado.
- `markHeaderVariousFromLine`: recalcula el agregado completo.
- `updateProjectDefaultInLines`: valida el destino y actualiza cabecera, lineas,
  asignaciones y costes dentro de una transaccion.
- `migrateVariousProjectMarker`: migra solo cabeceras cuyo `ProjId` coincide
  exactamente con el marcador anterior.
- `validateField`: la confirmacion del formulario AX guarda y propaga de forma
  atomica.

## Limites

- La migracion no toca lineas ni asignaciones historicas.
- `ProjIdHornos` es el proyecto operativo de linea. Una diferencia respecto al
  campo oculto `ProjId` no convierte por si sola la cabecera en varios.
- Si falta `INDProjIdVarious`, una inconsistencia deja la cabecera vacia y emite
  un warning hasta corregir la configuracion.

## Validacion pendiente en AX

- Importar y compilar la tabla.
- Probar hojas con 0, 1 y varias lineas, incluida mezcla con vacio.
- Probar cambios simultaneos y el cambio del parametro por empresa.
