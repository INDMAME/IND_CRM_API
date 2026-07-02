# Error handling

The API normalizes validation, authorization, Axapta, COM, timeout, and
external-service failures into standard response envelopes whenever possible.

```mermaid
flowchart TD
  Start["Incoming CRM request"]
  Mvc["IND_CRM_APP fetch or MVC proxy"]
  TokenCheck{"API token present and valid?"}
  ContextCheck{"Context headers valid?"}
  CompanyCheck{"X-IND-Company present?"}
  AxUserCheck{"X-IND-AxUserId present when required?"}
  RequestCheck{"DTO and route values valid?"}
  ComCall["Business Connector COM call"]
  AxResult{"Axapta result successful?"}
  ExternalCall{"External service involved?"}
  ExternalOk{"External service successful?"}
  Success["Return IndApiResponse of T or IndPagedResponse of T<br/>success=true + traceId"]

  AuthError["401 AUTH_REQUIRED or token error<br/>React may refresh or force relogin"]
  ContextError["403 AUTH_CONTEXT_REQUIRED / AUTH_CONTEXT_STALE / AUTH_FORBIDDEN<br/>React may refresh context or show access error"]
  CompanyError["422 VALIDATION_ERROR<br/>missing or invalid company"]
  AxUserError["422 VALIDATION_ERROR<br/>missing AX user"]
  ValidationError["400 or 422 VALIDATION_ERROR<br/>field or route validation errors"]
  ComError["500 or 503 AX_COM_ERROR / AX_SESSION_ERROR / AX_TIMEOUT<br/>COM, session, or timeout failure"]
  AxError["Business error from Axapta<br/>mapped CRM or validation error code"]
  ExternalError["429, 502, or 503 external/AI error<br/>may include Retry-After"]
  Envelope["Standard error envelope<br/>success=false, message, errorCode, errors, traceId"]

  Start --> Mvc
  Mvc --> TokenCheck
  TokenCheck -- "no" --> AuthError
  TokenCheck -- "yes" --> ContextCheck
  ContextCheck -- "no" --> ContextError
  ContextCheck -- "yes" --> CompanyCheck
  CompanyCheck -- "no" --> CompanyError
  CompanyCheck -- "yes" --> AxUserCheck
  AxUserCheck -- "no" --> AxUserError
  AxUserCheck -- "yes" --> RequestCheck
  RequestCheck -- "no" --> ValidationError
  RequestCheck -- "yes" --> ExternalCall
  ExternalCall -- "yes" --> ExternalOk
  ExternalOk -- "no" --> ExternalError
  ExternalOk -- "yes" --> ComCall
  ExternalCall -- "no" --> ComCall
  ComCall --> AxResult
  AxResult -- "yes" --> Success
  AxResult -- "business error" --> AxError
  AxResult -- "COM/session/timeout" --> ComError

  AuthError --> Envelope
  ContextError --> Envelope
  CompanyError --> Envelope
  AxUserError --> Envelope
  ValidationError --> Envelope
  ComError --> Envelope
  AxError --> Envelope
  ExternalError --> Envelope
```

## Observed behavior

- Missing or invalid authentication returns an auth error envelope.
- Missing company or AX user headers are treated as validation failures.
- Missing, expired, stale, or forbidden CRM context returns context-specific
  auth errors.
- Axapta COM/session failures are mapped to Axapta-specific error codes.
- Known AI rate-limit errors can return HTTP 429 and `Retry-After`.
- External-service timeouts or outages are mapped to external-service errors.

## Client behavior

The React API service handles session expiration, auth-required responses,
context-required responses, and stale-context responses. It can trigger
context refresh or force a login redirect depending on the error code.

Exact user-facing behavior for every older Razor-only screen is pendiente de
validar.
