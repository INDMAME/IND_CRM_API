# System context

This diagram shows the main runtime systems and external dependencies for
the CRM flows. The CRM API is the boundary that protects Axapta access and
normalizes responses for the web application.

```mermaid
flowchart LR
  User["CRM user"]
  Browser["Browser<br/>Razor pages and React islands"]
  WebApp["IND_CRM_APP<br/>ASP.NET Core MVC + Razor<br/>React TypeScript"]
  Entra["Microsoft Entra ID<br/>OIDC login and claims"]
  Api["IND_CRM_API<br/>Web API 2, .NET Framework 4.8<br/>OWIN self-host, x86"]
  Guard["API guard layer<br/>JWT, company, AX user, context token"]
  Bc["Business Connector COM<br/>request-scoped Axapta session"]
  Ax["Axapta 3.0<br/>AOT CRM service classes"]
  Blob["Azure Blob Storage<br/>ticket files and previews"]
  Ocr["Azure Document Intelligence<br/>receipt extraction"]
  Ai["OpenAI services<br/>speech, normalization, Q&A"]
  Fx["Exchange rate providers<br/>currency rates"]

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

## Responsibilities

`IND_CRM_APP` owns the user experience, Razor views, React islands, session
state, CSRF handling, MVC proxy endpoints, and calls to the CRM API through
`ICrmApiClient` and `ApiClientService`.

`IND_CRM_API` owns HTTP contracts, JWT validation, CRM context validation,
standard response envelopes, diagnostics, external service orchestration, and
the only server-side integration path to Axapta.

Axapta 3.0 remains the system of record for CRM entities such as activities,
visits, expense sheets, tickets, users, companies, currencies, and projects.
The reviewed code invokes Axapta by Business Connector COM only.

External services are used for identity, blob files, receipt OCR, speech or
text AI flows, and exchange rates. Exact provider fallback behavior for
exchange rates is pendiente de validar.

## Repository note

This documentation belongs to the CRM integration surface. It covers
`IND_CRM_APP`, `IND_CRM_API`, Axapta 3.0, and the external services used by
CRM modules.
