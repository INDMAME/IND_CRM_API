# Prompt for Codex: Global Graph Mail Utility in IND_INTERNAL_API

Use this prompt in project `C:\INDProjects\IND_INTERNAL_API`.

Date: 2026-05-25

## Goal

Implement a reusable, generic email sending capability in `IND_INTERNAL_API` using Microsoft Graph. This API is the transport layer only. It must be callable from any internal process, including Axapta through the existing COM-visible client DLL.

This project must not contain CRM, expense sheet, approval workflow, localization, or email template logic. Callers provide `from`, recipients, subject, body, metadata, and correlation data.

## Required context to read first

Read these files before editing code:

- `.codex/skills/ind-internal-api-guardrails/SKILL.md`
- `.codex/ENDPOINTS.md`
- `README.md`
- `Web.config` or `App.config` files present in the API project
- `App_Start/DependencyConfig.cs`
- Existing controllers under `Controllers`
- Existing auth/JWT implementation and authorization filters
- Existing logging helpers and response/error contracts
- `scripts/set-intserv-machine-all-env.ps1`
- `INDInternalApiClient/IINDInternalApiClient.cs`
- `INDInternalApiClient/INDInternalApiClient.cs`
- `INDInternalApiClient/InternalApiHttpClient.cs`

Follow the repository guardrails:

- Target .NET Framework 4.8.
- Keep x86 compatibility.
- Do not add a new test project.
- Do not hardcode secrets.
- Do not add CRM-specific or expense-sheet-specific logic.
- New internal endpoints must use the `api/internal/v1` namespace.
- New protected endpoints must require the existing JWT authentication flow.
- Update endpoint documentation when adding endpoints.

## Architecture decision

`IND_INTERNAL_API` owns only the generic mail transport:

1. It validates a generic mail request.
2. It obtains a Microsoft Graph application token using configured client credentials.
3. It calls Graph `sendMail` for the requested sender.
4. It logs enough metadata to diagnose failures.
5. It returns a structured response or structured error.

It must not decide who receives an expense sheet notification, what language to use, or how to build a CRM web deep link.

## New endpoint

Add:

```text
POST /api/internal/v1/mail/messages
```

Authentication:

- Requires the existing internal JWT bearer token.
- Add and document scope `internal.mail.send`.
- Existing service users that need to send mail must receive this scope through the existing auth configuration mechanism.

Request content type:

```text
application/json
```

Request contract:

```json
{
  "company": "es01",
  "from": {
    "email": "sender@example.com",
    "displayName": "Sender Name"
  },
  "to": [
    {
      "email": "recipient@example.com",
      "displayName": "Recipient Name"
    }
  ],
  "cc": [],
  "bcc": [],
  "replyTo": [],
  "subject": "Subject",
  "htmlBody": "<p>Message</p>",
  "textBody": "Message",
  "saveToSentItems": false,
  "sourceSystem": "IND_CRM_API",
  "sourceProcess": "ExpenseSheetNotifications",
  "eventType": "ExpenseSheetApproved",
  "aggregateType": "ExpenseSheet",
  "aggregateId": "12345",
  "idempotencyKey": "ExpenseSheetApproved:es01:12345:status-2",
  "correlationId": "optional-correlation-id"
}
```

Validation rules:

- `from.email` is required.
- At least one `to` recipient is required.
- `subject` is required and must be bounded to a reasonable max length.
- At least one body is required: `htmlBody` or `textBody`.
- Validate all email values with `System.Net.Mail.MailAddress`.
- Reject invalid input with the existing validation/error response style.
- Do not send partially if a recipient email is invalid. Return a validation error instead.
- `company`, `sourceSystem`, `sourceProcess`, `eventType`, `aggregateType`, `aggregateId`, `idempotencyKey`, and `correlationId` are metadata for logging and traceability. They must not be required for generic use unless an existing standard says otherwise.
- Do not log full email bodies.

Response contract:

```json
{
  "acceptedByProvider": true,
  "provider": "MicrosoftGraph",
  "providerStatusCode": 202,
  "fromEmail": "sender@example.com",
  "recipientCount": 1,
  "correlationId": "correlation-id-used",
  "idempotencyKey": "ExpenseSheetApproved:es01:12345:status-2"
}
```

Graph `202 Accepted` means Graph accepted the request. It does not prove final inbox delivery. Document that behavior.

## Microsoft Graph implementation

Create focused services, using existing naming conventions:

- `IGraphMailClient`
- `GraphMailClient`
- `GraphMailOptions`
- Request/response DTOs for mail
- A small token provider if no existing OAuth client credentials helper exists

Use Microsoft Graph through HTTP. Do not add new NuGet packages unless strongly justified. If the project already has a Microsoft Graph SDK dependency, use the existing version and patterns.

Token flow:

- Use OAuth2 client credentials.
- Scope should be `https://graph.microsoft.com/.default`, unless the environment already uses another configured Graph base URL.
- Token endpoint format:

```text
https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
```

Send endpoint:

```text
POST https://graph.microsoft.com/v1.0/users/{fromEmail}/sendMail
```

Graph payload shape:

```json
{
  "message": {
    "subject": "Subject",
    "body": {
      "contentType": "HTML",
      "content": "<p>Message</p>"
    },
    "toRecipients": [
      {
        "emailAddress": {
          "address": "recipient@example.com",
          "name": "Recipient Name"
        }
      }
    ],
    "ccRecipients": [],
    "bccRecipients": [],
    "replyTo": []
  },
  "saveToSentItems": false
}
```

If only `textBody` is provided, send `Text`. If `htmlBody` is provided, send `HTML`.

The project can assume the Azure app registration already has working Graph permissions to send as the requested sender. Still handle Graph authorization failures clearly.

## Configuration keys

Add these settings through the existing configuration helper and machine env script:

```text
INTSERV_GRAPH_MAIL_ENABLED
INTSERV_GRAPH_TENANT_ID
INTSERV_GRAPH_CLIENT_ID
INTSERV_GRAPH_CLIENT_SECRET
INTSERV_GRAPH_BASE_URL
INTSERV_GRAPH_TOKEN_URL_TEMPLATE
INTSERV_GRAPH_SEND_TIMEOUT_SECONDS
INTSERV_GRAPH_SAVE_TO_SENT_ITEMS_DEFAULT
```

Recommended defaults:

```text
INTSERV_GRAPH_MAIL_ENABLED=false
INTSERV_GRAPH_BASE_URL=https://graph.microsoft.com/v1.0
INTSERV_GRAPH_TOKEN_URL_TEMPLATE=https://login.microsoftonline.com/{tenantId}/oauth2/v2.0/token
INTSERV_GRAPH_SEND_TIMEOUT_SECONDS=30
INTSERV_GRAPH_SAVE_TO_SENT_ITEMS_DEFAULT=false
```

Do not store real tenant IDs, client IDs, or client secrets in source control. Use placeholders in scripts and docs.

If `INTSERV_GRAPH_MAIL_ENABLED` is false, return a structured not-configured error and log it.

## Error handling

Add error codes following the existing `INDErrorCodes` style:

```text
MAIL_NOT_CONFIGURED
MAIL_VALIDATION_FAILED
MAIL_TOKEN_FAILED
MAIL_SEND_FAILED
```

Use existing response helpers and exception filters. Keep raw Graph responses out of normal API responses if they may contain sensitive data. Log enough status and sanitized content for support.

Log at least:

- Correlation ID.
- Source system.
- Source process.
- Event type.
- Aggregate type and ID.
- Company.
- Sender email.
- Recipient count.
- Graph HTTP status.
- Error code and sanitized provider error when applicable.

Do not log:

- Graph client secret.
- OAuth access token.
- Full email body.

## COM client DLL extension for Axapta

Extend the existing COM-visible `INDInternalApiClient` without breaking current methods.

Add new methods with new `DispId` values after the existing ones. Preserve binary compatibility where possible.

Required generic method:

```csharp
string SendMailJson(
    string baseUrl,
    string username,
    string password,
    string mailRequestJson,
    string correlationId
);
```

Behavior:

1. Login using the existing auth flow.
2. Call `POST /api/internal/v1/mail/messages`.
3. Return the raw JSON response as a string on success.
4. Return a structured JSON error string or throw consistently with existing COM client error behavior.

Optional convenience method if it fits the existing COM style:

```csharp
bool SendMail(
    string baseUrl,
    string username,
    string password,
    string fromEmail,
    string toEmails,
    string subject,
    string htmlBody,
    string textBody,
    string company,
    string sourceSystem,
    string sourceProcess,
    string eventType,
    string aggregateType,
    string aggregateId,
    string idempotencyKey,
    string correlationId
);
```

For `toEmails`, accept semicolon-separated addresses. The method builds the same JSON request and calls `SendMailJson`.

Keep all new COM methods generic. They must not mention CRM, expense sheets, approval, paid remittances, or deep links.

## Documentation updates

Update:

- `.codex/ENDPOINTS.md`
- `README.md` or the project configuration docs if those are the established place for machine env keys
- `scripts/set-intserv-machine-all-env.ps1`

The endpoint docs must include:

- Route.
- Auth scope.
- Request sample.
- Response sample.
- Error codes.
- Graph `202 Accepted` note.
- Required machine env keys.

## Verification

Run the existing build workflow:

```powershell
.\scripts\build-internal-api.ps1 -Configuration Release
```

Also build the COM client project if it is not included in that script.

If no automated tests exist, add focused unit-level coverage only if the repository already has an appropriate test project. Do not create a new test project.

Manual verification checklist:

- Auth without `internal.mail.send` is rejected.
- Missing Graph config returns `MAIL_NOT_CONFIGURED`.
- Missing from/to/subject/body returns validation errors.
- Valid request calls Graph `/users/{fromEmail}/sendMail`.
- Graph `202` maps to `acceptedByProvider=true`.
- Graph `401/403/429/5xx` maps to structured errors and logs sanitized details.
- COM `SendMailJson` can authenticate and call the new endpoint.

## Out of scope

Do not implement:

- Expense sheet recipient resolution.
- Expense sheet link generation.
- Approval workflow changes.
- CRM app routes.
- Email localization.
- Retry queues or durable outbox, unless the project already has one and the implementation is trivial.
