# AX CRMHojaGastosTable changes - 2026-05-27

## Objetivo

Conectar los puntos reales de cambio de estado de `CRMHojaGastosTable` con el metodo global de notificacion de `INDCRMExpenseSheetService`.

## Metodos tocados

- `UpdateStatus`
  - Captura estado anterior, actualiza estado, hace `ttscommit` y luego llama al envio de mejor esfuerzo.
- `Aprobar_DesaprobarHojaDeGastos`
  - Captura estado anterior/nuevo y envia fuera del `tts` tanto en el flujo sin asiento como en el flujo con asiento.
- `Aprobar_DesaprobarHojaDeGastos_MEX`
  - Captura estado anterior/nuevo y envia fuera del `tts` en el flujo Mexico.
- `Aprobar_DesaprobarHojaDeGastos_TOTAL`
  - Captura estado anterior/nuevo y envia fuera del `tts` en el flujo total.
- `ContabilizaAsientoHojaGastos`
  - Mantiene el envio de pago despues de contabilizar y pasa `curUserId()` como `userPagador`.

## Reglas

- No se usa `modifiedField` ni `validateWrite` para lanzar emails.
- El envio externo se ejecuta despues de confirmar el cambio de estado.
- Si el email falla, no se revierte ni bloquea la operacion de negocio.
- La decision de que evento enviar queda centralizada en `INDCRMExpenseSheetService`.

## Job manual

Se agrega:

```text
.codex/Axapta/JOB_INDCRMExpenseSheetService_SendStatusNotification_2026-05-27.xpp
```

Sirve para probar manualmente el metodo global con una hoja real, estados simulados, actor y `userPagador` opcional.
