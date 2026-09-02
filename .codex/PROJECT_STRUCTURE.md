# Estructura y propiedad de IND_CRM_API

## Directorios principales

| Ruta | Responsabilidad |
|---|---|
| `Controllers/CRM/` | Endpoints CRM y autorización común mediante `BaseCrmController`. |
| `Controllers/System/` | Autenticación, salud y servicios de sistema. |
| `Contracts/` | DTO públicos de petición y respuesta. |
| `Models/Responses/` | Envoltorios, códigos de error y validaciones comunes. |
| `Services/` | Lógica de aplicación, integración AX/COM y proveedores externos. |
| `Services/Interfaces/` | Contratos de servicios. |
| `Helpers/` | Utilidades enfocadas, incluida identidad/contexto firmado. |
| `App_Start/` | Configuración Web API, Swagger y handlers. |
| `.codex/Axapta/` | Fuente canónica de los XPO versionados. |
| `.codex/postman/` | Colecciones y reglas Postman. |
| `docs/` | Arquitectura, funcionalidades y operaciones vigentes. |
| `Knowledge/` | Copia generada del conocimiento de ayuda CRM. |
| `scripts/` | Compilación, publicación y validación mantenidas. |
| `tests/` | Pruebas existentes del proyecto. |

## Archivos raíz relevantes

- `IND_CRM_API.csproj`: .NET Framework 4.8, C# 7.3 y x86.
- `App.config`: claves y valores de estructura; los secretos se resuelven externamente.
- `Program.cs` y `Startup.cs`: host OWIN y composición.
- `WebApiConfig.cs`: rutas y configuración Web API.
- `package.json`: comandos de sincronización documental y pruebas auxiliares.

## Regla de colocación

- Un endpoint coordina HTTP y delega; no contiene detalles COM ni lógica repetida.
- Un contrato público vive en `Contracts/` y mantiene compatibilidad de serialización.
- La sesión, serialización y recuperación COM se amplían en los servicios comunes existentes, no en cada controlador.
- No crear carpetas raíz, wrappers paralelos, catálogos duplicados ni documentos por tarea si ya existe un propietario temático.
