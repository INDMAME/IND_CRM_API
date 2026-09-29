# Normas de trabajo de IND_CRM_API

Esta carpeta contiene las reglas y catálogos vigentes. No se utiliza para bitácoras por tarea, prompts temporales, planes cerrados ni inventarios históricos.

## Jerarquía

1. Instrucciones del sistema y del usuario.
2. `AGENTS.md`.
3. Documento temático o catálogo aplicable.
4. Código, rutas, configuración y contratos que se ejecutan actualmente.

Si dos documentos del mismo nivel discrepan, el comportamiento actual del proyecto decide qué se conserva.

## Mapa temático

| Documento | Responsabilidad única |
|---|---|
| `AGENTS.md` | Forma de trabajo, estabilidad, seguridad, Git y publicación. |
| `PROJECT_STRUCTURE.md` | Propiedad y ubicación de archivos. |
| `TECH_SPECS.md` | Plataforma, API, contexto firmado, AX COM y configuración. |
| `ENDPOINTS.md` | Catálogo canónico de contratos HTTP. |
| `MCP_TOOLS.json` | Esquemas canónicos de las herramientas MCP. |
| `MCP_ENDPOINTS.md` | Explicación humana complementaria de MCP. |
| `POSTMAN.md` | Colecciones y entornos Postman vigentes. |
| `postman/POSTMAN_VERSIONING.md` | Versionado de colecciones. |
| `AX_XPO_WORKFLOW.md` | Metodología común AX/XPO y propiedad canónica. |
| `QUALITY_CHECKLIST.md` | Validación proporcional y límites del entorno de ejecución. |

## Mantenimiento

- La documentación se redacta en español y describe el estado actual, no la cronología.
- Un contrato se mantiene en una sola fuente; los demás documentos enlazan o resumen sin redefinirlo.
- El historial vive en Git. No crear `AX_*_CHANGES_*`, `TEMP_*` ni documentos fechados por intervención.
- Tras cambiar un Markdown raíz de `.codex`, ejecutar `npm run sync:codex:references` y `npm run check:codex:references`.
