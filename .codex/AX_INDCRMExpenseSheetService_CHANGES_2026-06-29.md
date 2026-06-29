# INDCRMExpenseSheetService - cambios 2026-06-29

## Objetivo
- Ejecutar validaciones funcionales de `CRMHojaGastosLine.validateField()` antes de crear o actualizar lineas desde API.
- Devolver en `Message` el motivo real de AX cuando una cabecera o linea no supera validaciones.

## Cambios
- `isValidGastoType()` permite `19` para que `CRMGastoType::Propinas` llegue a la validacion de tabla.
- Se agrego `validateExpenseSheetLineForApi()` para ejecutar `validateField(Type)`, `validateField(ProjIdHornos)` y `validateWrite()` antes de `insert/update`.
- Se agrego `validateExpenseSheetHeaderForApi()` para ejecutar validaciones de cabecera antes de `insert/update`.
- Se agrego `isValidLineReimbursableExpense()` para limitar `ReimbursableExpense` de lineas al enum `INDReimbursableExpenseLines` (`No/Yes`) y rechazar `Both`.
- `createExpenseSheet()` valida cabecera antes de `header.insert()` y cada linea antes de `line.insert()`.
- `updateExpenseSheetHeader()` valida la cabecera antes de `header.update()` y conserva el `startLine` correcto para errores DDE.
- `updateExpenseSheetLine()` valida la linea antes de `line.update()` y devuelve un mensaje explicito cuando `reimbursableExpense` de linea no pertenece a `INDReimbursableExpenseLines`.

## Notas
- En actualizacion de cabecera no se llama a `CRMHojaGastosTable.validateField(ProjId)` porque ese metodo abre dialogo cliente. Se usa `validateWrite()` para evitar bloquear el servicio API.
- El contrato HTTP no cambia; los endpoints siguen devolviendo el envelope actual con `Success=false` y `Message`.

## Validacion manual sugerida
- En una empresa sin `ActivarFuncionalidadesMexico`, intentar crear o actualizar linea con `Type = 19`.
- Esperado: `Success=false` y `Message` con el texto funcional de AX: `Este tipo de gasto no esta permitido en tu compania.`
