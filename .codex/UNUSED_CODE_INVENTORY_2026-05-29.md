# Unused Code Inventory

Date: 2026-05-29

## Scope

Project: `IND_CRM_API`.

The review covered:

- C# source files included in `IND_CRM_API.csproj`.
- Direct references with `rg` across the repository, excluding `bin`, `obj` and `packages`.
- Manual DI registrations in `App_Start/DependencyConfig.cs`.
- Web API controller discovery by reflection.
- Swagger/WebActivator startup hooks.
- Axapta XPO exports under `.codex/Axapta`.

## Removed

These classes/files had no active runtime owner in the current design and were removed from the project file and filesystem.

### Superseded local expense-sheet email flow

Removed because expense-sheet email decisions and sending now live in Axapta:

- `Services/ExpenseSheetNotificationService.cs`
- `Services/Interfaces/IExpenseSheetNotificationService.cs`
- `Services/InternalMailClient.cs`
- `Services/Interfaces/IInternalMailClient.cs`
- `Contracts/Notifications/ExpenseSheetNotificationContracts.cs`
- `Contracts/Notifications/InternalMailContracts.cs`

Reason:

- Not registered in `DependencyConfig`.
- Not called by `CrmExpenseSheetsController`.
- Superseded by `INDCRMExpenseSheetService -> INDCRMUtilityService -> INDInternalApiClientServer -> IND_INTERNAL_API`.

### Obsolete ECB HTTP wrapper

Removed:

- `Services/EcbHttpClient.cs`
- `Services/Interfaces/IEcbHttpClient.cs`

Reason:

- `EcbExchangeRateProvider` now owns its HTTP client internally and does not depend on `IEcbHttpClient`.
- No DI registration or direct references existed.

### Obsolete exchange-rate result type

Removed:

- `Services/ExchangeRateProviderResult.cs`

Reason:

- Current exchange-rate flow uses `ExchangeRateResult` and provider-specific results.
- No active references existed.

### Unused helper

Removed:

- `Helpers/PagingHelper.cs`

Reason:

- No direct references existed in controllers, services or helpers.

### Legacy Swagger startup no-op

Removed:

- `App_Start/SwaggerConfig.cs`
- `WebActivatorEx` reference from `IND_CRM_API.csproj`.
- `WebActivatorEx` entry from `packages.config`.

Reason:

- `Startup.cs` configures Swagger through `INDSwaggerConfig.Configure(config)`.
- The old `SwaggerConfig.Register()` method was a no-op.

## Kept By Design

These may appear unreferenced in a direct text search but are not dead code.

### Web API controllers

Controllers are discovered by Web API routing/reflection, not by direct code references. They must remain unless the route is intentionally removed.

Examples:

- `Controllers/CRM/CrmProjectsController.cs`
- `Controllers/CRM/CrmTemplateController.cs`
- `Controllers/System/McpToolsController.cs`
- `Controllers/System/SystemController.cs`
- `Controllers/System/INDExpenseSheetsAiController.cs`

### Request DTOs bound by Web API

Some request classes are referenced by action signatures and model binding rather than by explicit `new` calls.

Examples:

- `GetAccountsRequest`
- `GetContactosRequest`
- `GetActivitiesRequest`
- `GetExpenseSheetsListRequest`
- `UpdateExpenseSheetHeaderRequest`

### Internal helper/nested types

Several private classes are intentionally local to a single file. A cross-file-only search can flag them incorrectly.

Examples:

- `UserCompanyAccessCache.CacheEntry`
- `IND_OpenAiRateLimitHandler.EndpointLimit`
- `IND_OpenAiRateLimitHandler.RateWindowState`
- `CrmExpenseSheetTicketsController` private result classes.
- `ExpenseTicketBlobStorageService.StorageContext`
- `IndRouteDiagnosticsActionFilter.EnvelopeSummary`

### Axapta XPO exports

XPO objects are importable Axapta artifacts. They are not expected to have C# references.

Kept:

- `.codex/Axapta/INDCRMExpenseSheetService.xpo`
- `.codex/Axapta/INDCRMUtilityService.xpo`
- `.codex/Axapta/INDInternalApiClientServer.xpo`
- `.codex/Axapta/INDEmailTemplates.xpo`
- `.codex/Axapta/INDEmailTemplatesForm.xpo`
- `.codex/Axapta/INDEmailTemplateTargetModule.xpo`
- `.codex/Axapta/INDEmailTemplateFileHelper.xpo`
- `.codex/Axapta/INDEmailTemplateHtmlEditor.xpo`

## Follow-Up Notes

- Historical design documents can still mention removed classes as superseded design context.
- Do not delete controllers or DTOs only because they have no constructor call.
- Future cleanup should repeat this inventory pattern and then compile before committing.
