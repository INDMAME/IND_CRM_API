# Integration overview

The normal CRM path is browser UI to MVC/Razor services, then to the shared
API client, then to `IND_CRM_API`, and finally to Axapta through Business
Connector COM.

```mermaid
flowchart TB
  Browser["Browser"]
  Razor["Razor MVC pages<br/>server-rendered CRM views"]
  React["React TypeScript islands<br/>expense and ticket UI"]
  Proxy["MVC same-origin proxy routes<br/>/api/crm/... and /api/auth/..."]
  MvcSvc["MVC services<br/>token, context, blob preview"]
  Client["ICrmApiClient / ApiClientService<br/>HttpClient wrapper"]
  Headers["Outbound headers<br/>Authorization: Bearer token<br/>X-IND-Company<br/>X-IND-AxUserId<br/>X-IND-EntraOid<br/>X-IND-Context-Version<br/>X-IND-Permissions-Revision<br/>X-IND-Context-Token<br/>X-Correlation-Id"]
  Api["IND_CRM_API controllers<br/>Web API 2"]
  Guard["Base CRM guards<br/>auth, company, AX user, context"]
  AxSession["AxaptaSessionManager<br/>Business Connector COM"]
  Aot["Axapta AOT services<br/>INDCRMUtilityService<br/>INDCRMVisitsService<br/>INDCRMExpenseSheetService"]
  External["External service adapters<br/>Blob, OCR, OpenAI, exchange rates"]
  Response["Standard envelopes<br/>IndApiResponse of T<br/>IndPagedResponse of T<br/>traceId and errors"]

  Browser --> Razor
  Browser --> React
  Razor --> MvcSvc
  React --> Proxy
  Proxy --> MvcSvc
  MvcSvc --> Client
  Client --> Headers
  Headers --> Api
  Api --> Guard
  Guard --> AxSession
  AxSession --> Aot
  Api --> External
  Aot --> Response
  External --> Response
  Response --> Client
  Client --> MvcSvc
  MvcSvc --> Browser
```

## Main boundaries

`IND_CRM_APP` should not call Axapta directly. It uses MVC controllers and
services to hide token refresh, context refresh, CSRF, and API envelope
handling from React components.

`IND_CRM_API` should not expose raw COM or Axapta containers to web clients.
Controllers map HTTP DTOs to Axapta calls and return standard envelopes.

Business Connector COM is an implementation detail behind
`AxaptaSessionManager`. Its x86 requirement affects hosting, deployment, and
diagnostics.

## Detected inventory

Client-side and web-app entry points:

- React services call same-origin `/api/...` routes from the browser.
- MVC/Razor controllers expose proxy routes for auth context, expense sheets,
  tickets, AI helpers, exchange rates, and blob previews.
- `ICrmApiClient` and `ApiClientService` encapsulate outbound HTTP calls to
  `IND_CRM_API`.
- Token and context services store API token, Entra OID, selected company,
  AX user, context token, context version, and permissions revision in the web
  session.

Relevant API controllers detected:

- Auth: `POST /api/auth/login`, `POST /api/auth/refresh`,
  `POST /api/auth/entra/context`.
- Accounts and contacts: `POST /api/crm/accounts/listAccounts`,
  `POST /api/crm/accounts/listContacts`.
- Activities: `POST /api/crm/activities/list`,
  `POST /api/crm/activities/create`, `GET /api/crm/activities/{recId}`,
  `PUT /api/crm/activities/{recId}`, `DELETE /api/crm/activities/{recId}`,
  `GET /api/crm/activities/by-code/{code}`.
- Visits: `POST /api/crm/visits/createVisitaAsistente`,
  `DELETE /api/crm/visits/deleteVisitaAsistente`.
- Expense sheets and tickets: documented in the dedicated sequence files.
- AI and transcription: `POST /api/ia/service/speech`,
  `POST /api/ia/service/expensefromticket`,
  `POST /api/ia/service/expensesheets/ask`.
- System and health: health checks, environment/company name, projects, and
  exchange-rate endpoints.

Axapta integration points detected:

- `INDCRMUtilityService.loginEntraContext` for user/company/module context.
- `INDCRMVisitsService` for accounts, contacts, activities, and visit
  assistants.
- `INDCRMExpenseSheetService` for expense sheets, tickets, projects, and
  ticket AI persistence.

## Relevant headers

- `Authorization: Bearer <token>` authenticates the CRM API request.
- `X-IND-Company: <companyId>` selects the company context.
- `X-IND-AxUserId: <axUserId>` selects the Axapta CRM user.
- `X-IND-EntraOid: <entraOid>` binds the request to Entra identity context.
- `X-IND-Context-Version: <version>` and
  `X-IND-Permissions-Revision: <revision>` protect against stale context.
- `X-IND-Context-Token: <contextToken>` signs the company/module snapshot.
- `X-Correlation-Id: <id>` links logs across layers when supplied.

The exact set of context headers on older MVC/Razor pages outside the expense
React flow is pendiente de validar.
