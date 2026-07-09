# AX Change Log: INDInternalApiClientServer

Date: 2026-05-26

## Objective

Move the global Axapta email transport implementation into `INDInternalApiClientServer` so this class is the only Axapta class that calls the `IND.InternalApiClient` DLL for generic mail sending.

## Methods Added

- `resolveInternalApiConnection`
- `sendInternalApiMail`
- `sendInternalApiMailEx`

## Contract

The method signatures match the previous `INDCRMUtilityService` mail helpers:

- `sendInternalApiMail(...)` supports From, To, subject, HTML/text body, process/event metadata, aggregate metadata, idempotency key, and optional correlation id.
- `sendInternalApiMailEx(...)` adds sender display name, CC, BCC, Reply-To, SaveToSentItems, and Importance.

`sendInternalApiMail` uses the simple COM `SendMail` method directly. `sendInternalApiMailEx` remains the extended path for CC, BCC, Reply-To, SaveToSentItems, and Importance.

## Behavior

- Reads internal API configuration first from `INDDefaultParameters`, matching the working manual jobs:
  - `InternalAPIUrlService`
  - `APIUserId`
  - `INDAPIIntServiceUser().Password`
- Falls back to temporary `ParameterTable` placeholders only when the `INDDefaultParameters` value is missing.
- Validates config, From, To, Subject, and Body before calling the DLL.
- Normalizes `importance` to `low`, `normal`, or `high`; default is `normal`.
- `sendInternalApiMail` calls COM `IND.InternalApiClient.SendMail` directly so the simple expense notification path does not depend on a regenerated Axapta Business Class wrapper.
- `sendInternalApiMailEx` calls COM `IND.InternalApiClient.SendMailEx`.
- Sends `sourceSystem` as `AXAPTA`.
- Returns `false` and logs a warning on validation, COM, or API acceptance failures.
- Uses `Exception::Error` only in mail COM calls to keep the XPO compatible with Axapta 3.0.
- Does not trim or truncate `htmlBody` or `textBody` before passing them to the DLL.

## 2026-05-27 Debug Fix

- `sendInternalApiMail` and `sendInternalApiMailEx` now both resolve credentials through `resolveInternalApiConnection`, so the class uses the same working credential source as `Job_INDInternalApi_SendMail` and `Job_INDInternalApi_SendMailEx`.
- This avoids the observed `AUTH_INVALID_CREDENTIALS` response when the status-notification job was using `InternalApiClientId/InternalApiClientSecret` instead of `APIUserId/INDAPIIntServiceUser().Password`.
- The auxiliary smoke-test jobs `Job_INDInternalApi_SendMail_Basico` and `Job_INDInternalApi_SendMail_Extendido` were aligned with the working jobs: COM ProgId `IND.InternalApiClient` and password from `INDAPIIntServiceUser().Password`.

## Ownership

- Global Axapta callers should use `INDInternalApiClientServer::sendInternalApiMail*` directly.
- CRM/API compatibility callers can keep using `INDCRMUtilityService::sendInternalApiMail*`, which now delegates here.

## Risks

- `SendMailEx` still requires the deployed/registered COM DLL type library to expose that method before extended mail can be used.
- The simple notification path only requires the already existing COM `SendMail` method.
- Because `INDInternalApiClientServer` runs on server, `IND.InternalApiClient` must be registered on the AOS/server machine that executes the call.
- Temporary `ParameterTable` fields must later be replaced with the final technical configuration table.
