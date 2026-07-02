# AX Change Log: Expense Sheet Email Templates

Date: 2026-05-28

## Objective

Move expense-sheet notification emails from hardcoded HTML to `INDEmailTemplates`, keeping the current mail transport through `INDCRMUtilityService` and `IND_INTERNAL_API`.

## Objects Touched

- `INDCRMExpenseSheetService.xpo`
- `INDEmailTemplates.xpo`
- `INDEmailTemplatesForm.xpo`
- `INDEmailTemplateTargetModule.xpo`
- `INDEmailTemplateFileHelper.xpo`
- `INDEmailTemplateHtmlEditor.xpo`

## Notification Flow

- `sendExpenseSheetStatusNotification` remains the single global Axapta entry point for expense-sheet status emails.
- Supported transitions:
  - `Draft -> InReview`: `ExpenseSheetApprovalRequested`
  - `Rejected -> InReview`: `ExpenseSheetRejectionCancelled`
  - `InReview -> Approved`: `ExpenseSheetApproved`
  - `InReview -> Rejected`: `ExpenseSheetRejected`
  - `Any -> Paid`: `ExpenseSheetPaid`
- Email sending remains best-effort. Missing users, emails, links, templates or Graph/API failures log a warning and do not block the business process.

## Recipient Rules

- `Draft -> InReview`: sender is the expense-sheet owner, recipient is the direct manager from `CRMUsuarioSubordinadoTable.UserIdJefe` where `UserIdSubordinado == CRMHojaGastosTable.UserId`.
- `InReview -> Approved`: sender is the actor, recipient is `CRMHojaGastosTable.UserId`.
- `InReview -> Rejected`: sender is the actor, recipient is `CRMHojaGastosTable.UserId`.
- `Rejected -> InReview`: sender is the actor, recipient is `CRMHojaGastosTable.UserId`.
- `Paid`: sender is `_userPagador` when supplied, otherwise the actor; recipient remains `CRMHojaGastosTable.INDCreatedByUserId` when supplied, otherwise `CRMHojaGastosTable.UserId`.
- If sender and recipient resolve to the same CRM user, no email is sent.

## Template Lookup

- `INDEmailTemplates::findValid(targetModule, languageId, today())` resolves the active template.
- `targetModule` is based on destination status:
  - `InReview` -> `INDEmailTemplateTargetModule::CRMInReview`
  - `Approved` -> `INDEmailTemplateTargetModule::CRMApproved`
  - `Rejected` -> `INDEmailTemplateTargetModule::CRMRejected`
  - `Paid` -> `INDEmailTemplateTargetModule::CRMPaid`
- `LanguageId` is taken from `SysUserInfo.Language` of the AX user linked to the recipient CRM user.
- `FromDate` and `ToDate` define the validity period. Empty `ToDate` means open-ended.
- `INDEmailTemplates.validateWrite` prevents overlapping ranges for the same `TargetModule + LanguageId`.

## Template Parameters

`SubjectTemplate` and `HtmlTemplate` are rendered with `strFmt` using this fixed order:

- `%1`: `HojaGastosId`
- `%2`: visible event/status text
- `%3`: total amount with ISO currency
- `%4`: date in `DD.MM.YYYY`
- `%5`: description
- `%6`: CRM detail link
- `%7`: button text, currently `Abrir hoja de gastos`
- `%8`: year
- `%9`: abbreviated month in Spanish, uppercase
- `%10`: day with two digits
- `%11`: status comments
- `%12`: logo `src` value from `INDEmailTemplates.Logo`, formatted as `data:image/png;base64,...`

Recommended logo usage in the template:

```html
<img src="%12" alt="Insertec" width="210" style="display:block;width:210px;max-width:210px;height:auto;border:0;" />
```

## Fallback

If no active template exists for `TargetModule + LanguageId`, the email still sends as plain text with:

- event message,
- sheet id,
- visible status,
- amount,
- date,
- description,
- CRM detail link.

## Form And Helper Notes

- `INDEmailTemplatesForm` includes preview/import/export helpers for HTML templates.
- `INDEmailTemplateFileHelper` reads and writes UTF-8 HTML files through `ADODB.Stream`.
- `INDEmailTemplateHtmlEditor` wraps the standard Axapta HTML editor for manual edits.
- Table/form code touched in this iteration is marked with `//MMS - Ajustes CRM - 2026.05.28`.

## Risks And Validation

- Import and compile all touched XPO objects in Axapta after the file update.
- Verify `CRMUsuarioSubordinadoTable` has a manager row for subordinated users before testing `Draft -> InReview`.
- Verify `INDPersonaTable.Email` exists for both sender and recipient.
- Verify `SysUserInfo.Language` matches `INDEmailTemplates.LanguageId`.
- Verify `FromDate/ToDate` ranges do not overlap for the same target module and language.
- Keep the HTML template free of huge Base64 values. Store only raw Base64 in `INDEmailTemplates.Logo` and reference it with `%12`.
