# Diagramas funcionales

Explican lo que experimenta una persona usuaria del CRM sin exponer nombres de
controladores, clases DTO, llamadas COM ni otros detalles internos. Cada flujo
indica al principio su fuente técnica; ambos deben actualizarse juntos.

El mapa funcional para BI es una excepción deliberada: muestra nombres de
tablas AX porque su objetivo es facilitar la trazabilidad entre conceptos de
negocio y fuentes de datos. Fuera de ese mapa, los diagramas funcionales evitan
identificadores técnicos internos.

| Documento funcional | Fuente Mermaid | Fuente técnica |
| --- | --- | --- |
| [expense-sheets.md](expenses/expense-sheets.md) | [expense-sheets.mmd](expenses/expense-sheets.mmd) | [expense-sheets-sequence.md](../technical/expenses/expense-sheets-sequence.md) |
| [expense-sheet-line-create-edit-flow.md](expenses/expense-sheet-line-create-edit-flow.md) | [expense-sheet-line-create-edit-flow.mmd](expenses/expense-sheet-line-create-edit-flow.mmd) | [expense-sheet-line-create-edit-flow.md](../technical/expenses/expense-sheet-line-create-edit-flow.md) |
| [ax-bi-query-table-schema.md](integration/ax-bi-query-table-schema.md) | [ax-bi-query-table-schema.mmd](integration/ax-bi-query-table-schema.mmd) | [ax-bi-query-table-schema.md](../technical/integration/ax-bi-query-table-schema.md) |
| [tickets.md](tickets/tickets.md) | [tickets.mmd](tickets/tickets.mmd) | [tickets-sequence.md](../technical/tickets/tickets-sequence.md) |
| [ai-audio-transcription.md](ai/ai-audio-transcription.md) | [ai-audio-transcription.mmd](ai/ai-audio-transcription.mmd) | [ai-audio-transcription-sequence.md](../technical/ai/ai-audio-transcription-sequence.md) |
| [ticket-image-ai.md](ai/ticket-image-ai.md) | [ticket-image-ai.mmd](ai/ticket-image-ai.mmd) | [ticket-image-ai-sequence.md](../technical/ai/ticket-image-ai-sequence.md) |

## Reglas de redacción

- Describir el flujo desde el punto de vista de la persona usuaria.
- Emplear lenguaje de negocio y explicar cualquier término técnico inevitable.
- Mantener las mismas decisiones y pasos que en el diagrama técnico.
- Usar etiquetas cortas compatibles con los temas claro y oscuro de Mermaid.
- Marcar como `pendiente de validar` cualquier comportamiento sin evidencia.
