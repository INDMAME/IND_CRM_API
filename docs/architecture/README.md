# Arquitectura del CRM

Esta carpeta documenta los flujos vigentes entre `IND_CRM_APP`,
`IND_CRM_API`, Axapta 3.0 mediante Business Connector COM y los servicios
externos utilizados por el CRM. Lo que no esté demostrado por el código se
marca como `pendiente de validar`.

## Estructura

- `diagrams/technical`: detalle para desarrollo y mantenimiento.
- `diagrams/user`: explicaciones funcionales para negocio.
- `assets/technical` y `assets/user`: exportaciones SVG, PNG u otros formatos.
- `export-diagrams.ps1`: regenera las imágenes a partir de los archivos `.mmd`.

Cada diagrama conserva juntos su explicación Markdown y su fuente Mermaid.

## Navegación

- [Autenticación y contexto de empresa](security/authentication-and-company-context.md):
  identidad, autorización y aislamiento por empresa.
- [Catálogo de diagramas](diagrams/README.md): separa las vistas técnicas de
  las explicaciones funcionales y enlaza sus fuentes Mermaid.

## Visualización y exportación

GitHub representa directamente los bloques Mermaid de los archivos Markdown.
Para regenerar todos los SVG:

```powershell
.\docs\architecture\export-diagrams.ps1
```

Para exportar SVG y PNG:

```powershell
.\docs\architecture\export-diagrams.ps1 -Format both
```

El script solo lee las fuentes `.mmd` y escribe en `assets`; no modifica el
código del producto.

## Reglas de mantenimiento

- Conservar sin traducir identificadores, nombres de proyectos, rutas, cabeceras,
  DTO, clases, tablas y campos reales.
- Actualizar en el mismo cambio el documento técnico, el funcional y sus
  fuentes `.mmd` cuando describan el mismo flujo.
- No incluir secretos, tokens reales, identificadores de tenant, empresas,
  usuarios AX, URL completas de entornos ni cuerpos sensibles.
- Usar marcadores como `Bearer <token>`, `<companyId>` o `<contextToken>`.
- Mantener los detalles de implementación en los diagramas técnicos y lenguaje
  de negocio en los funcionales.
- Usar `pendiente de validar` cuando no exista evidencia en código, OpenAPI,
  clientes API o fuentes de Axapta.
