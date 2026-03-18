# Expense Sheets AI Ask Design

Date: 2026-03-17

## Goal

Add a first AI endpoint for expense sheet list questions while keeping the internal design reusable for future modules.

## Public Endpoint

- Route: `POST /api/ia/service/expensesheets/ask`
- Auth: `Authorize`
- Required headers:
  - `X-IND-Company`
  - `X-IND-AxUserId`
- Body:
  - `question`
  - `answerInstructions` (optional)
  - `listRequest` compatible with `POST /api/crm/expensesheets/list`
  - `sourceJson` (optional) with the full list response JSON or a direct array of records

## Main Rules

- The endpoint reuses the same list filters as the expense sheet list screen.
- `page` and `pageSize` are accepted for compatibility but ignored by the AI endpoint.
- The endpoint can analyze a captured `sourceJson` payload directly from Postman or the UI.
- If `sourceJson` is not provided, the endpoint loads the full filtered dataset server-side.
- Dates keep the same accepted input formats: `DDMMYYYY` and `DD.MM.YYYY`.
- Response dates echoed in `filtersApplied` use `DD.MM.YYYY`.

## Internal Design

### Dataset Provider

`ExpenseSheetAiDatasetProvider`

- Calls AX method `INDCRMExpenseSheetService.getExpenseSheetsList`
- Applies the same functional filters as the list endpoint
- Maps all returned rows to compact JSON records for AI consumption

### AI Answer Service

`IND_OpenAiDatasetAnswerService`

- Uses OpenAI Responses API
- Default model: `gpt-5-mini`
- Two execution modes:
  - `direct`: send all filtered records when the dataset is below the configured threshold
  - `chunked`: split the dataset into record chunks, summarize each chunk, then build a final answer

## Why Chunking Exists

Chunking prevents:

- very large prompts
- avoidable token cost spikes
- timeout risk on large filtered lists

It is only used when the filtered result set exceeds the direct threshold.

## Reuse Path

The design is reusable because future modules only need:

1. a dataset provider that loads and normalizes records
2. the shared AI answer service
3. a thin controller endpoint

Candidate follow-ups:

- tickets AI ask
- projects AI ask
- generic dataset ask endpoint based on source keys

## Validation and Safety

- Input validation uses the standard API envelope
- New route is protected by the existing OpenAI rate limit handler
- OpenAI requests use structured JSON output
- OpenAI requests set `store=false`
