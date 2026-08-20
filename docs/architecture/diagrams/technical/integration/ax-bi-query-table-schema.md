# Esquema AX de tablas para BI de consultas

Este mapa conecta las tablas AX de identidad, empresas permitidas, permisos y
hojas de gastos. Cada recuadro conserva el nombre exacto de la tabla, muestra
una seleccion de claves y medidas principales para BI y contiene una nota
breve.

Para ver mas campos y cardinalidades, use las dos laminas detalladas:

- [Identidad, empresas y permisos](ax-bi-access-table-schema.md).
- [Usuarios CRM y hojas de gastos](ax-bi-expense-table-schema.md).

Contraparte funcional:
[mapa funcional para BI](../../user/integration/ax-bi-query-table-schema.md).

```mermaid
flowchart TB
  subgraph AxSystem["Sistema AX"]
    Sys["<b>SysUserInfo</b><br/>Id, Language<br/><i>Identidad e idioma AX</i>"]
    User["<b>UserInfo</b><br/>Id, Name, Company<br/><i>Nombre y empresa predeterminada</i>"]
  end

  subgraph GlobalAccess["Configuracion global - SaveDataPerCompany = No"]
    Entra["<b>INDWebUserEntraIdentity</b><br/>UserId, AppCode, EntraOID<br/><i>Identidad Entra por aplicacion</i>"]
    Companies["<b>INDCiasPermitidas</b><br/>RecId, UserId, CiaId<br/><i>Empresas permitidas por usuario</i>"]
    App["<b>INDWebApp</b><br/>AppCode, Description, IsActive<br/><i>Catalogo de aplicaciones</i>"]
    Module["<b>INDWebModule</b><br/>AppCode, ModuleCode, IsActive<br/><i>Catalogo de modulos por aplicacion</i>"]
    Access["<b>INDWebModuleAccessLevel</b><br/>RefRecIdCiaPermitida, AppCode, ModuleCode<br/>AccessRights, DataVisibilityMode<br/><i>Permiso configurado</i>"]
    Target["<b>INDModuleDataVisibilityTarget</b><br/>INDWebModuleAccessLevel, TargetPersonAlias<br/>TargetAction, ValidFrom, ValidTo<br/><i>Excepciones manuales de visibilidad</i>"]
  end

  subgraph CompanyData["Identidad y gastos por empresa - unir con DATAAREAID"]
    Person["<b>INDPersonaTable</b><br/>RecId, Alias, UserId, RefRecIdCRM<br/><i>Puente persona, AX y CRM</i>"]
    Hierarchy["<b>INDModuleDataVisibilityHierarchyLine</b><br/>ParentPersonAlias, ChildPersonAlias<br/>ValidFrom, ValidTo<br/><i>Jerarquia de visibilidad</i>"]
    CrmUser["<b>CRMUsuarioTable</b><br/>RecId, UserId, AxaptaUserId<br/>Division, CategoriaId, Bloqueado<br/><i>Empleado CRM y valores de gastos</i>"]
    Manager["<b>CRMUsuarioSubordinadoTable</b><br/>UserIdJefe, UserIdSubordinado<br/>ExcluirAprobacionHojaGastos<br/><i>Jefes y aprobadores directos</i>"]
    Sheet["<b>CRMHojaGastosTable</b><br/>HojaGastosId, UserId, ExpenseSheetStatus<br/>CurrencyCode, ExchangeRateMode, INDCreatedByUserId<br/><i>Cabecera y propietario de la hoja</i>"]
    Line["<b>CRMHojaGastosLine</b><br/>RecId, HojaGastosId, UserId, TransDate<br/>Amount, AmountMST, ReimbursableAmount, FileId<br/><i>Hecho monetario de gasto</i>"]
    Ticket["<b>INDTicketInfoTable</b><br/>FileId, Status, TotalAmount, AmountMST<br/><i>Ticket opcional asociado</i>"]
  end

  Sys -->|Id = UserId| Entra
  Sys -->|Id = UserId| Companies
  User -. mismo principal AX .- Sys
  Entra -->|AppCode| App
  App -->|AppCode| Module
  Companies -->|RecId = RefRecIdCiaPermitida| Access
  App -->|AppCode| Access
  Module -->|AppCode + ModuleCode| Access
  Access -->|RecId| Target

  Companies -. UserId / CiaId = DATAAREAID .-> Person
  Sys -. Id = UserId .-> Person
  Person -. RefRecIdCRM = RecId .-> CrmUser
  Target -. TargetPersonAlias = Alias / empresa via CiaId .-> Person
  Hierarchy -->|ParentPersonAlias / ChildPersonAlias| Person

  CrmUser -. UserIdJefe / UserIdSubordinado .-> Manager
  CrmUser -->|DATAAREAID + UserId| Sheet
  Sheet -->|DATAAREAID + HojaGastosId + UserId| Line
  Line -->|DATAAREAID + FileId| Ticket

  classDef system fill:#f2f2f2,stroke:#666,color:#222
  classDef access fill:#e8f0fe,stroke:#315d9c,color:#10233f
  classDef people fill:#eef7e9,stroke:#4d7a36,color:#193016
  classDef expense fill:#fff4df,stroke:#a66b17,color:#3d2708
  class Sys,User system
  class Entra,Companies,App,Module,Access,Target access
  class Person,Hierarchy,CrmUser,Manager people
  class Sheet,Line,Ticket expense
```

## Leyenda y reglas de union

- Flecha continua: relacion declarada en XPO o relacion cabecera-linea
  confirmada.
- Flecha discontinua: union logica, resolucion X++ o fallback heredado.
- Las tablas web son globales. La empresa del permiso se obtiene mediante
  `INDWebModuleAccessLevel.RefRecIdCiaPermitida -> INDCiasPermitidas.RecId ->
  INDCiasPermitidas.CiaId`.
- `INDPersonaTable`, `CRMUsuarioTable`, subordinados, hojas, lineas, jerarquia y
  tickets son por empresa. En SQL o BI sus uniones deben incluir
  `DATAAREAID`.
- El propietario funcional es `CRMHojaGastosTable.UserId`.
  `INDCreatedByUserId` es un dato de auditoria, no el propietario.
- `CRMHojaGastosLine.AmountMST` es el importe bruto en la moneda contable de la
  empresa. `ReimbursableAmount` es el importe reembolsable derivado.

## Limites confirmados

`UserInfo` y `SysUserInfo` no tienen XPO completo en el repositorio; solo se
muestran los campos demostrados por las referencias y usos actuales. Ademas,
hay dos exportaciones divergentes de `INDWebApp`, una sin indice declarado y
otra con `AppCodeIdx` unico. El diagrama usa `AppCode` como clave logica, pero
la unicidad fisica debe validarse en el AOT o SQL vivo.

El mapa representa la fuente versionada. No demuestra que los mismos XPO esten
importados, compilados y sincronizados en el AOS activo.
