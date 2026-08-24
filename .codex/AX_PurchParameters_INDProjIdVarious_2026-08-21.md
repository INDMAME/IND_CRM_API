# PurchParameters.INDProjIdVarious - nota de integracion AX

## Regla del parametro

- Debe contener un valor no vacio, reservado y estable por empresa.
- No debe existir como proyecto real seleccionable o imputable en `ProjTable`.
- Nunca debe persistirse en `CRMHojaGastosLine` ni en sus asignaciones.

## Hook obligatorio en `PurchParameters.update()`

El export de `PurchParameters` no esta disponible en este repositorio. No se debe
crear un XPO parcial ni sustituir un `update()` existente. Exporte la tabla real y
fusione este bloque con la logica existente:

```x++
public void update()
{
    ProjId  oldMarker = this.orig().INDProjIdVarious;
    ProjId  newMarker = this.INDProjIdVarious;
    boolean migrateMarker;
    ;

    //MMS - Ajustes CRM - 2026.08.21
    // No migra un valor antiguo vacio porque representa cabeceras sin proyecto.
    migrateMarker = oldMarker && newMarker && oldMarker != newMarker;

    ttsbegin;
    if (migrateMarker)
        CRMHojaGastosTable::migrateVariousProjectMarker(oldMarker, newMarker);

    super();
    ttscommit;
}
```

Si la tabla ya tiene `update()`, conserve todas sus instrucciones y variables;
anada la captura de `orig()`, la condicion y la llamada dentro de la misma
transaccion que ejecuta `super()`. No use el `modified()` del formulario: dejaría
fuera actualizaciones por codigo, importaciones y otros formularios.

El helper trabaja en la empresa actual y actualiza solo cabeceras con
`ProjId == oldMarker`. No modifica lineas ni asignaciones. El cambio debe
repetirse en cada empresa cuyo parametro se actualice.

## Datos historicos

Las lineas contaminadas con marcadores o proyectos inexistentes requieren una
auditoria y correccion controlada independiente. No deben incluirse en la
migracion automatica del parametro.

## Pruebas de aceptacion

- `A -> B`: solo las cabeceras con `ProjId=A` pasan a `B`.
- `A -> ""`, `"" -> B` y `A -> A`: no se migra ninguna cabecera.
- Las lineas y `CRMHojaGastosLineCust` no cambian.
- Otra empresa permanece intacta.
- Un error forzado revierte tanto el parametro como las cabeceras.

## Activacion

Esta nota no modifica `PurchParameters`. El hook obligatorio y los XPO relacionados
requieren importacion, compilacion y prueba en AOS; publicar API o IIS no activa
codigo AX.
