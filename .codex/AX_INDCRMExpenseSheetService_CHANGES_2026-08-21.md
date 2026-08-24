# INDCRMExpenseSheetService - 2026-08-21

## Objetivo

Aplicar una semantica unica de proyecto en altas, detalle, propagacion y
actualizacion directa de hojas de gastos.

## Metodos modificados

- `createExpenseSheet`: la posicion 13 de cada linea indica si `lineProjId` fue
  proporcionado. Un vacio explicito se conserva; un valor explicito invalido se
  rechaza; si se omite, se usa `defaultProjectForNewLine`. El marcador legacy
  recibido en posicion 8 sin flag se considera omitido.
- `getExpenseSheet`: anade `DefaultLineProjId` al final de la cabecera. Es el
  extra 21 de `buildHeader`, posicion AX 23 contando exito y mensaje.
- `propagateExpenseSheetProjectDefault`: acepta proyecto en posicion 4 y el
  flag `projectProvided` en posicion 5. Los clientes legacy de cuatro posiciones
  conservan la inferencia por presencia. La propagacion es atomica.
- `updateExpenseSheetHeader`: la posicion 13 declara si el cliente cambio el
  proyecto. Solo entonces valida y actualiza el default de cabecera; las lineas
  se mantienen hasta invocar el endpoint dedicado de propagacion con
  confirmacion. Un guardado de otros campos nunca repone un proyecto obsoleto.
- `updateExpenseSheetLine`: rechaza un proyecto nuevo reservado o inelegible,
  conserva el borrado intencionado con flag explicito y mantiene la herencia
  historica de cabecera cuando el contenedor no incluye la posicion 17.

## Compatibilidad

- Los campos existentes del detalle no cambian de posicion.
- Una peticion dedicada sin body no limpia proyectos: la posicion 5 manda cuando
  esta presente.
- Los clientes legacy sin posicion 17 conservan la semantica historica de linea:
  proyecto no vacio explicito y, sin valor, herencia de cabecera salvo marcador
  varios. Los clientes nuevos solo anaden la posicion 17 al enviar el flag.
- Los proyectos automaticos historicos inelegibles se convierten en vacio; los
  valores explicitos no se corrigen silenciosamente.

## Validacion pendiente en AX

- Importar y compilar la clase en Axapta.
- Probar los contenedores antiguos y nuevos mediante Business Connector.
- Ejecutar pruebas de concurrencia y confirmar la forma real del contenedor.
