# Ajustes de INDCRMExpenseSheetService - 2026-08-03

## Objetivo

Reservar `INDReimbursableExpense::Both` para el estado de cabecera derivado de sus lineas. Las operaciones de creacion y actualizacion manual solo admiten `Yes=0` o `No=1`.

## Metodos modificados

- `createExpenseSheet`: valida el valor opcional de cabecera mediante `isWritableReimbursableExpense`.
- `updateExpenseSheetHeader`: aplica la misma validacion y rechaza el cambio cuando el valor persistido no es `Yes` ni `No`.

## Metodo nuevo

- `isWritableReimbursableExpense`: acepta exclusivamente `0=Yes` y `1=No` para escrituras manuales.

## Compatibilidad

- `isValidReimbursableExpense` conserva `0`, `1` y `2`, porque se usa en filtros y lectura.
- `Both=2` sigue siendo valido cuando `CRMHojaGastosTable` lo deriva de una mezcla de lineas.
- Una actualizacion que omite `ReimbursableExpense` puede modificar otros campos sin sobrescribir un estado derivado o legacy.
- No cambian los indices de los contenedores AX ni los campos devueltos a la API.

## Validacion pendiente en Axapta

1. Importar y compilar `INDCRMExpenseSheetService.xpo`.
2. Sincronizar el diccionario si Axapta lo solicita.
3. Comprobar que POST y PUT rechazan `Both=2` y aceptan `Yes=0` y `No=1`.
4. Comprobar que una hoja con lineas mixtas sigue devolviendo `Both=2`.
