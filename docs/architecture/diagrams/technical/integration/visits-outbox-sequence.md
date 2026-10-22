# Visitas: guardado local y entrega asíncrona

Estado: diseño propuesto, no contrato AX disponible. Los nombres de comandos y almacenamiento receptor son conceptuales. Véase el [diseño de sincronización](../../../integration/visitas-autonomas.md).

Exportaciones: [SVG](../../../assets/technical/integration/visits-outbox-sequence.svg) · [PNG](../../../assets/technical/integration/visits-outbox-sequence.png).

```mermaid
sequenceDiagram
  actor U as Persona usuaria
  participant SPA as SPA
  participant API as API propia
  participant DB as PostgreSQL
  participant W as Worker
  participant B as Puente AX
  participant AX as Transaccion AX
  U->>SPA: Guardar visita y asistentes
  SPA->>API: Comando, clave idempotente y version esperada
  API->>API: Validar token, empresa, permiso y agregado
  API->>DB: BEGIN
  API->>DB: Agregado + auditoria + outbox + resultado idempotente
  API->>DB: COMMIT
  API-->>SPA: Guardado local, version y entrega pendiente
  SPA-->>U: Confirmar guardado
  Note over U,AX: La respuesta interactiva no espera a AX
  W->>DB: Reclamar siguiente evento del agregado con lease
  DB-->>W: Evento inmutable y version
  Note over W,DB: La reclamacion termina antes de llamar a AX
  W->>B: Mensaje, version, hash y actor original
  B->>AX: BEGIN y comprobar recibo / version
  alt Mensaje nuevo y version admisible
    AX->>AX: Aplicar visita + asistentes + mapping + recibo
    AX->>AX: COMMIT
    AX-->>B: Resultado y correspondencias persistidos
  else Mensaje ya confirmado con mismo contenido
    AX-->>B: Devolver resultado anterior
  else Conflicto de version o contenido
    AX-->>B: Rechazo explicito
  end
  B-->>W: Acuse o error
  alt Acuse confirmado
    W->>DB: Registrar mapping y version exportada
    Note over W,DB: No marcar versiones locales posteriores como entregadas
  else Respuesta perdida o fallo transitorio
    W->>DB: Programar reintento del mismo mensaje
    Note over W,AX: Deduplicacion receptora evita repetir efectos tras commit AX
  else Error que requiere revision
    W->>DB: Conservar evento y causa para intervencion
  end
```

La deduplicación receptora y los cambios AX deben confirmar en la misma transacción. El outbox local no ofrece por sí solo esa garantía. Ordenar por agregado, conservar tombstones y retener recibos durante toda la ventana de reenvío/recuperación. Los errores previos al commit local no generan entrega.
