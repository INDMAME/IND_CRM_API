# Secuencia de autenticación y contexto

El flujo combina el inicio de sesión con Entra para la aplicación web, un JWT
para `IND_CRM_API` y un token de contexto CRM que delimita empresa y módulos.

```mermaid
sequenceDiagram
  autonumber
  participant Browser as Navegador
  participant WebApp as IND_CRM_APP
  participant Entra as Entra ID
  participant Session as Sesión web
  participant Client as ApiClientService
  participant ApiAuth as IND_CRM_API AuthController
  participant Guard as Servicios de autenticación y contexto
  participant Bc as Business Connector COM
  participant Ax as Axapta 3.0

  Browser->>WebApp: Inicia sesión o abre una página CRM
  WebApp->>Entra: Desafío OIDC
  Entra-->>WebApp: Token de identidad y claims
  WebApp->>Session: Guarda la cookie de sesión y el OID de Entra

  alt Falta el token API o ha caducado
    WebApp->>Client: Solicita token API
    Client->>ApiAuth: POST /api/auth/login o /api/auth/refresh
    ApiAuth->>Bc: Valida la sesión de Axapta
    Bc->>Ax: Autentica al usuario AX
    Ax-->>Bc: Resultado de la autenticación
    Bc-->>ApiAuth: Sesión válida
    ApiAuth-->>Client: IndApiResponse(token)
    Client-->>Session: Guarda metadatos del token API
  end

  WebApp->>Client: Garantiza el contexto CRM
  Client->>ApiAuth: POST /api/auth/entra/context<br/>Authorization + OID de Entra + código de aplicación
  ApiAuth->>Guard: Valida JWT, OID de Entra y código de aplicación
  Guard->>Bc: Ejecuta loginEntraContext
  Bc->>Ax: INDCRMUtilityService.loginEntraContext
  Ax-->>Bc: Empresas, módulos, usuario AX y valores predeterminados
  Bc-->>Guard: Datos de contexto
  Guard-->>ApiAuth: Token de contexto firmado y revisiones
  ApiAuth-->>Client: IndPagedResponse(EntraContextDto)
  Client-->>Session: Guarda empresa, usuario AX, token y revisiones
  WebApp-->>Browser: Representa la página o devuelve el contexto JSON

  Note over Client,ApiAuth: Las peticiones CRM posteriores incluyen:<br/>Authorization: Bearer token<br/>X-IND-Company<br/>X-IND-EntraOid<br/>X-IND-Context-Version<br/>X-IND-Permissions-Revision<br/>X-IND-Context-Token<br/>X-IND-AxUserId solo en contratos heredados<br/>El actor autorizado procede de la instantánea firmada
```

## Contratos observados

- `POST /api/auth/login` devuelve un token tras validar las credenciales en
  Axapta.
- `POST /api/auth/refresh` renueva el JWT de la API.
- `POST /api/auth/entra/context` devuelve `IndPagedResponse<EntraContextDto>`
  con metadatos del contexto, empresas, módulos, empresa y moneda
  predeterminadas, y usuario AX.

`X-IND-AxUserId` mantiene compatibilidad con contratos heredados, pero no
demuestra la identidad del solicitante. Los endpoints migrados obtienen el
actor autorizado de la instantánea firmada, como explica
[Autenticación y contexto de empresa](../../../security/authentication-and-company-context.md).

## Límite vigente

La ruta exacta que recupera el JWT de la API después de un inicio basado solo
en Entra depende de la sesión y de la configuración del entorno. No está
confirmada para todos los perfiles de despliegue y requiere una comprobación en
el entorno de ejecución correspondiente.
