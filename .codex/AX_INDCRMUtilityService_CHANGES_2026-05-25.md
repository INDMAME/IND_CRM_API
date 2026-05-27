# AX Change Log: INDCRMUtilityService

Date: 2026-05-25

## Objective

Keep the CRM-facing Axapta mail helper contract while moving the direct DLL call to `INDInternalApiClientServer`.

## Methods Added

- `sendInternalApiMail`
- `sendInternalApiMailEx`

## Contract

`sendInternalApiMail` receives company, sender, recipient string, subject, HTML body, text body, process metadata, event type, aggregate metadata, idempotency key, and optional correlation id.

`sendInternalApiMailEx` keeps the same transport behavior and adds optional sender display name, CC, BCC, Reply-To, SaveToSentItems, and Importance values. Both methods now delegate to `INDInternalApiClientServer` so existing CRM/API callers keep the same interface while global Axapta callers can use `INDInternalApiClientServer` directly.

There are two Axapta-friendly helper shapes:

- Simple CRM facade: `INDCRMUtilityService::sendInternalApiMail(...)`, delegated to `INDInternalApiClientServer::sendInternalApiMail(...)`.
- Extended CRM facade: `INDCRMUtilityService::sendInternalApiMailEx(...)`, delegated to `INDInternalApiClientServer::sendInternalApiMailEx(...)`.

Both helpers ultimately use the single internal HTTP endpoint `POST /api/internal/v1/mail/messages` in `IND_INTERNAL_API`; there are not separate simple/extended HTTP endpoints.

Temporary configuration placeholders:

- `ParameterTable.INDInternalApiBaseUrl`
- `ParameterTable.INDInternalApiClientId`
- `ParameterTable.INDInternalApiClientSecret`

Current runtime resolution lives in `INDInternalApiClientServer`: it uses `INDDefaultParameters.InternalAPIUrlService`, `INDDefaultParameters.APIUserId`, and `INDDefaultParameters.INDAPIIntServiceUser().Password` first, matching the working manual jobs, and only falls back to these placeholders if the default-parameter values are empty.

## Behavior

- Does not contain CRM or expense sheet link logic.
- Returns `false` when configuration, sender, recipients, subject, or body is missing.
- Does not instantiate the DLL/COM client directly.
- Delegates direct transport to `INDInternalApiClientServer`.
- Logs best-effort warnings and does not throw for mail failures.
- Does not trim or truncate `htmlBody`/`textBody` before sending to the DLL. Provider/API size limits still apply downstream.

## Risks

- The actual Axapta technical configuration table/fields must replace the temporary `ParameterTable` placeholders.
- `INDInternalApiClientServer` must be imported before this facade compiles.
- The simple mail facade can use the existing COM `SendMail` method. `SendMailEx` is only required for extended mail properties such as CC, BCC, Reply-To, SaveToSentItems, and Importance.
- Smoke test jobs for direct COM `SendMail` and `SendMailEx` calls are documented in `C:\INDProjects\IND_INTERNAL_API\.codex\IND_INTERNAL_API_CLIENT_DLL.md`.
