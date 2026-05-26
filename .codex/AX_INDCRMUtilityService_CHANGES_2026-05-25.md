# AX Change Log: INDCRMUtilityService

Date: 2026-05-25

## Objective

Add a generic Axapta mail helper that can call the COM-visible `IND.InternalApiClient` DLL, which then sends mail through `IND_INTERNAL_API`.

## Methods Added

- `sendInternalApiMail`
- `sendInternalApiMailEx`

## Contract

`sendInternalApiMail` receives company, sender, recipient string, subject, HTML body, text body, process metadata, event type, aggregate metadata, idempotency key, and optional correlation id.

`sendInternalApiMailEx` keeps the same transport behavior and adds optional sender display name, CC, BCC, Reply-To, SaveToSentItems, and Importance values. The simple helper delegates to the extended helper to preserve existing callers. JSON construction is owned by `INDInternalApiClient.SendMailEx`.

There are two Axapta-friendly helper shapes:

- Simple: `sendInternalApiMail(...)`, backed by COM `SendMail`.
- Extended: `sendInternalApiMailEx(...)`, backed by COM `SendMailEx`.

Both helpers ultimately use the single internal HTTP endpoint `POST /api/internal/v1/mail/messages` in `IND_INTERNAL_API`; there are not separate simple/extended HTTP endpoints.

Temporary configuration placeholders:

- `ParameterTable.INDInternalApiBaseUrl`
- `ParameterTable.INDInternalApiClientId`
- `ParameterTable.INDInternalApiClientSecret`

## Behavior

- Does not contain CRM or expense sheet link logic.
- Returns `false` when configuration, sender, recipients, subject, or body is missing.
- Calls `IND.InternalApiClient.SendMailEx`.
- Logs best-effort warnings and does not throw for mail failures.
- Does not trim or truncate `htmlBody`/`textBody` before sending to the DLL. Provider/API size limits still apply downstream.

## Risks

- The actual Axapta technical configuration table/fields must replace the temporary `ParameterTable` placeholders.
- The COM DLL must expose `SendMailEx` before the extended helper can send mail.
- Smoke test jobs for direct COM `SendMail` and `SendMailEx` calls are documented in `C:\INDProjects\IND_INTERNAL_API\.codex\IND_INTERNAL_API_CLIENT_DLL.md`.
