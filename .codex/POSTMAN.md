# IND_CRM_API Postman

Colecciones
- Principal: `.codex/postman/IND_CRM_API V19.postman_collection.json`
- Soporte: `Notes/IND_CRM_API V19.postman_collection.json`

Ambiente (variables sugeridas)
- `baseUrl` = `https://crm.insertec.biz:7776`
- `tokenId` = token JWT vigente
- `companyId` = compania obtenida desde Entra Context
- `axUserId` = usuario AX obtenido desde Entra Context

Notas
- Todos los endpoints protegidos usan `Authorization: Bearer {{tokenId}}`.
- Endpoints CRM usan `X-IND-Company: {{companyId}}`.
- Endpoints CRM que envian userId a AX usan `X-IND-AxUserId: {{axUserId}}`.
- `POST /api/auth/entra/context` retorna `defaultCurrencyCode`, companias y `allowSelfManagement`.
- Expense Sheets crea/actualiza lineas con `price` y AX calcula `amount` internamente.
- `PUT /api/crm/expensesheets/{hojaGastosId}` admite `estadoComentarios` en body (posicion AX `_data[10]`), y cuando se envia requiere `expenseSheetStatus` + `exchangeRateMode`.
- `GET /api/crm/expensesheets/{hojaGastosId}` y `POST /api/crm/expensesheets/list` retornan `estadoComentarios` en cabecera/listado.
- Delete de linea soporta `deleteMode` (0 LineOnly, 1 HeaderOnly alias de WholeSheet, 2 WholeSheet) y conserva `deleteWholeSheet` como legado.
- V19 recupera modulos CRM de cuentas, actividades, visitas y template.
- V19 agrega `GET /api/crm/expensesheets/subordinates`.
