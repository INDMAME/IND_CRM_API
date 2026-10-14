---
name: ind-crm-backend-guardrails
description: Usar al trabajar en IND_CRM_API con contratos Web API, COM de Axapta, contexto de autenticación, XPO, documentación de endpoints, Postman/MCP, preparación de entregas o despliegue de la API.
---

# Guardas de IND_CRM_API

## Preparación

1. Leer `references/AGENTS.md`, `references/PROJECT_STRUCTURE.md` y `references/TECH_SPECS.md`.
2. Cargar solo los catálogos necesarios:
   - HTTP: `references/ENDPOINTS.md`;
   - MCP: `references/MCP_ENDPOINTS.md` y `../../MCP_TOOLS.json`;
   - Postman: `references/POSTMAN.md` y `../../postman/POSTMAN_VERSIONING.md`;
   - AX/XPO: `references/AX_XPO_WORKFLOW.md`;
   - cierre/publicación: `references/QUALITY_CHECKLIST.md`.
3. Revisar estado de Git, flujo actual, propietario, consumidores y pruebas antes de editar.

## Reglas de ejecución

- Presentar un plan breve antes de cambios importantes.
- Mantener .NET Framework 4.8, C# 7.3, OWIN/Web API 2 y x86.
- Conservar contratos públicos y routing salvo cambio aprobado.
- Tratar las skills externas de REST o arquitectura como apoyo genérico: nunca
  sustituyen este stack, los contratos canónicos ni el acceso COM serializado.
- Centralizar sesión y acceso COM; no introducir concurrencia contra Axapta.
- Tratar el contexto firmado como autoridad funcional y separar la cuenta técnica `APIAX` del usuario real.
- Editar XPO solo en la fuente canónica API y sincronizar APP mediante `references/AX_XPO_WORKFLOW.md`.
- No crear bitácoras por tarea ni duplicar reglas canónicas.
- No hacer commit, push, despliegue o promoción sin petición explícita.

## Cierre

1. Ejecutar las comprobaciones proporcionales de `references/QUALITY_CHECKLIST.md`.
2. Revisar diff, routing, compatibilidad y documentación afectada.
3. Si cambiaron Markdown raíz de `.codex`, ejecutar `npm run sync:codex:references` y `npm run check:codex:references`.
4. Informar resultados, impacto contractual y cualquier importación/compilación/prueba AX pendiente.
