# IND_CRM_API Postman

Colecciones
- Principal: `.codex/postman/IND_CRM_API V21.postman_collection.json`
- Soporte: `Notes/IND_CRM_API V21.postman_collection.json`

Ambiente (variables sugeridas)
- `baseUrl` = `https://crm.insertec.biz:7776`
- `tokenId` = token JWT vigente
- `companyId` = compania obtenida desde Entra Context
- `axUserId` = usuario AX obtenido desde Entra Context
- `fileId` = se autocompleta desde respuestas de tickets
- `lineRecId` = se autocompleta desde respuestas de lineas de tickets
- `ticketImagePath` = ruta local para pruebas de upload en multipart (opcional)

Notas
- Todos los endpoints protegidos usan `Authorization: Bearer {{tokenId}}`.
- Endpoints CRM usan `X-IND-Company: {{companyId}}`.
- Endpoints CRM que envian userId a AX usan `X-IND-AxUserId: {{axUserId}}`.
- `POST /api/auth/entra/context` retorna `defaultCurrencyCode`, companias y `allowSelfManagement`.
- Expense Sheets usa `lines[].fileId` (INDFileId) en lugar de `lines[].ticket`.
- `PUT /api/crm/expensesheets/{hojaGastosId}` admite `estadoComentarios` en body (posicion AX `_data[10]`), y cuando se envia requiere `expenseSheetStatus` + `exchangeRateMode`.
- Delete de linea soporta `deleteMode` (0 LineOnly, 1 HeaderOnly alias de WholeSheet, 2 WholeSheet) y conserva `deleteWholeSheet` como legado.
- La coleccion V21 incluye CRUD completo de tickets + endpoints de archivo:
  - `POST /api/crm/expensesheets/tickets/{fileId}/file`
  - `DELETE /api/crm/expensesheets/tickets/{fileId}/file`
- `POST /api/ia/service/expensefromticket` soporta `persistTicket` y `ticketUrlFile` para persistir ticket en AX desde IA.
