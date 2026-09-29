# Documentación de IND_CRM_API

Este es el índice canónico de la documentación mantenida junto al código. El
código, los contratos OpenAPI y las fuentes XPO siguen siendo la autoridad
cuando una descripción quede desactualizada.

## Arquitectura

- [Índice de arquitectura](architecture/README.md): límites del sistema,
  integraciones, autenticación, errores, diagramas y flujos técnicos y
  funcionales.
- [Metodología común para AX y XPO](../.codex/AX_XPO_WORKFLOW.md):
  propiedad canónica, formato, sincronización, validación y activación manual
  compartidas con `IND_CRM_APP`.

## Operación

- [Índice de operación](operations/README.md): configuración de entornos y
  estabilidad de sesiones AX.

## Funcionalidades

- [Índice de funcionalidades](features/README.md): catálogos de Axapta,
  hojas de gastos, plantillas de correo electrónico y visibilidad de datos.

## Reglas de mantenimiento

- Documentar en español y conservar literalmente identificadores, rutas,
  contratos, clases, tablas y campos reales.
- Actualizar la documentación respaldada por código en el mismo cambio que
  modifica ese comportamiento.
- No guardar bitácoras, prompts, planes, informes de cierre ni listas de comprobación
  temporales; el historial de Git conserva esa trazabilidad.
- No duplicar una fuente canónica. Los documentos funcionales deben enlazar su
  fuente técnica y los diagramas deben mantener sincronizados Markdown,
  Mermaid y exportaciones.
- No incluir secretos, credenciales, tokens reales ni datos personales.
- Una exportación XPO solo demuestra el contenido versionado. La activación en
  Axapta exige importar, compilar, sincronizar cuando corresponda y validar en
  el entorno de ejecución.
