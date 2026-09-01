# User-level flow diagrams

This folder contains user-friendly versions of the architecture diagrams. They
explain what a CRM user or business stakeholder experiences without exposing
implementation details such as controller names, DTO classes, COM calls, or
service internals.

Each user-level diagram has a technical counterpart under
`docs/architecture/diagrams/technical`. When one side changes, update the
other side in the same work item.

## Index

| User-level document | User-level source | Technical source |
| --- | --- | --- |
| [expense-sheets.md](expenses/expense-sheets.md) | [expense-sheets.mmd](expenses/expense-sheets.mmd) | [expense-sheets-sequence.md](../technical/expenses/expense-sheets-sequence.md) |
| [expense-sheet-line-create-edit-flow.md](expenses/expense-sheet-line-create-edit-flow.md) | [expense-sheet-line-create-edit-flow.mmd](expenses/expense-sheet-line-create-edit-flow.mmd) | [expense-sheet-line-create-edit-flow.md](../technical/expenses/expense-sheet-line-create-edit-flow.md) |
| [ax-bi-query-table-schema.md](integration/ax-bi-query-table-schema.md) | [ax-bi-query-table-schema.mmd](integration/ax-bi-query-table-schema.mmd) | [ax-bi-query-table-schema.md](../technical/integration/ax-bi-query-table-schema.md) |
| [tickets.md](tickets/tickets.md) | [tickets.mmd](tickets/tickets.mmd) | [tickets-sequence.md](../technical/tickets/tickets-sequence.md) |
| [ai-audio-transcription.md](ai/ai-audio-transcription.md) | [ai-audio-transcription.mmd](ai/ai-audio-transcription.mmd) | [ai-audio-transcription-sequence.md](../technical/ai/ai-audio-transcription-sequence.md) |
| [ticket-image-ai.md](ai/ticket-image-ai.md) | [ticket-image-ai.mmd](ai/ticket-image-ai.mmd) | [ticket-image-ai-sequence.md](../technical/ai/ticket-image-ai-sequence.md) |

## Writing rules

- Explain the flow from the user's point of view.
- Use business terms: user, screen, company, ticket, image, audio, review,
  save, approve, error.
- Avoid implementation terms unless they are visible to the user.
- Keep the same business steps and decisions as the technical diagram.
- Explain unavoidable technical terms in parentheses.
- Keep labels short so Mermaid renders correctly in light and dark themes.
- If a behavior is not proven from code, write `pendiente de validar`.
