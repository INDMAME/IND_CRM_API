# Data flow

This diagram focuses on data items rather than runtime components. It shows
how identity, context, headers, request DTOs, envelopes, trace data, and
errors move through the CRM stack.

```mermaid
flowchart LR
  subgraph BrowserData["Browser and web session"]
    UserIdentity["User identity<br/>claims and session cookie"]
    EntraOid["Entra OID"]
    ApiToken["API JWT<br/>Bearer token"]
    SelectedCompany["Selected company<br/>X-IND-Company"]
    AxUser["AX user<br/>X-IND-AxUserId"]
    ContextState["Context metadata<br/>version, permissions revision, token"]
  end

  subgraph RequestData["Request data"]
    Filters["Filters and paging<br/>dates, status, user, page, size"]
    CommandDto["Command DTO<br/>create/update/delete data"]
    FileData["Multipart file data<br/>ticket image or audio"]
    Correlation["X-Correlation-Id<br/>optional client trace"]
  end

  subgraph ApiBoundary["IND_CRM_API boundary"]
    Headers["Validated headers<br/>Authorization<br/>company<br/>AX user<br/>Entra/context"]
    GuardResult["Guard result<br/>allowed, stale, forbidden, invalid"]
    AxDto["Axapta call container or DTO mapping"]
    Trace["traceId / X-Trace-Id"]
  end

  subgraph Systems["Systems of record and services"]
    Ax["Axapta 3.0 data<br/>CRM master and transactions"]
    Blob["Blob objects<br/>ticket files and previews"]
    Ocr["Receipt extraction"]
    Ai["AI response<br/>draft, speech, Q and A"]
  end

  subgraph Responses["Responses"]
    ApiEnvelope["IndApiResponse of T<br/>success, message, data, errorCode, errors, traceId"]
    PagedEnvelope["IndPagedResponse of T<br/>success, message, total, page, pageSize, items, traceId"]
    ErrorPayload["Error payload<br/>HTTP status, errorCode, validation errors, retry hints"]
  end

  UserIdentity --> EntraOid
  EntraOid --> ContextState
  ApiToken --> Headers
  SelectedCompany --> Headers
  AxUser --> Headers
  ContextState --> Headers
  Filters --> AxDto
  CommandDto --> AxDto
  FileData --> Blob
  Correlation --> Trace
  Headers --> GuardResult
  GuardResult --> AxDto
  AxDto --> Ax
  FileData --> Ocr
  Blob --> Ocr
  Ocr --> Ai
  Ai --> AxDto
  Ax --> ApiEnvelope
  Ax --> PagedEnvelope
  Blob --> ApiEnvelope
  Ai --> ApiEnvelope
  Trace --> ApiEnvelope
  Trace --> PagedEnvelope
  GuardResult --> ErrorPayload
  ErrorPayload --> ApiEnvelope
```

## Main DTO and envelope observations

The analyzed API uses `IndApiResponse<T>` for single-result commands and
`IndPagedResponse<T>` for list/detail-style CRM responses. Both include a
diagnostic `traceId` in the API implementation.

The web app has corresponding response models and TypeScript clients that
unwrap `success`, `message`, `errorCode`, `items`, `data`, paging fields, and
context errors.

Request DTO field-level contracts are intentionally not duplicated here
because they must remain inferred from source code, Swagger/OpenAPI, Postman,
or existing clients. Bodies containing sensitive or customer data are omitted.
