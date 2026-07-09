# CRMHojaGastosTable - cambios 2026-06-29

## Objetivo
- Establecer `ReimbursableExpense` por defecto en cabecera y propagarlo a lineas con el enum correcto.

## Cambios
- `initValue()` inicializa `ReimbursableExpense` en `INDReimbursableExpense::Yes`.
- `markHeaderVariousFromLine()` compara por valor numerico para soportar cabecera `INDReimbursableExpense` y lineas `INDReimbursableExpenseLines`.
- `updateReimbursableExpenseInLines()` mapea cabecera `No/Yes` al enum de lineas y sigue bloqueando la propagacion cuando cabecera esta en `Both`.
