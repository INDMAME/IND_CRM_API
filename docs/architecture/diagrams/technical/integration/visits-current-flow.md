# Visitas: arquitectura actual

Estado: comportamiento comprobado en las fuentes indicadas por el [análisis de separación](../../../integration/visitas-autonomas.md). La disponibilidad real de AOT y entornos queda pendiente de validar.

Exportaciones: [SVG](../../../assets/technical/integration/visits-current-flow.svg) · [PNG](../../../assets/technical/integration/visits-current-flow.png).

```mermaid
flowchart LR
  U["Persona usuaria"] --> WEB["IND_CRM_APP<br/>MVC y Razor + islas React"]
  WEB -->|"OIDC y cookie"| ENTRA["Microsoft Entra"]
  WEB -->|"Token tecnico y contexto firmado"| API["IND_CRM_API<br/>Web API 2 / .NET Framework x86"]
  API --> SCOPE["Sesion COM por peticion<br/>acceso serializado"]
  SCOPE --> AX["Axapta / X++<br/>INDCRMVisitsService"]
  AX --> DATA["Tablas AX<br/>visita, asistentes y maestros"]
  AX --> ACCESS["Reglas AX<br/>empresa, propietario y visibilidad"]
  WEB -->|"Dictado opcional"| AIAPI["Servicio speech de API CRM"]
  AIAPI --> PROVIDER["Proveedor de IA"]
```

El inicio Entra no elimina la dependencia AX: el contexto funcional, los maestros, las consultas y las escrituras pasan por API/AX. Alta de cabecera y altas de asistentes son operaciones separadas. La cuenta técnica y el propietario funcional son identidades distintas.
