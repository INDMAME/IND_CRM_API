# Visitas con backend y base de datos propios

Estado: análisis de arquitectura y propuesta para revisión. El sistema descrito como destino todavía no está implementado. Este documento mantiene las decisiones de separación de Visitas; la especificación ejecutable pertenecerá al nuevo producto cuando se cree.

## 1. Objetivo y decisiones de partida

Crear una aplicación independiente que conserve la experiencia de Visitas del CRM, autentique con Microsoft Entra y gestione sus datos y reglas con backend y PostgreSQL propios. Axapta será origen periódico de datos de referencia y destino asíncrono de las visitas trabajadas desde la web.

**Alcance decidido por el usuario el 2026-10-21: visitas, asistentes y clientes**, con los contactos de cada cliente como origen de los asistentes. Ofertas, consultas, proyectos, eventos y campañas quedan fuera: el producto no los gestiona ni modifica sus objetos AX, y las visitas importadas conservan esos enlaces solo como atributos de compatibilidad de lectura.

**Secuencia decidida por el usuario el 2026-10-21 (VIS-06): primero un MVP funcional poblado con una carga inicial única de solo lectura desde Axapta; la sincronización de ida y vuelta descrita en las secciones 10 y 11 (extracción incremental, receptor idempotente, outbox/worker, bloqueo de escritores AX y corte) es una fase posterior.** Mientras no exista, la copia local del MVP no es autoridad frente a AX y sus escrituras no llegan a AX.

**Decisión confirmada por el usuario: las visitas migradas se editarán exclusivamente en la nueva web.** Axapta recibirá sus cambios. Esta decisión exige impedir o controlar las modificaciones de esas visitas desde AX, el CRM anterior, jobs e importadores durante la convivencia.

El alcance inicial incluye administración web, diagnóstico con logs en la misma PostgreSQL y soporte para actuar como otra persona. **Decisión confirmada: al activar soporte se permite consultar y editar con los mismos permisos de la persona seleccionada desde el comienzo**, sin un segundo paso para habilitar escritura. La identidad real del operador se conserva siempre en auditoría.

Se mantiene la lógica de jerarquía de Visitas como base reutilizable. Gastos podrá consumir ese mismo motor y relaciones en una fase futura, con permisos por módulo y reglas de negocio propias; ahora no se implementan sus pantallas, aprobaciones ni transacciones.

La web y su API deberán consultar y guardar visitas aunque AX o el puente estén fuera de servicio. Entra, PostgreSQL y los recursos locales necesarios seguirán siendo dependencias del servicio interactivo. La independencia de AX no implica funcionamiento sin conexión del navegador ni una nueva capacidad PWA.

| Ficha mínima del producto | Valor de partida |
| --- | --- |
| Nombre | `CRM_INSERTEC`, decidido por el usuario el 2026-10-21; el primer módulo es Visitas. Ficha `web-api` y especificación preparadas en `IND_APP_STANDARD/proyectos/CRM_INSERTEC/` (`project.json` y `ESPECIFICACION.md`), sin generar el producto. |
| Objetivo | Operar Visitas con lógica y persistencia propias y replicar los resultados a AX. |
| Usuarios | Usuarios corporativos de Visitas; población piloto y matriz de permisos pendientes. |
| Funciones | Historial y operaciones de Visitas/asistentes; soporte como otro usuario; administración, configuración y diagnóstico; sincronización AX. |
| Datos iniciales | Maestros, identidades de negocio e histórico de Axapta; UI y contratos del CRM como referencia. |
| Perfil del estándar | `web-api`; `network-map` pertenece al piloto de redes. |
| Destino propuesto | Carpeta hermana independiente del maestro: `C:\INDProjects\CRM_INSERTEC` (decidido; debe estar vacía o no existir al generar). |

## 2. Evidencia y límites del análisis

Se revisaron las fuentes de los tres repositorios principales y las fronteras de la API interna. Las revisiones de partida estaban limpias:

| Repositorio | Revisión analizada | Alcance |
| --- | --- | --- |
| `IND_CRM_APP` | `b5fdac644b86b053219c170b680ec829e033e940` | Arranque, identidad, MVC, servicios HTTP, UI Visitas, componentes compartidos y pruebas. |
| `IND_CRM_API` | `c5029c648fafe928139a759b827aa1e1b8643245` | Host, contratos, controladores, contexto firmado, COM y XPO canónicos. |
| `IND_APP_STANDARD` | `b05c2afe9d8a22ffb8ff7cfb7cdca9ca0da9122d` | Normas, generador, planificador, plantilla, autenticación, persistencia y entrega. |

Los XPO son evidencia del código versionado, no del AOT instalado. No se ha accedido a datos reales, ejecutado una visita en AX, inspeccionado el esquema SQL vivo ni probado el tenant Entra. Tampoco se han creado la nueva aplicación, una base de datos o infraestructura.

La exportación externa facilitada por el usuario, `AOT_DEV_20261910/AOT`, contiene las familias completas del AOT. Se han localizado y revisado `Data Dictionary/Tables/CRMActividadTable.xpo`, `CRMCustVendVisitaAsistenteTable.xpo`, `CRMAccountTable.xpo` y `CRMContactoTable.xpo`, que no estaban en la carpeta XPO del repositorio. Esto permite comprobar sus campos, índices, relaciones y métodos. El inventario de dependencias EDT/enums, de los escritores de esas tablas en todo el AOT y de los caminos que invocan cada validación se ha completado sobre esa exportación y se recoge en la sección 16. Siguen pendientes el contraste con el AOT instalado y con los datos SQL (unicidad real de `ActividadId`, longitudes efectivas, volúmenes). El modelo de la sección 8 sigue siendo lógico, no un DDL definitivo.

La fuente externa se consulta en lectura y no sustituye automáticamente al XPO canónico de API. En seis objetos de acceso/Visitas comparados se han encontrado diferencias de envoltura de exportación y de código comentado sin una diferencia funcional detectada: servicio de Visitas, resolver, permiso de módulo, jerarquía, targets y subordinados CRM. La igualdad de reglas en esas piezas no acredita que todo el snapshot coincida con el código desplegado.

Las afirmaciones de estado actual remiten al inventario de evidencia de la sección 15. Las tablas, módulos, rutas y mecanismos del destino son propuestas expresamente identificadas.

## 3. Arquitectura y alcance actuales

| Capa actual | Responsabilidad comprobada | Consecuencia para la separación |
| --- | --- | --- |
| `IND_CRM_APP` | ASP.NET Core MVC/.NET 10, Razor, React 19/TypeScript, Tailwind 4 y esbuild. Cookie/OIDC, sesión, empresa, filtros de módulo y adaptación HTTP. | Portar experiencia y componentes; rehacer composición y acceso a datos como SPA del estándar. |
| `IND_CRM_API` | .NET Framework 4.8/C# 7.3, Web API 2/OWIN, servicio Windows x86. JWT técnico, contexto firmado, contenedores AX, proveedores de IA. | Extraer reglas y contratos; conservar temporalmente las capacidades AX detrás de un adaptador de integración. |
| Axapta 3.0 | Persistencia, numeración, inicializaciones desde cuenta, propietario, visibilidad/mutación y transacciones X++. | Trasladar reglas interactivas al dominio nuevo; mantener compatibilidad de escritura en el receptor AX. |
| `IND_INTERNAL_API` | Transporte técnico de correo Graph, borrado Blob y consulta de entorno AX; autenticación propia con dependencia AX. | No ofrece actualmente un contrato de réplica de Visitas. Su reutilización exige ampliación explícita y contrato propio. |

El flujo interactivo es navegador → MVC → API CRM → Business Connector COM → X++. La persistencia no reside en la web. Véase [flujo actual](../diagrams/technical/integration/visits-current-flow.md).

La revisión general también identifica Gastos, Tickets, IA de gastos, ayuda CRM/MCP, almacenamiento de justificantes y correo. Son dominios distintos. La primera aplicación autónoma debe incluir los servicios transversales necesarios para Visitas: identidad, empresas, permisos, maestros, auditoría, errores y operación. Gastos/Tickets, contabilidad, OCR de tickets, aprobaciones y correo de gastos conservan su ciclo actual fuera de esta primera separación.

### 3.1 Inicio de sesión y permisos

Entra autentica hoy en APP, pero `IndAuthContextService` obtiene además un token técnico de API y consulta el contexto Entra de CRM. Empresa, usuario AX funcional, permisos y visibilidad se resuelven mediante API/AX. Un clon literal mantendría esa dependencia incluso después del inicio de sesión corporativo.

La web aplica niveles de módulo y controles visuales; API valida el contexto firmado y X++ calcula visibilidad/mutación. Son controles diferentes. La identidad Entra, el usuario AX, el usuario CRM y la persona comercial tampoco son identificadores intercambiables.

Se ha observado una deuda concreta: los controladores de Visitas/Actividades obtienen el actor de `RequireAxUserIdOrReturn422`, que exige la presencia de `X-IND-AxUserId`; la validación previa de empresa almacena por separado el actor del contexto firmado. El helper de Visitas no los equipara. El destino debe derivar siempre al actor humano de los claims validados `tid + oid` y resolver su identidad de negocio en PostgreSQL. Este hallazgo es estático; no se ha realizado una prueba de explotación del servicio desplegado.

### 3.2 Contratos que utiliza Visitas

| Necesidad | Superficie actual, a contrastar con OpenAPI al implementar |
| --- | --- |
| Cabecera de visita | `/api/crm/activities`: alta, actualización, borrado, detalle por identificador/código y listado. |
| Asistentes | `/api/crm/visits`: altas y bajas de asistentes. |
| Clientes y contactos | `/api/crm/accounts/listAccounts` y `/api/crm/accounts/listContacts`. |
| Enums | `/api/crm/enums/by-name` y `/api/crm/enums/by-id`, con catálogo por aplicación/empresa. |
| Identidad y visibilidad | Contexto Entra, empresa, módulo y resolución de usuarios visibles. |
| Dictado utilizado en Visitas | `/api/ia/service/speech`, a través del proxy MVC de transcripción. |
| Servicio complementario de API | `/api/ia/service/text/format` está disponible; no se ha observado su consumo por la UI de Visitas revisada. |

Estas son superficies existentes, no las rutas propuestas para la nueva API. Los verbos y variantes exactos constan en los controladores y contratos canónicos; la sección 9 propone un contrato nuevo bajo `/api/v1`.

Operaciones principales comprobadas: `POST /api/crm/activities/create`, `POST /api/crm/activities/list`, `GET /api/crm/activities/{recId}`, `GET /api/crm/activities/by-code/{code}`, `PUT /api/crm/activities/{recId}`, `DELETE /api/crm/activities/{recId}`, `POST /api/crm/visits/createVisitaAsistente` y `DELETE /api/crm/visits/deleteVisitaAsistente` (este último recibe cuerpo). Completan la superficie `GET /api/crm/data-visibility/visible-users` (módulo `VISITAS_GESTION` por defecto) y la plantilla oculta `GET /api/crm/template/sample`. La API tiene baja de asistentes; no se ha encontrado su exposición en la UI principal de Visitas. Diseñar el agregado completo no implica añadir al primer clon una pantalla nueva de gestión de asistentes.

Deuda observada en el listado actual: `IND_CRM_APP` envía a `/api/crm/activities/list` solo `fromDate`, `toDate`, `accountNum` y `ownerAxUserId`, sin `page` ni `pageSize`; la API aplica `page=1` y `pageSize=50` (`MaxPageSize`), AX ordena el conjunto por `INDCreatedByUserId` y el controlador MVC reordena por fecha y repagina en local con `total` igual al tamaño de esa única página. Con más de 50 visitas en el rango y alcance visibles, el historial omite visitas sin aviso, y no necesariamente las más antiguas. El producto nuevo debe paginar y ordenar en PostgreSQL y devolver `total` real; la cifra de 50 no es un requisito funcional.

### 3.3 Reglas de negocio que deben trasladarse

| Regla demostrada | Tratamiento necesario |
| --- | --- |
| AX asigna `ActividadId` con `NumberSeq` y persiste `RecId`. | Crear identidad local al guardar; mantener código local y correspondencia AX posterior. El usuario no esperará a la numeración AX. |
| Alta establece tipo actividad Visita, estado Finalizada, prioridad Normal y país derivado de la cuenta. | Convertir inicializaciones en políticas explícitas y probar su compatibilidad con AX. |
| La valoración tiene una excepción por empresa. | Decidido (VIS-04): el usuario indica la valoración (Bien, Regular, Mal) en alta y detalle; el producto no aplica un valor automático por empresa y el receptor conserva el enviado. |
| Cuenta de cliente y cliente potencial ejecutan inicializadores diferentes. | Importar origen de cuenta y las referencias necesarias; comprobar todos los campos derivados antes de cerrar el esquema. |
| `INDCreatedByUserId` actúa como propietario funcional; `UserId` puede ser un identificador CRM heredado. | Separar propietario, creador y último actor. Resolver la propiedad histórica antes de migrar. |
| Actualización conserva propietario y normaliza algunos valores heredados. | Mantener propietario inmutable salvo operación expresa; un dato histórico incompleto requiere resolución, no adjudicarlo al editor. |
| Visibilidad y mutación se calculan por separado, con jerarquías, vigencias y excepciones. | Reproducir la matriz efectiva en backend; poder leer no concede automáticamente editar. |
| El modo `ModuleBusinessRules` aparece en el enum, pero su rama del resolver está comentada. | No habilitarlo como permiso nuevo por interpretar únicamente el catálogo de valores. |
| Alta de asistentes fuerza `Cliente_Proveedor` en X++, aunque recibe tipo. | Confirmar tipos realmente usados y diseñar compatibilidad; conservar explícitamente tipos históricos. |
| Borrado elimina asistentes y cabecera dentro de una transacción AX, llamando a métodos `delete()`. | Borrado lógico local más comando externo controlado; verificar hooks y dependencias AX antes de habilitar exportación de borrados. |

La actualización actual es una sustitución de campos, no un PATCH: necesita cuenta/tipo/fecha y convierte textos omitidos a vacío; omitir contacto puede restablecer el enum a `0`. En la API AX `0` puede ser un enum válido y `-1` no significa «sin cambio». La UI tiene validaciones más restrictivas para la selección de tipo. El contrato nuevo debe declarar estas diferencias y normalizar nulos, vacíos y omisiones de forma inequívoca.

Diversos contratos AX ya transportan identificadores como texto; otros, como la proyección de contactos, devuelven valores numéricos dentro de arrays genéricos. Existen flujos que admiten identificadores negativos distintos de cero. El adaptador nuevo debe normalizarlos sin pérdida y no convertirlos indiscriminadamente a `Number` JavaScript ni imponer positividad sin verificar el objeto AX correspondiente.

### 3.4 Correspondencias de datos comprobadas

| Concepto | Campo o referencia AX comprobada | Modelo de destino propuesto |
| --- | --- | --- |
| Identidad y código de visita | `CRMActividadTable.RecId`, `ActividadId` y empresa `changecompany` | UUID local, código web y mapping de origen/empresa/clave/código AX. |
| Cuenta y nombre | `AccountNum`, `Name`; `CRMAccountTable.Origen` | Cuenta local y atributos de compatibilidad derivados. |
| Clasificación | `ActividadType`, `TipoVisita`, `ContactMethod` | Tipo actividad fijo y referencias de catálogo con códigos externos conservados. |
| Propiedad heredada | `UserId`, `INDCreatedByUserId` | Persona propietaria independiente de actor creador/modificador y mappings legacy. |
| Fecha y narrativas | `TransDate`, `Description`, `Comentarios`, `VisitaAntecedentes`, `VisitaConclusiones` | Fecha civil, asunto y tres textos sin truncamiento ni traducción automática. |
| Valores inicializados | `Country`, `Status`, `Prioridad`, `VisitaValoracion` | Políticas y snapshots de compatibilidad confirmados por empresa. |
| Asistente y relación padre | `CRMCustVendVisitaAsistenteTable.RecId`, `RefRecId` | UUID de asistente y FK de visita; mapping externo inequívoco. |
| Contacto del asistente | `ContactoRecId`, `AsistenteId`, `AsistenteCargo`, `AsistenteTipo` | FK de contacto opcional, nombre/cargo históricos y tipo externo; no usar nombre como identidad. |
| Cuentas | `CRMAccountTable`: `AccountNum`, `NombreComercial`, `RazonSocial`, `Origen`, `INDSearchKey` | Maestros locales con origen cliente/potencial y búsqueda. |
| Contactos | `CRMContactoTable`: `RecId`, `AccountNum`, `Name`, `Cargo`, `Empresa`, `Origen` | Contactos locales por cuenta y empresa, incluidos registros históricos incompletos cuando corresponda. |

El detalle actual solo devuelve tres campos por asistente: ID textual mostrado, tipo y cargo. La baja AX localiza por visita y `AsistenteId`; contactos homónimos pueden resultar ambiguos. La nueva extracción necesita las identidades de tabla/contacto, y el receptor no debe basar la sustitución de asistentes únicamente en sus nombres. Conservar tipos históricos exige un receptor que los respete; reconstruirlos todos con el alta actual los convertiría al tipo forzado por X++.

Para acceso, inventariar `INDWebUserEntraIdentity`, `INDCiasPermitidas`, `INDWebApp`, `INDWebModule`, `INDWebModuleAccessLevel`, `INDModuleDataVisibilityTarget`, `INDModuleDataVisibilityHierarchyLine`, `INDPersonaTable` y `CRMUsuarioTable`. Las relaciones globales de permiso obtienen su empresa a través de `INDCiasPermitidas`; personas y jerarquía se resuelven por empresa.

La revisión de las tablas completas añade estas dependencias:

- `CRMAccountTable` declara unicidad por `Origen + AccountNum`, dentro de empresa. El helper heredado `FindByAccountNum` no incluye origen; el modelo nuevo debe conservar la clave completa y resolver posibles ambigüedades.
- `CRMActividadTable` contiene fechas y horas de modificación, pero eso no aporta por sí solo un diario consistente de cambios y borrados. Su `validateDelete` (60 días desde `TransDate` para `ActividadType=Visita`) y su `validateWrite` (en la empresa `REF` exige `TipoVisita`) solo los disparan los DataSources de formulario. `INDCRMVisitsService` llama a `insert()`, `update()` y `delete()` directamente, que en X++ no invocan esas validaciones: la ruta API las elude hoy (sección 16.5). El producto nuevo debe decidir expresamente si adopta la regla de 60 días y la de `REF` como políticas propias.
- Los métodos de asistentes de tipo interno crean/actualizan/eliminan filas de `CRMCustListaVisitasTable`. Preservar asistentes históricos y sus efectos AX exige revisar también esa tabla y sus consumidores; la copia de dos tablas no representa todas las consecuencias de negocio.
- Cuenta/contacto regeneran campos de búsqueda y contactos contienen validaciones adicionales. Clasificar qué reglas ejecuta la UI AX, cuáles se invocan por método y cuáles debe imponer el backend nuevo.

## 4. Qué puede reutilizarse de la web

El módulo ya tiene páginas React de historial, creación y detalle, además de calendarios, filtros, paginación, selectors y campos narrativos compartidos. Es una base útil para conservar la experiencia, pero depende de bootstrap Razor, rutas MVC, diccionarios globales, proveedores de autenticación y servicios orientados al contrato CRM.

| Elemento | Estrategia propuesta |
| --- | --- |
| Aspecto y comportamiento de formularios, historial y detalle | Portar gradualmente a `FRONTEND/src/features/visits`, con pruebas de paridad. |
| Componentes visuales puros y helpers de fecha | Reutilizar con adaptación de imports, contratos y tema del estándar. |
| Filtros, calendario, paginación y selección de responsable | Conservar interacciones; reemplazar transporte y claves de identidad. |
| `AuthProvider`, contexto de empresa y servicios `/api` MVC | Sustituir por MSAL y cliente OpenAPI del nuevo backend. |
| `window.__IND_I18N__`, recursos `.resx` y rutas de vistas | Migrar diccionarios y composición; el estándar genera UI española, no toda la localización actual. |
| Recuperación de altas parciales y compensaciones de cliente | Reemplazar por una operación local atómica del agregado e idempotencia servidor. |
| IA/audio | Mantener como capacidad opcional de backend con proveedor aislado; guardar visitas debe funcionar sin el proveedor. |
| Assets generados y espejo `wwwroot` | Tomar fuentes de `Web/wwwroot`; generar nuevos bundles Vite en el producto independiente. |

Comportamientos a preservar: cliente, tipo, descripción y comentarios obligatorios en alta; descripción limitada a 200 caracteres; asistentes opcionales; antecedentes/conclusiones; fecha civil sin introducir horas; borradores y recuperación; cambios de empresa/cuenta; permisos y solo lectura; foco, accesibilidad y móvil.

Estos campos obligatorios describen la UI. El DTO marca descripción requerida, pero el controlador API revisado aplica su propia validación manual y puede enviarla vacía, igual que los comentarios. El backend nuevo debe imponer el contrato funcional acordado, con errores por campo, en lugar de heredar esa diferencia entre capas.

La búsqueda de cuentas comienza a partir de cuatro caracteres; cuentas y contactos se cargan en lotes de diez. Los contactos se cargan al abrir su selector para una cuenta y se filtran sin ese mínimo. Historial empieza con 90 días y muestra seis visitas por página. El nuevo backend puede adoptar límites generales del estándar y permitir que esta pantalla siga solicitando seis. La fecha inicial de creación usa reloj del navegador y la del historial usa reloj del servidor; acordar una zona de negocio para «hoy» sin convertir la fecha civil de la visita en un instante UTC.

En Historial, aplicar filtros consulta la lista; editar el borrador del filtro no debe hacerlo. Cancelar restaura el borrador previo y Limpiar conserva el diálogo abierto. Conservar selección de responsable, rangos recordados, resultados vacíos y recuperación tras fallo. Las cachés son ayudas de interfaz, no autorización.

El CRM tiene seis culturas documentadas. Debe decidirse si el primer producto mantiene las seis o comienza en español; cualquier reducción es una decisión funcional expresa, no una consecuencia automática de cambiar el host.

No se han encontrado en el flujo de Visitas exportación PDF/Excel, envío de correo o gestión documental persistente. El audio se utiliza para dictado. La ausencia de esos recorridos en los archivos revisados no acredita la inexistencia de efectos laterales en métodos AOT ausentes.

## 5. Adopción concreta de IND_APP_STANDARD

La solución propuesta es un monolito modular con proceso de integración separado, dentro de un repositorio de producto. No necesita inicialmente Redis, Kafka, RabbitMQ ni otro servicio de colas: PostgreSQL puede conservar el trabajo pendiente de forma durable.

| Área | Base verificada en el maestro | Aplicación al producto |
| --- | --- | --- |
| Runtime | Node `>=24.15.0 <25`, x64; CI del maestro fija `24.19.0`. | Fijar runtime soportado en desarrollo, build y servicio. |
| Frontend | React `19.3.0`, TypeScript `5.9.3`, Vite `8.2.2`, Tailwind `4.3.3`. | SPA por funciones y primitivas compartidas. |
| Identidad | MSAL browser `5.21.0`, MSAL React `5.7.0`. | SPA y API Entra diferenciadas por entorno; sin Graph por defecto. |
| Backend | NestJS `11.2.3`, Fastify `5.12.3`, ESM. | Controlador delgado → servicio de dominio/aplicación → repositorio. |
| Datos | Drizzle ORM `0.45.2`, Kit `0.31.10`, `pg` `8.23.0`. | PostgreSQL privado, migraciones revisadas y restricciones DB. |
| Contratos | OpenAPI/Swagger protegido, Problem Details RFC 9457. | Cliente TypeScript generado; errores de campo y conflicto estables. |
| Calidad | Vitest `4.1.11`, Playwright `1.63.0`. | Pruebas de dominio, DB, contrato, identidad, UI e integración. |

Son versiones fijadas en los manifiestos y lockfiles revisados, no una afirmación de que sean las últimas disponibles. PostgreSQL 17 figura como línea inicial propuesta, no como servidor instalado ni obligación de reemplazar runtimes compartidos.

El paquete distribuye `standard.json` versión `1.0.0`; la norma se identifica como base documental `1.1`. Registrar SHA y manifiesto de adopción evita confundir esos niveles. La nueva aplicación conserva `ind-standard.lock.json` y decide sus actualizaciones expresamente.

### 5.1 Capacidades que la plantilla todavía no aporta

La semilla tiene shell, MSAL, `/me`, health, documentación protegida y una tabla `audit_events`. Hay que desarrollar Visitas, empresas, cuentas, contactos, permisos por registro, integración, outbox/inbox y administración operativa. La conexión Drizzle existe, pero requiere proveedor de persistencia para los servicios de negocio.

También deben cerrarse diferencias entre norma y semilla antes de tomarla como contrato definitivo:

1. La norma menciona `@nestjs/config`; la semilla usa Zod y carga explícita de entorno. No añadir una segunda prioridad de configuración: resolver y documentar la adopción elegida.
2. El contrato normativo de Problem Details exige detalles que el filtro inicial no implementa por completo, incluidos errores por campo y un `instance` opaco.
3. `@Inject` es explícito y `emitDecoratorMetadata` está desactivado. Validación de DTO y decoradores requieren pruebas reales, no se habilitan por instalar un paquete.
4. Readiness hace una comprobación DB básica. El producto debe comprobar la versión de esquema y separar la salud de AX del servicio interactivo.
5. Existen generación y comprobación de migraciones; hace falta un procedimiento explícito de aplicación y recuperación con identidad de migración separada.
6. El guard de usuario exige scope delegado y rol. La autenticación de procesos no está resuelta por ese guard.
7. El logger Fastify de la semilla emite logs estructurados con nivel `info` y redacción básica; no tiene destino PostgreSQL ni visor administrativo. La tabla `audit_events` tampoco implementa por sí sola la auditoría de cada operación.
8. La semilla no trae estructura por funcionalidad: los controladores están en un único archivo, `AppModule` se define dentro de `createApp` y el frontend no tiene enrutador (la navegación es estado interno de `Dashboard.tsx`). La organización por `features` que piden la norma y las skills se crea en el producto.
9. La conexión Drizzle (`database.ts`) devuelve `db` y `probe`, pero `main.ts` solo usa `probe`: `db` no está inyectado en Nest. El proveedor de persistencia para los servicios de negocio es trabajo del producto.
10. La plantilla no incluye CI (`templates/web-api/.github` no existe); el CI del maestro solo genera y prueba `IND_NETWORK_MAP`. La guía pide preparar un CI propio sin copiar ese workflow.
11. Configuración: la semilla usa `PUBLIC_ORIGIN` donde la norma nombra `PUBLIC_BASE_URL`, no define `APP_NAME` ni `LOG_LEVEL` y fija el nivel del logger a `info`; `main.ts` descarta el error de arranque sin registrar la causa. El frontend genera solo tipos (`openapi-typescript`), sin cliente `openapi-fetch`, y el nombre del paquete `FRONTEND` está fijo como `ind-web-frontend`.
12. El estándar no define reglas de idempotencia (`Idempotency-Key`), de proceso worker ni de servicio Windows concreto (solo la decisión de usar un wrapper mantenido, pendiente de inventario). Los apartados 9, 10 y 11 de este documento los aportan como requisitos del producto.

Estas observaciones no cambian el maestro durante este análisis; deben resolverse en la adopción del producto o en una mejora separada del estándar.

La documentación de esta propuesta usa Mermaid por pertenecer al CRM actual. El producto nuevo deberá adoptar `docs/diagramas/` y la convención Archify del estándar, validando la herramienta antes de incorporarla. No trasladar automáticamente las convenciones históricas de CRM al generador.

## 6. Alternativas y arquitectura propuesta

| Alternativa | Ventaja | Coste o límite | Evaluación |
| --- | --- | --- | --- |
| Clonar todo el host CRM y añadir DB local | Acerca inicialmente el aspecto actual. | Conserva dos stacks heredados y numerosas dependencias AX; requiere excepción al estándar nuevo. | Referencia de comportamiento, no base recomendada del producto. |
| Producto `web-api`, dominio local y puente AX aislado | Cumple el estándar y permite operación independiente, separación gradual y reutilización de UI. | Requiere portar reglas y construir una integración fiable. | **Recomendada.** |
| Varios microservicios con broker desde el inicio | Separación y escalado por servicio. | Añade operación y transacciones distribuidas sin volumen medido que lo justifique. | Reconsiderar si aparecen requisitos comprobados. |

Componentes propuestos:

| Componente | Responsabilidad | Dependencias permitidas |
| --- | --- | --- |
| `FRONTEND` | Sesión, navegación, formularios y consulta del contrato público. | Entra y API propia. |
| API / identidad y acceso | Claims, empresas permitidas, alcance y políticas. | Entra/JWKS y PostgreSQL. |
| API / Visitas | Validación, propiedad, agregado, concurrencia y auditoría. | Repositorios locales; publica trabajo durable. |
| API / maestros | Cuentas, contactos y catálogos locales. | PostgreSQL. |
| API / soporte | Sesiones de actuación, contexto efectivo y trazabilidad de actor real/persona representada. | Guard Entra, permisos y PostgreSQL. |
| API / administración | Configuración tipada, permisos/jerarquías y operación autorizada. | Capacidades del actor real y PostgreSQL. |
| API / diagnóstico | Consulta de logs, auditoría y explicación de permisos. | Vistas paginadas de PostgreSQL con acceso acotado. |
| Proceso worker | Importar, exportar, reintentar y reconciliar. | PostgreSQL y puente AX; módulos compartidos del producto. |
| Puente AX | Autenticar servicio técnico y ejecutar contratos de extracción/aplicación. | Infraestructura AX/COM x86 encapsulada y serializada. |
| PostgreSQL | Dominio, autorización, auditoría, correspondencias y estado de integración. | Acceso privado de procesos autorizados. |

El worker será otro proceso del mismo producto, sin una API pública innecesaria. La API de usuario no abrirá sesiones COM ni consultará AX en validaciones, catálogos, lectura, escritura o refresh de permisos.

Para el puente se puede ampliar una superficie de integración existente o crear un adaptador específico; debe decidirse tras inventariar despliegue y contratos. `IND_INTERNAL_API` no trae esos endpoints de Visitas hoy. Reutilizar `IND_CRM_API` como puente inicial tampoco convierte sus rutas de pantalla en un protocolo de sincronización completo.

Fuentes: [arquitectura destino](../diagrams/technical/integration/visits-autonomous-target.md), [guardado y exportación](../diagrams/technical/integration/visits-outbox-sequence.md), [vista funcional](../diagrams/user/integration/visits-autonomous-workflow.md).

El flujo de soporte se detalla en [actuar como otra persona](../diagrams/technical/auth/visits-support-context-sequence.md) y su [vista funcional](../diagrams/user/auth/visits-support-context.md).

## 7. Propiedad de datos y autorización autónoma

| Conjunto | Autoridad propuesta tras el corte | Entrada periódica desde AX |
| --- | --- | --- |
| Visitas asumidas por la web y sus asistentes | Nueva web. | Solo observación/reconciliación de la copia AX; nunca sobrescritura del agregado local. |
| Histórico aún no asumido | AX hasta completar la migración de su alcance. | Importación íntegra, validada y con correspondencias. |
| Cuentas, contactos y enums | AX inicialmente, copia local para operar. | Actualización de maestros, inactivación y validación de referencias. |
| Usuarios comerciales y mapeos AX | Carga inicial desde AX, administración definida en producto. | Los cambios entrantes se revisan según la propiedad acordada. |
| Acceso a la aplicación | Asignación/scope/rol Entra. | No se importa desde una consulta interactiva a AX. |
| Empresas, permisos finos, jerarquías y excepciones | PostgreSQL tras el corte de autorización. | Importación inicial; no reescribir ajustes locales de forma implícita. |

Una visita importada debe registrar su autoridad explícita. Cuando se asume en la web, la transición a `WEB` ocurre de forma controlada; el usuario puede editarla sin esperar a AX. Un corte por empresa/cohorte es más sencillo de proteger que dos editores alternando libremente sobre el mismo registro.

Entra aporta la identidad `tid + oid`, no toda la autorización de negocio. El backend debe resolver persona, empresas, módulo, nivel de acceso, visibilidad y mutación de forma local, con rechazo restrictivo ante mapeos ambiguos o incompletos. La selección de empresa es una petición del cliente que se contrasta con sus concesiones locales.

El importador debe detectar duplicados y ambigüedades: `INDPersonaTable.UserId` admite duplicados y la tabla de identidad Entra no demuestra una relación universal uno a uno por aplicación/OID. Resolverlos es una condición de migración, no una suposición del modelo nuevo.

La matriz del módulo distingue View=1, Edit=2, Add=3 y FullAccess=4. El CRM web usa umbrales específicos para crear, editar y borrar; el resolver X++ no reproduce por sí solo todos esos umbrales. Declarar políticas por operación en la API nueva. En el alcance de datos, `OwnOnly` no se amplía con targets; las exclusiones prevalecen, las personas bloqueadas se restringen y los permisos duplicados se resuelven de forma restrictiva. Importar todas las filas y unir sus permisos ampliaría incorrectamente el acceso.

Las personas históricas deben poder existir sin una cuenta Entra activa. La cuenta técnica de integración se audita como proceso y conserva el actor humano original de cada comando; no suplanta a ese usuario ni recibe permisos interactivos por usar su identificador AX.

Definir un procedimiento local de baja y revocación de permisos. Retirar una asignación Entra no invalida instantáneamente todos los access tokens emitidos. La independencia operativa requiere revisar permisos locales y no depender de un refresh AX para hacer efectiva una baja.

### 7.1 Paridad del sistema de visibilidad de Axapta

Es viable implementar en NestJS/PostgreSQL las mismas reglas de acceso funcional observadas en X++, manteniendo Entra como autenticación. La política se evalúa con la persona del usuario validado, empresa, aplicación, módulo y fecha de evaluación. No requiere ejecutar AX por cada consulta ni que los grupos de Entra reproduzcan toda la jerarquía comercial.

| Configuración AX comprobada | Resultado a conservar en backend propio |
| --- | --- |
| `OwnOnly` | Visitas propias; este modo ignora targets tanto de inclusión como de exclusión. |
| `OwnAndHierarchy` | Propias más personas de la jerarquía autorizada. |
| `HierarchyOnly` | Personas de la jerarquía, sin añadir automáticamente al responsable. |
| `ManualTargetsOnly` | Personas configuradas mediante excepciones vigentes. |
| `AllUsers` | Personas admitidas dentro de la empresa y del módulo autorizado. |
| `DirectOnly` / `FullDescendants` | Solo subordinados directos o todos los descendientes vigentes. |
| Target `Add` / `Remove` | Añadir o excluir personas fuera de `OwnOnly`; las exclusiones prevalecen. |
| Target `TargetOnly` / `TargetAndHierarchy` | Aplicar la excepción a una persona o a esa persona y su jerarquía, según profundidad. |

La jerarquía que utiliza Visitas es `INDModuleDataVisibilityHierarchyLine`, por empresa y alias de persona. **No es equivalente a `CRMUsuarioSubordinadoTable`**, usada por recorridos de aprobación de gastos. Copiar únicamente jefe/subordinado de esa tabla daría una visibilidad diferente de la actual.

El esquema de visibilidad admite relaciones entre personas con vigencias y no exige un único padre global por persona. Conservar esa semántica como relaciones de grafo; no reducirla a una columna `managerId` sin decidir expresamente un cambio de requisitos.

La tabla de jerarquía valida personas existentes/no bloqueadas, responsable distinto de subordinado, rangos coherentes, ausencia de relaciones duplicadas con vigencias solapadas y prevención de ciclos dentro del periodo. La resolución aplica fechas inclusivas y permite límites abiertos. Los permisos de módulo validan también su configuración y disponibilidad de jerarquía activa. Estas validaciones deben aplicarse al importar y al administrar reglas desde la nueva aplicación.

Para conservar paridad hay dos matices: los permisos duplicados efectivos seleccionan una fila por ranking de modo, profundidad, mutación, nivel de acceso y RecId; no se calculan mediante unión o intersección arbitraria. El bloqueo de personas se aplica al resultado final; el recorrido de jerarquía no necesariamente poda los descendientes activos de un nodo intermedio bloqueado.

Lectura y modificación siguen siendo decisiones separadas. Un responsable puede ver visitas del equipo y editar solo las propias si `MutationPolicy=OwnOnly`. `SameAsVisibility` permite mutar dentro del alcance visible, siempre sujeto al nivel del módulo para la operación concreta. No activar políticas que solo aparezcan en catálogos o bloques comentados sin implementación efectiva.

Secuencia propuesta de autorización: claims Entra validados → identidad/persona local → empresa autorizada → permiso de módulo → conjunto de propietarios visibles → filtro de la consulta PostgreSQL. Aplicar el mismo alcance a listado, detalle, búsquedas y cualquier operación futura de exportación; comprobar además la política de mutación al escribir. El navegador no decide el usuario cuyos permisos se calculan.

Para reproducir la configuración actual hacen falta **código y registros de configuración**. El XPO demuestra el algoritmo y estructura, pero no contiene necesariamente las asignaciones reales de usuarios, empresas, permisos, jerarquías y targets. Su extracción inicial debe preservar claves, empresa y vigencias y rechazar identidades ambiguas; después se administrarán según la autoridad local acordada. Una eventual importación periódica de permisos necesita una política explícita para no sobrescribir ajustes locales.

Las cachés de acceso deben incluir identidad, empresa, módulo y revisión; invalidarse al cambiar permisos o identidad y no superar el siguiente cambio de vigencia. El conjunto visible se calcula en backend, con respuesta restrictiva ante configuración inválida.

La aceptación exige una matriz de paridad con casos sintéticos: propio, responsable directo, responsable de varios niveles, solo jerarquía, inclusión/exclusión, target con descendientes, fecha de inicio/fin, persona bloqueada, empresa ajena, permiso duplicado restrictivo y lectura permitida con modificación denegada. La comparación contra AX real se realizará con una extracción y entorno autorizados; revisar fuente no equivale a ejecutar esa prueba.

### 7.2 Actuar como otra persona para soporte

Esta capacidad forma parte de la primera aplicación de Visitas. El operador inicia sesión con su propia cuenta Entra y abre una sesión de actuación sobre una persona concreta. La sesión permite desde su activación leer y escribir con los permisos efectivos de esa persona; una denegación debe reproducirse aunque el operador tenga capacidades administrativas.

| Identidad o ámbito | Uso |
| --- | --- |
| Actor real | `tid + oid` validados del operador; autorización para iniciar soporte y atribución de sus acciones. |
| Persona efectiva | Persona seleccionada; determina empresas permitidas, jerarquía, niveles de módulo y políticas de Visitas. |
| Propietario del registro | Propietario funcional de la visita; se conserva en edición y se asigna según reglas del alta. No se confunde con el operador. |
| Actor técnico | Worker/puente que entrega el comando a AX, conservando el contexto de la operación original. |

Flujo propuesto:

1. Un operador autorizado elige persona, empresa y motivo o referencia de incidencia desde el panel.
2. El backend valida su capacidad de soporte y ámbito, la persona de destino y su configuración local. Crea y audita una sesión corta, revocable y vinculada al actor real, persona, empresa y módulo Visitas.
3. La pestaña activa el contexto mediante un identificador opaco que acompaña al access token Entra real. No basta enviar libremente un ID de usuario en una cabecera.
4. Cada petición valida nuevamente actor, concesión de soporte, sesión activa, ámbito y permisos efectivos actuales. Para las operaciones de Visitas se utiliza el permiso de la persona representada sin sumar el del administrador.
5. La cabecera muestra permanentemente «Actuando como <persona> · Cuenta real: <operador> · Empresa: <empresa> · Finalizar» y la caducidad.
6. Finalizar, revocar, cambiar de identidad o vencer la sesión bloquea ese contexto. Se cancelan consultas y se invalidan cachés antes de volver explícitamente a la cuenta real o abrir otra sesión.

Las mutaciones revalidan la sesión y su revisión de acceso dentro de la transacción que autoriza el cambio, coordinando esa comprobación con la revocación. Finalizar soporte no permite que una operación todavía no autorizada cambie silenciosamente de actor; las operaciones ya confirmadas conservan su resultado.

La duración decidida es 30 minutos, renovable manualmente y revocable, con acceso para quien tenga `FullAccess` en el módulo de Visitas según la configuración replicada de AX (VIS-13); no se exige una segunda habilitación de edición. Sesiones anidadas no se permiten. Cambiar persona o empresa requiere cerrar y abrir un contexto definido, no modificar el destino de una sesión existente.

El backend conserva `identity` del guard como actor real y añade un contexto efectivo separado. `/api/v1/me` sigue identificando al operador: el frontend del estándar lo contrasta con MSAL. La persona representada se obtiene de otro contrato de contexto. No se obtiene ni fabrica un token Entra de la otra persona, ni se cambia su contraseña. El flujo OBO de Microsoft propaga la identidad del usuario autenticado entre APIs y no constituye este selector de usuario para soporte. Véase [documentación de flujos MSAL](https://learn.microsoft.com/en-us/entra/identity-platform/msal-authentication-flows).

El contexto de soporte es explícito por petición y propio de la pestaña. No cambia una cookie global ni la cuenta MSAL de otras pestañas. Su handle operativo permanece en memoria; un marcador de interfaz sin credenciales permite detectar soporte pendiente tras recarga o duplicación. En ese caso se bloquea la continuación hasta reactivar o salir expresamente. El servidor y el cliente deben comprobar el contexto esperado de cada mutación; ninguna operación se reenvía silenciosamente bajo la identidad real.

Las claves de caché, borradores, peticiones pendientes y recibos idempotentes incorporan actor real, persona efectiva, empresa y contexto/revisión. Una respuesta de un contexto anterior no puede restaurar datos ni permisos en el nuevo. Evitar que el borrador propio del operador aparezca como borrador de la persona representada.

Una persona activa sin permiso sobre el módulo puede reproducir el mismo rechazo de autorización; la sesión de soporte no le concede acceso. Personas desactivadas, ajenas al ámbito del operador o con identidad ambigua no se habilitan como contexto operativo; el panel puede explicar la denegación sin abrir acceso a sus visitas. El diagnóstico distingue comprobaciones locales de asignaciones/estado Entra no verificados en vivo.

Las funciones administrativas se ejecutan con el actor real fuera del contexto de actuación. Seleccionar a un administrador como persona efectiva no concede acceso a administración, logs, secretos ni sesiones de soporte encadenadas. La capacidad de administrar la aplicación no concede por sí sola lectura universal de visitas; el soporte es una facultad explícita con ámbito propio.

Toda operación conserva actor real, persona efectiva, sesión/motivo y propietario. La auditoría y el outbox guardan ambos actores desde la transacción original; el worker conserva esos datos aunque la sesión de soporte haya terminado. La caducidad no deshace una escritura ya confirmada ni cancela su entrega AX. Si AX no tiene campos para ambos, el contrato y la trazabilidad local mantienen la atribución completa.

Este modo reproduce datos, permisos y reglas locales de la aplicación. No reproduce automáticamente MFA, Conditional Access, la sesión Entra ni las condiciones del navegador de la persona, incluidos sus borradores o almacenamiento local. Para esos fallos hacen falta diagnóstico de cliente/correlación y, cuando corresponda, evidencia del inicio real de sesión.

## 8. Modelo lógico propuesto

Los nombres siguientes son propuestas. Deben concretarse completando las dependencias de las tablas AOT ya localizadas y perfilando una extracción autorizada.

| Entidad propuesta | Contenido y restricciones esenciales |
| --- | --- |
| `companies` | UUID local, código AX y origen; estado activo. |
| `identities` / `people` / `identity_company_links` | Entra `tid+oid` único, persona de negocio, mapeos AX/CRM por empresa; distinguir cuentas activas e historia. |
| `access_grants`, `visibility_rules`, `hierarchy_edges` | Empresa, módulo, nivel, políticas, targets, vigencias y revisión. Integridad y ausencia de ciclos según política definida. |
| `accounts` | UUID, empresa, instancia AX, `Origen + AccountNum`, nombre, cliente/potencial, estado y atributos derivados necesarios. |
| `contacts` | UUID, empresa, cuenta, clave AX, nombre/cargo y estado; conservar correspondencia y no unir solo por nombre. |
| `catalog_entries` | Enum, `EnumIndex` funcional externo, etiqueta, aplicación, empresa/alcance y activo. `EnumValue` y `SortOrder` no sustituyen a `EnumIndex`; no renumerar enums. |
| `visits` | UUID, empresa, cuenta, propietario, código web, código AX opcional, fecha civil, tipo, contacto, descripción, tres textos, atributos de compatibilidad, versión, autoridad y borrado lógico. |
| `visit_attendees` | UUID, visita, contacto opcional, identidad externa, tipo y snapshot de nombre/cargo; referencia coherente con empresa/cuenta. |
| `external_id_mappings` | Instancia origen, alcance global/empresa, entidad/tabla, clave externa y UUID local; unicidad en ambas direcciones donde aplique. |
| `integration_outbox` | Evento inmutable, agregado/versión, payload/version de contrato, hash, intentos, siguiente intento, lease y resultado; actor real, persona efectiva, sesión de soporte y propietario originales. |
| `command_receipts` | Actor/empresa/operación/clave idempotente únicos, hash de petición y respuesta confirmada; mismo commit que el agregado/outbox. |
| `integration_inbox`, `sync_runs`, `sync_checkpoints` | Deduplicación entrada, lotes, checkpoints persistidos y errores por entidad/empresa. |
| `ax_observations` / staging | Copia observada, hash/versión y procedencia; separada de agregados cuya autoridad es WEB. |
| `audit_events` | Actor real humano o proceso, persona efectiva, sesión de soporte, propietario afectado, empresa, operación, entidad/versión y correlación. Extender la semilla sin inventar OID de usuario para jobs. |
| `support_sessions` | Actor real, persona efectiva, empresa/módulo, motivo, inicio/caducidad/revocación y revisión de acceso. Identificador opaco vinculado al actor; sin tokens Entra del objetivo. |
| `admin_grants` | Capacidades y ámbitos administrativos/soporte del actor real, con revisión y revocación local. Separados de la visibilidad de visitas. |
| `app_settings` / `app_setting_changes` | Claves admitidas, tipo, ámbito, valor validado, versión e historial del cambio; no secretos ni instrucciones ejecutables. |
| `application_logs` | Evento técnico saneado, nivel/componente/versión, contexto y correlación; persistencia en la misma PostgreSQL con retención propia. |

Restricciones de diseño:

- UUID local disponible antes de exportar. `ActividadId`, `RecId`, usuario AX y usuario CRM son referencias externas diferenciadas.
- Incorporar instancia/entorno origen y empresa en las correspondencias. El alcance global debe representarse de manera inequívoca, con unicidad efectiva también para registros globales.
- IDs AX como texto en JSON y tipo SQL exacto después de verificar tamaño/signo. No emparejar por nombre, fecha o descripción.
- `businessDate` como `date`/`YYYY-MM-DD`; instantes de auditoría y sincronización con zona UTC. `TransDate` no es fecha de modificación.
- Cada agregado tiene versión monotónica. Actualización exige versión esperada y genera conflicto explícito si otro editor se adelantó.
- Toda FK relevante debe impedir referencias entre empresas. Tener UUID distintos no sustituye comprobar empresa en consultas y escrituras.
- Mantener referencias históricas aunque un contacto o cuenta se inactive; un maestro ausente en un lote incompleto no se considera borrado.
- No decidir longitudes ni cascadas SQL definitivas sin esquema AOT y reglas probadas. Los límites UI actuales también deben contrastarse con almacenamiento y reglas AX. Longitudes ya comprobadas en el AOT DEV (sección 16.1): `Description` 80, `Name` 80, `AccountNum`/`ActividadId`/`UserId` 15, `INDCreatedByUserId` 5, `AsistenteId`/`AsistenteCargo` 80 y narrativas `memo`; la UI admite 200 caracteres en descripción, por encima del campo AX; el producto adopta 80 (VIS-03).

Índices iniciales candidatos: empresa/fecha/ID, empresa/propietario/fecha/ID, empresa/cuenta/fecha/ID; claves externas únicas; outbox por estado/siguiente intento y por agregado/versión. Confirmarlos con cardinalidad y planes reales antes de optimizar.

## 9. Contrato nuevo y límites del agregado

El recurso principal propuesto es una visita con sus asistentes. El guardado debe validar y persistir cabecera, asistentes, auditoría y outbox en una sola transacción PostgreSQL. Hoy el cliente crea primero la actividad y después los asistentes, con recuperación parcial y compensación condicionada por permisos; conservar esa fragmentación recrearía una fragilidad que la base local permite eliminar.

| Ruta propuesta | Comportamiento |
| --- | --- |
| `GET /api/v1/me` y recurso de contexto de negocio | Identidad validada y empresas/capacidades locales; contrato de contexto por diseñar. |
| `GET /api/v1/companies/{companyId}/accounts` | Búsqueda paginada sobre maestros locales. |
| `GET /api/v1/companies/{companyId}/accounts/{accountId}/contacts` | Contactos permitidos de la cuenta y empresa. |
| `GET /api/v1/companies/{companyId}/catalogs` | Valores activos y ámbitos locales. |
| `GET /api/v1/companies/{companyId}/visits` | Filtros y orden estable; paginación en PostgreSQL. |
| `POST /api/v1/companies/{companyId}/visits` | Alta atómica e idempotente del agregado; `201` tras commit local. |
| `GET /api/v1/companies/{companyId}/visits/{visitId}` | Detalle y capacidades calculadas por backend. |
| `PUT /api/v1/companies/{companyId}/visits/{visitId}` | Sustitución de campos editables con versión esperada. |
| `DELETE /api/v1/companies/{companyId}/visits/{visitId}` | Borrado lógico con versión esperada y evento durable. |
| Recurso de operación de sincronización | Consulta/reintento autorizado, con alcance por empresa y auditoría; rutas por diseñar. |

Estas rutas no existen todavía. La API pública de Visitas y el protocolo técnico AX tendrán audiencias, credenciales y contratos separados. El contrato fija la descripción en 80 caracteres (decisión VIS-03 del 2026-10-21, paridad con el campo AX sin cambio de XPO) y lo aplica también al receptor; para asistentes nuevos exige contacto como referencia y conserva el tipo real, sin reproducir el forzado a `Cliente_Proveedor` del servicio actual.

Usar las convenciones del estándar: JSON camelCase, colecciones `{ items, page, pageSize, total }`, fechas civiles ISO, paginación inicial 25 y máximo 100 si se adopta la norma, orden con desempate estable y Problem Details. Concretar `409` para versión esperada o `412` si se decide `If-Match`; no mezclar dos protocolos de concurrencia.

La clave de idempotencia de alta debe vincularse a actor/empresa/operación y hash de petición. Persistir su recibo/resultado en la misma transacción del agregado/outbox y proteger su unicidad en DB para cubrir peticiones simultáneas. Repetir la misma operación devuelve el resultado ya confirmado; reutilizar la clave con otro contenido se rechaza. La idempotencia del navegador y la de la exportación AX son garantías distintas.

En soporte, el recibo incluye también la persona efectiva y el contexto esperado. No reutilizar una clave de un contexto al cambiar de persona o salir de soporte.

Si una escritura tiene resultado incierto y después caduca soporte, se consulta el recibo original por identificador estable bajo autorización actual del actor y ámbito correspondiente, sin volver a ejecutar la operación ni cambiar su atribución. Una nueva sesión sobre la misma persona puede recuperar ese resultado; el detalle devuelto se limita a lo permitido actualmente. La UI conserva la referencia de la operación pendiente y bloquea un alta equivalente hasta resolverla. La ausencia de recibo mientras pueda haber una petición en curso no se interpreta automáticamente como fallo seguro ni autoriza crear otra visita.

Las respuestas distinguen datos guardados y estado de entrega AX. El estado de negocio Finalizada no significa Sincronizada. La visita local debe poder tener código web mientras su código AX está pendiente.

## 10. Sincronización fiable

Esta sección describe la fase de sincronización, posterior al MVP (VIS-06). El MVP se puebla con una carga inicial única de solo lectura (SQL de solo lectura autorizado o exportación equivalente; canal por confirmar) que sigue los pasos 2 a 4 de 10.1 una sola vez, con recuentos reconciliados, y no envía nada a AX.

### 10.1 Entrada periódica AX → PostgreSQL

1. Extraer con credencial técnica y alcance por empresa, no con los permisos de un comercial. El listado UI recorta por visibilidad y no representa una exportación global.
2. Cargar staging y registrar contrato, origen, lote, integridad y alcance. Validar claves, referencias, enums, propietarios, texto, fechas y duplicados antes de publicar.
3. Aplicar maestros y agregado histórico completo en orden de dependencias: empresas y personas; permisos/catálogos/cuentas; contactos; visitas; asistentes y correspondencias.
4. Guardar el checkpoint con la aplicación confirmada del lote. Un fallo no debe avanzar el cursor perdiendo registros.
5. Para agregados `WEB`, actualizar exclusivamente la observación AX. Verificar acuses y divergencias sin sustituir la visita local.
6. Publicar frescura, último éxito, errores y métricas. La indisponibilidad de AX deja pendiente la importación y mantiene la operación local.

El endpoint de listado actual obtiene un conjunto completo AX y la API pagina en memoria; no es paginación de extracción en origen. Omite textos narrativos y ciertos identificadores de asistentes/contactos. Consultar cada detalle permite recuperar narrativas para un piloto pequeño medido, pero el detalle tampoco devuelve las identidades de asistente/contacto ni todos los atributos inicializados de cabecera. Esa hidratación no completa una migración íntegra: hace falta un contrato de extracción más amplio, además de consistencia e incremento.

No se ha encontrado un contrato de cambios con secuencia durable, altas/modificaciones/borrados, snapshot coherente ni cursor de réplica. Algunos maestros exportados desactivan fechas de modificación. No puede darse por disponible un incremental basado en timestamps. Una fecha de visita tampoco detecta que se editó hoy una visita antigua.

Los catálogos UI solo publican opciones activas; la extracción necesita también los valores inactivos referenciados por histórico. La búsqueda UI de contactos excluye ciertas filas sin nombre/cargo. Esas proyecciones tampoco equivalen a un inventario íntegro de maestros.

Opciones de extracción, pendientes de medir y validar:

| Opción | Condiciones |
| --- | --- |
| Exportador X++/puente con diario de cambios y secuencia | Preferible para incremento fiable; necesita cobertura de mutaciones relevantes y borrados dentro de transacción. Validar captura de todos los caminos AOT. |
| Lectura SQL de solo lectura autorizada | Puede facilitar snapshots o réplica por lotes; exige esquema/aislamiento/claves demostrados. SQL directo de escritura no sustituye reglas X++. |
| Snapshots completos acotados con hashes | Alternativa inicial si volumen y ventana lo permiten; marcar completitud y reconciliar con seguridad. La ausencia en una página o lote fallido nunca demuestra borrado. |

Si se adopta cursor temporal, necesita campo fiable de modificación, desempate por clave, límite superior estable y solapamiento/deduplicación, además de una estrategia separada de borrados. La carga inicial y el diario necesitan una frontera consistente o reconciliación adicional para cubrir cambios durante la extracción.

### 10.2 Salida PostgreSQL → AX

La API guarda agregado, auditoría y outbox en la misma transacción y responde éxito local. Un worker reclama después el trabajo durable y lo envía al puente. El patrón evita perder el envío cuando el proceso cae después del commit. Véanse [transacciones Drizzle](https://orm.drizzle.team/docs/transactions) y [Transactional Outbox de Microsoft](https://learn.microsoft.com/en-us/azure/architecture/databases/guide/transactional-out-box-cosmos).

Cada comando deberá incluir ID de mensaje, contrato/version de schema, instancia destino, empresa, UUID local, tipo de operación, versión del agregado, actor real de origen, persona efectiva, sesión de soporte cuando aplique, propietario y payload inmutable o snapshot explícito. El worker/puente añade su identidad técnica sin sustituir a los actores originales. Conservar hash para reconocer reutilización incompatible de IDs. Trace ID y correlación son diagnóstico, no claves de deduplicación de la operación.

El receptor AX debe guardar la deduplicación y la mutación del agregado en la **misma transacción AX**. Repetir el ID confirmado devuelve su resultado y correspondencias; no vuelve a generar `NumberSeq` ni asistentes. Si AX confirma y se pierde el HTTP de respuesta, el reintento recupera el resultado ya comprometido. Un registro de salida en PostgreSQL por sí solo no resuelve ese caso. El [patrón de consumidor idempotente](https://learn.microsoft.com/en-us/azure/architecture/patterns/idempotent-consumer) explica esta frontera.

El receptor necesita además correspondencia durable única entre instancia/empresa/UUID externo y el registro AX, con última versión y hash admitidos. Importar una visita AX preexistente debe establecer su mapping y versión base antes del primer envío desde WEB; ese envío actualiza el registro vinculado y no ejecuta un alta nueva. Versiones iguales con hashes distintos, versiones atrasadas y cambios incompatibles producen conflicto y reconciliación.

La operación receptora debe aplicar cabecera y asistentes conjuntamente, respetando las reglas y efectos AOT confirmados. Las rutas actuales separadas no demuestran esa garantía. Harán falta un contrato técnico y posiblemente objetos X++ nuevos/adaptados; sus nombres, lista XPO y orden de activación solo pueden cerrarse tras el diseño AOT. En esta propuesta no existe un XPO nuevo listo para importar.

### 10.3 Orden, concurrencia y estados

- Procesar versiones en orden por agregado. Una segunda versión no debe adelantar a la primera, incluidos alta, cambios y borrado.
- Reclamar trabajo mediante lease durable y token de reclamación. Un worker caído deja trabajo recuperable; un worker antiguo no puede confirmar una reclamación nueva.
- Si se usa `FOR UPDATE SKIP LOCKED`, reservarlo para reclamar la cola, no para consultas de negocio que exijan una vista consistente. No mantener una transacción DB abierta mientras se espera a AX. [Documentación PostgreSQL](https://www.postgresql.org/docs/current/sql-select.html).
- Una confirmación de versión anterior solo avanza `lastExportedVersion` hasta esa versión. Nunca marca como sincronizados cambios locales más recientes.
- El receptor debe impedir aplicación de versiones atrasadas o saltos no admitidos. Un lease local no sustituye el control de versión/deduplicación del receptor.
- Reintentar errores transitorios con espera creciente y variación. Validación, mapeos, permisos y datos incompatibles requieren intervención explícita; no un bucle indefinido.
- Estados técnicos propuestos: pendiente, en proceso, reintento programado, entregado, error que requiere revisión y divergencia detectada. No son nuevos estados de negocio de la visita.
- Retención, replay y limpieza deben mantener la garantía de idempotencia. No purgar deduplicación antes del máximo intervalo de reenvío/recuperación acordado.

### 10.4 Borrados, ecos y divergencias

El borrado local deja una marca durable y mantiene correspondencia externa hasta confirmar la política AX. Un borrado en AX no borra automáticamente una visita de autoridad WEB. Los asistentes eliminados también requieren una representación inequívoca en el snapshot/comando.

La copia importada de un cambio exportado debe reconocerse por correspondencia, versión o hash normalizado; no generar un nuevo evento de salida por cada eco recibido. La reconciliación compara campos compartidos, normalizaciones AX y versiones, no bytes JSON sin normalizar.

Un cambio manual inesperado en AX sobre una visita WEB se registra como divergencia y se alerta. No adoptar «última fecha gana» ni sobrescribir silenciosamente. La decisión operativa podrá restaurar desde la web o aprobar una corrección expresa; el corte debe definir quién puede hacerlo.

## 11. Operación y disponibilidad

Decisión del 2026-10-21 (VIS-09, VIS-16): el MVP se despliega en Railway (API, frontend y PostgreSQL), no en el IIS/Windows descrito a continuación, que queda como modelo del estándar y como ubicación del futuro worker/puente AX. El resto de la sección se mantiene como requisitos de operación con ese ajuste.

IIS publicará frontend compilado y `/api/v1` en un origen HTTPS propio; Node escuchará en loopback, PostgreSQL permanecerá privado y el worker tendrá servicio/identidad separados. El wrapper Windows, sitio, DNS, puerto, certificados y recursos deben concretarse con inventario del entorno. El puente conservará sus restricciones Windows/x86 sin propagarlas al backend nuevo.

Configurar secretos fuera del sitio y del repositorio. Las variables `VITE_` son públicas. La semilla exige configuración válida y no contiene un bypass de Entra para resolver datos pendientes.

El guard estándar de usuario verifica firma, issuer, audiencia, tenant, expiración, scope y rol. El worker no debe usar tokens de usuario ni el guard delegado para fingir un proceso. El puente necesita identidad técnica propia de mínimo privilegio y auditoría de proceso más actor original.

La aplicación de usuario conservará la jerarquía de Visitas en un servicio de acceso compartido por dominio. Su futura reutilización por Gastos no activa ahora su lógica de aprobación: las capacidades, políticas de mutación y transacciones se evalúan por módulo.

Métricas iniciales: frescura por maestro/empresa, visitas pendientes, antigüedad del evento más antiguo, intentos, errores permanentes, divergencias y última reconciliación. Separar estas señales de liveness y readiness del servicio local.

Respaldar conjuntamente dominio, outbox, correspondencias y checkpoints. Ensayar recuperación después de restaurar PostgreSQL a un instante anterior a envíos ya aplicados en AX; la deduplicación receptora y la reconciliación deben evitar duplicados. Un restore puede reutilizar el número de una versión con contenido nuevo: AX debe rechazar igualdad de versión con hash distinto y reconciliar antes de reabrir la entrega, sin confirmar un éxito falso. Fijar RPO/RTO y ventana de retención con el responsable antes del corte.

Durante una recuperación, mantener el worker detenido, reconciliar versiones/correspondencias y decidir expresamente cómo recuperar o registrar cambios dentro del RPO antes de reactivar envíos y edición del alcance afectado. La deduplicación evita repetir efectos, pero no reconstruye por sí sola escrituras locales posteriores al backup. Conservar IDs y recibos durante toda la ventana de restore/replay acordada.

El corte requiere cerrar las entradas de escritura anteriores incluidas en su alcance, obtener una frontera/snapshot final consistente, consolidar mappings/versiones base y después habilitar autoridad WEB. Cubrir CRM anterior, cliente AX, jobs e importadores con el inventario de 16.4, salvo la excepción decidida para ofertas: no se modifica ni bloquea la cascada desde `CRMOfertaTable`; sus efectos se detectan como divergencia y se conserva la visita local (VIS-05). Marcar un campo en la web por sí solo no impide las demás escrituras. La importación inicial/reconciliación debe verificar el cierre acordado antes de asumir visitas existentes.

Una reversión de código no revierte datos ni migraciones. Si se decide volver temporalmente a editar desde AX, se necesita drenar/reconciliar o detener de forma controlada la salida y devolver la autoridad del alcance afectado. Publicar el host anterior sin esa transición crearía dos escritores.

### 11.1 Panel de administración de la aplicación

El panel forma parte del mismo sitio y utiliza la misma API, con permisos de servidor propios. La persona entra con Entra; administrar el producto es una capacidad local autorizada, no un privilegio de administrador global del tenant.

| Pantalla inicial | Funciones de la primera versión |
| --- | --- |
| Identidades y empresas | Consultar/corregir correspondencias Entra-persona-AX, estado local, empresas permitidas y ambigüedades. No administra contraseñas corporativas. |
| Permisos y jerarquía de Visitas | Niveles, modos, profundidad, relaciones, vigencias, targets y bloqueos con las validaciones equivalentes a X++. |
| Explicación de acceso | Indicar por qué una persona puede ver/modificar una visita: empresa, permiso efectivo, propietario, camino de jerarquía, targets y regla de mutación. |
| Soporte | Iniciar/finalizar/revocar actuación, identificar operador/persona/empresa y consultar su trazabilidad. |
| Configuración | Editar valores permitidos, tipados y versionados; mostrar alcance, validación, fecha de aplicación y efecto. |
| Logs y auditoría | Buscar incidencias por Trace ID y contexto, consultar cambios y sesiones de soporte con permisos diferenciados. |
| Sincronización AX | Últimos éxitos, frescura, pendientes/errores, divergencias y reintentos controlados de trabajos existentes. |

Capacidades propuestas: administrar configuración, administrar accesos, actuar como otra persona, consultar diagnóstico, consultar auditoría y operar sincronización. Pueden agruparse en pocos perfiles iniciales; no es necesario construir un editor genérico de roles. Las concesiones se comprueban con el actor real y el ámbito de empresa. Un perfil administrativo no elimina las políticas de datos de Visitas.

El primer administrador se configura de forma controlada durante el alta del producto, con identidad Entra concreta y concesión inicial explícita. No se concede al primer usuario que entre ni se infiere por email. La administración de accesos debe impedir elevaciones mediante cambios de la propia identidad y proteger la continuidad de un administrador autorizado. Las retiradas de soporte/acceso invalidan sus revisiones y contextos activos sin esperar al siguiente login.

La configuración web se limita a un catálogo de claves admitidas: frecuencias y lotes de importación dentro de rangos, parámetros funcionales definidos de Visitas, retención autorizada y diagnóstico temporal. Cada ajuste tiene tipo, límites, ámbito aplicación/empresa/módulo, versión esperada, autor, motivo y efecto inmediato o en el siguiente ciclo. Los cambios de configuración/acceso se auditan en su misma transacción; conflictos de edición se muestran expresamente.

Credenciales AX, `DATABASE_URL`, certificados, tenant/audiencia Entra y secretos permanecen en la configuración externa protegida del estándar. El panel puede mostrar si un recurso está configurado y su estado saneado, pero no sus valores secretos. No incluye SQL libre, comandos de sistema, ejecución arbitraria X++ ni un editor de variables de entorno.

La configuración cargada desde DB necesita una base de arranque externa para conectarse y autenticar. Cada proceso registra qué versión efectiva usa y refresca los cambios con un mecanismo acotado; el panel muestra esa propagación. La revocación de acceso se comprueba inmediatamente en backend y no depende de un refresco eventual de ajustes operativos. Los parámetros de deduplicación/idempotencia no pueden reducirse por debajo de la ventana de replay/restore acordada.

### 11.2 Logs en PostgreSQL y diagnóstico desde la web

Se adopta como requisito que los logs de aplicación consultables se almacenen en la misma PostgreSQL del producto. No se depende de archivos de texto para el diagnóstico normal. El almacenamiento lógico de operación puede separarse por esquema/tablas y permisos sin introducir otra base o un servicio de colas externo.

| Información | Garantía y uso |
| --- | --- |
| Auditoría de cambios | Actor real/efectivo, entidad, empresa, versión, operación y campos modificados permitidos. Se confirma en la misma transacción que visita, permiso o ajuste. Si falla, la operación requerida no se confirma. |
| Logs técnicos | Diagnóstico de API, frontend, worker e integraciones, con nivel y contexto saneado. Escritor por lotes y cola de memoria acotada; un fallo aislado del logger no invalida una visita si su transacción/auditoría funcionan. |
| Estado de integración | Outbox, recibos, intentos y checkpoints durables. No se reconstruye el estado del sincronizador buscando mensajes de texto. |

La auditoría registra además inicio/final/revocación de soporte, denegaciones y operaciones administrativas sensibles. Los eventos rechazados se guardan sin confirmar una mutación de negocio; los cambios aceptados quedan unidos a su transacción. Si no puede guardarse la auditoría obligatoria de una sesión/acción privilegiada, esta se rechaza. La aplicación ofrece consulta de auditoría sin edición; mantenimiento y retención se ejecutan con permisos separados.

Campos técnicos propuestos: ID, fecha UTC, entorno, servicio/componente, release/SHA, nivel, código de evento, mensaje saneado, Trace ID/request ID/correlación, actor real y efectivo, sesión de soporte, empresa/módulo, entidad/versión, estado HTTP, duración y tipo de error. El detalle técnico y stack se acotan y sanean antes de persistir; los campos concretos se cerrarán en el contrato del producto.

Registrar fallos de frontend autenticado mediante un contrato acotado de eventos de cliente, enlazando Trace ID cuando exista, versión de build, ruta normalizada y contexto efectivo. El backend atribuye identidad/empresa a partir de la petición validada; un mensaje del navegador no se convierte en auditoría autoritativa. Aplicar límites, deduplicación y muestreo a eventos repetidos. Antes de autenticar, el diagnóstico puede quedar en la consola del cliente y en el proveedor de identidad; no se abre por ello un receptor anónimo ilimitado.

El visor filtra por rango de fecha, nivel, componente, empresa, actor real/efectivo, sesión de soporte, entidad, evento y Trace ID. Utiliza paginación por cursor, límites y un rango inicial acotado. Desde una incidencia se puede navegar a petición, sesión de soporte y entrega AX relacionados según permisos. La explicación de acceso se consulta aparte de los logs y muestra la revisión de reglas utilizada.

No guardar tokens, cookies, contraseñas, conexiones, audio, narrativas completas, parámetros SQL o dumps de peticiones/respuestas. Para textos de visita, auditar el campo afectado y su versión/hash cuando baste; un historial de contenido funcional requeriría su propia especificación. Los errores del driver/proveedor se convierten en estructuras saneadas. Esta política y las pruebas de fallo del logger siguen la [guía de logging de OWASP](https://cheatsheetseries.owasp.org/cheatsheets/Logging_Cheat_Sheet.html).

El escritor de logs utilizará timeout, límites de tamaño, batches y una cola finita. Separar su cuota de conexiones de las transacciones de Visitas para que una tormenta de errores no agote el pool de negocio; dimensionar ambas contra el máximo global PostgreSQL. Ante saturación se agregan/descartan primero eventos de diagnóstico menos relevantes y se mantienen contadores de pérdidas. No usar el mismo logger para registrar recursivamente sus fallos de INSERT ni generar un nuevo log normal por cada fila leída en el visor.

Si PostgreSQL está caído, la aplicación no puede registrar allí la causa de esa caída. Se necesita una señal mínima saneada por `stderr`/monitorización del servicio; Windows Event Log solo si se configura ese destino. Sin un segundo almacén durable no se promete conservar todos los logs técnicos durante caída de DB/proceso. Esto no cambia la garantía transaccional de auditoría de las operaciones que sí se hayan confirmado. El visor de la misma web tampoco es un monitor independiente de disponibilidad.

| Conservación inicial propuesta | Plazo sujeto a volumen y responsable |
| --- | --- |
| DEBUG/TRACE | 24–72 horas; activación acotada por contexto/componente y caducidad automática. |
| INFO/WARN | 30 días. |
| ERROR | 90 días. |
| Auditoría | 12 meses como propuesta operativa, sin afirmar una obligación legal concreta. |
| Recibos de integración | Ventana de replay/restore acordada, independiente de la retención técnica. |

Medir eventos/día, tamaño medio, índices, WAL y backups antes de fijar cuotas. Implementar limpieza por lotes o particiones si el volumen lo justifica, fuera del camino interactivo. Reducir retención requiere revisión, auditoría y respeto de las ventanas de recuperación; no exponer un botón de borrar arbitrariamente la auditoría desde el panel.

## 12. Fases y condiciones de avance

Secuencia acordada el 2026-10-21: las fases 0 a 3 forman el MVP, con la fase 2 reducida a una carga inicial única de solo lectura; la fase 4 (entrega AX), el bloqueo de escritores y el corte (fase 5) se abordan en la fase de sincronización posterior. La tabla conserva el detalle de cada fase; el calendario vigente está en `IND_APP_STANDARD/proyectos/CRM_INSERTEC/ESPECIFICACION.md`.

| Fase | Entregable | Condición verificable para avanzar |
| --- | --- | --- |
| 0. Contrato y AOT | Inventario completo de tablas, hooks, claves, reglas, permisos, volúmenes y estrategia de corte. Tablas, enums, claves, numeración, escritores y validaciones ya inventariados sobre el AOT DEV (sección 16); faltan volúmenes, datos SQL, contrato de extracción/aplicación y procedimiento de corte. | Resolver propietarios ambiguos, `ActividadId` no único, política de maestros/borrados y contrato de extracción/aplicación. |
| 1. Base independiente | Producto `web-api`, adopción fijada, Entra, PostgreSQL, módulos/CI, capacidades administrativas, auditoría y logger DB inicial. | Arranque, identidad real, esquema y permisos sin AX; acceso admin controlado y fallo del logger probado. |
| 2. Importación y consulta | Staging, maestros/histórico, jerarquía equivalente, panel de acceso/configuración/diagnóstico y sesión de soporte. | Recuentos y muestreo; consulta bajo permisos efectivos; separación de actor/contexto, revocación y aislamiento de pestañas. |
| 3. Dominio local | Alta/edición/borrado y asistentes atómicos; auditoría/outbox; soporte con las mismas capacidades de escritura del usuario efectivo. | Operaciones con AX caído, concurrencia/idempotencia y paridad con/sin soporte; atribución doble de cada cambio. |
| 4. Entrega AX | Receptor idempotente, worker, acuses, orden y recuperación; visor/reintentos de integración. | Duplicados, timeout tras commit, versiones/borrados y conservación del contexto original en AX aislado. |
| 5. Corte piloto | Empresa/cohorte delimitada con web como único editor, soporte y reconciliación. | Edición AX bloqueada/controlada, cola drenada, consistencia y aceptación de usuarios. |
| 6. Ampliación | Más usuarios/histórico/empresas y retirada de dependencia interactiva CRM. | Operación sostenida, backup/restore y métricas aceptadas. |

Se puede construir el dominio local antes del receptor definitivo, pero no habilitar edición productiva con una ruta de retorno AX sin validar. La consulta piloto debe conservar claramente qué sistema es todavía editor de cada alcance.

La duración depende del AOT ausente, volumen/histórico, matriz de acceso y contrato de extracción. El análisis de fuentes no permite una estimación cerrada responsable. El esfuerzo principal está en reglas, identidad de datos y fiabilidad de integración; la plantilla acelera la base técnica.

### Recorrido de adopción del maestro

1. Abrir `IND_APP_STANDARD` y fijar la revisión revisada.
2. Completar nombre, población piloto y alcance con los datos ya conocidos.
3. Cuando se solicite crear, revisar una carpeta hermana nueva/vacía, preparar ficha `web-api`, previsualizar, generar y ejecutar `check`.
4. Ejecutar pruebas locales y preparar CI/repositorio propio dentro del alcance que se solicite.
5. Abrir el producto generado y mantener allí `docs/ESPECIFICACION.md`, modelo, contratos y decisiones. El maestro distribuye la base.

El generador no copia automáticamente una especificación particular del perfil genérico ni implementa Visitas. No desarrollar el producto dentro de `templates/`. La generación, importación real, configuración Entra, activación AX y publicación son entregables diferentes.

## 13. Matriz de aceptación necesaria

| Área | Casos mínimos |
| --- | --- |
| Entra | Login/logout/caducidad, tenant/audiencia incorrectos, asignación/scope/rol ausentes y cambio de cuenta. |
| Autorización | Empresa ajena, propietario manipulado, lectura frente a mutación, jerarquías/targets/vigencias y datos sin propietario resuelto. |
| Soporte | Lectura/escritura equivalente al usuario efectivo desde activación; sin privilegios añadidos del administrador; sesión ajena, caducidad/revocación, contextos de pestaña y ausencia de fallback silencioso. |
| Escritura incierta de soporte | Alta confirmada, respuesta perdida, caducidad, nueva sesión y recuperación del recibo original: una sola visita, auditoría y evento outbox, sin convertir la recuperación en otra alta. |
| Administración | Capacidades por actor/empresa, alta inicial controlada, explicación de permisos, config tipada/versionada y denegación de cambios que eleven privilegios o expongan secretos. |
| Logs y auditoría | Consulta acotada y autorizada, saneamiento, frontend no autoritativo, retención, saturación/fallo DB sin recursión y auditoría atómica con el cambio. |
| UI | Alta/detalle/historial, vacío/error/recuperación, cancelación de filtros, asistentes opcionales, seis culturas si se mantienen y foco/teclado. |
| Responsive | Escritorio, 390×844 y 320×568; paisaje y acciones siempre accesibles. |
| Transacción local | Fallo en asistentes o outbox revierte todo; envío repetido de alta devuelve mismo resultado; dos editores producen conflicto. |
| Extracción | Lote repetido, corte durante snapshot, paginación estable, actualización de una visita antigua, bajas y referencias inactivas. |
| Exportación | AX detenido, respuesta perdida tras commit, worker muerto, lease expirado, duplicado concurrente y versiones fuera de orden. |
| Reconciliación | Eco de exportación, normalización de campos, cambio manual AX y prohibición de pisar autoridad WEB. |
| Compatibilidad AX | Historial con más de 50 visitas en el rango; borrado de visita de más de 60 días; alta en `REF` sin tipo de visita; dos visitas con el mismo `ActividadId`; descripción de más de 80 caracteres; borrado de una oferta con actividades; asistente sin contacto y contactos homónimos. |
| Recuperación | Restore DB con mensajes ya aplicados en AX; replay seguro; backups y rollback de código/migraciones. |
| Entorno | AOT importado/compilado/sincronizado si procede, Entra real, DB real aislada, HTTPS y operación desde otro equipo. |

Las pruebas de la plantilla y del CRM son material de partida. No prueban el comportamiento de la aplicación futura ni sustituyen el ensayo del receptor AX.

## 14. Decisiones todavía pendientes

- Decidido el 2026-10-21 (VIS-01): empresa piloto `TAZ` (AZAROS), corte por empresa completa; pendiente la lista de usuarios piloto.
- Política de autoridad y administración de usuarios comerciales y permisos después del corte inicial (clientes y contactos: decidido en VIS-10).
- Decidido el 2026-10-21 (VIS-07): la carga inicial del MVP incluye solo las visitas de 2026 de la empresa piloto; las narrativas no tienen límite funcional porque sus EDT (`CRMComentarios`, `FreeTxt`) son memo sin tamaño en el AOT; queda por fijar un tope técnico de la API. Volumen real, frecuencia y retraso de la sincronización siguen pendientes.
- Decidido el 2026-10-21 (VIS-06): el MVP se puebla con una carga inicial única de solo lectura; canal concreto (SQL de solo lectura o exportación) por confirmar. El contrato de cambios/borrados, el receptor idempotente (nuevo servicio X++ o reutilización, pendiente) y el bloqueo de escritores AX (VIS-05) pasan a la fase de sincronización, posterior al MVP. Decidido también (VIS-18): el MVP es desechable; al iniciar la sincronización se reinicia la base con una nueva carga desde AX y se descarta lo creado en el MVP, con aviso visible en la web. El procedimiento de corte (VIS-17) se difiere a esa fase.
- Compatibilidad de los consumidores AX con visitas numeradas por el receptor (el código visible ya está decidido en VIS-02).
- Decidido el 2026-10-21 (VIS-14): en la fase de sincronización el borrado lógico web borra cabecera y asistentes en AX; pendientes obligaciones de conservación y tombstones del receptor.
- Decidido el 2026-10-21 (VIS-13): soporte de 30 minutos renovable y revocable, concedido a quien tenga `FullAccess` en el módulo de Visitas según la configuración replicada de AX.
- Decidido el 2026-10-21 (VIS-16): el MVP (API, frontend y PostgreSQL) se despliega en Railway, desviación documentada respecto al IIS/Windows de la sección 11; la carga inicial se ejecuta desde la red interna y el futuro worker/puente AX correrá en la red interna conectado a Railway. Dominio del MVP: `crm.insertec-dev.biz` (por comprar), con certificado automático de Railway; `dev.insertec.biz:2053` y la API `:2083` siguen en el servidor actual. El entorno `dev` de Railway se despliega automáticamente desde la rama de desarrollo del producto; producción, por promoción manual.
- Decidido el 2026-10-21 (VIS-12): seis culturas y dictado por voz desde el MVP; pendientes proveedor y claves propias del producto, moderación y cuotas.
- Decidido el 2026-10-21 (VIS-11): el AOT DEV manda; `INDMutationPolicy` con dos valores en el producto, comprobación SQL pendiente al cargar permisos.
- Decidido el 2026-10-21 (VIS-09): PostgreSQL alojado en Railway, cuenta por crear por el usuario. Pendientes: conectividad saliente TLS desde el servidor Windows y desde la carga inicial, aprobación del alojamiento externo de datos de clientes y contactos, bases separadas, roles, copias y restore; identidad de servicio y recurso HTTPS del producto.
- Decidido el 2026-10-21 (VIS-10): clientes y contactos editables en la web desde el MVP; su reconciliación con AX y la asignación de `AccountNum` se diseñan en la fase de sincronización (VIS-18 ampliado).
- Decidido el 2026-10-21 (VIS-08): Entra solo autentica; la autorización se replica de las tablas de configuración AX enlazando el OID de Entra con el usuario CRM (`INDWebUserEntraIdentity`) y aplicando la lógica del resolver y de los métodos de permiso de `INDCRMVisitsService`. Soporte y su duración están decididos en VIS-13; quedan pendientes el nivel AX que concede administración y los detalles de concesión/revocación.
- Decidido el 2026-10-21 (VIS-15): retención inicial de 11.2 (DEBUG 24–72 h, INFO/WARN 30 días, ERROR 90 días, auditoría 12 meses); cuotas tras medir; pendientes señal mínima sin base y propagación de configuración.
- Decidido el 2026-10-21 (VIS-02): dos códigos. Código web propio, único y visible siempre; `ActividadId` lo asigna el receptor AX con la secuencia de Visitas (`CRMCustVisitaId`) y se guarda como atributo no único; correspondencia por `RecId + empresa`. Queda pendiente solo el formato del código web.
- Decidido el 2026-10-21 (VIS-03): descripción de 80 caracteres en web, API y receptor; la UI actual permitía 200 y AX recortaba al guardar, así que el histórico ya cumple el límite.
- Mecanismo de bloqueo de los escritores AX que no pasan por el servicio (formularios, `TraspasarConsultasOfertasACliente`, `CerrarActividades`, `AsignarCuenta`, API antigua `INDCrmApiClass_Old` y jobs históricos, sección 16.4): pendiente (VIS-05) entre un campo de autoridad con validación X++ y una restricción por permisos/menús. Decidido: las ofertas quedan fuera del alcance y no se modifica `CRMOfertaTable`; su cascada de borrado sobre actividades se detecta como divergencia en la reconciliación y la visita local se conserva.
- Decidido el 2026-10-21 (VIS-04): el borrado se limita a visitas de hasta 60 días como política configurable por empresa, con excepción administrativa auditada; `TipoVisita` obligatorio en todas las empresas; la valoración la indica el usuario en alta y detalle y el receptor AX no la sustituye por el valor por defecto de la empresa.
- Divergencia entre XPO del repositorio y AOT DEV en `INDMutationPolicy` (sección 16.8): confirmar qué versión está activa antes de importar la configuración de permisos.

Las decisiones de escritor único, foco actual en Visitas, jerarquía reutilizable, soporte con lectura/edición desde activación, panel administrador y logs en la misma PostgreSQL ya están confirmadas y no forman parte de los pendientes.

## 15. Inventario de evidencia local

Las líneas identifican puntos de entrada de las revisiones indicadas; al evolucionar el código deben comprobarse de nuevo. Los enlaces son a las fuentes, no a una segunda copia de sus contratos.

| Ref. | Fuente | Evidencia |
| --- | --- | --- |
| E01 | [APP Program.cs](../../../../IND_CRM_APP/Program.cs) y [proyecto](../../../../IND_CRM_APP/IND_CRM_APP.csproj) | Host MVC/OIDC, DI y plataforma web. |
| E02 | [IndAuthContextService](../../../../IND_CRM_APP/App/Services/IndAuthContextService.cs), líneas 279–303 y 388–402 | Contexto funcional y token técnico dependientes de API/AX. |
| E03 | [fuentes React de Visitas](../../../../IND_CRM_APP/Web/wwwroot/react/src/pages/visitas) | Historial, creación y detalle existentes. |
| E04 | [API proyecto](../../../IND_CRM_API.csproj), [Startup](../../../Startup.cs), [sesiones AX](../../operations/estabilidad-sesiones-ax.md) | .NET Framework/x86, OWIN y restricciones COM. |
| E05 | [CrmActivitiesController](../../../Controllers/CRM/CrmActivitiesController.cs), líneas 59–165, 302–360 y 1189–1377 | Validación, actor, mutaciones, filtros y paginación en memoria. |
| E06 | [CrmVisitsController](../../../Controllers/CRM/CrmVisitsController.cs) | Contratos de asistentes y actor enviado a AX. |
| E07 | [BaseCrmController](../../../Controllers/CRM/BaseCrmController.cs), líneas 146–205 y 248–277 | Contexto firmado frente a helper de cabecera AX. |
| E08 | [INDCRMVisitsService.xpo](../../../.codex/Axapta/INDCRMVisitsService.xpo), líneas 110–230 y 981–1105 | Alta/update, numeración, inicializadores, propietario, defaults y transacciones. |
| E09 | [INDCRMVisitsService.xpo](../../../.codex/Axapta/INDCRMVisitsService.xpo), líneas 234–468 y 675–807 | Asistentes, borrado y proyección limitada del listado. |
| E10 | [resolver de visibilidad](../../../.codex/Axapta/INDControlDataVisibilityResolver.xpo) | Identidad/persona, visibilidad, mutación, jerarquías y selección restrictiva. |
| E11 | [modelo AX de acceso](../diagrams/technical/integration/ax-bi-access-table-schema.md) y sus fuentes XPO | Grano por empresa/global, ambigüedades de identidad y límites AOT. |
| E12 | [CrmAccountsController](../../../Controllers/CRM/CrmAccountsController.cs), [CrmEnumsController](../../../Controllers/CRM/CrmEnumsController.cs), [catálogo AX](../../../.codex/Axapta/INDAxEnumAppCatalogTable.xpo) | Maestros y catálogos; campos de modificación no universales. |
| E13 | [INDSpeechController](../../../Controllers/System/INDSpeechController.cs) y [INDTextFormattingController](../../../Controllers/System/INDTextFormattingController.cs) | Proveedores de dictado/formato existentes. |
| E14 | [IND_INTERNAL_API README](../../../../IND_INTERNAL_API/README.md) y [controladores internos](../../../../IND_INTERNAL_API/Controllers/Internal) | Superficie técnica actual; no aporta réplica de Visitas. |
| E15 | [estándar normativo](../../../../IND_APP_STANDARD/estandar/ESTANDAR_APLICACIONES_WEB_IND.md), secciones 2–9 | Stack, organización, datos, acceso, operación y calidad. |
| E16 | [API package](../../../../IND_APP_STANDARD/templates/web-api/API/package.json), [frontend package](../../../../IND_APP_STANDARD/templates/web-api/FRONTEND/package.json), [standard.json](../../../../IND_APP_STANDARD/standard.json) | Versiones fijadas y manifiesto del paquete. |
| E17 | [guard Entra](../../../../IND_APP_STANDARD/templates/web-api/API/src/auth.ts), líneas 25–79, y [schema semilla](../../../../IND_APP_STANDARD/templates/web-api/API/src/database/schema.ts) | Claims/scope/rol; solo tabla de auditoría, todavía sin dominio Visitas. |
| E18 | [config real](../../../../IND_APP_STANDARD/templates/web-api/API/src/config.ts), [Problem Details](../../../../IND_APP_STANDARD/templates/web-api/API/src/problem.ts) y [contrato normativo](../../../../IND_APP_STANDARD/skills/ind-api-problem-details/references/CONTRATO.md) | Diferencias de configuración y errores que cerrar en adopción. |
| E19 | [planificador](../../../../IND_APP_STANDARD/.agents/skills/ind-project-planner/SKILL.md) y [nueva aplicación](../../../../IND_APP_STANDARD/.agents/skills/ind-project-planner/references/NUEVA_APLICACION.md) | Ficha, generación explícita, producto independiente y separación de entregas. |
| E20 | [decisiones del estándar](../../../../IND_APP_STANDARD/estandar/ESTANDAR_IND/DECISIONES.md), [acceso](../../../../IND_APP_STANDARD/estandar/ESTANDAR_IND/ACCESO_Y_ENTREGA.md) y [arquitectura documental](../../../../IND_APP_STANDARD/skills/ind-architecture-docs/SKILL.md) | Contrato API, identidad, versiones/concurrencia y convención documental del producto nuevo. |
| E21 | [useCreateSubmit](../../../../IND_CRM_APP/Web/wwwroot/react/src/hooks/useCreateSubmit.ts), líneas 207–216 y 249–357, y [VisitNarrativeFields](../../../../IND_CRM_APP/Web/wwwroot/react/src/components/visitas/VisitNarrativeFields.tsx), líneas 34–63 | Validación UI, alta parcial recuperable, asistentes y narrativas. |
| E22 | [HistorialController](../../../../IND_CRM_APP/Web/Controllers/Visitas/HistorialController.cs), líneas 49–53 y 148–243, y [HistoryPage](../../../../IND_CRM_APP/Web/wwwroot/react/src/pages/visitas/historial/HistoryPage.tsx) | Rango inicial, paginado/filtros, errores frente a vacío y preservación de TraceId. |
| E23 | [INDModuleAuthorizeFilter](../../../../IND_CRM_APP/App/Infrastructure/Security/Filters/INDModuleAuthorizeFilter.cs), líneas 291–301, y [VisitMutationPermissionService](../../../../IND_CRM_APP/App/Services/VisitMutationPermissionService.cs) | Umbrales de módulo y permiso por registro comprobados en servidor web. |
| E24 | [test de recuperación](../../../../IND_CRM_APP/tests/frontend-mutation-recovery.test.mjs), [caché](../../../../IND_CRM_APP/tests/history-query-cache.test.mjs), [fallos de historial](../../../../IND_CRM_APP/tests/history-query-failures.test.mjs) y [sesión](../../../../IND_CRM_APP/tests/authentication-session-lifecycle.test.mjs) | Regresiones aisladas ejecutadas sobre el CRM actual. |
| E25 | Fuente externa `AOT_DEV_20261910/AOT/Classes/INDControlDataVisibilityResolver.xpo`, líneas 83–138, 196–349 y 529–655 | Algoritmo de visibilidad/mutación, selección de permiso efectivo y filtrado final de personas. |
| E26 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Tables/INDModuleDataVisibilityHierarchyLine.xpo`, líneas 357–450 y 585–677, y `INDModuleDataVisibilityTarget.xpo`, líneas 590–653 | Validación de jerarquía, vigencias, recorrido y expansión de targets. |
| E27 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Tables/INDWebModuleAccessLevel.xpo`, líneas 234–247 y 554–667, y `CRMUsuarioSubordinadoTable.xpo`, líneas 224–325 | Permiso global con empresa indirecta y distinción de subordinación para aprobación de gastos. |
| E28 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Tables/CRMActividadTable.xpo`, líneas 1372–1393 y 1666–1836; `CRMCustVendVisitaAsistenteTable.xpo`, líneas 506–645; `CRMAccountTable.xpo`, líneas 409–421; `CRMContactoTable.xpo`, líneas 1074–1167 | Tablas antes ausentes del repositorio: claves, validaciones y efectos laterales comprobados en fuente. |
| E29 | [app.ts de la semilla](../../../../IND_APP_STANDARD/templates/web-api/API/src/app.ts), [conexión DB](../../../../IND_APP_STANDARD/templates/web-api/API/src/database/database.ts) y [configuración](../../../../IND_APP_STANDARD/templates/web-api/API/src/config.ts) | Logger estructurado/redacción, pool inicial y ausencia de destino PostgreSQL/configuración del logger del producto. |
| E30 | [auth.tsx del frontend](../../../../IND_APP_STANDARD/templates/web-api/FRONTEND/src/auth.tsx), líneas 130–134, y [guard de API](../../../../IND_APP_STANDARD/templates/web-api/API/src/auth.ts) | `/me` corresponde al actor MSAL real; el contexto de soporte se añade por separado. |
| E31 | [ActivitiesFilter](../../../../IND_CRM_APP/App/Models/Activities/ActivitiesFilter.cs), [ApiClientService](../../../../IND_CRM_APP/App/Services/ApiClientService.cs), líneas 1112–1120, [CrmActivitiesController](../../../Controllers/CRM/CrmActivitiesController.cs), líneas 27 y 268–273, y [HistorialController](../../../../IND_CRM_APP/Web/Controllers/Visitas/HistorialController.cs), líneas 195–204 | Listado sin página desde APP, `MaxPageSize = 50` en API y repaginación local en MVC. |
| E32 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Tables/CRMActividadTable.xpo`, líneas 40–798 (campos), 899–923 (índices), 937–1296 (relaciones), 1323–1324 (sin DeleteActions), 1687–1708, 1788, 1808–1822 y 1824–1832 | Campos, longitudes, enums, ausencia de índice único, `initValue`/`insert`, `validateDelete` 60 días y `validateWrite` de `REF`. |
| E33 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Tables/CRMCustVendVisitaAsistenteTable.xpo`, líneas 40–285, 335, 349–442, 506–620; `CRMCustListaVisitasTable.xpo`, líneas 284, 445–518; `CRMAccountTable.xpo`, líneas 410–435, 605–747; `CRMContactoTable.xpo`, líneas 696–720, 1074–1163 | Asistentes sin índice único ni cascada, efectos en lista de visitas, claves de cuentas/contactos y validaciones. |
| E34 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Base Enums/` (`CRMActividadType`, `CRMTipoVisita`, `INDContactMethod`, `CRMActivityStatus`, `CRMActivityPrioridad`, `CRMCustVisitaValoracion`, `CRMCustVendVisitaAsistente`, `CRMActividadOrigen`, `CRMConsultaOrigen`, `INDMutationPolicy`, `INDDataVisibilityMode`, `SysAccessRights`) | Nombres reales y valores de los enums del módulo y de acceso. |
| E35 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Tables/CustParameters.xpo`, líneas 1629–1633 y 1811–1815; `Classes/NumberSeqReference_Customer.xpo`, líneas 290–291 y 399–400; `Forms/CRMCustVisitasTable.xpo`, líneas 157–169; `Forms/CRMActividadTable.xpo`, línea 152 | Dos secuencias numéricas distintas para `ActividadId`. |
| E36 | Fuente externa `AOT_DEV_20261910/AOT/Forms/CRMCustVisitasTable.xpo` (DataSources 173 y 543, métodos 323, 454, 512–531, 686–724), `Forms/CRMActividadTable.xpo` (DataSource 175, 91–92, 370–372, 425–442, 3357–3360), `Data Dictionary/Tables/CRMOfertaTable.xpo` (1639–1643, 1907–1915, 1960–1968, 2060, 3005–3010), `CRMConsultaTable.xpo` (1376–1385, 1433, 1580), `CustTable.xpo` 8179, `CRMClientePotencialTable.xpo` 1702, `CRMProveedorPotencialTable.xpo` 1265, `Classes/INDCrmApiClass_Old.xpo`, `Classes/INDCRMActividadUserIdRepair.xpo` 249 | Escritores AX de visitas y asistentes fuera del servicio; cascada desde ofertas. |
| E37 | Fuente externa `AOT_DEV_20261910/AOT/Data Dictionary/Tables/INDWebApp.xpo` (sin índices), `INDWebModule.xpo` 167, `INDWebModuleAccessLevel.xpo` 235, `INDModuleDataVisibilityTarget.xpo` 233, `INDModuleDataVisibilityHierarchyLine.xpo` 214, `INDPersonaTable.xpo` 561–626, `INDWebUserEntraIdentity.xpo` 140, `INDCiasPermitidas.xpo` 139, `CRMUsuarioTable.xpo` 738 | Alcance global/por empresa e índices únicos de las tablas de acceso. |
| E38 | [ModuleRecordMutationPermissionService](../../../../IND_CRM_APP/App/Services/ModuleRecordMutationPermissionService.cs), líneas 157–203 y 521–565; [ContactsCombobox](../../../../IND_CRM_APP/Web/wwwroot/react/src/components/visitas/ContactsCombobox.tsx), líneas 206–208; [useDetailMutations](../../../../IND_CRM_APP/Web/wwwroot/react/src/pages/visitas/detail/useDetailMutations.ts), líneas 124–176; [ActivityDtoArrayConverter](../../../../IND_CRM_APP/App/Models/Activities/ActivityDtoArrayConverter.cs), líneas 252–256; [VisitasController](../../../../IND_CRM_APP/Web/Controllers/Visitas/VisitasController.cs), líneas 395–397, 438 y 584–633 | Umbrales de mutación por registro, nombre de asistente como identificador, edición sin validar obligatorios, canal de contacto limitado a 0–2 y rutas MVC sin uso. |

## 16. Inventario de Fase 0 sobre el AOT DEV y contraste de decisiones

Fuente: exportación AOT DEV del 19/10/2026 (`AOT_DEV_20261910/AOT`, Axapta 3.0 SP2), leída en Windows-1252 y contrastada con `IND_CRM_APP` `b5fdac64`, `IND_CRM_API` `c5029c6` e `IND_APP_STANDARD` `b05c2af`. Es evidencia del código exportado ese día, no del AOT instalado ni de los datos SQL. Nada de lo descrito aquí se ha implementado.

### 16.1 Tablas y campos del módulo

`CRMActividadTable` es por empresa (`SaveDataPerCompany=Yes`), no tiene índice único ni `PrimaryIndex` y sus tres índices (`ModifiedDateIdx`, `CreatedDateIdx`, `UserIdx`) admiten duplicados. Tiene 32 campos; los que afectan a Visitas:

| Campo AX | Tipo y longitud comprobados | Obligatorio AX | Uso en el producto |
| --- | --- | --- | --- |
| `ActividadId` | `CRMActividadId` → `CRMAccountNum`, cadena 15 | Sí, `AllowEdit=No` | Código AX heredado; no único (16.3). |
| `AccountNum` | `CRMAccountNum`, cadena 15 | Sí | Clave de cuenta junto con `Origen` de `CRMAccountTable`. |
| `ActividadType` | enum `CRMActividadType` (0 None, 1 Fax, 2 Email, 3 LlamadaTelefono, 4 Visita, 5 Tarea) | Sí | Fijo `Visita`; el producto no importa otros tipos. |
| `UserId` | `CRMUserId` → cadena 15 | Sí | Usuario CRM heredado; no es el propietario funcional. |
| `INDCreatedByUserId` | `INDCreatedByUserId` → `userId`, cadena 5 | No | Propietario funcional; se rellena en `insert()` si llega vacío. |
| `Description` | `Description`, cadena 80 | No | Asunto; la UI permite 200. |
| `TransDate` | `TransDate`, fecha | Sí | Fecha civil de la visita. |
| `Comentarios`, `VisitaAntecedentes`, `VisitaConclusiones` | `CRMComentarios`, memo | No | Narrativas sin límite práctico. |
| `Name` | `Name`, cadena 80 | No | Copiado de la cuenta en `insert()`. |
| `Status` | enum `CRMActivityStatus` (0 None, 1 EnCurso, 2 Finalizada, 3 Anulado) | No | `initValue` pone EnCurso; el servicio graba Finalizada. |
| `Prioridad` | enum `CRMActivityPrioridad` (0 None, 1 Normal, 2 Urgente, 3 LargoPlazo) | Sí | Política fija Normal. |
| `VisitaValoracion` | enum `CRMCustVisitaValoracion` (0 None, 1 Bien, 2 Regular, 3 Mal) | No | Bien salvo empresa `RSI`. |
| `TipoVisita` | enum `CRMTipoVisita` (0 None, 1 Comercial, 2 Tecnica) | No | Obligatorio solo en `REF` por `validateWrite`. |
| `ContactMethod` | enum `INDContactMethod` (0 InPerson, 1 PhoneCall, 2 OnlineMeeting) | No | Catálogo configurable; el APP recorta a 0–2 al listar. |
| `Country` | `CountryId`, cadena 15 | No | Derivado de la cuenta. |
| `Origen`, `OrigenCustAccount`, `CustAccount`, `RefRecId`, `VisitaTipoExpediente`, `VisitaExpedienteId` | enums `CRMActividadOrigen` / `CRMCliente_ClientePotencial_Proveedor` / `CRMCustVisitaTipoExpediente`, cadenas y `RefRecId` | No | Enlaces a consulta/oferta/proyecto/evento; conservar como atributos de compatibilidad, sin editarlos en el producto. |
| `Asistentes`, `AsistentesCliente` | `FreeText`, cadena 255 | No | Texto libre histórico; distinto de la tabla de asistentes. |

`CRMCustVendVisitaAsistenteTable` (por empresa, sin índice único; `RefRecIdIdx` admite duplicados): `RefRecId` (visita), `AsistenteTipo` (enum `CRMCustVendVisitaAsistente`: 0 None, 1 Cliente_Proveedor, 2 Insertec, 3 Externo; obligatorio), `AsistenteId` (`Name`, cadena 80), `ContactoRecId` (`RefRecId`), `AsistenteCargo` (cadena 80), `AsistenteTipoExterior`, `AsistenteTipoExteriorAccountNum`, `Phone`, `Email`, `INDCreatedByUserId`. `CRMActividadTable` no declara `DeleteActions`: los asistentes solo se borran si quien borra la visita lo hace explícitamente, como hace el servicio; otros caminos dejan huérfanos. `modifiedField(ContactoRecId)` copia nombre y cargo desde `CRMContactoTable`, y `insert`/`update`/`delete` mantienen `CRMCustListaVisitasTable` solo para `AsistenteTipo=Insertec`.

`CRMAccountTable`: índice único `Origen + AccountNum`; `INDSearchKey` (cadena 130) se regenera en `insert`/`update` como `accountNum|nombreComercial|razonSocial` en minúsculas; `FindByAccountNum` ignora `Origen`. `CRMContactoTable`: `AccountNum` obligatorio, índices no únicos por `AccountNum` y `RecId`, `validateField` comprueba que `PlantaRecId` pertenezca a la cuenta y valida tres correos; sin reglas por empresa. `CRMCustListaVisitasTable`: índice único `UserId + TransDate + Origen + CustAccount`, `validateDelete` de 60 días para cualquier línea y una excepción heredada para un usuario concreto en `ActualizarHojasDeGastos`; no trasladarla como regla general de Visitas.

### 16.2 Nombres reales de los enums

Los nombres `ActividadType`, `TipoVisita`, `ContactMethod` o `VisitaValoracion` son campos, no enums. Los enums son los indicados en la tabla anterior, todos con `UseEnumValue=Yes`. La importación debe conservar el nombre del enum y su código numérico real de negocio, con una correspondencia explícita entre el `EnumIndex` publicado por el catálogo actual y el valor declarado en AOT; no sustituirlo por el campo homónimo `EnumValue` de otra tabla ni por `SortOrder`. `CRMOfertaGrupoTecnicoHornos` demuestra que no se puede inferir el código desde su posición ni renumerar valores. Las etiquetas de `CRMActivityStatus`, `CRMActivityPrioridad` y `CRMCustVisitaValoracion` son identificadores de etiqueta (`@IST188`, `@SYS665`...), no texto: el producto necesita sus propias etiquetas por cultura.

### 16.3 Numeración de `ActividadId`

Dos secuencias alimentan el mismo campo, sin índice único que lo proteja:

| Secuencia | Referencia | Quién la usa |
| --- | --- | --- |
| «Visitas Refractarios», EDT `CRMCustVisitaId` | `CustParameters::numRefCustVsititaRefractariosId()` | `INDCRMVisitsService.createActivity` (`NumberSeq::newGetNum` sin liberación en abort) y el formulario `CRMCustVisitasTable`. |
| «CRM Número Actividad», EDT `CRMActividadId` | `CustParameters::numRefCRMActividadId()` | Formulario `CRMActividadTable`, `CRMConsultaTable.CreaActividad`, `CRMOfertaTable.CreaActividad`, `INDCrmApiClass_Old`. |

Consecuencias: la correspondencia externa se apoya en `RecId + empresa`, nunca en `ActividadId`; `GET /api/crm/activities/by-code/{code}` puede ser ambiguo; y el receptor AX, cuando asigne numeración a una visita nacida en la web, debe usar la secuencia de visitas y comprobar colisiones. La unicidad real en SQL queda pendiente de validar.

### 16.4 Escritores de AX que hay que bloquear o controlar en la convivencia

| Origen | Objetos | Operación sobre visitas/asistentes |
| --- | --- | --- |
| Formularios interactivos | `CRMCustVisitasTable` (DataSources de visita y asistentes con alta, edición y borrado permitidos), `CRMActividadTable` (alta, edición, borrado y botón que pone `Finalizada`) | Edición manual completa. Entradas de menú: `CRMActividadTable`, `CRMCustVisitasTable`, `CRMConsultarActividades`, `CRMActividadTable_Consulta` (solo consulta los dos últimos). |
| Métodos de tabla estándar | `CRMConsultaTable.CreaActividad`/`CerrarActividades`/`ConvertirEnOferta`; `CRMOfertaTable.CreaActividad`/`CerrarActividades`/`AsignarCuenta`/`TraspasarANuevaOferta`; `TraspasarConsultasOfertasACliente` en `CustTable`, `CRMClientePotencialTable` y `CRMProveedorPotencialTable`; `CRMActividadTable.TraspasarActividadesAbiertas` | Altas por copia y `doUpdate()` de estado o de cuenta sobre actividades existentes, sin pasar por el servicio. |
| Cascada | `CRMOfertaTable` declara `DeleteAction Cascade` sobre `CRMActividadTable` | Borrar una oferta borra sus actividades, sin `validateDelete` y sin borrar asistentes. |
| Código IND | `INDCRMVisitsService` (ruta API actual), `INDCrmApiClass_Old` (API antigua, aún compilada), `INDCRMActividadUserIdRepair` (recorre todas las empresas), `INDImportMexicoProcesos` | `insert`/`update`/`delete` y `doUpdate` de asistentes. |
| Jobs | `CRM_UpdateCustomCreatedBy_Step1`, `MEX1_Post_CorrigeRefRecId` y jobs numerados históricos con `doUpdate()` | Correcciones masivas puntuales. |

No existe ningún `insert_recordset`, `update_recordset` ni `delete_from` sobre estas tablas; informes, vistas, consultas y macros solo leen. El corte por empresa/cohorte (sección 11) debe cubrir los formularios, los métodos de tabla que mutan actividades de tipo Visita, la cascada desde ofertas, la API antigua y los jobs; un campo de autoridad en la web no los detiene.

### 16.5 Validaciones que la ruta API elude

`INDCRMVisitsService` no tiene `deleteVisita`; sus métodos son `createActivity`, `updateActivity`, `deleteActivity`, `createVisitaAsistente` y `deleteVisitaAsistente`. `deleteActivity` borra asistentes y cabecera con `delete()`; `createActivity`/`updateActivity` usan `insert()`/`update()`. En X++ esas llamadas no ejecutan `validateWrite` ni `validateDelete`, que solo disparan los DataSources de formulario. Por tanto hoy la API: borra visitas de más de 60 días, graba `TipoVisita=None` en `REF` y borra asistentes de tipo Insertec saltándose los 60 días de `CRMCustListaVisitasTable`. El producto debe declarar estas reglas como políticas propias (adoptarlas, acotarlas o retirarlas), sin deducirlas de un comportamiento que los dos canales actuales aplican de forma distinta.

Otros hechos del servicio comprobados: `createVisitaAsistente` ignora el tipo recibido y fuerza `Cliente_Proveedor`; `deleteVisitaAsistente` localiza por `RefRecId + AsistenteId` (nombre, cadena 80); una falta de visibilidad o permiso se responde como «Actividad no encontrada» (404 en la API); un listado vacío devuelve el centinela `[["Sin datos."]]` con `Total=1`; el listado ordena por `INDCreatedByUserId`.

### 16.6 Tablas de acceso: alcance e índices

| Tabla | Alcance | Índice único |
| --- | --- | --- |
| `INDWebUserEntraIdentity` | Global | `UserId + AppCode + EntraOID` |
| `INDCiasPermitidas` | Global | `UserId + CiaId` (primario) |
| `INDWebApp` | Global | Ninguno en el AOT DEV. |
| `INDWebModule` | Global | `AppCode + ModuleCode` |
| `INDWebModuleAccessLevel` | Global | `RefRecIdCiaPermitida + ModuleCode + AppCode` |
| `INDModuleDataVisibilityTarget` | Global | `INDWebModuleAccessLevel + TargetPersonAlias + TargetAction + TargetScope + ValidFrom + ValidTo` |
| `INDModuleDataVisibilityHierarchyLine` | Por empresa | `ParentPersonAlias + ChildPersonAlias + ValidFrom + ValidTo` (EDT de `ValidFrom`/`ValidTo` cruzados) |
| `INDPersonaTable` | Por empresa | `AliasIdIdx[Alias]`; `UserId` admite duplicados |
| `CRMUsuarioTable` | Por empresa | `UserId` (primario) |

`SysAccessRights` confirma View=1, Edit=2, Add=3, FullAccess=4. `INDDataVisibilityMode` tiene los cinco modos descritos en 7.1 e `INDDataVisibilityHierarchyDepth` los dos. La importación inicial debe cargar las tablas globales una vez y las de empresa por cada empresa, y rechazar personas con `UserId` duplicado o sin alias.

### 16.7 Contraste de las decisiones confirmadas

| Decisión | Evidencia contrastada | Resultado |
| --- | --- | --- |
| Escritor único web para las visitas migradas | Inventario 16.4 | Se mantiene para el alcance del corte; bloquear sus formularios, métodos de tabla, API antigua y jobs. La cascada de ofertas queda expresamente exceptuada del bloqueo y se trata como divergencia (VIS-05). |
| Soporte con lectura y edición desde la activación, actor real auditado | Guard de la semilla exige `scp` y rol y `/me` devuelve el actor MSAL (E17, E30); el CRM resuelve la mutación por registro en `ModuleRecordMutationPermissionService` (E38) | Se mantiene; el contexto efectivo se añade al guard sin sustituir `identity`. |
| Jerarquía de Visitas reutilizable | `INDModuleDataVisibilityHierarchyLine` por empresa con índice único y vigencias (16.6) | Se mantiene; la importación valida duplicados de `INDPersonaTable.UserId`. |
| Panel de administración y logs en la misma PostgreSQL | La semilla no aporta logger DB, auditoría efectiva ni inyección de `db` (5.1) | Se mantiene como requisito del producto, con los huecos de 5.1 asumidos por él. |
| Guardado local atómico con outbox y worker | El estándar no define idempotencia, worker ni servicio del proceso (5.1, punto 12) | Se mantiene; se documenta en la especificación del producto como requisito propio. |
| Entrega del MVP en Railway | VIS-09/VIS-16 documentan la desviación respecto al modelo Windows/IIS del estándar | Railway aloja API, frontend y PostgreSQL; el futuro worker/puente AX permanece en la red interna. |
| Perfil `web-api` del estándar | `network-map` reservado al piloto de redes; `project.json` solo admite cinco campos (`name` `^[A-Z][A-Z0-9_]{2,63}$`, `slug`, descripción de hasta 180 caracteres sin comillas) | Se mantiene; la ficha preparada cumple el esquema. |

### 16.8 Divergencias entre repositorio, AOT DEV y documentación

- `INDMutationPolicy`: el XPO del repositorio tiene tres valores (0 OwnOnly, 1 SameAsVisibility, 2 ModuleBusinessRules) y el AOT DEV solo dos; en ambos resolvers la rama `ModuleBusinessRules` está comentada. No habilitar ese modo y confirmar qué versión del enum está activa antes de importar permisos.
- `INDWebApp`: el repositorio conserva `INDWebApp.xpo` (sin índices) e `INDWebAppTable.xpo` (con `AppCodeIdx` único); el AOT DEV no tiene índices en la tabla, lo que coincide con el primero. Sigue pendiente la comprobación en SQL que exige `AX_XPO_WORKFLOW.md`.
- `.codex/ENDPOINTS.md` frente al código: declara `description` obligatoria en alta y actualización y `contactoRecId` obligatorio en asistentes, pero los controladores no los validan; describe las filas del listado con nombres cuando son arrays por posición que además incluyen país, asistentes, `OwnerAxUserId` y `OwnerName`; usa `Name` donde el DTO de detalle usa `Nombre`. Corregir el catálogo es una tarea del CRM, separada de este análisis.
- Umbrales de mutación por registro: residen en `ModuleRecordMutationPermissionService` (nivel Edit y política de propietario), no en `VisitMutationPermissionService`, que delega.
- Fechas: la fecha inicial de alta y los filtros rápidos del historial usan el reloj del navegador; el rango inicial del historial, el del servidor. `NormalizeDate` del detalle usa `DateTime.TryParse` dependiente de la cultura, con riesgo para fechas `dd.MM.yyyy` en culturas distintas de `es-ES`.
- Rutas MVC sin uso: `UpdateAsistenteTipo` (insertaría filas duplicadas con `ContactoRecId` vacío) y `/CreateVisitaAsistente/CreateAsistente` (sin módulo registrado, denegado por el filtro). El PUT de detalle envía `asistenteTipo`, que el DTO de la API no contempla, y permite vaciar descripción y comentarios.

### 16.9 Qué cambia en la planificación

1. El corte de autoridad (sección 11) incorpora la lista 16.4 como alcance mínimo de bloqueo en AX; el mecanismo sigue pendiente (VIS-05). Las ofertas quedan fuera del alcance: la cascada `CRMOfertaTable -> CRMActividadTable` no se bloquea en AX, se detecta como divergencia.
2. El modelo (sección 8) fija longitudes AX comprobadas y añade `ax_activity_number_sequence` (cuál de las dos secuencias originó el código) a las correspondencias; `visits.axCode` se marca no único.
3. El contrato (sección 9) fija la descripción en 80 caracteres (VIS-03) y lo aplica también al receptor AX; declara tipo de asistente real y contacto como FK obligatoria para asistentes nuevos.
4. Decidido (VIS-04): 60 días de antigüedad para borrar como política configurable por empresa con excepción administrativa auditada, `TipoVisita` obligatorio en todas las empresas y valoración elegida por el usuario; el receptor AX aplica lo recibido sin reproducir los valores por defecto de `createActivity`.
5. La matriz de aceptación (sección 13) añade: historial con más de 50 visitas en el rango, visitas de más de 60 días, empresa `REF` sin tipo, códigos AX duplicados y borrado de oferta con actividades.
6. Decidido (VIS-06, VIS-18): MVP desechable con carga inicial única de solo lectura; la sincronización de ida y vuelta, el receptor y el corte se abordan después, reiniciando la base con una nueva carga.
7. La ficha y la especificación inicial del producto viven en `IND_APP_STANDARD/proyectos/CRM_INSERTEC/`; la especificación se trasladará a `docs/ESPECIFICACION.md` del producto cuando se genere, como exige el estándar.

## 17. Validación de esta propuesta

La validación documental debe comprobar enlaces locales, fuentes Mermaid emparejadas, exportaciones, ausencia de datos sensibles y `git diff --check`. Los resultados concretos se comunican al entregar, sin convertirlos en una validación del sistema futuro.

Se ejecutaron estos comandos desde `IND_CRM_APP`, ambos con código de salida 0:

```powershell
node --test tests/company-context-guard.test.mjs tests/browser-storage-isolation.test.mjs tests/frontend-mutation-recovery.test.mjs tests/history-query-cache.test.mjs
node --test tests/history-query-failures.test.mjs tests/authentication-session-lifecycle.test.mjs tests/authentication-session-registry.test.mjs
```

Resultados: 42/42 pruebas en el primer comando y 3/3 en el segundo. El lifecycle informa además 80 comprobaciones internas, que no son 80 tests adicionales. El archivo de recuperación mezcla Visitas y auxiliares de Gastos; no son 45 escenarios exclusivos de Visitas.

Estas pruebas combinan verificaciones estáticas, scripts con DOM/API/storage simulados, controlador MVC real con dependencias aisladas y cookie/OIDC con proveedor de identidad local simulado. No prueban Entra real ni AX. Se revisó el alcance del test de diálogo de filtros, pero no se ejecutó; tampoco se ejecutaron los E2E DEV de alta/edición/borrado porque escriben datos reales.

Quedan pendientes todas las pruebas del nuevo producto, tenant/usuarios reales, esquema DB vivo y contrato AX de sincronización.
