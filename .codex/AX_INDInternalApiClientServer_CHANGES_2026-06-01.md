# AX INDInternalApiClientServer changes - 2026-06-01

## Objetivo
- Hacer que el envio extendido de email acepte la importancia desde el enum `INDEmailImportance` sin romper el contrato actual con IND_INTERNAL_API.

## Cambios
- Se agrega `normalizeInternalApiMailImportance(str _importance)`.
- El metodo acepta tanto los textos de Microsoft Graph/API (`low`, `normal`, `high`) como los valores numericos del enum:
  - `0` -> `low`
  - `1` -> `normal`
  - `2` -> `high`
- `sendInternalApiMailEx` usa este normalizador antes de llamar a la DLL COM.

## Motivo del fallo
- `enum2value(INDEmailImportance::high)` devuelve `2`, no `high`.
- Antes, `2` no era reconocido por el normalizador antiguo y podia caer a `normal`, por eso Outlook no mostraba la importancia alta.

## Importacion
- Importar/compilar esta clase antes de importar el formulario `INDEmailTemplatesForm`, porque el formulario llama a `normalizeInternalApiMailImportance`.
