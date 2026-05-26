# AX Change Log: INDInternalApiClientServer

Date: 2026-05-26

## Objective

Move the global Axapta email transport implementation into `INDInternalApiClientServer` so this class is the only Axapta class that calls the `IND.InternalApiClient` DLL for generic mail sending.

## Methods Added

- `sendInternalApiMail`
- `sendInternalApiMailEx`

## Contract

The method signatures match the previous `INDCRMUtilityService` mail helpers:

- `sendInternalApiMail(...)` supports From, To, subject, HTML/text body, process/event metadata, aggregate metadata, idempotency key, and optional correlation id.
- `sendInternalApiMailEx(...)` adds sender display name, CC, BCC, Reply-To, SaveToSentItems, and Importance.

`sendInternalApiMail` delegates to `sendInternalApiMailEx`.

## Behavior

- Reads temporary internal API configuration from `ParameterTable::find().INDInternalApiBaseUrl`, `INDInternalApiClientId`, and `INDInternalApiClientSecret`.
- Validates config, From, To, Subject, and Body before calling the DLL.
- Normalizes `importance` to `low`, `normal`, or `high`; default is `normal`.
- Calls `INDIntApiINDInternalApiClient.SendMailEx`.
- Sends `sourceSystem` as `AXAPTA`.
- Returns `false` and logs a warning on validation, COM/CLR, or API acceptance failures.
- Does not trim or truncate `htmlBody` or `textBody` before passing them to the DLL.

## Ownership

- Global Axapta callers should use `INDInternalApiClientServer::sendInternalApiMail*` directly.
- CRM/API compatibility callers can keep using `INDCRMUtilityService::sendInternalApiMail*`, which now delegates here.

## Risks

- The Axapta Business Class Wizard wrapper `INDIntApiINDInternalApiClient` must be regenerated/reimported if the deployed DLL type library does not yet expose `SendMailEx`.
- Temporary `ParameterTable` fields must later be replaced with the final technical configuration table.
