## Objetivo

Ajustar `loginEntraContext` para que solo inserte companias en el contexto cuando el usuario AX resuelto para la app tenga un registro CRM valido en la compania actual.

## Clase AX

- `INDCRMUtilityService`

## Metodo impactado

### `loginEntraContext`

Cambios aplicados:

- Se reinicia el estado CRM por cada iteracion del bucle de companias para evitar arrastre de buffers entre companias.
- La resolucion del usuario CRM sigue el patron ya usado en otros metodos AX:
  - primero `SysUserInfo::getCRMUsuarioTable(sysUserInfo.Id)`
  - si no devuelve `UserId`, fallback a `CRMUsuarioTable::Find(sysUserInfo.Id)`
- Si despues de esa resolucion no existe `CRMUsuarioTable.RecId`, la compania se descarta y no se inserta en `companiesCon`.
- `firstAllowedCompany` e `initialDefaultAllowed` ahora solo se calculan para companias que realmente quedan visibles en la respuesta.

## Causa raiz

El metodo insertaba la compania en funcion del buffer `crmUsuarioTable`, pero ese buffer y `companyCrmUserId` no se reiniciaban en cada iteracion. Eso permitia que una compania sin mapeo CRM heredara el estado valido de una iteracion previa.

## Compatibilidad

- No cambia el contrato del container de salida.
- No cambia el criterio de modulos visibles.
- Solo se ocultan companias que ya no deberian exponerse porque el usuario AX no tiene registro CRM valido en esa compania.

## Riesgos

- Si existia algun flujo que dependia de mostrar companias sin mapeo CRM, ese comportamiento deja de estar disponible. El cambio pedido lo corrige de forma explicita.

## Verificacion

- Revision manual del flujo completo de `loginEntraContext`.
- Comparacion con el patron de resolucion AX->CRM usado en `INDCRMExpenseSheetService`.
- Pendiente compilacion real en entorno AX, no disponible desde este workspace.
