# Postman project (actualizado 2026-01-12)

Archivos
- Collection: .codex/postman/IND_CRM_API.postman_collection.json

Variables globales (collection variables)
- baseUrl: URL base compartida (por defecto https://crm.insertec.biz:7776)
- tokenId: se llena automaticamente desde /api/auth/login
- companyId: definir manualmente o tomar desde /api/auth/entra/context
- axUserId: se llena automaticamente desde /api/auth/entra/context (Header.AxUserId)

Base URL
- {{baseUrl}} (usada en todas las requests de la collection)

Uso rapido
1) Importa la collection en Postman.
2) Revisa o ajusta baseUrl si corresponde.
3) Ejecuta Auth/Login y verifica que tokenId se actualice.
4) Ejecuta Auth/Entra Context para llenar companyId y axUserId.
5) Ejecuta los endpoints CRM con Authorization, X-IND-Company y X-IND-AxUserId.

Notas
- Los endpoints CRM ya incluyen el header X-IND-Company apuntando a {{companyId}}.
- Los endpoints que requieren usuario incluyen X-IND-AxUserId apuntando a {{axUserId}}.
- Los endpoints protegidos usan Authorization: Bearer {{tokenId}}.
