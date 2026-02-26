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

## Regla critica de estabilidad
- Los endpoints existentes en produccion o ya publicados NO se deben eliminar, desregistrar ni sacar del `.csproj` salvo peticion explicita del usuario responsable.
- Si un endpoint existente parece obsoleto, primero documentar impacto y pedir confirmacion antes de desactivarlo.

## Regla critica de enrutamiento (OBLIGATORIA)
- En CADA creacion o modificacion de endpoints se debe revisar el enrutamiento de forma minuciosa antes de cerrar el trabajo.
- Esta revision es gate de salida: no se da por terminado un cambio de API sin validacion de rutas.
- Checklist obligatorio de routing:
  - Verificar colisiones entre rutas literales y parametrizadas (ej: `tickets` vs `{id}`).
  - Verificar unicidad por combinacion `HTTP method + route template`.
  - Aplicar constraints en parametros de ruta cuando exista riesgo de ambiguedad (`int`, `guid`, `regex`, etc.).
  - Revisar impacto de `RoutePrefix` y rutas hermanas del mismo recurso.
  - Revisar compatibilidad con rutas legacy y convencionales (`MapHttpRoute`).
  - Probar explicitamente los endpoints potencialmente conflictivos en Postman.
  - Confirmar en logs de diagnostico que el request llega al controlador/accion esperado (`API-PIPE-MATCH`, `API-ROUTE-IN`).
- Si se detecta ambiguedad de rutas, se debe corregir antes de continuar (no diferir).

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

9) Validacion de enrutamiento (obligatoria en cambios API)
- Ejecutar checklist de `Regla critica de enrutamiento`.
- Documentar en el resumen final que la revision de rutas fue realizada.

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
