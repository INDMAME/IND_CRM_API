# Visitas: soporte desde el contexto de otra persona

Estado: funcionamiento propuesto. La persona autorizada de soporte puede consultar y editar con los mismos permisos de la persona seleccionada. Fuente técnica: [actuación trazable](../../technical/auth/visits-support-context-sequence.md).

Exportaciones: [SVG](../../../assets/user/auth/visits-support-context.svg) · [PNG](../../../assets/user/auth/visits-support-context.png).

```mermaid
flowchart TB
  LOGIN["Entrar con la cuenta corporativa propia"] --> ADMIN["Abrir soporte autorizado"]
  ADMIN --> CHOOSE["Elegir persona, empresa y motivo"]
  CHOOSE --> CONTEXT["Actuar como esa persona<br/>con sus permisos de lectura y edicion"]
  CONTEXT --> BANNER["Aviso permanente de persona representada<br/>y cuenta real del operador"]
  BANNER --> ACTION["Consultar, crear o editar Visitas"]
  ACTION --> CHECK{"La persona representada<br/>tiene permiso?"}
  CHECK -->|"Si"| SAVE["Aplicar la operacion y registrar<br/>quien actuo y a quien representaba"]
  CHECK -->|"No"| DENY["Mostrar el mismo rechazo<br/>sin ampliar permisos"]
  SAVE --> MORE["Continuar mientras la sesion este activa"]
  DENY --> MORE
  MORE --> ACTION
  MORE --> END["Finalizar, caducar o revocar"]
  END --> CLEAN["Bloquear el contexto y volver<br/>explicitamente a la cuenta propia"]
  NOTE["El panel permite investigar permisos y logs.<br/>La autenticacion corporativa sigue siendo la del operador."] --- ADMIN
```

El operador no necesita la contraseña de la persona representada. El sistema conserva quién realizó realmente cada cambio. Este modo reproduce las reglas y datos de Visitas; los fallos del inicio corporativo o del navegador de la otra persona pueden necesitar diagnóstico adicional. Los cambios confirmados mantienen su auditoría y su envío pendiente a Axapta después de cerrar soporte.
