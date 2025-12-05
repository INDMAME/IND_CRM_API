using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using AxaptaCOMConnector;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;
using Swashbuckle.Swagger.Annotations;
using System.Runtime.InteropServices;
using Newtonsoft.Json;
using System.Net;
using System.Net.Http;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/activities")]
    public class CrmActivitiesController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;
 
        public CrmActivitiesController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        public class GetActivitiesRequest
        {
            public string userId { get; set; }
            public string fromDate { get; set; }
            public string toDate { get; set; }
        }

        /// <summary>
        /// Crea una actividad CRM en Axapta.
        /// </summary>
        /// <remarks>
        /// Devuelve 201 con IndApiResponse si la actividad se crea.  
        /// ErrorCode posibles:  
        /// - CrmActivityMissingFields cuando faltan campos obligatorios.  
        /// - AxComError cuando hay errores de contenedor o llamada COM.  
        /// - InternalError en fallos inesperados.
        /// </remarks>
        /// <param name="body">DTO con datos de la actividad a crear.</param>
        [HttpPost, Route("create")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        [SwaggerResponse(HttpStatusCode.Created, "Actividad creada correctamente", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.BadRequest, "Error en llamada a AX", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult CreateActivity([FromBody] CreateActivityRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Request body is required." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.accountNum))
                    validationErrors.Add(new IndValidationError { Field = "accountNum", Message = "accountNum is required." });
                if (string.IsNullOrWhiteSpace(body.visitType))
                    validationErrors.Add(new IndValidationError { Field = "visitType", Message = "visitType is required." });
                if (string.IsNullOrWhiteSpace(body.userId))
                    validationErrors.Add(new IndValidationError { Field = "userId", Message = "userId is required." });
                if (string.IsNullOrWhiteSpace(body.transDate) || !DateTime.TryParse(body.transDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate is required and must be a valid date." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Validation error.",
                    ErrorCode = IndErrorCodes.CrmActivityMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();

                Logger.Log($"[API-IN] CreateActivity llamado por {username}");
                Logger.Log($" -> accountNum: {body.accountNum}");
                Logger.Log($" -> visitType: {body.visitType}");
                Logger.Log($" -> userId: {body.userId}");
                Logger.Log($" -> transDate: {body.transDate}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.accountNum?.Trim() ?? string.Empty);
                con.Append(body.visitType?.Trim() ?? string.Empty);
                con.Append(body.userId?.Trim() ?? string.Empty);
                con.Append(body.description?.Trim() ?? string.Empty);

                DateTime dt = DateTime.Parse(body.transDate);
                string axDate = dt.ToString("yyyyMMdd");
                con.Append(axDate);

                con.Append(body.comentarios ?? string.Empty);
                con.Append(body.antecedentes ?? string.Empty);
                con.Append(body.conclusiones ?? string.Empty);

                Logger.Log("Container enviado a AX (CreateActivity):");
                for (int i = 1; i <= con.Length(); i++)
                    Logger.Log($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "createActivity",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;

                if (root == null || root.Length() == 0)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Empty container from AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                var row = root.Peek(1) as AxaptaCOMConnector.IAxaptaContainer;

                if (row == null || row.Length() < 2)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Unexpected response structure.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                string result = row.Peek(1)?.ToString() ?? string.Empty;
                string message = row.Peek(2)?.ToString() ?? string.Empty;

                bool successFlag =
                    string.Equals(result, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);

                Logger.Log($"[API-OUT] Resultado CreateActivity: {result} - {message}");

                var okResponse = new IndApiResponse<object>
                {
                    Success = successFlag,
                    Message = message,
                    ErrorCode = null,
                    Data = new { Result = result, Message = message },
                    TraceId = traceId
                };

                if (successFlag)
                    return Content(HttpStatusCode.Created, okResponse);

                okResponse.Success = false;
                okResponse.ErrorCode = IndErrorCodes.AxComError;
                return Content(HttpStatusCode.BadRequest, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateActivity API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = $"Error CreateActivity: {ex.GetType().FullName} {ex.Message}",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Obtiene la lista paginada de actividades CRM filtrada por usuario y rango de fechas (GET).
        /// </summary>
        /// <remarks>
        /// Devuelve 200 con IndPagedResponse.  
        /// ErrorCode posibles: CrmActivityMissingFields en validacion, AxComError/AxSessionError en fallos de AX.
        /// </remarks>
        /// <param name="userId">Identificador de usuario de AX.</param>
        /// <param name="fromDate">Fecha inicio (yyyy-MM-dd).</param>
        /// <param name="toDate">Fecha fin (yyyy-MM-dd).</param>
        [HttpGet, Route("list")]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de actividades", typeof(IndPagedResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error en AX/COM", typeof(IndApiResponse<object>))]
        public IHttpActionResult ListActivitiesGet([FromUri] string userId, [FromUri] string fromDate, [FromUri] string toDate)
        {
            var request = new GetActivitiesRequest
            {
                userId = userId,
                fromDate = fromDate,
                toDate = toDate
            };
            return BuildActivitiesListResponse(request, 1, 0);
        }

        /// <summary>
        /// Obtiene la lista paginada de actividades CRM (POST, compatibilidad clientes actuales).
        /// </summary>
        /// <remarks>
        /// Igual que GET /list pero recibe el filtro en el cuerpo. ErrorCode y respuestas iguales.
        /// </remarks>
        /// <param name="body">Filtros de usuario y rango de fechas.</param>
        [HttpPost, Route("list")]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de actividades", typeof(IndPagedResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error en AX/COM", typeof(IndApiResponse<object>))]
        public IHttpActionResult ListActivities([FromBody] GetActivitiesRequest body)
        {
            return BuildActivitiesListResponse(body, 1, 0);
        }

        private IHttpActionResult BuildActivitiesListResponse(GetActivitiesRequest body, int page, int pageSize)
        {
            object resultObj = null;
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Request body is required." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.userId))
                    validationErrors.Add(new IndValidationError { Field = "userId", Message = "userId is required." });
                if (string.IsNullOrWhiteSpace(body.fromDate) || !DateTime.TryParse(body.fromDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "fromDate", Message = "fromDate is required and must be a valid date." });
                if (string.IsNullOrWhiteSpace(body.toDate) || !DateTime.TryParse(body.toDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "toDate", Message = "toDate is required and must be a valid date." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Validation error.",
                    ErrorCode = IndErrorCodes.CrmActivityMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.userId ?? string.Empty);
                con.Append(DateTime.Parse(body.fromDate).ToString("yyyyMMdd"));
                con.Append(DateTime.Parse(body.toDate).ToString("yyyyMMdd"));

                resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getActivityContainer",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null)
                {
                    var nullResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Null container from AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.InternalServerError, nullResponse);
                }

                try
                {
                    var data = Helpers.AxContainerHelper.ToArray(root) ?? Array.Empty<object>();
                    var size = pageSize > 0 ? pageSize : data.Length;
                    return Ok(new IndPagedResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        Total = data.Length,
                        Page = page,
                        PageSize = size,
                        Items = data.ToList(),
                        TraceId = traceId
                    });
                }
                catch (COMException comEx)
                {
                    // Attempt to safely serialize the resultObj for diagnostics
                    string serialized = SafeSerializeResultObject(resultObj);
                    Logger.Log($"[ERROR] ListActivities COMException: HResult={comEx.ErrorCode} Message={comEx.Message} {comEx}");
                    Logger.Log($"[ERROR] Serialized resultObj: {serialized}");
                    var response = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = $"Error ListActivities: COMException HResult={comEx.ErrorCode} Message={comEx.Message}",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.InternalServerError, response);
                }
            }
            catch (Exception ex)
            {
                // If there is a COMException at higher level, try to log serialized resultObj as well
                try
                {
                    if (resultObj != null)
                    {
                        string serialized = SafeSerializeResultObject(resultObj);
                        Logger.Log($"[ERROR] ListActivities - resultObj serialized on exception: {serialized}");
                    }
                }
                catch { /* ignore */ }

                Logger.Log($"[ERROR] ListActivities API: {ex}");
                int h = ex is COMException cex ? cex.ErrorCode : 0;
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = $"Error ListActivities: {ex.GetType().FullName} {ex.Message} HResult={h}",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        private string SafeSerializeResultObject(object obj)
        {
            try
            {
                if (obj == null) return "<null>";

                // If it's a container, convert to array first (this returns error placeholders instead of throwing)
                if (obj is AxaptaCOMConnector.IAxaptaContainer con)
                {
                    try
                    {
                        var arr = AxContainerHelper.ToArray(con);
                        return JsonConvert.SerializeObject(arr);
                    }
                    catch (Exception ex)
                    {
                        return $"<Failed to ToArray container: {ex.Message}>";
                    }
                }

                // Fallback to JSON serialize
                try
                {
                    return JsonConvert.SerializeObject(obj);
                }
                catch (Exception ex)
                {
                    return $"<Failed to JsonSerialize: {ex.Message} ObjectToString: {obj.ToString()}>";
                }
            }
            catch (Exception ex)
            {
                return $"<SafeSerialize failed: {ex.Message}>";
            }
        }

        /// <summary>
        /// Endpoint de prueba que reusa la logica de listado de actividades.
        /// </summary>
        /// <param name="body">Filtros de usuario y fechas.</param>
        [HttpPost, Route("test")]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        public IHttpActionResult TestActivities([FromBody] GetActivitiesRequest body)
        {
            return BuildActivitiesListResponse(body, 1, 0);
        }
    }
}


