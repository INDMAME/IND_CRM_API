# Visitas: actuar como otra persona con identidad trazable

Estado: diseño propuesto, no implementado. Decisión confirmada: lectura y edición con los permisos de la persona seleccionada desde que se activa el contexto. Fuente: [diseño de soporte](../../../integration/visitas-autonomas.md#72-actuar-como-otra-persona-para-soporte).

Exportaciones: [SVG](../../../assets/technical/auth/visits-support-context-sequence.svg) · [PNG](../../../assets/technical/auth/visits-support-context-sequence.png).

```mermaid
sequenceDiagram
  actor O as Operador de soporte
  participant SPA as Web
  participant API as API propia
  participant DB as PostgreSQL
  participant W as Worker AX
  O->>SPA: Seleccionar persona, empresa y motivo
  SPA->>API: Iniciar soporte con token Entra real
  API->>API: Validar actor y capacidad de soporte
  API->>DB: Guardar sesion y auditoria de inicio
  DB-->>API: Sesion vinculada y permisos efectivos
  API-->>SPA: Contexto opaco y persona efectiva
  Note over O,API: /me y MSAL mantienen la cuenta real
  SPA-->>O: Actuando como persona / cuenta real / Finalizar
  O->>SPA: Consultar o editar Visitas
  SPA->>API: Token real + contexto soporte esperado
  API->>DB: Comprobar sesion, concesion y reglas actuales
  API->>API: Aplicar permisos de la persona efectiva
  alt Operacion permitida
    API->>DB: Mutacion + auditoria de ambos actores + outbox
    DB-->>API: Commit local
    API-->>SPA: Resultado y version
    W->>DB: Leer evento con contexto original
    W->>W: Entregar a AX conservando atribucion
  else Permiso denegado o contexto no valido
    API->>DB: Auditar rechazo saneado
    API-->>SPA: Rechazo con Trace ID
    Note over SPA,API: Sin permisos extra del administrador<br/>Sin reintento bajo la cuenta real
  end
  O->>SPA: Finalizar actuacion
  SPA->>API: Cerrar contexto con token real
  API->>DB: Revocar sesion y auditar cierre
  API-->>SPA: Contexto cerrado
  SPA->>SPA: Cancelar consultas e invalidar caches
  Note over SPA,W: El cierre no cancela cambios ya confirmados ni sus envios AX
```

El token Entra autentica siempre al actor real. El contexto efectivo es local, revocable y propio de la pestaña. Toda ruta administrativa se evalúa como actor real fuera de la actuación. La sesión y las concesiones se comprueban por petición y al autorizar una mutación; lectura y escritura siguen los permisos de la persona representada. La confirmación de entrega AX se produce con el contrato asíncrono descrito en el diseño general.
