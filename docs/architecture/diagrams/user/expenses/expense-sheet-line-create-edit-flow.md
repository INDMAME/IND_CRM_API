# Flujo de usuario: crear o editar una línea de hoja de gastos

Fuente técnica:
[expense-sheet-line-create-edit-flow.md](../../technical/expenses/expense-sheet-line-create-edit-flow.md)

Este diagrama explica qué ocurre al cambiar los campos de una línea manual y
al guardarla. Mantiene las mismas decisiones de negocio que el diagrama
técnico, pero evita nombres internos de clases y servicios.

```mermaid
flowchart TD
  Start([Abrir una línea de hoja de gastos]) --> Mode{¿Crear o editar?}
  Mode -->|Crear| Defaults["Preparar valores iniciales<br/>fecha de hoy; cantidad 1; proyecto válido de la hoja o vacío<br/>divisa de empresa, con la hoja como alternativa"]
  Mode -->|Editar| Load["Cargar los valores guardados"]
  Load --> Locked{Se puede editar?}
  Locked -->|Hoja bloqueada| ReadOnly["Mostrar la línea en modo consulta"]
  Locked -->|Ticket vinculado| Ticket["Mantener la línea bloqueada<br/>al pulsar Editar, abrir el ticket"]
  Locked -->|Sí| Form["Editar los campos de la línea"]
  Defaults --> Form

  Form --> Changed{¿Qué campo cambia?}
  Changed -->|Tipo o fecha| Km{¿Es kilometraje?}
  Km -->|Sí| KmPrice["Obtener el precio por kilómetro<br/>y bloquear el precio"]
  Km -->|No| Ready["Línea lista para revisar"]
  KmPrice --> Ready

  Changed -->|Cantidad o precio| Amount["Calcular importe<br/>Cantidad x Precio"]
  Changed -->|Importe| Price["Mantener cantidad<br/>recalcular el precio"]
  Amount --> Settlement["Actualizar el importe en divisa de empresa<br/>conservar el valor manual si es la misma divisa"]
  Price --> Settlement
  Settlement --> Ready

  Changed -->|Divisa| Currency{¿Es la misma divisa?}
  Changed -->|Fecha con divisa extranjera| Foreign
  Currency -->|Sí| Local["Usar cambio 100<br/>importe en divisa de empresa = importe"]
  Currency -->|No| Foreign["Consultar el cambio oficial<br/>y calcular el importe en divisa de empresa"]
  Local --> Ready
  Foreign --> Ready

  Changed -->|Tipo de cambio| Rate["Recalcular el importe<br/>en divisa de empresa"]
  Changed -->|Importe en divisa de empresa| Gross["En otra divisa, recalcular<br/>el tipo de cambio"]
  Changed -->|Es reembolsable| Reimb["Guardar la elección y previsualizar<br/>Sí: importe en divisa de empresa; No: 0"]
  Changed -->|Descripción, internacional, proyecto| Other["Actualizar el dato sin recalcular importes"]
  Rate --> Ready
  Gross --> Ready
  Reimb --> Ready
  Other --> Ready

  Ready --> Save["Pulsar Guardar"]
  Save --> Valid{¿Datos obligatorios válidos?}
  Valid -->|No| Fix["Mostrar el campo que debe corregirse"]
  Valid -->|Sí| SaveMode{¿Alta o edición?}
  SaveMode -->|Alta| Create["Añadir la línea a la hoja existente"]
  SaveMode -->|Edición| Update["Actualizar la línea existente"]
  Create --> BusinessCheck["Comprobar usuario, permisos,<br/>estado de la hoja y reglas contables"]
  Update --> BusinessCheck
  BusinessCheck --> Accepted{¿Se cumplen las reglas?}
  Accepted -->|No| Error["Deshacer el guardado<br/>y mostrar el motivo"]
  Accepted -->|Sí| Recalculate["Recalcular el importe definitivo<br/>y el valor reembolsable"]
  Recalculate --> Persist["Guardar la línea y actualizar<br/>proyecto e indicadores de la hoja"]
  Persist --> Linked{¿Existe una relación con un ticket<br/>procedente de otro flujo?}
  Linked -->|Sí| Sync["Sincronizar los datos relacionados<br/>con el ticket"]
  Linked -->|No| FinishMode{¿Alta o edición?}
  Sync --> FinishMode
  FinishMode -->|Alta| SuccessCreate["Volver al detalle de la hoja"]
  FinishMode -->|Edición| SuccessEdit["Recargar el detalle de la línea"]
```

## Reglas principales

- Una línea nueva comienza con fecha de hoy, cantidad `1` y la divisa
  predeterminada de la empresa; si falta, usa la de la hoja. Solo copia el
  proyecto de la hoja cuando es válido y único; si está vacío, es `VARIOS` o ya
  no admite gastos, la línea empieza sin proyecto.
- El precio de kilometraje se obtiene para la fecha elegida y no se puede
  escribir manualmente desde esta pantalla.
- Cambiar cantidad, precio o importe actualiza los otros valores relacionados.
  Si se introdujo manualmente el importe en divisa de empresa y ambas divisas
  coinciden, la pantalla conserva ese valor.
- En la misma divisa se usa el cambio `100`. En otra divisa se consulta el
  cambio oficial o se deriva del importe en divisa de empresa introducido.
- Cambiar la fecha solo refresca el tipo de cambio cuando la divisa es
  extranjera; también puede refrescar el precio de kilometraje.
- Marcar la línea como reembolsable no cambia el importe original: decide si el
  importe en divisa de empresa se incluye como reembolsable o queda en `0`.
- Al guardar, el sistema vuelve a calcular y validar los valores definitivos.
  Si una regla falla, no conserva un guardado parcial.

## Líneas vinculadas a un ticket

Una línea existente vinculada a un ticket no se edita como línea manual desde
esta pantalla. Al pulsar Editar, la aplicación conduce al editor del ticket. La
vinculación y la desvinculación son recorridos independientes de este guardado.
