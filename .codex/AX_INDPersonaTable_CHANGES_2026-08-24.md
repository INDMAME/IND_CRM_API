# AX INDPersonaTable Changes - 2026-08-24

## Objetivo

Documentar el XPO actualizado entregado para `INDPersonaTable`.

## Cambios

- El campo `Email` pasa a ser obligatorio.
- Se incorpora `PersonalEmail` con EDT `Email`, longitud 80 y escritura opcional.
- `Email` y `PersonalEmail` se incluyen en el grupo `Identificacion`.

## Activacion AX pendiente

- Importar `.codex/Axapta/INDPersonaTable.xpo` en el entorno correspondiente.
- Compilar la tabla y sincronizar el diccionario de datos.
- Verificar los registros existentes sin `Email` antes de validar altas o ediciones.
- Confirmar la visualizacion y persistencia de `PersonalEmail` en el formulario consumidor.

La publicacion Git y el redespliegue de `IND_CRM_API` no activan este cambio dentro de Axapta.
