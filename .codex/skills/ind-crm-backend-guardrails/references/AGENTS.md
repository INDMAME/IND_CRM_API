Eres un asistente que trabaja sobre el proyecto IND_CRM_API.

PROPOSITO
- Este archivo define reglas estables de trabajo para el backend en produccion.
- No usar este documento como backlog ni como historial de tareas puntuales.
- Para contratos HTTP vivos, priorizar siempre `.codex/ENDPOINTS.md`.

CONTEXTO TECNICO ESTABLE
- Proyecto: IND_CRM_API.
- Stack: .NET Framework 4.8, Web API 2, OWIN self-host.
- Plataforma: x86 obligatoria por AxaptaCOMConnector.
- ERP backend: Navision Axapta 3.0 SP2 via Business Connector COM.
- No migrar a .NET Core ni cambiar la version de framework salvo peticion explicita.

RESTRICCIONES NO NEGOCIABLES
- Mantener .NET Framework 4.8 y x86.
- No romper la integracion con Axapta COM ni la logica de `AxaptaSessionManager`.
- No introducir multithreading o concurrencia que pueda romper el cliente COM.
- No tocar configuracion critica de AxaptaCOMConnector, OWIN host o despliegue salvo peticion explicita.
- No agregar dependencias pesadas ni nuevas librerias sin una necesidad clara y acotada.
- No hardcodear secretos, passwords, tokens, connection strings, tenant ids, `companyId`, `axUserId`, URLs de entorno ni configuraciones sensibles.
- Si se necesita una nueva clave o secreto, usar la ruta de configuracion del sistema ya existente para conservar interoperabilidad entre DEV y PROD.

POLITICA DE CAMBIOS EN PRODUCCION
- Antes de cambios medianos o grandes, resumir la estructura actual y presentar un plan corto.
- Preferir cambios pequenos, quirurgicos y con bajo radio de impacto.
- Mantener compatibilidad de endpoints, rutas, contratos y comportamiento salvo bug claro o peticion explicita.
- No hacer refactors amplios, migraciones de envelopes, cambios globales de estilo o reescrituras cruzadas solo porque un documento historico lo sugiera.
- Cuando haya varias formas validas de resolver algo, explicar opciones y pedir confirmacion antes de implementar.
- Si algo no esta claro o puede cambiar comportamiento de negocio, preguntar antes de codificar.

ARQUITECTURA Y MODULARIDAD
- Aplicar arquitectura limpia antes de codificar: separar controlador, DTO, validacion, servicio, mapper e integracion.
- Mantener limites claros entre Web API, logica de aplicacion y acceso a Axapta.
- No filtrar detalles de AX a contratos publicos si no es estrictamente necesario.
- Si una misma logica aparece en varios puntos, preferir helper, mapper, validador o servicio compartido solo cuando el reuse sea real y no especulativo.
- Refactorizar solo lo necesario para que el cambio quede claro, mantenible y seguro.

ESTANDARES API VIGENTES
- El estandar actual de respuestas usa `IndApiResponse<T>`, `IndPagedResponse<T>`, `IndValidationError` e `IndErrorCodes`.
- Para endpoints ya alineados a ese estandar, mantener el mismo shape salvo peticion explicita.
- Para endpoints nuevos o tocados, seguir el patron ya usado por los controladores CRM actuales.
- Usar `Controllers/CRM/CrmTemplateController.cs` y controladores vecinos como referencia estructural, no como excusa para copiar codigo sin revisar.
- No iniciar una migracion global de envelopes o codigos HTTP a menos que el usuario lo pida de forma explicita.

SWAGGER Y OPENAPI
- Mantener Swagger/OpenAPI en Web API 2 con la configuracion actual del proyecto.
- No migrar a paquetes de ASP.NET Core.
- Cuando cambie un contrato, actualizar tambien XML docs, `ResponseType`, `SwaggerResponse` y cualquier filtro OpenAPI afectado.
- Priorizar documentar y anotar rutas existentes antes que renombrarlas.

ROUTING, HEADERS Y FECHAS
- Toda creacion o modificacion de endpoint debe cerrar con revision de routing.
- Checklist minimo:
  - revisar colisiones entre rutas literales y parametrizadas
  - validar unicidad por `HTTP method + route template`
  - aplicar constraints cuando haya ambiguedad
  - revisar `RoutePrefix`, rutas hermanas y compatibilidad con routing legacy
  - probar endpoints potencialmente conflictivos cuando aplique
- En endpoints CRM de negocio, exigir `X-IND-Company` segun el contrato canonico.
- En endpoints que envian identidad a AX, exigir `X-IND-AxUserId` segun el contrato canonico.
- En `tickets` y `hojas de gastos`, request acepta `DDMMYYYY` y `DD.MM.YYYY`.
- En `tickets` y `hojas de gastos`, response devuelve fechas en `DD.MM.YYYY`.
- No exponer formatos internos de AX en respuestas publicas.

INTEGRACION AXAPTA
- Encapsular llamadas COM con manejo defensivo de errores y logging razonable.
- Mantener interfaz publica compatible cuando se refactoricen wrappers o servicios AX.
- Si se toca una clase AX o un contrato AX->API:
  - analizar primero metodos, indices de `container`, validaciones y compatibilidad
  - crear o actualizar `.codex/AX_<ClassName>_CHANGES_YYYY-MM-DD.md`
  - usar ese archivo como bitacora del cambio hasta cerrar AX y API
- No cerrar un cambio AX->API si la bitacora temporal no refleja el estado final.

POSTMAN Y MCP
- Para versionado Postman, usar como fuente `.codex/Postman/POSTMAN_VERSIONING.md`.
- Mantener separadas las lineas `DEV` y `PROD`.
- Para catalogo MCP, usar `.codex/MCP_TOOLS.json` como archivo canonico y `.codex/MCP_ENDPOINTS.md` como apoyo descriptivo.

FUENTES CANONICAS
- `.codex/ENDPOINTS.md`: contratos HTTP, headers requeridos, fechas y notas de routing.
- `.codex/MCP_TOOLS.json`: catalogo MCP y schemas.
- `.codex/MCP_ENDPOINTS.md`: descripcion detallada de tools MCP.
- `.codex/POSTMAN.md`: estado operativo de colecciones y variables.
- `.codex/Postman/POSTMAN_VERSIONING.md`: reglas de versionado Postman.
- `.codex/AX_*_CHANGES_*.md`: bitacoras historicas o temporales por clase AX. No tratarlas como reglas universales.

VALIDACION DE SALIDA
- Validar cambios con el flujo normal de compilacion o ejecucion de este repo.
- Si hubo cambios de contrato, actualizar la documentacion canonica afectada.
- Si hubo cambios API, dejar constancia de que se reviso routing.
- Cerrar el trabajo indicando que se valido, que riesgos quedan y si hubo algo que no se pudo comprobar.
