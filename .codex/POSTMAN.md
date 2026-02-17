# Postman project (actualizado 2026-02-17)

Archivos
- Collection: .codex/Postman/IND_CRM_API V16.postman_collection.json
- Collection exportable (versionable): Notes/IND_CRM_API V16.postman_collection.json

Variables globales (collection variables)
- baseUrl: URL base compartida (por defecto https://crm.insertec.biz:7776)
- username: usuario para /api/auth/login
- password: clave para /api/auth/login
- tokenId: se llena automaticamente desde /api/auth/login
- companyId: definir manualmente o tomar desde /api/auth/entra/context
- axUserId: se llena automaticamente desde /api/auth/entra/context (Header.AxUserId)
- defaultCurrencyCode: se llena automaticamente desde /api/auth/entra/context (Header.DefaultCurrencyCode o Companies[].CurrencyCode)
- entraOid: GUID de Entra requerido por /api/auth/entra/context
- appCode: codigo de app para /api/auth/entra/context (por defecto CRM)

Base URL
- {{baseUrl}} (usada en todas las requests de la collection)

Uso rapido
1) Importa la collection en Postman.
2) Revisa o ajusta baseUrl si corresponde.
3) Configura username y password.
4) Configura entraOid.
5) Ejecuta Auth/Login y verifica que tokenId se actualice.
6) Login ejecuta automaticamente una llamada interna a /api/auth/entra/context y rellena companyId + axUserId.
7) Entra Context tambien rellena defaultCurrencyCode cuando viene en el contexto.
8) Si quieres validar manualmente el contexto, ejecuta Auth/Entra Context.
9) Ejecuta los endpoints CRM con Authorization, X-IND-Company y X-IND-AxUserId.

Notas
- Los endpoints CRM ya incluyen el header X-IND-Company apuntando a {{companyId}}.
- Los endpoints que requieren usuario incluyen X-IND-AxUserId apuntando a {{axUserId}}.
- El listado de proyectos usa page y pageSize en query.
- El listado de hojas de gastos usa POST con body y soporta filtros opcionales: createdDateFrom, createdDateTo, projId, currencyCode.
- Create/Update de hojas de gastos ahora incluyen los campos opcionales: expenseSheetStatus y exchangeRateMode.
- Get de hoja de gastos incluye expenseSheetStatus, exchangeRateMode y createdDate.
- List de hojas de gastos incluye expenseSheetStatus, exchangeRateMode, userId, exchRate y createdDate.
- Expense Sheets incluye GET /api/crm/expensesheets/currencies para obtener CurrencyCode y CurrencyCodeISO.
- System incluye GET /api/system/exchange-rate con query baseCurrency, targetCurrency y date opcional.
- Los endpoints protegidos usan Authorization: Bearer {{tokenId}}.
