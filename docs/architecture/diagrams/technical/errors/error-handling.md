# Tratamiento de errores

Siempre que es posible, la API normaliza los fallos de validación,
autorización, Axapta, COM, tiempos de espera y servicios externos en envoltorios de
respuesta estándar.

```mermaid
flowchart TD
  Start["Petición CRM entrante"]
  Mvc["fetch de IND_CRM_APP o proxy MVC"]
  TokenCheck{"¿Token API presente y válido?"}
  ContextCheck{"¿Cabeceras de contexto válidas?"}
  CompanyCheck{"¿X-IND-Company presente?"}
  AxUserCheck{"¿X-IND-AxUserId presente cuando se exige?"}
  RequestCheck{"¿DTO y valores de ruta válidos?"}
  ComCall["Llamada Business Connector COM"]
  AxResult{"¿Resultado correcto de Axapta?"}
  ExternalCall{"¿Interviene un servicio externo?"}
  ExternalOk{"¿El servicio externo responde correctamente?"}
  Success["Devuelve IndApiResponse de T o IndPagedResponse de T<br/>success=true + traceId"]

  AuthError["401 AUTH_REQUIRED o error de token<br/>React puede renovar o exigir otro inicio de sesión"]
  ContextError["403 AUTH_CONTEXT_REQUIRED / AUTH_CONTEXT_STALE / AUTH_FORBIDDEN<br/>React puede renovar el contexto o mostrar el error"]
  CompanyError["422 VALIDATION_ERROR<br/>Empresa ausente o no válida"]
  AxUserError["422 VALIDATION_ERROR<br/>Usuario AX ausente"]
  ValidationError["400 o 422 VALIDATION_ERROR<br/>Errores de campo o ruta"]
  ComError["500 o 503 AX_COM_ERROR / AX_SESSION_ERROR / AX_TIMEOUT<br/>Fallo COM, de sesión o tiempo de espera"]
  AxError["Error de negocio de Axapta<br/>Código CRM o de validación mapeado"]
  ExternalError["Error externo o de IA 429, 502 o 503<br/>Puede incluir Retry-After"]
  Envelope["Envoltorio de error estándar<br/>success=false, message, errorCode, errors, traceId"]

  Start --> Mvc
  Mvc --> TokenCheck
  TokenCheck -- "no" --> AuthError
  TokenCheck -- "sí" --> ContextCheck
  ContextCheck -- "no" --> ContextError
  ContextCheck -- "sí" --> CompanyCheck
  CompanyCheck -- "no" --> CompanyError
  CompanyCheck -- "sí" --> AxUserCheck
  AxUserCheck -- "no" --> AxUserError
  AxUserCheck -- "sí" --> RequestCheck
  RequestCheck -- "no" --> ValidationError
  RequestCheck -- "sí" --> ExternalCall
  ExternalCall -- "sí" --> ExternalOk
  ExternalOk -- "no" --> ExternalError
  ExternalOk -- "sí" --> ComCall
  ExternalCall -- "no" --> ComCall
  ComCall --> AxResult
  AxResult -- "sí" --> Success
  AxResult -- "error de negocio" --> AxError
  AxResult -- "COM/sesión/tiempo de espera" --> ComError

  AuthError --> Envelope
  ContextError --> Envelope
  CompanyError --> Envelope
  AxUserError --> Envelope
  ValidationError --> Envelope
  ComError --> Envelope
  AxError --> Envelope
  ExternalError --> Envelope
```

## Comportamiento observado

- La autenticación ausente o no válida devuelve un error de autenticación.
- La ausencia de empresa o usuario AX se trata como fallo de validación.
- Un contexto CRM ausente, caducado, obsoleto o prohibido devuelve un error
  específico de contexto.
- Los fallos COM o de sesión se asignan a códigos de error de Axapta.
- Los límites de uso conocidos de IA pueden devolver HTTP 429 y `Retry-After`.
- Los tiempos de espera agotados o las caídas externas se asignan a errores de servicio externo.

## Comportamiento del cliente

El servicio API de React trata la caducidad de sesión y las respuestas que
exigen autenticación o contexto. Según el código recibido, puede renovar el
contexto o redirigir al inicio de sesión.

El comportamiento visible exacto de cada pantalla Razor antigua no está
confirmado; debe verificarse en la pantalla y el flujo concretos antes de
documentarlo como uniforme.
