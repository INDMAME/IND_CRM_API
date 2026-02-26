Eres un asistente que trabaja sobre el proyecto IND_CRM_API.

CONTEXTO GENERAL
- Proyecto: IND_CRM_API.
- Tecnología: .NET Framework 4.8, Web API 2, OWIN self-host.
- Plataforma: x86 obligatoria por AxaptaCOMConnector.
- ERP backend: Navision Axapta 3.0 (Axapta 3.0 SP2) a través de Business Connector COM (AxaptaCOMConnector).
- No debes migrar este proyecto a .NET Core ni cambiar la versión de framework.

RESTRICCIONES TÉCNICAS
- Mantener siempre el target en .NET Framework 4.8, x86.
- No romper la integración con Axapta COM ni la lógica de AxaptaSessionManager.
- No introducir multihilo que pueda romper el cliente COM.
- No cambiar configuración crítica de AxaptaCOMConnector ni de x86 salvo que se pida explícitamente.
- No añadir dependencias pesadas ni nuevas librerías salvo que sea estrictamente necesario y lo expliques con un comentario claro en el código.

OBJETIVOS DE DISEÑO
- Mantener la API estable mientras mejoras estructura, legibilidad y seguridad.
- Aplicar principios de código limpio: métodos pequeños y enfocados, nombres claros, evitar duplicación.
- Mantener el comportamiento actual de los endpoints REST salvo que detectes un bug claro.
- Tratar la integración con Axapta como dependencia crítica: nunca romper las llamadas COM.

SWAGGER / OPENAPI
- Debes añadir y configurar Swagger / OpenAPI usando Swashbuckle para Web API 2 (.NET 4.8).
- No usar Swashbuckle.AspNetCore ni paquetes de ASP.NET Core.
- Exponer un documento OpenAPI estable para los endpoints CRM, para que otros proyectos puedan generar clientes tipados.
- No renombrar rutas salvo que sea estrictamente necesario; prioriza documentar y anotar las rutas existentes.

INTEGRACIÓN AXAPTA COM
- AxaptaSessionManager debe seguir siendo seguro para x86 y Business Connector COM.
- Encapsular las llamadas COM en código defensivo (try/catch, logging) sin cambiar la lógica de negocio.
- No introducir patrones de concurrencia o multi threading que puedan romper el cliente COM.
- Si refactorizas wrappers COM, mantén la interfaz pública compatible o explica claramente cualquier cambio.

DOCUMENTACIÓN Y ESTILO
- Todos los comentarios y documentación XML deben estar en español sencillo.
- Evitar jerga innecesaria; explicar el propósito, entradas, salidas y casos de error en una o dos frases cortas en español.
- Añadir documentación XML en controladores públicos, servicios y clases clave de integración con Axapta.
- Añadir comentarios breves en métodos nuevos o lógica no evidente para que otro desarrollador entienda el flujo.

ESTÁNDAR DE RESPUESTAS REST

Objetivo: implementar un patrón estándar de respuesta REST en IND_CRM_API con:
1) Un envoltorio de respuesta unificado (success, message, data, errorCode, etc.).
2) Un envoltorio específico para listas/paginación.
3) Una clase centralizada de códigos de error de negocio.
4) Uso consistente de códigos HTTP en todos los controladores.
5) Código documentado de forma clara.

MODELO DE RESPUESTA ESTÁNDAR
Crea o actualiza las siguientes clases en un espacio de nombres común, por ejemplo: IND_CRM_API.Models.Responses

1) IndApiResponse<T>
- Propiedades:
  - bool Success
  - string Message
  - string ErrorCode
  - T Data
  - List<IndValidationError> Errors
  - string TraceId
- Comportamiento:
  - Success = true y ErrorCode = null para operaciones correctas.
  - Success = false para respuestas de error.
  - TraceId se usa para almacenar un identificador de correlación si está disponible.
- Añade comentarios XML a la clase y propiedades en español explicando:
  - Qué representa la clase.
  - Para qué sirve cada campo.
- Añade comentarios en línea solo donde la lógica no sea obvia.

2) IndPagedResponse<T>
- Propiedades:
  - bool Success
  - string Message
  - int Total
  - int Page
  - int PageSize
  - List<T> Items
  - string TraceId
- Uso:
  - Para endpoints de listas (paginadas o grandes volúmenes).
  - Total: número total de registros en el origen de datos.
  - Page y PageSize: información de la página actual.
- Añade documentación XML y comentarios breves en español.

3) IndValidationError
- Propiedades:
  - string Field
  - string Message
- Uso:
  - Describir errores de validación y problemas a nivel de campo.
- Añade documentación XML y comentarios breves.

4) IndErrorCodes
- Crea una clase estática sin instancias.
- Define constantes string para códigos de error de negocio, agrupados por módulo. Por ejemplo:
  - public const string ValidationError = "VALIDATION_ERROR";
  - public const string AuthRequired = "AUTH_REQUIRED";
  - public const string AuthTokenExpired = "AUTH_TOKEN_EXPIRED";
  - public const string CrmActivityMissingFields = "CRM_ACTIVITY_MISSING_FIELDS";
  - public const string CrmActivityNotFound = "CRM_ACTIVITY_NOT_FOUND";
  - public const string AxSessionError = "AX_SESSION_ERROR";
  - public const string AxComError = "AX_COM_ERROR";
- Usa nombres significativos alineados con los endpoints reales de IND_CRM_API.
- Añade documentación XML a la clase y a cada constante explicando el escenario de uso.

ESTÁNDAR DE CÓDIGOS HTTP
Aplica estas reglas en todos los controladores y endpoints:

- GET (recurso único)
  - 200 OK cuando el recurso existe y se devuelve.
  - 404 Not Found cuando el recurso no existe (Success = false y ErrorCode de tipo NOT_FOUND).

- GET (lista / búsqueda)
  - 200 OK con IndPagedResponse<T> o al menos con Items y Total.

- POST (crear)
  - 201 Created cuando el recurso se crea correctamente.
  - Devolver IndApiResponse<T> con Success = true y Data con el recurso creado o su identificador.
  - 400 Bad Request para errores de formato o datos inválidos a nivel sintáctico.
  - 422 Unprocessable Entity para errores de negocio/validación (usar IndValidationError y códigos de IndErrorCodes específicos de módulo).

- PUT / PATCH (actualizar)
  - 200 OK cuando se devuelve el recurso actualizado.
  - 204 No Content si se actualiza correctamente y no se devuelve body.
  - 400, 404, 422 cuando corresponda, manteniendo el envelope estándar.

- DELETE
  - 204 No Content cuando la eliminación tiene éxito sin body.
  - 404 Not Found cuando el recurso no existe.

FORMATO DE RESPUESTA DE ERROR
Para cualquier error (4xx o 5xx) la estructura JSON debe ser:

{
  "success": false,
  "message": "Descripcion corta del error",
  "errorCode": "CODIGO_DE_ERROR",
  "errors": [ ... ],
  "traceId": "id-correlaction-opcional"
}

TAREAS DE IMPLEMENTACION
1) Analizar la solución IND_CRM_API:
   - Localizar modelos de respuesta existentes y usos de respuestas anónimas.
   - Identificar endpoints que devuelven formas distintas de respuesta.

2) Introducir las clases estándar:
   - Añadir IndApiResponse<T>, IndPagedResponse<T>, IndValidationError, IndErrorCodes en una carpeta común (por ejemplo Models/Responses).
   - Asegurar compilación en .NET Framework 4.8 x86.
   - Documentar en español con XML doc.

3) Refactorizar controladores:
   - Para cada controlador y endpoint:
     - Sustituir respuestas ad-hoc por IndApiResponse<T> o IndPagedResponse<T>.
     - Ajustar tipos de retorno (IHttpActionResult u otros) para usar los modelos estándar.
     - Aplicar los códigos HTTP definidos antes.
   - En endpoints de lista, usar siempre Total, Page, PageSize, Items, TraceId cuando tenga sentido.
   - En acciones de crear/actualizar/borrar, usar IndApiResponse<T>.

4) Manejo de errores global:
   - Si existe filtro global de excepciones o middleware OWIN, actualizarlo para:
     - Capturar excepciones no controladas.
     - Registrar o trazar la excepción respetando el logging existente.
     - Devolver IndApiResponse<object> con:
       - Success = false
       - Message = "Error interno del servidor"
       - ErrorCode = un código de IndErrorCodes (por ejemplo "INTERNAL_ERROR")
       - TraceId = id de correlación si existe
     - Usar HTTP 500 Internal Server Error.
   - No cambiar tipos de excepciones específicas de Axapta ni lógica COM; solo la construcción de la respuesta HTTP.

5) Códigos de error de negocio:
   - Para cada endpoint, identificar:
     - Campos obligatorios/invalidos.
     - Recurso no encontrado.
     - Problemas de autenticación/autorización.
     - Errores de sesión Axapta o COM.
   - Asignar constantes apropiadas de IndErrorCodes.
   - Añadir nuevas constantes cuando aparezcan escenarios no cubiertos, documentándolos.

6) Swagger / OpenAPI:
   - Configurar Swashbuckle para Web API 2 con .NET 4.8.
   - Exponer esquemas IndApiResponse<T> e IndPagedResponse<T> como modelos de respuesta.
   - Documentar respuestas típicas de error (400, 401, 404, 422, 500) con el envelope estándar.
   - No migrar a ASP.NET Core ni cambios de framework.

ESTILO DE TRABAJO
- Antes de cambios grandes, resume brevemente:
  - Estructura actual que has encontrado.
  - Plan corto (lista) de archivos a modificar y patrones a aplicar.
- Prefiere refactors incrementales que compilen en cada paso.
- No añadir dependencias nuevas pesadas sin comentario explícito justificando el motivo.
- Mantener configuración de .NET 4.8 x86 y AxaptaCOMConnector intacta salvo petición explícita.

OBJETIVO FINAL
Tras tus cambios, todos los controladores de IND_CRM_API deben:
- Usar IndApiResponse<T> o IndPagedResponse<T> de forma consistente.
- Devolver códigos HTTP acordes a buenas prácticas REST.
- Utilizar IndErrorCodes para errores de negocio y Axapta.
- Estar documentados en español de forma clara para que cualquier desarrollador entienda el propósito y manejo de errores.




REGLAS PARA NUEVOS ENDPOINTS IND_CRM_API

OBJETIVO
- Solo crear y modificar NUEVOS endpoints siguiendo el estandar ya definido en el proyecto.
- Usar SIEMPRE la estructura y patrones de CrmTemplateController.cs como referencia base.
- Aplicar de forma consistente el modelo de respuesta (IndApiResponse, IndPagedResponse, IndValidationError, IndErrorCodes).
- Actualizar siempre la documentacion OpenAPI/Swagger para cada nuevo endpoint.
- Preparar o solicitar el contrato del metodo de Axapta necesario para cada nuevo endpoint.
- Registrar logs basicos de seguimiento con el codigo HTTP de salida.
- Aplicar estas reglas a TODOS los futuros endpoints.

REGLAS GENERALES
- No cambiar endpoints existentes salvo que se pida de forma explicita.
- Nuevos endpoints: ubicarlos en el controlador adecuado heredando o imitando la estructura de CrmTemplateController.cs.
- Mantener la integracion con Axapta 3.0 via AxaptaSessionManager y Business Connector COM sin romper la logica existente.
- Usar siempre los modelos de respuesta estandar:
  - IndApiResponse<T> para operaciones de detalle / accion.
  - IndPagedResponse<T> para listas/paginacion.
- Usar codigos HTTP coherentes (200, 201, 204, 400, 401, 404, 422, 500) segun el comportamiento del endpoint.
- Mapear errores funcionales a IndErrorCodes apropiados.

FLUJO PARA CADA NUEVO ENDPOINT
1) Disenar el contrato REST:
   - Definir ruta, verbo HTTP, parametros (ruta, query, body) y DTOs de entrada/salida.
   - Elegir si la respuesta sera IndApiResponse<T> o IndPagedResponse<T>.
   - Definir codigos HTTP esperados y posibles ErrorCode de IndErrorCodes.

2) Solicitar / definir el metodo Axapta:
   - Proponer nombre de clase y metodo X++ (por ejemplo IND_CRM_<Entidad>Service.get<Entidad>List).
   - Definir firma y tipos basicos del metodo X++ (parametros simples y contenedor de salida).
   - Asegurar que el endpoint .NET llamara al metodo X++ a traves de AxaptaSessionManager.

3) Implementar el endpoint en C#:
   - Crear la accion siguiendo la estructura de CrmTemplateController.cs.
   - Consumir Axapta via AxaptaSessionManager y mapear contenedor Axapta a DTO de salida.
   - Devolver siempre IndApiResponse<T> o IndPagedResponse<T> con Success, Message, ErrorCode, etc.
   - Usar codigos HTTP estandar segun el resultado real (exito, no encontrado, validacion, error interno).

4) Actualizar OpenAPI/Swagger:
   - Añadir/ajustar XML documentation en el metodo del controlador (summary, remarks, param, returns) en espanol.
   - Declarar tipos de respuesta en las anotaciones ([ResponseType] / [SwaggerResponse]) usando IndApiResponse<T> o IndPagedResponse<T>.
   - Documentar posibles ErrorCode relevantes en las remarks.

5) Logs de seguimiento:
   - Usar la infraestructura de log existente.
   - Registrar como minimo para cada llamada:
     - Nombre de accion / ruta.
     - Verbo HTTP.
     - Codigo HTTP de respuesta final.
   - No registrar datos sensibles ni cuerpos completos si no es necesario; enfocar el log en trazabilidad y estado.

APLICACION FUTURA
- Todas las nuevas funcionalidades expuestas como API REST deben seguir este flujo.
- Si se crea un nuevo modulo o controlador, repetir estas mismas reglas de diseno, respuesta, documentacion y logs.

RECORDATORIO OPERATIVO
- Cada endpoint nuevo debe nacer con DTOs y responses tipados; refactoriza las interfaces (contratos de entrada/salida) en cuanto los toques para mantener el codigo limpio y alineado con IndApiResponse/IndPagedResponse e IndErrorCodes.



Usa siempre IndPagedResponse<T> para endpoints de lectura (GET y POST que solo devuelven datos). Estructura: success, message, items (lista tipada), opcional total, page, pageSize solo si realmente paginas; traceId siempre.
Para detalle único, devuelve items con un solo elemento y omite total/page/pageSize si no aplican.
Para listas, rellena items con todos los registros y, si paginas, añade total, page, pageSize.
En errores o validaciones, sigue usando IndApiResponse<T> con success=false, message corto, errorCode de IndErrorCodes, errors si hay validación, traceId.
No metas el objeto en Message ni serialices manualmente; el objeto va en items y Message es texto breve (“OK”, “Validación”, etc.).
Loguea la ruta, método y código HTTP; no loguees bodies sensibles.
Prompt/código base (copiar-pegar y adaptar)

[HttpGet, Route("by-code/{code}")]
[ResponseType(typeof(IndPagedResponse<ActivityDetailDto>))]
public IHttpActionResult GetActivityByCode(string code)
{
    var traceId = Guid.NewGuid().ToString("N");
    if (string.IsNullOrWhiteSpace(code))
        return Content((HttpStatusCode)422, new IndApiResponse<object>{
            Success=false, Message="code es obligatorio.", ErrorCode=IndErrorCodes.CrmActivityMissingFields,
            Errors=new List<IndValidationError>{ new IndValidationError{Field="code", Message="Valor inválido."}},
            TraceId=traceId });

    try
    {
        var username = GetAuthenticatedUsername();
        Logger.Log($"[API-IN] GetActivityByCode code={code} user={username}");

        var ax = SessionManager.GetAxInstanceForUser(username);
        var con = ax.CreateContainer();
        con.Append(code.Trim());

        var resultObj = ax.CallStaticClassMethod("INDCRMVisitsService","getActivityByCode",con);

        // deserializa si AX devolvió JSON ya serializado
        var pre = TryUnwrapSerializedActivityResponse(resultObj, traceId);
        if (pre != null) return Ok(pre);

        var root = resultObj as IAxaptaContainer;
        var dto = MapActivityDetail(root);
        if (dto == null)
            return Content(HttpStatusCode.NotFound, new IndApiResponse<object>{
                Success=false, Message="Actividad no encontrada.", ErrorCode=IndErrorCodes.CrmActivityNotFound, TraceId=traceId });

        return Ok(new IndPagedResponse<ActivityDetailDto>{
            Success=true, Message="OK", Items=new List<ActivityDetailDto>{ dto }, TraceId=traceId });
    }
    catch (Exception ex)
    {
        Logger.Log($"[ERROR] GetActivityByCode: {ex}");
        return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>{
            Success=false, Message=$"Error GetActivityByCode: {ex.Message}",
            ErrorCode=ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
            TraceId=traceId });
    }
}
Regla breve para nuevos endpoints de consulta

Detalle o lista sin mutar datos → IndPagedResponse<T> con items.
Comandos (crear/actualizar/borrar) → IndApiResponse<T> con data.
Nunca serialices manualmente; deja que el formatter JSON entregue el objeto tipado.
ACTUALIZACION 2026-02-26
- Lista de endpoints actualizada en .codex/ENDPOINTS.md.
- Documentacion Postman en .codex/POSTMAN.md.
- Collection Postman en .codex/Postman/IND_CRM_API V21.postman_collection.json.
- Regla de versionado Postman en .codex/Postman/POSTMAN_VERSIONING.md.
- Nueva directriz: todo endpoint que requiera userId debe tomarlo desde el header X-IND-AxUserId.
- Nueva directriz: listas de proyectos y hojas de gastos deben usar paginacion con page y pageSize (>= 1).
- Nueva directriz: todos los endpoints de negocio deben exigir companyId via header X-IND-Company.
  Las excepciones deben documentarse de forma explicita en ENDPOINTS.md.
