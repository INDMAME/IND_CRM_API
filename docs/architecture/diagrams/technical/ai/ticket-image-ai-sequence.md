# Ticket image AI sequence

This diagram documents the full ticket image flow used by the quick-create
endpoint. It includes image persistence, Azure Document Intelligence receipt
analysis, OpenAI normalization, mapping into the existing ticket contract, and
Axapta persistence.

```mermaid
sequenceDiagram
  autonumber
  participant Ui as React ticket UI
  participant Proxy as MVC ticket proxy
  participant Limit as OpenAI rate limit handler
  participant Api as CrmExpenseSheetTicketsController
  participant Guard as Base CRM guards
  participant Ax as Axapta through COM
  participant BlobSvc as Ticket blob storage service
  participant Blob as Azure Blob Storage
  participant Pipe as Ticket AI processing service
  participant Ocr as Azure Document Intelligence
  participant Norm as OpenAI ticket normalizer
  participant OpenAI as OpenAI Responses API

  Ui->>Proxy: POST /api/crm/expensesheets/tickets/quick-create<br/>multipart image + metadata
  Proxy->>Limit: Forward request with auth and context
  Limit->>Limit: Check per-user AI rate<br/>and concurrency

  alt Rate or concurrency limit exceeded
    Limit-->>Proxy: 429 IndApiResponse<br/>AI limit error
    Proxy-->>Ui: Retry or show limit message
  else Request allowed
    Limit->>Api: Continue to quick-create
    Api->>Guard: Validate JWT, company,<br/>AX user, and CRM context
    Guard-->>Api: Request allowed
    Api->>Api: Read multipart and validate image<br/>extension, content type, 50 MB max
    Api->>Ax: createExpenseSheetTicket<br/>header-only provisional ticket
    Ax-->>Api: fileId and provisional ticket data

    Api->>BlobSvc: UploadTicketFile(company, axUser,<br/>fileId, finalFileName, image)
    BlobSvc->>Blob: Store original ticket image
    Blob-->>BlobSvc: blobUrl and blobName
    BlobSvc-->>Api: Upload result
    Api->>Ax: updateExpenseSheetTicket<br/>sync urlFile and fileName
    Ax-->>Api: File metadata persisted

    Api->>Pipe: ProcessFromStoredBlobAsync(blobUrl,<br/>fileName, QuickCreate profile)
    Pipe->>BlobSvc: CreateReadOnlyBlobUrl(blobUrl)
    BlobSvc-->>Pipe: Short-lived read URL
    Pipe->>Ocr: Analyze receipt from blob URL<br/>urlSource payload
    Ocr-->>Pipe: AzureReceiptAnalysisResult<br/>RawJson + PromptJson + fields
    Pipe->>Norm: NormalizeReceiptAsync(ocr result,<br/>fileName, profile)
    Norm->>OpenAI: Responses API request<br/>prompt + compact OCR JSON
    OpenAI-->>Norm: Structured draft JSON
    Norm-->>Pipe: ExpenseSheetDraftResponse<br/>normalizedJson + attempts
    Pipe-->>Api: Draft + ocrJson + normalizedJson

    Api->>Api: Map draft to UpdateExpenseSheetTicketFromIARequest<br/>header, lines, ocrJson, normalizedJson

    alt Valid ticket lines
      Api->>Ax: updateExpenseSheetTicket<br/>replace header and lines from AI
      Ax-->>Api: processedByAI, fileName, line ids
      Api->>Api: completedStage = ticket-finalized
    else Header-only fallback
      Api->>Ax: updateExpenseSheetTicket<br/>header and DocuRef JSON only
      Ax-->>Api: processedByAI and fileName
      Api->>Api: Return created ticket for manual review
    end

    opt Existing expense sheet was supplied
      Api->>Ax: getExpenseSheetTicket
      Ax-->>Api: Final ticket detail
      Api->>Ax: createExpenseSheet mode 2<br/>link ticket line to existing sheet
      Ax-->>Api: Link result
      Api->>Api: completedStage = sheet-linked
    end

    Api-->>Proxy: 201 IndApiResponse(QuickCreateResult)<br/>fileId, urlFile, fileName,<br/>processedByAI, linkedToSheet,<br/>completedStage, stepTraceIds
    Proxy-->>Ui: Created ticket result
  end

  Note over Api,Pipe: Draft-only endpoint:<br/>/api/ia/service/expensefromticket<br/>uses the same AI pipeline.<br/>It calls ProcessFromImageAsync,<br/>writes a temporary blob,<br/>deletes it in cleanup,<br/>and can persist a ticket only<br/>when persistTicket=true.
```

## Observed contracts

Primary creation endpoint:

- `POST /api/crm/expensesheets/tickets/quick-create`
- Input: multipart ticket image and optional metadata.
- Required business headers: `Authorization`, `X-IND-Company`,
  `X-IND-AxUserId`, and CRM context headers.
- Success envelope: `IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>`.
- Result fields observed: `FileId`, `UrlFile`, `FileName`,
  `ProcessedByAI`, `LinkedToSheet`, `HojaGastosId`, `CompletedStage`,
  `StepTraceIds`.

Draft-only endpoint using the same AI pipeline:

- `POST /api/ia/service/expensefromticket`
- Input: `ticketImage`, optional `persistTicket`, optional `ticketUrlFile` or
  `urlFile`.
- Success envelope: `IndApiResponse<ExpenseSheetDraftResponse>`.
- Draft fields inherit the expense-sheet creation shape and add `gastoType`,
  `transDate`, `Confidence`, `Warnings`, `RawCurrency`, `Merchant`, and
  optional `TicketCreation`.

## Internal contract mapping

Azure Document Intelligence returns a structured receipt result represented by
`AzureReceiptAnalysisResult`. The relevant server-side fields are:

- `RawJson`: original OCR payload kept for audit/persistence.
- `PromptJson`: compact OCR JSON passed to OpenAI.
- `MerchantName`, `TransactionDate`, `CurrencyCode`, `RawCurrency`,
  `TotalAmount`, `ItemCount`, `Warnings`, and `CurrencyHints`.

OpenAI normalization maps that OCR JSON into `ExpenseSheetDraftResponse` and a
server-side `normalizedJson` string. Quick-create then maps the draft into
`UpdateExpenseSheetTicketFromIARequest` with header fields, ticket lines,
`ocrJson`, `normalizedJson`, file URL, file name, and file extension.

## Side effects

- Quick-create creates a provisional Axapta ticket before image upload.
- The ticket image is stored in Azure Blob and then synced back to Axapta
  ticket metadata.
- A short-lived read URL is generated for Azure Document Intelligence.
- OpenAI receives compact OCR JSON, not the original browser upload.
- The final Axapta update may replace ticket lines or fall back to header-only
  update when lines are invalid.
- If an existing expense sheet id is supplied, the ticket can be linked to that
  sheet by adding a line through Axapta.

## Pending validation

- Exact multipart field list for quick-create should be validated against the
  latest React caller and Postman collection before publishing as a public
  contract.
- Exact Axapta container indices remain an AOT/X++ contract detail and are not
  duplicated here.
