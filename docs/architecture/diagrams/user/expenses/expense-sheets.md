# Flujo funcional: hojas de gastos

Fuente técnica:
[expense-sheets-sequence.md](../../technical/expenses/expense-sheets-sequence.md)

Esta versión conserva el orden del flujo técnico, pero usa lenguaje de negocio
y explica entre paréntesis los términos necesarios.

```mermaid
sequenceDiagram
  autonumber
  participant User as Usuario
  participant Screen as Pantalla de gastos
  participant System as Sistema CRM<br/>(aplicación que coordina)
  participant Check as Validación<br/>(comprueba permisos y datos)
  participant Ax as Axapta<br/>(sistema donde se guardan datos)
  participant Files as Archivos<br/>(imágenes o adjuntos)

  Note over User,Ax: Consultar hojas de gasto
    User->>Screen: Busca hojas con filtros<br/>(fechas, estado, página)
    Screen->>System: Pide la lista de hojas
    System->>Check: Comprueba usuario, empresa<br/>y permisos
    Check->>Ax: Consulta hojas de gasto
    Ax-->>System: Devuelve resultados
    System-->>Screen: Lista paginada<br/>(resultados en páginas)
    Screen-->>User: Muestra las hojas encontradas

    User->>Screen: Abre una hoja concreta
    Screen->>System: Pide el detalle de la hoja
    System->>Check: Comprueba el contexto<br/>(empresa y usuario activos)
    Check->>Ax: Consulta la cabecera y las líneas
    Ax-->>System: Devuelve el detalle
    System-->>Screen: Detalle de la hoja
    Screen-->>User: Muestra la hoja completa

  Note over User,Ax: Crear o cambiar hojas de gasto
    User->>Screen: Crea una hoja nueva
    Screen->>System: Envía datos de cabecera y líneas
    System->>Check: Comprueba permisos y datos obligatorios
    Check->>Ax: Guarda la nueva hoja
    Ax-->>System: Resultado de guardado
    System-->>Screen: Confirmación o aviso de error
    Screen-->>User: Muestra el resultado

    User->>Screen: Cambia la cabecera<br/>(descripción, moneda, proyecto)
    Screen->>System: Envía cambios de cabecera
    System->>Ax: Actualiza la cabecera
    Ax-->>System: Resultado de la actualización
    System-->>Screen: Confirmación o aviso de error

    User->>Screen: Cambia o borra una línea
    Screen->>System: Envía el cambio de línea
    System->>Ax: Actualiza o borra la línea
    Ax-->>System: Resultado de la línea
    System-->>Screen: Confirmación o aviso de error

  opt Borrar hoja con archivos relacionados
    User->>Screen: Solicita borrar la hoja
    Screen->>Files: Borra adjuntos relacionados si aplica
    Screen->>System: Pide borrar o actualizar datos
    Note over Screen,Files: El borrado de archivos vinculados<br/>no está confirmado en el recorrido actual.
  end
```

## Explicación funcional

La persona puede buscar, abrir, crear y actualizar hojas de gastos. Antes de
guardar en Axapta, el sistema comprueba la empresa activa, los permisos y los
datos obligatorios.

Si la operación termina correctamente, la pantalla muestra la información
actualizada. Si falta algo o no puede guardarse, muestra un aviso explicativo.
