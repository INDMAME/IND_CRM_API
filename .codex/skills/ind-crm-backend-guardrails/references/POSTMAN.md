# Postman de IND_CRM_API

## Fuentes vigentes

| Entorno | Colección canónica | Copia operativa |
|---|---|---|
| DEV | `.codex/postman/DEV/IND_CRM_API V07.postman_collection.json` | `.codex/postman/DEV/IND_CRM_API_DEV.postman_collection.json` y `Notes/DEV/IND_CRM_API V07.postman_collection.json` |
| PROD | `.codex/postman/PROD/IND_CRM_API V31.postman_collection.json` | `.codex/postman/IND_CRM_API V31.postman_collection.json` y `Notes/PROD/IND_CRM_API V31.postman_collection.json` |

Las copias de una misma versión deben ser idénticas byte a byte. Las versiones anteriores se conservan como snapshots de colección, no como documentación normativa.

## Variables

- `baseUrl`: host del entorno.
- `tokenId`: bearer técnico obtenido en login/refresh.
- `companyId`: empresa seleccionada y permitida.
- `axUserId`: valor funcional devuelto por el contexto para contratos que aún lo requieren.
- `entraOid`, `contextVersion`, `permissionsRevision`, `contextToken`: contexto firmado devuelto por `/api/auth/entra/context`.

No guardar credenciales o tokens reales dentro de las colecciones versionadas.

## Flujo de autenticación

1. Ejecutar login y guardar `tokenId`.
2. Solicitar el contexto Entra y guardar empresa, usuario AX y campos firmados.
3. Los scripts de colección añaden las cabeceras de contexto a las rutas CRM correspondientes.
4. Un `AUTH_CONTEXT_REQUIRED` o `AUTH_CONTEXT_STALE` exige renovar el contexto; `AUTH_FORBIDDEN` no se corrige cambiando cabeceras manualmente.

## Contratos

- El catálogo HTTP canónico es `.codex/ENDPOINTS.md`.
- Las fechas de tickets y hojas aceptan `DDMMYYYY` o `DD.MM.YYYY`; las respuestas usan `DD.MM.YYYY`.
- El GET de detalle de hoja usa el viewer del contexto firmado y no requiere `X-IND-AxUserId` para autorizar la lectura.
- Cuando cambie un endpoint, actualizar en la misma intervención código, colección DEV vigente, pruebas de Postman y documentación contractual afectada.

## Versionado

Aplicar `.codex/postman/POSTMAN_VERSIONING.md`. No crear prompts Markdown auxiliares para trasladar cambios al frontend; el contrato se documenta en `ENDPOINTS.md` y el cambio coordinado se gestiona en el plan de trabajo/Git.
