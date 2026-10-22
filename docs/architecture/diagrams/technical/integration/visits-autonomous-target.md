# Visitas: arquitectura autónoma propuesta

Estado: propuesta, todavía sin implementar. Decisión confirmada: las visitas migradas se editan solo en la nueva web. Véase el [análisis y las fases](../../../integration/visitas-autonomas.md).

Exportaciones: [SVG](../../../assets/technical/integration/visits-autonomous-target.svg) · [PNG](../../../assets/technical/integration/visits-autonomous-target.png).

```mermaid
flowchart TB
  USER["Persona usuaria"] --> SPA["FRONTEND<br/>React / Vite / MSAL"]
  SPA <-->|"Authorization Code + PKCE"| ENTRA["Microsoft Entra"]
  SPA -->|"Access token para API propia"| API["API propia<br/>NestJS / Fastify"]
  API --> DOMAIN["Dominio local<br/>acceso, maestros y Visitas"]
  API --> ADMIN["Administracion y soporte<br/>configuracion y diagnostico"]
  ADMIN --> DB
  DOMAIN -->|"Transaccion: agregado + auditoria + outbox"| DB[("PostgreSQL privado")]
  LOGS["Logs tecnicos saneados<br/>escritor acotado por lotes"] --> DB
  API -.-> LOGS
  WORKER["Worker del mismo producto<br/>importacion, entrega y reconciliacion"] <--> DB
  WORKER -.-> LOGS
  WORKER <-->|"Contrato tecnico autenticado<br/>extraccion y aplicacion"| BRIDGE["Puente AX<br/>Windows / COM x86 serializado"]
  BRIDGE <--> AX["Axapta / X++<br/>receptor idempotente y exportador"]
  AX --> AXDATA[("Copia AX y maestros")]
  WORKER -.->|"Importacion periodica<br/>maestros e historico autorizado"| DB
  WORKER -.->|"Agregados WEB: solo observacion y comparacion"| DB
  NOTE["El flujo interactivo usa datos locales.<br/>WEB es autoridad de las visitas migradas."] --- DOMAIN
```

El worker y el receptor AX son capacidades nuevas. La semilla estándar no implementa sincronización. Un proceso técnico utiliza su propia identidad; el guard delegado de usuarios no se reutiliza como autenticación de jobs. La caída de AX mantiene pendientes los trabajos y permite continuar con datos locales.
