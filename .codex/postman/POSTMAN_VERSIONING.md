# Versionado de Postman

## Líneas separadas

- DEV vive en `.codex/postman/DEV` y PROD en `.codex/postman/PROD`.
- Una versión DEV nueva parte de la versión DEV numerada más alta e incrementa su número.
- Una promoción a PROD parte de la colección DEV aprobada, recibe el siguiente número PROD y ajusta únicamente la configuración pública del entorno.
- Nunca sobrescribir una versión numerada anterior.

## Copias operativas

- La versión DEV vigente se refleja en `.codex/postman/DEV/IND_CRM_API_DEV.postman_collection.json` y en `Notes/DEV`.
- La versión PROD vigente se refleja en `.codex/postman/IND_CRM_API V<N>.postman_collection.json` y en `Notes/PROD`.
- Las copias de la misma versión deben tener el mismo SHA-256 que su archivo canónico.

## Reglas de contenido

- Actualizar `info.name` y generar un `_postman_id` nuevo para una versión numerada nueva.
- No incluir tokens, contraseñas, secretos ni datos personales reales.
- Mantener variables y scripts de contexto firmados alineados con `.codex/ENDPOINTS.md`.
- No crear prompts o bitácoras Markdown junto a una colección; el historial queda en las versiones JSON y en Git.
