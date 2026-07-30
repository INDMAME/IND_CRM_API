# Cambios AX - CRMUsuarioTable - 2026-07-30

## Objetivo

Exponer en `CRMUsuarioTable` dos metodos `display` que resumen las relaciones
directas de jefe configuradas en `CRMUsuarioSubordinadoTable` y mostrarlos en
`CRMUsuarioTableForm`.

## Metodos agregados

- `JefesAsignados()`: devuelve todos los jefes directos de la persona.
- `JefesAprobadoresHojaGastos()`: devuelve solo las relaciones con
  `ExcluirAprobacionHojaGastos == NoYes::No`.
- `construirListaJefes(boolean)`: centraliza la consulta y el formato utilizado
  por ambos metodos `display`.
- Antes de consultar `CRMUsuarioSubordinadoTable`, comprueba la persona asociada
  mediante `INDPersonaTable::findByCRM(this.RecId)`. Si
  `AllowSelfManagement == NoYes::Yes`, el display de aprobadores devuelve
  inmediatamente `AUTOGESTIONADO`; `JefesAsignados()` conserva la jerarquia
  informativa configurada.

## Formulario

- `CRMUsuarioTableForm` incorpora los controles `JefesAsignados` y
  `JefesAprobadoresHojaGastos` en la seccion de detalle.
- Ambos controles consumen sus respectivos metodos `display` de
  `CRMUsuarioTable` y mantienen visibles los valores multilínea.

## Formato y compatibilidad

- Cada jefe se representa como `Nombre(UserIdJefe)`.
- Los valores se separan mediante ` - ` sin dejar separador al final.
- El resultado se ordena por `UserIdJefe` y evita duplicados legacy.
- Si falta el registro maestro del jefe, se conserva visible su identificador.
- No se modifica la logica de autorizacion, la jerarquia ni ningun contrato API.

## Validacion pendiente en AX

1. Importar `CRMUsuarioTable.xpo`.
2. Importar `CRMUsuarioTableForm.xpo` y compilar y sincronizar ambos objetos.
3. Verificar que los dos controles `display` aparecen en la seccion de detalle.
4. Verificar una persona sin jefes, con un jefe, con varios jefes y con una
   relacion marcada para excluir la aprobacion de hojas de gastos.
5. Verificar que una persona con `AllowSelfManagement == NoYes::Yes` devuelve
   `AUTOGESTIONADO` en `JefesAprobadoresHojaGastos()` y conserva sus relaciones
   informativas en `JefesAsignados()`.
