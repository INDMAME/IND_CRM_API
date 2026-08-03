# Prompt for Frontend Codex: Expense Sheet Line Defaults and Header Propagation

Date: 2026-05-21

Use this prompt in the IND_CRM_APP frontend project. Read the backend context files first and then implement the web behavior. If any route, DTO field, enum value, or existing frontend abstraction does not match this prompt, stop and ask before changing the flow.

## Backend Context Files

- `C:\INDProjects\IND_CRM_API\.codex\ENDPOINTS.md`
- `C:\INDProjects\IND_CRM_API\.codex\MCP_ENDPOINTS.md`
- `C:\INDProjects\IND_CRM_API\.codex\MCP_TOOLS.json`
- `C:\INDProjects\IND_CRM_API\.codex\POSTMAN.md`
- `C:\INDProjects\IND_CRM_API\.codex\TEMP_EXPENSE_SHEET_HEADER_PROPAGATION_FRONTEND_2026-05-21.md`
- `C:\INDProjects\IND_CRM_API\.codex\AX_CRMHojaGastosTables_CHANGES_2026-05-21.md`
- `C:\INDProjects\IND_CRM_API\.codex\AX_INDCRMExpenseSheetService_CHANGES_2026-05-21.md`
- `C:\INDProjects\IND_CRM_API\.codex\Axapta\CRMHojaGastosTable.xpo`
- `C:\INDProjects\IND_CRM_API\.codex\Axapta\CRMHojaGastosLine.xpo`
- `C:\INDProjects\IND_CRM_API\.codex\Axapta\INDCRMExpenseSheetService.xpo`
- `C:\INDProjects\IND_CRM_API\Controllers\CRM\CrmExpenseSheetsController.cs`
- `C:\INDProjects\IND_CRM_API\Contracts\Responses\ExpenseSheetPropagationResultDto.cs`

## Required API Endpoints

All endpoints require `Authorization: Bearer <token>`, `X-IND-Company`, `X-IND-AxUserId`, and the normal signed CRM context headers used by the frontend client.

- `GET /api/crm/expensesheets/{hojaGastosId}`
  - Use after every propagation to refresh header and lines.

- `PUT /api/crm/expensesheets/{hojaGastosId}`
  - Updates header only.
  - Relevant body fields: `description`, `currencyCode`, `exchRate`, `projId`, `reimbursableExpense`, `expenseSheetStatus`, `exchangeRateMode`, `estadoComentarios`.
  - This endpoint does not propagate values to existing lines.

- `PUT /api/crm/expensesheets/{hojaGastosId}/lines/{lineRecId}`
  - Updates one line.
  - Relevant optional fields: `projId`, `currencyCode`, `amountMST`, `exchRate`, `reimbursableExpense`.

- `POST /api/crm/expensesheets/{hojaGastosId}/currency-defaults/propagate?recalculateAmountMST=true&force=false`
  - Propagates header `currencyCode` and `exchRate` to all existing lines.
  - `recalculateAmountMST` defaults to `true`.
  - `force` defaults to `false`.
  - If lines are already multi-currency, the backend blocks unless `force=true`.
  - Use `force=true` only after explicit user confirmation that existing line currencies/exchange rates will be overwritten.
  - Response `data`:

```json
{
  "hojaGastosId": "009998",
  "propagationType": "currencyDefaults",
  "updatedLines": 3,
  "recalculateAmountMST": true
}
```

- `POST /api/crm/expensesheets/{hojaGastosId}/project-default/propagate`
  - Propagates header `projId` to existing line `projId` and `projIdHornos`.
  - AX also rebuilds each line project assignment.
  - Backend blocks if header `projId` is empty or is the mixed marker.
  - Response `data.propagationType` is `projectDefault`.

- `POST /api/crm/expensesheets/{hojaGastosId}/reimbursable-expense/propagate`
  - Propagates header `reimbursableExpense` to all existing lines.
  - Backend blocks if header `reimbursableExpense` is `Both`.
  - Response `data.propagationType` is `reimbursableExpense`.

## Mixed Value Markers

These values mean "mixed values in existing lines". They must not be silently pushed into lines.

- Currency mixed marker: AX sets header `currencyCode` to `INDDefaultParameters.CRMCurrencyVarios`.
- Project mixed marker: AX sets header `projId` to `PurchParameters.INDProjIdVarious`.
- Reimbursable mixed marker: AX sets header `reimbursableExpense` to `INDReimbursableExpense::Both`.

Enum values:

- `INDReimbursableExpense::Yes = 0`
- `INDReimbursableExpense::No = 1`
- `INDReimbursableExpense::Both = 2`

## Frontend Behavior To Implement

1. When the user edits header `currencyCode` or `exchRate`, save the header first with `PUT /api/crm/expensesheets/{hojaGastosId}`.
2. If the sheet has existing lines, show a confirmation action to propagate currency/exchange-rate to lines.
3. If the user confirms normal propagation, call `currency-defaults/propagate?recalculateAmountMST=true&force=false`.
4. If backend returns validation saying lines are multi-currency, show a stronger confirmation. If the user accepts overwriting line currencies, call the same endpoint with `force=true`.
5. When the user edits header `projId`, save the header first. If the sheet has existing lines, ask whether to update all line projects. If accepted, call `project-default/propagate`.
6. When the user edits header `reimbursableExpense`, save the header first. If the sheet has existing lines, ask whether to update all line reimbursable values. If accepted, call `reimbursable-expense/propagate`.
7. After any propagation, refresh the detail with `GET /api/crm/expensesheets/{hojaGastosId}`.
8. When a line changes `currencyCode` to a value different from the header, require/send a valid line `exchRate` or `amountMST`. The backend only reuses the header exchange rate when the line currency still matches the header currency.
9. When a line changes `projId` or `ProjIdHornos` away from the header, expect the next refresh to show the header project mixed marker.
10. When a line changes `reimbursableExpense` away from the header, expect the next refresh to show header `reimbursableExpense = 2`.

## UX Requirements

- Do not auto-propagate after header save. Propagation must be an explicit confirmed action.
- Make the confirmation text field-specific:
  - Currency: explain that line currency, exchange rate, and optionally `amountMST` will be recalculated.
  - Currency with `force=true`: explain that current multi-currency line values will be overwritten.
  - Project: explain that all line projects and project assignment details will be changed to the header project.
  - Reimbursable: explain that all line reimbursable values will be changed to the header value.
- If backend rejects because the header contains a mixed marker, ask the user to choose a real header value before propagating.
- Keep existing frontend services, API client wrappers, and state management patterns. Do not create a second API client.

## Verification

- Verify the frontend sends the correct query params for currency propagation, especially `force`.
- Verify each propagation refreshes the detail and updates visible line values.
- Verify validation errors from the backend are shown as actionable messages.
- Verify existing create/edit line flows still send `currencyCode`, `amountMST`, `exchRate`, `projId`, and `reimbursableExpense` as currently supported.
