# Autenticación y contexto de empresa

## Objetivo

Separar la cuenta técnica que abre Axapta de la identidad funcional del usuario y evitar que dos sesiones compartan empresas o permisos.

## Identidades distintas

- `APIAX` autentica el canal técnico y abre el Business Connector. No representa al usuario que navega.
- El usuario real se identifica por `tenantId + entraOid`.
- El usuario AX funcional, sus empresas y permisos se guardan en una instantánea temporal propia mediante `UserCompanyAccessCache`.
- La empresa activa pertenece a la sesión web del usuario y debe estar incluida en esa instantánea.

## Flujo vigente

1. APP solicita `/api/auth/entra/context` para el OID autenticado.
2. API consulta AX con la cuenta técnica y construye una instantánea con usuario AX, empresa predeterminada, empresas permitidas, versión y revisión de permisos.
3. `UserContextTokenService` firma un token de contexto asociado a esa instantánea.
4. En cada petición CRM dependiente de empresa, APP envía:
   - `X-IND-Company`;
   - `X-IND-EntraOid`;
   - `X-IND-Context-Version`;
   - `X-IND-Permissions-Revision`;
   - `X-IND-Context-Token`.
5. `BaseCrmController` valida firma, expiración, identidad, versión, revisión y pertenencia de la empresa.
6. El actor AX autorizado se obtiene de la instantánea firmada cuando el endpoint lo exige.

## Papel de `X-IND-AxUserId`

Esta cabecera continúa en contratos heredados y en operaciones donde AX necesita un usuario o propietario concreto. No debe tratarse como prueba de la identidad del solicitante.

En particular, el detalle de una hoja obtiene al usuario que consulta con `RequireValidatedSnapshotAxUserIdOrReturn403`. Así, modificar `X-IND-AxUserId` en el navegador no permite leer una hoja ajena. Los endpoints heredados que aún llaman a `RequireAxUserIdOrReturn422` deben conservarse compatibles y migrarse de forma explícita, no mediante una regla documental ficticia.

## Errores y recuperación

| Código | Significado | Respuesta esperada de APP |
|---|---|---|
| `AUTH_CONTEXT_REQUIRED` | Falta contexto firmado. | Refrescar contexto y reintentar una vez. |
| `AUTH_CONTEXT_STALE` | Instantánea o token desactualizado o caducado. | Renovar el contexto y reintentar una vez. |
| `AUTH_FORBIDDEN` | La empresa u operación no está permitida. | No elevar permisos; mostrar el rechazo. |

La renovación silenciosa mejora la continuidad, pero no transforma un permiso denegado en autorizado.

## Invariantes de seguridad

- Ninguna caché se indexa solo por `APIAX` ni se comparte entre OID distintos.
- La empresa solicitada se valida en cada petición contra el contexto firmado.
- La cuenta técnica y el usuario funcional nunca se intercambian.
- Una cabecera controlable por el cliente no decide por sí sola la autorización o la visibilidad.
- Los tokens y las revisiones no se registran completos ni se incluyen en la documentación.
- No se necesita Redis para este aislamiento; añadir un almacén distribuido no sustituiría estas comprobaciones.

## Validación mínima al modificar el flujo

- Dos usuarios simultáneos con empresas diferentes no contaminan sus instantáneas.
- El cambio de empresa, el cierre de sesión, el nuevo inicio de sesión, la expiración y la renovación conservan el aislamiento.
- Un OID, empresa, versión, revisión o token manipulados se rechazan.
- El detalle y las mutaciones de Gastos se prueban con usuario normal, autogestión, responsable y subordinado.
- La cuenta `APIAX` puede seguir abriendo AX sin convertirse en identidad funcional.
