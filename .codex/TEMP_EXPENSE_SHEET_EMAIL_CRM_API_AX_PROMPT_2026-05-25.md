# Prompt for Codex: Expense Sheet Email Notification Orchestration in IND_CRM_API and Axapta

Use this prompt in project `C:\INDProjects\IND_CRM_API`.

Date: 2026-05-25

## Goal

Implement the CRM and Axapta side of expense sheet email notifications.

Responsibilities:

- `IND_CRM_API` owns CRM-specific notification orchestration for web-originated expense sheet status changes.
- Axapta owns Axapta-originated notification triggers, including paid remittance notifications.
- `IND_INTERNAL_API` owns the generic Microsoft Graph email transport.
- `IND_CRM_APP` owns resolving the emailed web deep link when a user clicks it.

Do not send mail directly through Microsoft Graph from `IND_CRM_API`. Always call `IND_INTERNAL_API`.

## Required context to read first

Read these files before editing code:

- `.codex/skills/ind-crm-backend-guardrails/SKILL.md`
- `docs/plans/2026-05-22-expense-sheet-email-deeplinks-design_API.md`
- `.codex/ENDPOINTS.md`
- `README.md`
- `App_Start/DependencyConfig.cs`
- `Helpers/AppSettingsHelper.cs`
- `Controllers/CRM/CrmExpenseSheetsController.cs`
- `Contracts/Requests/UpdateExpenseSheetHeaderRequest.cs`
- `Contracts/Responses/ExpenseSheetDetailDto.cs`
- `scripts/set-indcrm-machine-env.ps1`
- `scripts/set-indcrm-machine-critical-env.ps1`
- `scripts/set-indcrm-machine-all-env.ps1`
- `.codex/Axapta/INDCRMUtilityService.xpo`
- `.codex/Axapta/INDCRMExpenseSheetService.xpo`
- `.codex/Axapta/CRMHojaGastosTable.xpo`

Follow repository rules:

- Target .NET Framework 4.8.
- Keep x86 compatibility.
- Do not add new test projects.
- Add new `.cs` files to `IND_CRM_API.csproj`.
- Do not hardcode secrets or URLs.
- Keep behavior best-effort for email failures.
- Use existing logging patterns.
- Use existing Axapta COM patterns.
- Add brief comments to new classes and public methods.

## End-to-end flow

There are three notification events in scope:

```text
ExpenseSheetApprovalRequested
ExpenseSheetApproved
ExpenseSheetPaid
```

Deep links always navigate to the CRM web detail page. They never approve, reject, pay, or mutate data by themselves.

Canonical web resolver URL:

```text
{INDCRM_WEB_BASE_URL}/Gastos/ExpenseSheetLink?hojaGastosId={id}&targetCompanyId={companyId}&source={source}
```

Final detail route after resolver:

```text
/Gastos/ExpenseSheetDetail?hojaGastosId={id}
```

`targetCompanyId` is used only by the web app resolver to switch/validate company context. Do not include it in the final detail URL unless the existing web route requires it.

## IND_CRM_API implementation

### New configuration keys

Add these settings through the existing app settings helper and machine env scripts:

```text
INDCRM_INTERNAL_API_BASE_URL
INDCRM_INTERNAL_API_CLIENT_ID
INDCRM_INTERNAL_API_CLIENT_SECRET
INDCRM_EXPENSE_NOTIFICATIONS_ENABLED
INDCRM_EXPENSE_NOTIFICATIONS_BEST_EFFORT
INDCRM_EXPENSE_NOTIFY_TRANSITIONS
INDCRM_WEB_BASE_URL
```

Recommended defaults:

```text
INDCRM_EXPENSE_NOTIFICATIONS_ENABLED=false
INDCRM_EXPENSE_NOTIFICATIONS_BEST_EFFORT=true
INDCRM_EXPENSE_NOTIFY_TRANSITIONS=ExpenseSheetApprovalRequested,ExpenseSheetApproved
```

`INDCRM_WEB_BASE_URL` already exists in this repo. Reuse it; do not introduce another CRM web base URL key.

Do not store real secrets in source control.

### Internal mail client

Add a small client that authenticates against `IND_INTERNAL_API` and calls:

```text
POST /api/internal/v1/mail/messages
```

Suggested classes:

- `Services/Interfaces/IInternalMailClient.cs`
- `Services/InternalMailClient.cs`
- `Contracts/Notifications/InternalMailRequest.cs`
- `Contracts/Notifications/InternalMailResponse.cs`

The request sent to `IND_INTERNAL_API` must use the generic mail contract:

```json
{
  "company": "es01",
  "from": { "email": "sender@example.com" },
  "to": [{ "email": "recipient@example.com" }],
  "subject": "Localized subject",
  "htmlBody": "<p>Localized message</p><p><a href=\"...\">...</a></p>",
  "textBody": "Localized message\n...",
  "saveToSentItems": false,
  "sourceSystem": "IND_CRM_API",
  "sourceProcess": "ExpenseSheetNotifications",
  "eventType": "ExpenseSheetApproved",
  "aggregateType": "ExpenseSheet",
  "aggregateId": "12345",
  "idempotencyKey": "ExpenseSheetApproved:es01:12345:2",
  "correlationId": "existing-request-correlation-id"
}
```

Use existing HTTP helper conventions. If none exist, use `HttpClient` with bounded timeout and defensive error handling.

### Notification service

Add a focused service:

- `IExpenseSheetNotificationService`
- `ExpenseSheetNotificationService`

Responsibilities:

1. Detect which notification event should be sent.
2. Build the web deep link using `INDCRM_WEB_BASE_URL`.
3. Resolve sender and recipients through Axapta-backed data.
4. Build minimal localized subject/body.
5. Call `IInternalMailClient`.
6. Log success or failure.
7. Never block the expense sheet business action when email fails and best-effort is enabled.

Email body should be minimal:

- A short localized message.
- The expense sheet identifier.
- The deep link to the CRM web detail resolver.

Do not add approve/reject/payment action links.

### Recipient rules

All email addresses come from `INDPersonaTable.Email`.

Actor/sender:

- For web-originated updates, sender is the logged CRM/Axapta user from the current request context.
- Resolve `INDPersonaTable.Email` for that user.
- If the sender email is missing, log and skip sending.

Recipients:

- `ExpenseSheetApprovalRequested`: recipient list is the user or users that can approve the current expense sheet in Axapta. Resolve using Axapta authoritative logic, not client-side assumptions.
- `ExpenseSheetApproved`: recipient is the creator/requester of the sheet. Use `CRMHojaGastosTable.INDCreatedByUserId` and resolve `INDPersonaTable.Email`.
- `ExpenseSheetPaid`: recipient is the creator/requester of the sheet. Use `CRMHojaGastosTable.INDCreatedByUserId` and resolve `INDPersonaTable.Email`.

If a recipient email is missing, log and skip sending. Do not block the business process.

If the existing Axapta export does not expose a reliable way to resolve approvers for `ExpenseSheetApprovalRequested`, add a method to `INDCRMExpenseSheetService.xpo` to do it from the same source used by the approval UI/workflow. If that source cannot be found, stop and ask before guessing.

### Status transition detection in CRM API

Update `CrmExpenseSheetsController.UpdateExpenseSheetHeader` only after understanding current behavior.

Required behavior:

1. Read current sheet detail before the update.
2. Execute the existing update.
3. If the update succeeds, read the sheet detail again from Axapta.
4. Compare before/after status.
5. Send notification best-effort only for configured transitions.
6. Return the same API response shape as today.

Transition mapping:

```text
ExpenseSheetApprovalRequested: status changes to the approval requested state.
ExpenseSheetApproved: status changes to the approved state.
```

Do not trigger `ExpenseSheetPaid` from web/API update. That event is Axapta-only.

Avoid duplicate sends:

- Send only when status actually changes.
- Build a deterministic idempotency key:

```text
{eventType}:{companyId}:{hojaGastosId}:{afterStatus}
```

If there is no storage for idempotency in this repo, still pass the key to `IND_INTERNAL_API` for logging and future hardening.

### Localization in CRM API

Use the existing localization approach if this API already has one.

If there is no localization infrastructure in `IND_CRM_API`, keep subjects and bodies in one small resource/helper class with keys per event and language, and select language from the existing request/user/company context. Do not invent a large localization framework.

Messages must exist for all languages supported by the CRM app:

```text
es-ES
en
eu-ES
it
pt
zh-Hans
```

If no language can be resolved, fallback to `es-ES`.

## Axapta implementation

Axapta needs two layers:

1. A generic internal mail helper in `INDCRMUtilityService.xpo`.
2. Expense-sheet-specific notification helpers in `INDCRMExpenseSheetService.xpo`.

### Generic helper in INDCRMUtilityService.xpo

Add a generic method that any Axapta process can use to send email through `IND_INTERNAL_API` via the COM DLL.

Suggested static method:

```xpp
public static boolean sendInternalApiMail(
    str _companyId,
    str _fromEmail,
    str _toEmails,
    str _subject,
    str _htmlBody,
    str _textBody,
    str _sourceProcess,
    str _eventType,
    str _aggregateType,
    str _aggregateId,
    str _idempotencyKey,
    str _correlationId = ''
)
```

Use these temporary parameter fields exactly as placeholders. The user will later replace them with the real technical configuration table:

```xpp
ParameterTable.INDInternalApiBaseUrl
ParameterTable.INDInternalApiClientId
ParameterTable.INDInternalApiClientSecret
```

The generic helper must:

- Build the generic mail JSON expected by `IND_INTERNAL_API`.
- Instantiate the COM-visible client DLL.
- Call `SendMailJson` or the convenience `SendMail` method exposed by `IND.InternalApiClient`.
- Catch CLR/COM exceptions.
- Log failures best-effort.
- Return `true` only when the internal API accepts the send.

Do not include CRM-specific URL generation here.
Do not include expense-sheet-specific subject/body logic here.

### Expense sheet helper in INDCRMExpenseSheetService.xpo

Add expense-sheet-specific methods here, not in `INDCRMUtilityService.xpo`.

Suggested methods:

```xpp
public static str buildExpenseSheetWebLink(str _companyId, str _hojaGastosId, str _source = 'axapta')
```

Use this temporary parameter field as a placeholder:

```xpp
ParameterTable.INDCrmWebBaseUrl
```

Build:

```text
{ParameterTable.INDCrmWebBaseUrl}/Gastos/ExpenseSheetLink?hojaGastosId={id}&targetCompanyId={companyId}&source={source}
```

URL-encode query values if an existing AX helper exists. If no helper exists, implement minimal safe encoding for spaces and reserved query characters.

Add:

```xpp
public static boolean sendExpenseSheetPaidNotification(CRMHojaGastosTable _expenseSheet)
```

Behavior:

- Triggered only from Axapta when the expense sheet payment remittance is posted/paid.
- From email: `INDPersonaTable.Email` for the logged Axapta user, normally `curUserId()`.
- To email: `INDPersonaTable.Email` for `CRMHojaGastosTable.INDCreatedByUserId`.
- Link source: `axapta`.
- Event type: `ExpenseSheetPaid`.
- Source process: `AxaptaExpenseSheetPayment`.
- Aggregate type: `ExpenseSheet`.
- Aggregate ID: `CRMHojaGastosTable.HojaGastosId`.
- Body: minimal localized message plus link.
- If from/to email is missing, log and return false without blocking payment posting.
- Call `INDCRMUtilityService::sendInternalApiMail`.

Add recipient resolver methods for API usage if needed:

```xpp
public static container getExpenseSheetNotificationRecipients(str _companyId, str _hojaGastosId, str _eventType)
public static str getPersonaEmailByUserId(str _userId)
```

Use `INDPersonaTable.Email`. Validate actual field names in AOT before finalizing.

### Axapta payment/remittance trigger

Find the authoritative method that marks or posts the payment remittance for expense sheets. The current export suggests checking `CRMHojaGastosTable.xpo`, especially payment/posting methods such as `ContabilizaAsientoHojaGastos`, but validate the actual flow before editing.

After the posting succeeds, call:

```xpp
INDCRMExpenseSheetService::sendExpenseSheetPaidNotification(expenseSheetRecord);
```

Do not call before the posting is committed/successful.
Do not throw if email fails.
Log any failure.

## Logging requirements

Log in all involved layers:

- Axapta: when sender/recipient/config is missing, COM call fails, or internal API returns error.
- `IND_CRM_API`: when notification is skipped, internal API call fails, or unexpected mapping occurs.
- `IND_INTERNAL_API`: when Graph config is missing, Graph token fails, Graph send fails, or Graph accepts send.

Do not log Graph secrets, tokens, or full email bodies.

## Documentation updates

Update docs in this repo:

- `.codex/ENDPOINTS.md`
- `docs/plans/2026-05-22-expense-sheet-email-deeplinks-design_API.md`
- Any existing release/config doc that lists machine environment variables

Document:

- New CRM API config keys.
- New Axapta `ParameterTable` placeholders.
- Event ownership:
  - `ExpenseSheetApprovalRequested`: web/API status transition.
  - `ExpenseSheetApproved`: web/API status transition.
  - `ExpenseSheetPaid`: Axapta payment posting only.
- Link format.
- Best-effort behavior.
- Internal API endpoint dependency.

## Verification

Build:

```powershell
.\scripts\build-api.ps1 -Configuration Release
```

If the repo uses another build script, use the established one.

Manual verification checklist:

- Updating an expense sheet without a status transition sends no email.
- Transition to approval requested sends one best-effort email to approvers.
- Transition to approved sends one best-effort email to the sheet creator/requester.
- Paid remittance posting in Axapta sends one best-effort email to `INDCreatedByUserId`.
- Missing sender email logs and does not block.
- Missing recipient email logs and does not block.
- Internal API failure logs and does not block when best-effort is enabled.
- Deep link uses `/Gastos/ExpenseSheetLink`.
- No Graph keys exist in `IND_CRM_API`.
- No expense-sheet business logic exists in `IND_INTERNAL_API`.
