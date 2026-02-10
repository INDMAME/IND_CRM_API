---
name: ind-crm-backend-guardrails
description: Skill local master del backend IND CRM. Gobierna APIs, servicios, contratos, seguridad, validacion, i18n backend y despliegues.
activation:
  trigger:
    - crear endpoint
    - modificar endpoint
    - nuevo controlador
    - nueva API
    - cambiar contrato
---

# IND CRM Backend Guardrails (MASTER SKILL)

## Objetivo
Esta skill es la autoridad arquitectonica del backend IND CRM.
Se ejecuta SIEMPRE antes de generar o modificar codigo de API.

## Fuente de verdad
- Leer TODOS los archivos `.md` de la carpeta `.codex`.
- Usarlos como contexto primario.
- Nunca contradecir reglas documentadas ahi.
- Referencias locales sincronizadas: `.codex/skills/ind-crm-backend-guardrails/references/*.md`.

## Required Sub-Skill Routing

1) Analisis (`brainstorming`)
- Objetivo funcional.
- Impacto en legacy (.NET Framework 4.8, integraciones existentes).
- Riesgos tecnicos y de compatibilidad.

2) Especificacion (`rest-api-expert`)
- Recurso REST.
- Rutas y metodos HTTP.
- DTOs request/response.
- Codigos HTTP y errores.
- Versionado, paginacion y filtros.

3) Patrones de diseno de API (`api-design-patterns`)
- Seleccion de estilo API (REST, GraphQL, gRPC) segun contexto.
- Patrones de versionado, autenticacion y manejo de errores.
- Reglas de evolucion de contratos sin romper clientes.

4) Arquitectura backend (`backend-architect`)
- Limites de servicios y contratos entre componentes.
- Resiliencia, observabilidad y escalabilidad.
- Riesgos de acoplamiento y plan de rollout tecnico.

5) Diseno REST especializado (`rest-api-design` / `rest-api-design-expert`)
- Hardening de endpoints y validaciones.
- Semantica HTTP y convenciones REST defensivas.
- Medidas para evitar exposicion de datos y errores de diseno.

6) Implementacion (`dotnet-framework-4.8-expert`)
- Codigo compatible con .NET Framework 4.8.
- Controladores defensivos.
- Separacion clara de capas.
- Estabilidad ante errores.

7) Documentacion (`api-documenter`)
- OpenAPI actualizado.
- Ejemplos request/response.
- Errores documentados.

8) Verificacion (`code-review`)
- Calidad de codigo.
- Consistencia REST.
- Cumplimiento de reglas `.codex`.

## Enlace con skills aptas
Esta skill local es la autoridad de decision para el repositorio.
Las skills instaladas en `C:/Users/marco.meza/.codex/skills` se usan como apoyo tecnico y no reemplazan reglas locales.
Ruta esperada: `C:/Users/marco.meza/.codex/skills/<skill-name>/SKILL.md`.

Skills backend enlazadas:
- `brainstorming`
- `rest-api-expert`
- `api-design-patterns`
- `backend-architect`
- `rest-api-design`
- `dotnet-framework-4.8-expert`
- `api-documenter`
- `code-review`

## Reglas de precedencia
1) `ind-crm-backend-guardrails`
2) Documentacion `.codex`
3) Skills instaladas en `C:/Users/marco.meza/.codex/skills` aptas de backend y REST
4) Buenas practicas generales
