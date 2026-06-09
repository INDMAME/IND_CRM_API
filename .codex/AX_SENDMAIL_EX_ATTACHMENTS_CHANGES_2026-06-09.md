# AX SendMailEx Attachments Alignment - 2026-06-09

## Scope

Align Axapta CRM mail transport with the current `IND_INTERNAL_API` / COM DLL contract.

The active mail flow now uses only `SendMailEx`. Basic/raw COM mail methods are no longer called from the exported AX objects.

## Updated Objects

- `INDInternalApiClientServer`
- `INDCRMUtilityService`
- `INDCRMExpenseSheetService`
- `INDEmailTemplatesForm`
- `Job_INDInternalApi_SendMail`
- `Job_INDInternalApi_SendMail_Basico`
- `Job_INDInternalApi_SendMailEx`
- `Job_INDInternalApi_SendMail_Extendido`

## Contract

`attachmentFilePaths` was added after `textBody` and before `saveToSentItems`.

```text
htmlBody,
textBody,
attachmentFilePaths,
saveToSentItems,
importance
```

Rules:

- Optional string. Empty or null means no attachments.
- Absolute staged paths separated by `;`.
- Files must already exist in the shared path configured in Axapta as `INDDefaultParameters.FilePathEmails`.
- `IND_CRM_API` must not read files and must not receive Base64 for this flow.
- The DLL reads files from AOS, infers content type, converts to Base64 and calls `IND_INTERNAL_API`.
- Current limits are 10 attachments, 25 MB per file and 50 MB total before Base64.

## Behavioral Notes

- Expense-sheet status emails now call `INDCRMUtilityService::sendInternalApiMailEx` with empty `attachmentFilePaths`.
- Template test buttons now pass empty `attachmentFilePaths`.
- Smoke-test jobs use `clientt.SendMailEx(...)` directly and include the new argument.
- `sendInternalApiMail` basic helper exports were removed from the active XPOs to avoid calling a DLL method that no longer exists.
- No `IND_CRM_API` C# controller, DTO or Swagger endpoint for direct email sending exists in this project. The public CRM API contract remains the expense-sheet update endpoint; Axapta owns the notification send decision.

## Verification

Final code search should show no COM call to `.SendMail(`. Only `.SendMailEx(` should remain for mail sending.
