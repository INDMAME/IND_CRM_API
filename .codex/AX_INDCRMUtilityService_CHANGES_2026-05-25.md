# AX Change Log: INDCRMUtilityService

Date: 2026-05-25

## Objective

Add a generic Axapta mail helper that can call the COM-visible `IND.InternalApiClient` DLL, which then sends mail through `IND_INTERNAL_API`.

## Methods Added

- `buildMailRecipientsJson`
- `jsonEscape`
- `sendInternalApiMail`

## Contract

`sendInternalApiMail` receives company, sender, recipient string, subject, HTML body, text body, process metadata, event type, aggregate metadata, idempotency key, and optional correlation id.

Temporary configuration placeholders:

- `ParameterTable.INDInternalApiBaseUrl`
- `ParameterTable.INDInternalApiClientId`
- `ParameterTable.INDInternalApiClientSecret`

## Behavior

- Does not contain CRM or expense sheet link logic.
- Returns `false` when configuration, sender, recipients, subject, or body is missing.
- Calls `IND.InternalApiClient.SendMailJson`.
- Logs best-effort warnings and does not throw for mail failures.

## Risks

- The actual Axapta technical configuration table/fields must replace the temporary `ParameterTable` placeholders.
- The COM DLL must expose `SendMailJson` before this helper can send mail.
