# Flujo de usuario: crear o editar una linea de hoja de gastos

Fuente tecnica:
[expense-sheet-line-create-edit-flow.md](../../technical/expenses/expense-sheet-line-create-edit-flow.md)

Este diagrama explica que ocurre al cambiar los campos de una linea manual y
al guardarla. Mantiene las mismas decisiones de negocio que el diagrama
tecnico, pero evita nombres internos de clases y servicios.

```mermaid
flowchart TD
  Start([Abrir una linea de hoja de gastos]) --> Mode{Crear o editar?}
  Mode -->|Crear| Defaults["Preparar valores iniciales<br/>fecha de hoy; cantidad 1; proyecto valido de la hoja o vacio<br/>divisa de empresa, con la hoja como alternativa"]
  Mode -->|Editar| Load["Cargar los valores guardados"]
  Load --> Locked{Se puede editar?}
  Locked -->|Hoja bloqueada| ReadOnly["Mostrar la linea en modo consulta"]
  Locked -->|Ticket vinculado| Ticket["Mantener la linea bloqueada<br/>al pulsar Editar, abrir el ticket"]
  Locked -->|Si| Form["Editar los campos de la linea"]
  Defaults --> Form

  Form --> Changed{Que campo cambia?}
  Changed -->|Tipo o fecha| Km{Es kilometraje?}
  Km -->|Si| KmPrice["Obtener el precio por kilometro<br/>y bloquear el precio"]
  Km -->|No| Ready["Linea lista para revisar"]
  KmPrice --> Ready

  Changed -->|Cantidad o precio| Amount["Calcular importe<br/>Cantidad x Precio"]
  Changed -->|Importe| Price["Mantener cantidad<br/>recalcular el precio"]
  Amount --> Settlement["Actualizar el importe en divisa de empresa<br/>conservar el valor manual si es la misma divisa"]
  Price --> Settlement
  Settlement --> Ready

  Changed -->|Divisa| Currency{Es la misma divisa?}
  Changed -->|Fecha con divisa extranjera| Foreign
  Currency -->|Si| Local["Usar cambio 100<br/>importe en divisa de empresa = importe"]
  Currency -->|No| Foreign["Consultar el cambio oficial<br/>y calcular el importe en divisa de empresa"]
  Local --> Ready
  Foreign --> Ready

  Changed -->|Tipo de cambio| Rate["Recalcular el importe<br/>en divisa de empresa"]
  Changed -->|Importe en divisa de empresa| Gross["En otra divisa, recalcular<br/>el tipo de cambio"]
  Changed -->|Es reembolsable| Reimb["Guardar la eleccion y previsualizar<br/>Si: importe en divisa de empresa; No: 0"]
  Changed -->|Descripcion, internacional, proyecto| Other["Actualizar el dato sin recalcular importes"]
  Rate --> Ready
  Gross --> Ready
  Reimb --> Ready
  Other --> Ready

  Ready --> Save["Pulsar Guardar"]
  Save --> Valid{Datos obligatorios validos?}
  Valid -->|No| Fix["Mostrar el campo que debe corregirse"]
  Valid -->|Si| SaveMode{Alta o edicion?}
  SaveMode -->|Alta| Create["Anadir la linea a la hoja existente"]
  SaveMode -->|Edicion| Update["Actualizar la linea existente"]
  Create --> BusinessCheck["Comprobar usuario, permisos,<br/>estado de la hoja y reglas contables"]
  Update --> BusinessCheck
  BusinessCheck --> Accepted{Se cumplen las reglas?}
  Accepted -->|No| Error["Deshacer el guardado<br/>y mostrar el motivo"]
  Accepted -->|Si| Recalculate["Recalcular el importe definitivo<br/>y el valor reembolsable"]
  Recalculate --> Persist["Guardar la linea y actualizar<br/>proyecto e indicadores de la hoja"]
  Persist --> Linked{Existe una relacion con un ticket<br/>procedente de otro flujo?}
  Linked -->|Si| Sync["Sincronizar los datos relacionados<br/>con el ticket"]
  Linked -->|No| FinishMode{Alta o edicion?}
  Sync --> FinishMode
  FinishMode -->|Alta| SuccessCreate["Volver al detalle de la hoja"]
  FinishMode -->|Edicion| SuccessEdit["Recargar el detalle de la linea"]
```

## Reglas principales

- Una linea nueva comienza con fecha de hoy, cantidad `1` y la divisa
  predeterminada de la empresa; si falta, usa la de la hoja. Solo copia el
  proyecto de la hoja cuando es valido y unico; si esta vacio, es `VARIOS` o ya
  no admite gastos, la linea empieza sin proyecto.
- El precio de kilometraje se obtiene para la fecha elegida y no se puede
  escribir manualmente desde esta pantalla.
- Cambiar cantidad, precio o importe actualiza los otros valores relacionados.
  Si se introdujo manualmente el importe en divisa de empresa y ambas divisas
  coinciden, la pantalla conserva ese valor.
- En la misma divisa se usa el cambio `100`. En otra divisa se consulta el
  cambio oficial o se deriva del importe en divisa de empresa introducido.
- Cambiar la fecha solo refresca el tipo de cambio cuando la divisa es
  extranjera; tambien puede refrescar el precio de kilometraje.
- Marcar la linea como reembolsable no cambia el importe original: decide si el
  importe en divisa de empresa se incluye como reembolsable o queda en `0`.
- Al guardar, el sistema vuelve a calcular y validar los valores definitivos.
  Si una regla falla, no conserva un guardado parcial.

## Lineas vinculadas a un ticket

Una linea existente vinculada a un ticket no se edita como linea manual desde
esta pantalla. Al pulsar Editar, la aplicacion conduce al editor del ticket. La
vinculacion y la desvinculacion son recorridos independientes de este guardado.
