# Contexto del sistema

Este diagrama muestra los sistemas principales y las dependencias externas de
los flujos CRM. La API protege el acceso a Axapta y normaliza las respuestas
que recibe la aplicación web.

```mermaid
flowchart LR
  User["Usuario del CRM"]
  Browser["Navegador<br/>Páginas Razor e islas React"]
  WebApp["IND_CRM_APP<br/>ASP.NET Core MVC + Razor<br/>React TypeScript"]
  Entra["Microsoft Entra ID<br/>Inicio OIDC y claims"]
  Api["IND_CRM_API<br/>Web API 2, .NET Framework 4.8<br/>OWIN self-host, x86"]
  Guard["Capa de protección de la API<br/>JWT, empresa, usuario AX y token de contexto"]
  Bc["Business Connector COM<br/>Sesión Axapta por petición"]
  Ax["Axapta 3.0<br/>Clases de servicio CRM en AOT"]
  Blob["Azure Blob Storage<br/>Archivos y vistas previas de tickets"]
  Ocr["Azure Document Intelligence<br/>Extracción de justificantes"]
  Ai["Servicios OpenAI<br/>Voz, normalización y preguntas"]
  Fx["Proveedores de tipo de cambio"]

  User --> Browser
  Browser --> WebApp
  WebApp --> Entra
  WebApp --> Api
  Api --> Guard
  Guard --> Bc
  Bc --> Ax
  Api --> Blob
  Api --> Ocr
  Api --> Ai
  Api --> Fx
```

## Responsabilidades

`IND_CRM_APP` gestiona la experiencia de usuario, las vistas Razor, las islas
React, la sesión, la protección CSRF, los endpoints proxy MVC y las llamadas a
la API mediante `ICrmApiClient` y `ApiClientService`.

`IND_CRM_API` gestiona los contratos HTTP, la validación JWT y del contexto
CRM, los envoltorios de respuesta, el diagnóstico, la coordinación de servicios
externos y la única ruta de integración del servidor con Axapta.

Axapta 3.0 sigue siendo el sistema de registro para actividades, visitas,
hojas de gastos, tickets, usuarios, empresas, monedas y proyectos. El código
revisado accede a Axapta solo mediante Business Connector COM.

Los servicios externos aportan identidad, almacenamiento de archivos, OCR de
justificantes, IA de voz o texto y tipos de cambio. El orden exacto de
alternativas entre proveedores de cambio no está confirmado y requiere una
comprobación contra la configuración y el entorno de ejecución correspondiente.

## Alcance

La documentación cubre `IND_CRM_APP`, `IND_CRM_API`, Axapta 3.0 y los
servicios externos utilizados por los módulos CRM.
