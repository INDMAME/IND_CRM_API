# Prompt frontend - Visitas con jerarquia de visibilidad

Fecha: 2026-06-09
Proyecto origen API: `C:\INDProjects\IND_CRM_API`
Coleccion Postman DEV: `.codex/postman/DEV/IND_CRM_API_DEV.postman_collection.json`

## Instruccion principal para el agente/frontend

No modificar paginas de Notion en esta implementacion. Notion debe quedar solo como fuente de contexto y consulta. Antes de tocar codigo frontend, preparar un plan detallado y ejecutable, separado por archivos/componentes/hooks/servicios, para aplicar la jerarquia solo al modulo de visitas.

Referencias Notion de contexto:
- Jerarquia de permisos a nivel registro: `https://app.notion.com/p/373d344f518880c9be1ae6e5885615f5`
- Inventario tecnico - Puntos de ajuste para visibilidad por jerarquia: `https://app.notion.com/p/374d344f5188814f9cd1f6be8c8e1965`
- Paso a paso tecnico - Implementacion de visibilidad por modulo en Axapta y API: `https://app.notion.com/p/374d344f518881d2ac15d8806287f33b`
- Propuesta tecnica funcional - Capa simple de visibilidad por modulo: `https://app.notion.com/p/373d344f5188817b80bedc5b0a2cd319`

## Objetivo funcional

Implementar en el frontend CRM el filtro de visitas por jerarquia usando la nueva capa `INDControlDataVisibility`.

El usuario debe poder:
- Ver sus propias visitas cuando no tenga jerarquia o permisos ampliados.
- Ver todas las visitas de los usuarios visibles por su jerarquia/configuracion cuando el backend lo permita.
- Filtrar explicitamente por un usuario visible concreto, con una experiencia similar al filtro de subordinados de Hojas de gastos.

Importante: esta adaptacion aplica solo a visitas. No modificar Hojas de gastos, tickets ni la logica legacy de subordinados.

## Contexto tecnico ya implementado en API/AX

La API CRM ya expone:

```http
GET /api/crm/data-visibility/visible-users?appCode=CRM&moduleCode=VISITAS_GESTION&includeCrmUserId=true
```

Headers requeridos:

```http
Authorization: Bearer {{tokenId}}
X-IND-Company: {{companyId}}
X-IND-AxUserId: {{axUserId}}
X-IND-EntraOid: {{entraOid}}
X-IND-Context-Version: {{contextVersion}}
X-IND-Permissions-Revision: {{permissionsRevision}}
X-IND-Context-Token: {{contextToken}}
```

Respuesta esperada:

```json
{
  "Success": true,
  "Message": "OK",
  "Total": 2,
  "Items": [
    {
      "Alias": "MMEZA",
      "AxUserId": "MMEZA",
      "CrmUserId": "000123",
      "Name": "Marco Meza",
      "Source": "INDControlDataVisibility"
    }
  ],
  "TraceId": "..."
}
```

Reglas clave:
- La lista solo devuelve personas visibles que tienen `INDPersonaTable.UserId` resoluble.
- Personas configuradas en jerarquia pero sin usuario AX no deben aparecer en el filtro.
- No usar `GET /api/crm/expensesheets/subordinates` para visitas.
- No usar `CRMUsuarioSubordinadoTable` en frontend para visitas.

El listado de visitas ya acepta filtro opcional:

```http
POST /api/crm/activities/list
```

Body base:

```json
{
  "fromDate": "20200101",
  "toDate": "20991231",
  "page": 1,
  "pageSize": 50,
  "accountNum": ""
}
```

Body con usuario visible explicito:

```json
{
  "fromDate": "20200101",
  "toDate": "20991231",
  "ownerAxUserId": "MMEZA",
  "page": 1,
  "pageSize": 50,
  "accountNum": ""
}
```

Regla de uso:
- Para ver todas las visitas visibles por jerarquia: no enviar `ownerAxUserId` o enviarlo vacio.
- Para filtrar por un usuario concreto: enviar `ownerAxUserId` con un valor devuelto por `visible-users`.
- Si se envia un `ownerAxUserId` que no esta dentro del set visible, AX no debe ampliar visibilidad.

## UX esperada en visitas

Agregar un filtro de propietario visible en el listado de visitas.

Opcion recomendada:
- Selector `Usuario` o `Propietario`.
- Primera opcion: `Todos los usuarios visibles`.
- Resto de opciones: usuarios devueltos por `visible-users`, mostrando `Name` y, si aporta claridad, `AxUserId`.

Comportamiento:
- Al entrar al listado, cargar `visible-users` para `CRM / VISITAS_GESTION`.
- Si `Total <= 1`, se puede ocultar el selector o dejarlo deshabilitado mostrando el usuario actual.
- Si el usuario elige `Todos los usuarios visibles`, llamar `activities/list` sin `ownerAxUserId`.
- Si el usuario elige una persona concreta, llamar `activities/list` con `ownerAxUserId`.
- Al cambiar empresa, usuario/contexto, fechas o filtros, invalidar/recalcular la lista de usuarios visibles.
- Mantener paginacion y filtros actuales de visitas.
- Mantener permisos visuales actuales por `AccessRights`; la jerarquia solo afecta alcance de datos, no debe conceder acciones nuevas por si sola.

## Plan tecnico solicitado para frontend

Antes de implementar, entregar un plan detallado con:

1. Archivos/componentes actuales que renderizan el listado de visitas.
2. Servicio/API client donde se implementara `getVisibleVisitUsers`.
3. Tipo/DTO frontend para usuarios visibles:

```ts
type DataVisibilityVisibleUser = {
  alias: string;
  axUserId: string;
  crmUserId?: string;
  name: string;
  source: string;
};
```

4. Estado nuevo necesario:
- `visibleVisitUsers`
- `selectedOwnerAxUserId`
- `visibleUsersLoading`
- `visibleUsersError`

5. Cambios en el request de listado de visitas:
- agregar `ownerAxUserId?: string`
- omitir el campo cuando se selecciona `Todos los usuarios visibles`

6. Estrategia de carga:
- cargar usuarios visibles despues de tener contexto valido (`companyId`, `axUserId`, token/context headers).
- cachear por `companyId + axUserId + permissionsRevision + moduleCode`.
- refrescar si cambia `permissionsRevision`.

7. Estados UI:
- loading del selector
- error no bloqueante del selector
- lista vacia
- un solo usuario visible
- multiples usuarios visibles

8. Pruebas:
- usuario sin jerarquia: solo ve propias visitas.
- jefe con jerarquia: ve todas las visitas visibles cuando selecciona `Todos`.
- jefe selecciona un subordinado: listado reducido a ese usuario.
- persona sin usuario AX no aparece en selector.
- cambio de empresa refresca usuarios visibles.
- manipular `ownerAxUserId` desde cliente no amplia visibilidad.

## Flujo de validacion con Postman antes/despues del frontend

Importar:

```plain text
C:\INDProjects\IND_CRM_API\.codex\postman\DEV\IND_CRM_API_DEV.postman_collection.json
```

Ejecutar:

1. `Auth / Login`
2. `Auth / Entra Context`
3. `CRM Data Visibility / Get Visible Users - Visits`
4. `CRM Activities / List Activities (POST)`

La request `Get Visible Users - Visits` guarda:
- `visibleOwnerAxUserId`
- `visibleOwnerAlias`
- `visibleUsersTotal`

La request `List Activities (POST)` ya incluye:

```json
"ownerAxUserId": "{{visibleOwnerAxUserId}}"
```

Para probar "Todos los usuarios visibles", quitar temporalmente `ownerAxUserId` del body o dejarlo vacio.

## Criterios de aceptacion

- El frontend no llama endpoints legacy de subordinados para visitas.
- El filtro de visitas usa `GET /api/crm/data-visibility/visible-users`.
- El selector no muestra personas sin `AxUserId`.
- `Todos los usuarios visibles` lista visitas de todo el set permitido por AX.
- Seleccionar un usuario visible envia `ownerAxUserId`.
- Cambiar empresa o contexto refresca usuarios visibles.
- El comportamiento de Hojas de gastos no cambia.
- La implementacion no modifica paginas Notion.

## Riesgos y decisiones

- El backend es la autoridad de seguridad. El frontend solo ofrece filtros y ergonomia.
- `ownerAxUserId` nunca debe construirse desde texto libre; debe venir de `visible-users`.
- Si `visible-users` falla, no bloquear toda la pantalla: mantener listado sin `ownerAxUserId` o mostrar mensaje segun patron actual del frontend.
- No usar `CrmUserId` como identidad principal para visitas; se mantiene solo como dato compatible.
- La migracion de Hojas de gastos a `INDControlDataVisibility` queda fuera de este alcance.
