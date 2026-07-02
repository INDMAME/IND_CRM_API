# Cambios Axapta - INDCRMExpenseSheetService - 2026-05-29

## Objetivo

Marcar los emails de notificacion de hojas de gastos con importancia alta para que Outlook muestre el indicador visual de prioridad.

## Metodo tocado

- `INDCRMExpenseSheetService::sendExpenseSheetStatusNotification`

## Detalle

- No cambia la firma del metodo.
- No cambia el tipo de retorno; sigue devolviendo `boolean`.
- Se sustituye la llamada interna:
  - antes: `INDCRMUtilityService::sendInternalApiMail`
  - ahora: `INDCRMUtilityService::sendInternalApiMailEx`
- Los nuevos parametros extendidos se envian vacios cuando no aplican:
  - `fromDisplayName = ''`
  - `ccEmails = ''`
  - `bccEmails = ''`
  - `replyToEmails = ''`
- `saveToSentItems = false`.
- `importance = 'high'`.

## Compatibilidad

El contrato consumido por Axapta, CRM API y llamadas existentes no cambia. El ajuste solo afecta al transporte interno del email para activar la prioridad alta.

## Validacion pendiente

Importar/compilar la clase en Axapta y enviar una prueba real para confirmar que Outlook muestra el signo de exclamacion.
