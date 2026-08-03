# Cambios AX - INDTicketInfoTable - 2026-08-03

## Objetivo

Acotar la validacion de duplicidad de tickets con fecha y hora validas al mismo usuario propietario, sin alterar la excepcion existente para tickets sin hora ni los flujos AX que aun no informan `CreatedByUserId`.

## Metodo modificado

- `validateUniqueTicketDateTime()`:
  - omite la comprobacion si falta `TicketDate` o si `TicketTime == 0`;
  - cuando `CreatedByUserId` esta informado, compara fecha y hora solo contra tickets del mismo usuario;
  - cuando `CreatedByUserId` esta vacio, conserva como fallback la comprobacion global legacy;
  - mantiene el `FileId` del registro conflictivo en el mensaje.

## Compatibilidad y riesgos

- No cambia el contrato de contenedores AX ni los campos de la tabla.
- La API ya informa `CreatedByUserId` antes de validar altas y actualizaciones.
- El fallback global protege altas manuales o integraciones legacy que no hayan informado propietario.
- Requiere importar y compilar `INDTicketInfoTable` en Axapta y ejecutar los casos manuales antes de promover a PROD.

## Casos de prueba manual

1. Mismo usuario, misma fecha y ambos `TicketTime == 0`: ambos tickets se guardan.
2. Mismo usuario, misma fecha y misma hora valida: el segundo se rechaza y muestra el `FileId` del primero.
3. Usuarios distintos, misma fecha y misma hora valida: ambos tickets se guardan.
4. `CreatedByUserId` vacio, misma fecha y misma hora valida: se conserva el rechazo global legacy.
5. Edicion del mismo registro sin cambiar fecha y hora: no se detecta a si mismo como duplicado.
