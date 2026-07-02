# Prompt para IND_CRM_APP - Mostrar propietario CRM en detalle de hoja de gastos

## Proyecto destino

`C:\INDProjects\IND_CRM_APP`

## Objetivo

Actualizar el contrato y la UI del detalle de hoja de gastos para consumir el nuevo campo `UserName` devuelto por `IND_CRM_API` en:

`GET /api/crm/expensesheets/{hojaGastosId}`

La API mantiene todos los campos anteriores y agrega `UserName` como campo adicional del item de detalle. No se cambia la ruta, headers ni el envelope.

## Contrato backend actualizado

El item de `Items[]` sigue devolviendo `UserId` como propietario CRM de la hoja y ahora tambien devuelve:

```json
{
  "UserId": "P00044",
  "UserName": "Nombre Apellido"
}
```

Origen backend:

- `UserId`: `CRMHojaGastosTable.UserId`
- `UserName`: `CRMUsuarioTable.Name` del mismo `UserId`

Compatibilidad:

- Si el frontend consume una API anterior, `UserName` puede venir ausente, `null` o vacio.
- No usar `UserName` para autorizacion. Es un campo solo visual.

## Cambios esperados en frontend

1. Localiza el DTO/modelo de detalle usado por `GET /api/crm/expensesheets/{hojaGastosId}`.
   - Archivos localizados en el proyecto:
     - `App/Models/CRM/ExpenseSheetDetailDto.cs`
     - `App/Services/ApiClientService.cs`
     - `App/Services/ICrmApiClient.cs`
     - `App/Services/ApiHelpers/ApiRoutes.cs`
     - `Program.cs` con ruta proxy `api/crm/expensesheets/{hojaGastosId}`
     - React: `wwwroot/react/src/context/AuthContext.tsx` ya expone `currentCrmUserId`.
   - El DTO C# actual guarda muchos campos en `Extra`; si el frontend React lee desde `Extra`, puedes mapear `userName` desde ahi o tiparlo explicitamente en el modelo usado por la pantalla.

2. Agrega el nuevo campo opcional:

```ts
userName?: string | null;
```

o equivalente segun el patron del proyecto (`UserName`/`userName`). Respeta la convencion actual de casing que ya use el cliente API.

3. En la pantalla de detalle de hoja de gastos, agrega un campo de solo lectura al principio de los datos de cabecera.

Label sugerida:

`Usuario propietario`

Valor visual:

```ts
const ownerDisplay = [detail.userId, detail.userName].filter(Boolean).join(' ');
```

Si `UserName` no viene informado, mostrar solo `UserId`.

4. Visibilidad del campo:

Mostrarlo solo cuando la hoja no se esta viendo por su propietario, es decir, cuando el usuario actual CRM sea distinto del `UserId` de la hoja.

Regla recomendada:

```ts
const currentCrmUserId = /* CRM user id del contexto actual/empresa actual */;
const ownerCrmUserId = detail.userId;
const showOwnerField =
  !!currentCrmUserId &&
  !!ownerCrmUserId &&
  currentCrmUserId.toLowerCase() !== ownerCrmUserId.toLowerCase();
```

Si no se puede resolver `currentCrmUserId`, ocultar el campo por defecto.

5. El campo debe ser solo lectura.

No enviarlo en `PUT /api/crm/expensesheets/{hojaGastosId}` ni permitir edicion. El update de cabecera no necesita cambiar.

## Validaciones

- Probar una hoja propia: el campo `Usuario propietario` no debe verse.
- Probar una hoja de subordinado/jefatura: el campo debe verse al comienzo de la cabecera con formato `UserId Name`.
- Probar contra API antigua o respuesta sin `UserName`: la pantalla no debe romper.
- Confirmar que guardar la hoja no envia `UserName` en el body.

## No hacer

- No cambiar rutas ni headers.
- No alterar permisos ni reglas de acceso con este campo.
- No mover otros campos ni redisenar el detalle fuera del ajuste pedido.
