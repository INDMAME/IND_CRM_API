# Esquema AX de usuarios CRM y hojas de gastos para BI

Este diagrama detalla los recuadros de personas, usuarios CRM, jerarquia de
aprobacion, cabeceras, lineas y tickets. Los campos mostrados son una seleccion
de claves, dimensiones y medidas principales con el nombre exacto usado por
los XPO. `DATAAREAID`, `RecId`, `CreatedDate` y `ModifiedDate` son columnas de
sistema AX habilitadas por las propiedades de cada tabla.

Vista conjunta:
[esquema AX para BI](ax-bi-query-table-schema.md).

Contraparte funcional conjunta:
[mapa funcional para BI](../../user/integration/ax-bi-query-table-schema.md).

```mermaid
classDiagram
direction LR

class SysUserInfo {
  <<sistema AX>>
  +Id
  +Language
}

class INDPersonaTable {
  <<por empresa>>
  +DATAAREAID
  +RecId
  +Alias
  +Name
  +UserId
  +RefRecIdCRM
  +Email
  +Blocked
}

class CRMUsuarioTable {
  <<por empresa>>
  +DATAAREAID
  +RecId
  +UserId
  +Name
  +AxaptaUserId
  +Division
  +DepartamentoId
  +CategoriaId
  +Bloqueado
  +EmplId
  +Email
  +CocheEmpresa
  +VisaEmpresa
  +PriceKm_MEX
  +NoContabilizarGasolina
  +CreatedDate
  +ModifiedDate
}

class CRMUsuarioSubordinadoTable {
  <<por empresa>>
  +DATAAREAID
  +RecId
  +UserIdJefe
  +UserIdSubordinado
  +ExcluirAprobacionHojaGastos
  +INDRecIdMex
}

class CRMHojaGastosTable {
  <<por empresa>>
  +DATAAREAID
  +RecId
  +HojaGastosId
  +UserId
  +Division
  +FromDate
  +ToDate
  +Description
  +CurrencyCode
  +ExchRate
  +ExchangeRateMode
  +AmountAnticipo
  +CategoriaId
  +ProjId
  +ViajeId
  +Aprobado
  +ExpenseSheetStatus
  +ReimbursableExpense
  +Voucher
  +VoucherPhysical
  +DatePhysical
  +DateFinancial
  +TransDateAprobacion
  +NoContabilizarGasolina
  +MostrarParaPago
  +INDCreatedByUserId
  +CreatedDate
  +ModifiedDate
}

class CRMHojaGastosLine {
  <<por empresa>>
  +DATAAREAID
  +RecId
  +HojaGastosId
  +UserId
  +TransDate
  +Type
  +Description
  +Division
  +AccountNum
  +Internacional
  +Ticket
  +Qty
  +Price
  +Amount
  +Currency
  +ExchRate
  +AmountMST
  +ReimbursableExpense
  +ReimbursableAmount
  +NoCobrar
  +VisaEmpresa
  +ModalidadPago
  +TaxAmount
  +TaxPercent
  +AmountIVA
  +AmountRetencion
  +InvoiceId
  +VendName
  +ProjId
  +ProjIdHornos
  +ViajeId
  +FileId
  +INDCreatedByUserId
  +CreatedDate
  +ModifiedDate
}

class INDTicketInfoTable {
  <<por empresa, opcional>>
  +DATAAREAID
  +RecId
  +FileId
  +Description
  +Status
  +CurrencyCode
  +TotalAmount
  +AmountMST
  +ExchRate
  +GastoType
  +TicketDate
  +TicketTime
  +CreatedByUserId
  +ProcessedByAI
  +SearchKey
}

note for SysUserInfo "Usuario AX recibido por el servicio. Estructura completa pendiente de AOT."
note for INDPersonaTable "Puente por empresa entre usuario AX y referencia CRM."
note for CRMUsuarioTable "Empleado CRM y configuracion base usada al crear hojas."
note for CRMUsuarioSubordinadoTable "Jefes directos y exclusion de aprobacion. Sin FK ni indice unico compuesto."
note for CRMHojaGastosTable "Una cabecera por hoja. UserId es el propietario funcional."
note for CRMHojaGastosLine "Una linea de gasto. Es el grano monetario principal del BI."
note for INDTicketInfoTable "Ticket opcional relacionado por FileId."

SysUserInfo ..> INDPersonaTable : Id = UserId / DATAAREAID / resolucion X++
SysUserInfo ..> CRMUsuarioTable : Id = AxaptaUserId / DATAAREAID / fallback heredado
CRMUsuarioTable "1" --> "0..*" INDPersonaTable : RecId = RefRecIdCRM + DATAAREAID
CRMUsuarioTable "1" ..> "0..*" CRMUsuarioSubordinadoTable : DATAAREAID + UserIdJefe
CRMUsuarioTable "1" ..> "0..*" CRMUsuarioSubordinadoTable : DATAAREAID + UserIdSubordinado
CRMUsuarioTable "1" --> "0..*" CRMHojaGastosTable : DATAAREAID + UserId
CRMHojaGastosTable "1" --> "0..*" CRMHojaGastosLine : DATAAREAID + HojaGastosId + UserId
CRMHojaGastosLine "0..*" --> "0..1" INDTicketInfoTable : DATAAREAID + FileId
```

## Flujo de creacion confirmado

```text
AxUserId
  -> resolucion de CRMUsuarioTable.UserId dentro de DATAAREAID
  -> CRMHojaGastosTable.UserId
  -> initFromUsuarioTable
  -> Division, CategoriaId y NoContabilizarGasolina
  -> CRMHojaGastosLine con el mismo propietario CRM
```

`CRMHojaGastosTable.UserId` es el propietario funcional. El campo
`INDCreatedByUserId` conserva el usuario AX que creo el registro y no debe
usarse como sustituto del propietario.

## Claves y controles de calidad

| Tabla | Grano recomendado | Control necesario |
| --- | --- | --- |
| `CRMUsuarioTable` | `DATAAREAID + UserId` | `AxaptaUserId` admite duplicados y solo es un fallback heredado. |
| `CRMUsuarioSubordinadoTable` | `DATAAREAID + UserIdJefe + UserIdSubordinado` | El XPO no declara FK ni indice unico compuesto; `validateWrite` controla duplicados y ciclos en runtime. |
| `CRMHojaGastosTable` | `DATAAREAID + HojaGastosId` | Validar que `UserId` exista en `CRMUsuarioTable` de la misma empresa. |
| `CRMHojaGastosLine` | `DATAAREAID + RecId` | Validar cabecera con `DATAAREAID + HojaGastosId + UserId`. |
| `INDTicketInfoTable` | `DATAAREAID + FileId` | La linea admite `FileId` repetido fisicamente; revisar duplicados antes de asumir uno a uno. |

## Medidas monetarias

- `Amount` es el importe original expresado en `Currency`.
- `AmountMST` es el importe bruto en la moneda contable de la empresa. Aunque
  el label historico mencione EUR, el BI no debe fijar EUR para todas las
  companias.
- `ReimbursableAmount` deriva de `ReimbursableExpense` y vale cero cuando la
  linea queda excluida del reembolso.
- `VisaEmpresa` conserva una semantica heredada inversa; no debe ser el criterio
  principal para determinar el reembolso.
- Los totales exactos a pagar tambien aplican `NoCobrar`, anticipos y reglas de
  modalidad de pago. Una suma simple no debe etiquetarse como pago AX definitivo
  hasta reconciliarla con los metodos de total del XPO.

La fuente demuestra el esquema versionado, no la importacion, compilacion o
sincronizacion actual en el AOT activo.
