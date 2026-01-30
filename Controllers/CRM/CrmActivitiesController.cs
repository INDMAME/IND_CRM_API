using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
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
using System.Globalization;

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
            public string accountNum { get; set; }
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
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion o negocio", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult CreateActivity([FromBody] CreateActivityRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            DateTime transDate = default(DateTime);

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmActivityMissingFields);
            if (userError != null)
                return userError;

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
                if (string.IsNullOrWhiteSpace(body.transDate) || !TryParseAxDate(body.transDate, out transDate))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser yyyyMMdd o yyyy-MM-dd." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
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

                if (!string.IsNullOrWhiteSpace(body.userId) &&
                    !string.Equals(body.userId.Trim(), axUserId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"[WARN] CreateActivity userId mismatch body={body.userId} header={axUserId} token={username}");
                }

                if (!string.IsNullOrWhiteSpace(body.createdByUserId) &&
                    !string.Equals(body.createdByUserId.Trim(), axUserId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"[WARN] CreateActivity createdByUserId mismatch body={body.createdByUserId} header={axUserId} token={username}");
                }

                Logger.Log($"[API-IN] CreateActivity llamado por {username} axUserId={axUserId} company={company}");
                Logger.Log($" -> accountNum: {body.accountNum}");
                Logger.Log($" -> visitType: {body.visitType}");
                Logger.Log($" -> userId(header): {axUserId}");
                Logger.Log($" -> createdByUserId(header): {axUserId}");
                Logger.Log($" -> transDate: {body.transDate}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(company);
                con.Append(body.accountNum?.Trim() ?? string.Empty);
                con.Append(body.visitType?.Trim() ?? string.Empty);
                con.Append(axUserId);
                con.Append(axUserId);
                con.Append(body.description?.Trim() ?? string.Empty);

                string axDate = transDate.ToString("yyyyMMdd");
                con.Append(axDate);

                con.Append(body.comentarios ?? string.Empty);
                con.Append(body.antecedentes ?? string.Empty);
                con.Append(body.conclusiones ?? string.Empty);

                Logger.Log("Container enviado a AX (CreateActivity):");
                for (int i = 1; i <= con.Length(); i++)
                    Logger.Log($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "createActivity",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;

                if (root == null || root.Length() == 0)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
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
                        Message = "Error al procesar la respuesta de AX.",
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
                    Message = successFlag ? (string.IsNullOrWhiteSpace(message) ? "OK" : message) : (string.IsNullOrWhiteSpace(message) ? "No se pudo crear la actividad." : message),
                    ErrorCode = null,
                    Data = successFlag ? new { Result = result, Message = message } : null,
                    TraceId = traceId
                };

                if (successFlag)
                    return Content(HttpStatusCode.Created, okResponse);

                okResponse.Success = false;
                okResponse.ErrorCode = IndErrorCodes.ValidationError;
                return Content((HttpStatusCode)422, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateActivity API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Obtiene la lista paginada de actividades CRM.
        /// </summary>
        /// <remarks>
        /// Recibe el filtro en el cuerpo. ErrorCode y respuestas iguales.
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

        /// <summary>
        /// Actualiza una actividad CRM existente en Axapta.
        /// </summary>
        /// <remarks>
        /// Llama al metodo X++ updateActivity.  
        /// ErrorCode posibles: CrmActivityMissingFields en validacion, CrmActivityNotFound si el recId no existe, AxComError/InternalError en errores de AX.
        /// </remarks>
        /// <param name="recId">Identificador de la actividad (RecId).</param>
        /// <param name="body">Datos para actualizar la actividad.</param>
        [HttpPut, Route("{recId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        [SwaggerResponse(HttpStatusCode.OK, "Actividad actualizada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Actividad no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpdateActivity(long recId, [FromBody] UpdateActivityRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            DateTime transDate = default(DateTime);

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmActivityMissingFields);
            if (userError != null)
                return userError;

            if (recId == 0)
                validationErrors.Add(new IndValidationError { Field = "recId", Message = "recId es obligatorio y distinto de cero." });

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.accountNum))
                    validationErrors.Add(new IndValidationError { Field = "accountNum", Message = "accountNum es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.visitType))
                    validationErrors.Add(new IndValidationError { Field = "visitType", Message = "visitType es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.transDate) || !TryParseAxDate(body.transDate, out transDate))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser yyyyMMdd o yyyy-MM-dd." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
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
                Logger.Log($"[API-IN] UpdateActivity recId={recId} llamado por {username}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                // Convertir recId a cadena para evitar problemas de marshalling de Int64 en COM
                con.Append(company);
                con.Append(recId.ToString());
                con.Append(body.accountNum?.Trim() ?? string.Empty);
                con.Append(body.visitType?.Trim() ?? string.Empty);
                con.Append(axUserId);
                con.Append(body.description?.Trim() ?? string.Empty);
                con.Append(transDate.ToString("yyyyMMdd"));
                con.Append(body.comentarios ?? string.Empty);
                con.Append(body.antecedentes ?? string.Empty);
                con.Append(body.conclusiones ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "updateActivity",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null || root.Length() == 0)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
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
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
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

                var isNotFound = message.IndexOf("no encontrada", StringComparison.OrdinalIgnoreCase) >= 0;
                var okResponse = new IndApiResponse<object>
                {
                    Success = successFlag,
                    Message = successFlag ? (string.IsNullOrWhiteSpace(message) ? "OK" : message) : (isNotFound ? "Actividad no encontrada." : (string.IsNullOrWhiteSpace(message) ? "No se pudo actualizar la actividad." : message)),
                    ErrorCode = successFlag ? null : (isNotFound ? IndErrorCodes.CrmActivityNotFound : IndErrorCodes.ValidationError),
                    Errors = null,
                    Data = successFlag ? new { RecId = recId, Message = message } : null,
                    TraceId = traceId
                };

                if (successFlag)
                    return Ok(okResponse);

                if (okResponse.ErrorCode == IndErrorCodes.CrmActivityNotFound)
                    return Content(HttpStatusCode.NotFound, okResponse);

                return Content((HttpStatusCode)422, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] UpdateActivity API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Elimina una actividad CRM por recId.
        /// </summary>
        /// <remarks>
        /// Llama al metodo X++ deleteActivity.  
        /// ErrorCode posibles: CrmActivityNotFound si el recId no existe, AxComError/InternalError en errores de AX.
        /// </remarks>
        /// <param name="recId">Identificador de la actividad (RecId).</param>
        [HttpDelete, Route("{recId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        [SwaggerResponse(HttpStatusCode.OK, "Actividad eliminada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion o negocio", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Actividad no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteActivity(long recId)
        {
            var traceId = Guid.NewGuid().ToString("N");

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            if (recId == 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "recId es obligatorio y distinto de cero.",
                    ErrorCode = IndErrorCodes.CrmActivityMissingFields,
                    Errors = new List<IndValidationError> { new IndValidationError { Field = "recId", Message = "Valor invalido." } },
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] DeleteActivity recId={recId} llamado por {username}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                // Convertir recId a cadena para evitar problemas de marshalling de Int64 en COM
                con.Append(company);
                con.Append(recId.ToString());

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "deleteActivity",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null || root.Length() == 0)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
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
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
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

                var isNotFound = message.IndexOf("no encontrada", StringComparison.OrdinalIgnoreCase) >= 0;
                var okResponse = new IndApiResponse<object>
                {
                    Success = successFlag,
                    Message = successFlag ? (string.IsNullOrWhiteSpace(message) ? "OK" : message) : (isNotFound ? "Actividad no encontrada." : (string.IsNullOrWhiteSpace(message) ? "No se pudo eliminar la actividad." : message)),
                    ErrorCode = successFlag ? null : (isNotFound ? IndErrorCodes.CrmActivityNotFound : IndErrorCodes.ValidationError),
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };

                // Devolver mensaje de AX en el body cuando la eliminacion es exitosa.
                if (successFlag)
                    return Ok(okResponse);

                if (okResponse.ErrorCode == IndErrorCodes.CrmActivityNotFound)
                    return Content(HttpStatusCode.NotFound, okResponse);

                return Content((HttpStatusCode)422, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] DeleteActivity API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Obtiene una actividad CRM por recId.
        /// </summary>
        /// <remarks>
        /// Llama al metodo X++ getActivityByRecIdContainer.  
        /// ErrorCode posibles: CrmActivityNotFound si no existe, AxComError/AxSessionError en errores de AX.
        /// </remarks>
        /// <param name="recId">Identificador de la actividad (RecId).</param>
        [HttpGet, Route("{recId}")]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        [SwaggerResponse(HttpStatusCode.OK, "Actividad encontrada", typeof(IndPagedResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Actividad no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetActivityByRecId(long recId)
        {
            var traceId = Guid.NewGuid().ToString("N");

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            if (recId == 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "recId es obligatorio y distinto de cero.",
                    ErrorCode = IndErrorCodes.CrmActivityMissingFields,
                    Errors = new List<IndValidationError> { new IndValidationError { Field = "recId", Message = "Valor invalido." } },
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            object resultObj = null;
            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetActivityByRecId recId={recId} llamado por {username}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                // Convertir recId a cadena para evitar problemas de marshalling de Int64 en COM
                con.Append(company);
                con.Append(recId.ToString());

                resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "getActivityByRecIdContainer",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null || root.Length() == 0)
                {
                    var notFound = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Actividad no encontrada.",
                        ErrorCode = IndErrorCodes.CrmActivityNotFound,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.NotFound, notFound);
                }

                // Convertir el contenedor a arreglo legible
                var data = Helpers.AxContainerHelper.ToArray(root);

                var okResponse = new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Items = (data ?? Array.Empty<object>()).ToList(),
                    TraceId = traceId
                };
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                try
                {
                    if (resultObj != null)
                    {
                        string serialized = SafeSerializeResultObject(resultObj);
                        Logger.Log($"[ERROR] GetActivityByRecId - resultObj: {serialized}");
                    }
                }
                catch { /* ignore */ }

                Logger.Log($"[ERROR] GetActivityByRecId API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Obtiene una actividad CRM por su ActivityId alfanumérico.
        /// </summary>
        /// <remarks>
        /// Llama al método X++ getActivityByCode.  
        /// ErrorCode posibles: CrmActivityNotFound si no existe, AxComError/AxSessionError en errores de AX, CrmActivityMissingFields cuando falta el id.
        /// </remarks>
        /// <param name="code">Identificador alfanumérico de la actividad.</param>
        [HttpGet, Route("by-code/{code}")]
        [ResponseType(typeof(IndPagedResponse<ActivityDetailDto>))]
        [SwaggerOperation(Tags = new[] { "Actividades" })]
        [SwaggerResponse(HttpStatusCode.OK, "Actividad encontrada", typeof(IndPagedResponse<ActivityDetailDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Actividad no encontrada", typeof(IndApiResponse<ActivityDetailDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validación", typeof(IndApiResponse<ActivityDetailDto>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<ActivityDetailDto>))]
        public IHttpActionResult GetActivityByCode(string code)
        {
            var traceId = Guid.NewGuid().ToString("N");

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            if (string.IsNullOrWhiteSpace(code))
            {
                var validationResponse = new IndApiResponse<ActivityDetailDto>
                {
                    Success = false,
                    Message = "code es obligatorio.",
                    ErrorCode = IndErrorCodes.CrmActivityMissingFields,
                    Errors = new List<IndValidationError> { new IndValidationError { Field = "code", Message = "Valor inválido." } },
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            object resultObj = null;
            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetActivityByCode code={code} llamado por {username}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(code.Trim());

                resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "getActivityByCode",
                    con
                );

                // AX puede devolver un JSON ya serializado; lo deserializamos para evitar doble parse en el cliente.
                var preSerialized = TryUnwrapSerializedActivityResponse(resultObj, traceId);
                if (preSerialized != null)
                {
                    return Ok(preSerialized);
                }

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null || root.Length() == 0)
                {
                    return Content(HttpStatusCode.NotFound, BuildActivityNotFound(traceId));
                }

                var dto = MapActivityDetail(root);
                if (dto == null)
                {
                    return Content(HttpStatusCode.NotFound, BuildActivityNotFound(traceId));
                }

                return Ok(BuildActivityOk(dto, traceId));
            }
            catch (Exception ex)
            {
                try
                {
                    if (resultObj != null)
                    {
                        string serialized = SafeSerializeResultObject(resultObj);
                        Logger.Log($"[ERROR] GetActivityById - resultObj: {serialized}");
                    }
                }
                catch { /* ignore */ }

                Logger.Log($"[ERROR] GetActivityByCode API: {ex}");
                var response = new IndApiResponse<ActivityDetailDto>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Construye la respuesta exitosa con el detalle de la actividad dentro de Items.
        /// </summary>
        private IndPagedResponse<ActivityDetailDto> BuildActivityOk(ActivityDetailDto dto, string traceId)
        {
            return new IndPagedResponse<ActivityDetailDto>
            {
                Success = true,
                Message = "OK",
                Items = new List<ActivityDetailDto> { dto },
                TraceId = traceId
            };
        }

        /// <summary>
        /// Respuesta estándar de actividad no encontrada.
        /// </summary>
        private IndApiResponse<ActivityDetailDto> BuildActivityNotFound(string traceId)
        {
            return new IndApiResponse<ActivityDetailDto>
            {
                Success = false,
                Message = "Actividad no encontrada.",
                ErrorCode = IndErrorCodes.CrmActivityNotFound,
                Errors = null,
                Data = null,
                TraceId = traceId
            };
        }

        /// <summary>
        /// Deserializa un envelope JSON que venga como texto para evitar que el cliente tenga que deserializar dos veces.
        /// </summary>
        private IndPagedResponse<ActivityDetailDto> TryUnwrapSerializedActivityResponse(object rawResult, string traceId)
        {
            try
            {
                if (rawResult is string rawString)
                {
                    var parsed = DeserializeActivityEnvelope(rawString, traceId);
                    if (parsed != null)
                    {
                        Logger.Log("[INFO] GetActivityByCode: respuesta JSON pre-serializada recibida, se deserializa antes de retornar.");
                        return parsed;
                    }
                }

                var container = rawResult as AxaptaCOMConnector.IAxaptaContainer;
                if (container != null && container.Length() == 1)
                {
                    try
                    {
                        var inner = container.Peek(1) as string;
                        var parsed = DeserializeActivityEnvelope(inner, traceId);
                        if (parsed != null)
                        {
                            Logger.Log("[INFO] GetActivityByCode: envelope deserializado desde contenedor de un solo elemento.");
                            return parsed;
                        }
                    }
                    catch
                    {
                        // Ignorar y continuar con el flujo normal.
                    }
                }
            }
            catch
            {
                // No romper el flujo si la detección falla.
            }

            return null;
        }

        /// <summary>
        /// Intenta materializar el JSON en IndPagedResponse, IndApiResponse o en ActivityDetailDto.
        /// </summary>
        private IndPagedResponse<ActivityDetailDto> DeserializeActivityEnvelope(string rawJson, string traceId)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            try
            {
                // Caso esperado: respuesta paginada (Items)
                var paged = JsonConvert.DeserializeObject<IndPagedResponse<ActivityDetailDto>>(rawJson);
                if (paged != null && paged.Items != null)
                {
                    if (paged.Items.Count == 0 && TryExtractSingleFromData(paged, out var dtoFromData))
                    {
                        paged.Items = new List<ActivityDetailDto> { dtoFromData };
                    }
                    if (string.IsNullOrWhiteSpace(paged.TraceId))
                        paged.TraceId = traceId;
                    return paged;
                }
            }
            catch
            {
                // Ignorar y continuar.
            }

            try
            {
                var envelope = JsonConvert.DeserializeObject<IndApiResponse<ActivityDetailDto>>(rawJson);
                if (envelope != null && envelope.Success && envelope.Data != null)
                {
                    if (string.IsNullOrWhiteSpace(envelope.TraceId))
                        envelope.TraceId = traceId;
                    return BuildActivityOk(envelope.Data, envelope.TraceId);
                }
            }
            catch
            {
                // Ignorar y probar con el DTO directo.
            }

            try
            {
                var dto = JsonConvert.DeserializeObject<ActivityDetailDto>(rawJson);
                if (dto != null)
                {
                    return BuildActivityOk(dto, traceId);
                }
            }
            catch
            {
                // No es JSON válido para nuestros modelos.
            }

            return null;
        }

        /// <summary>
        /// Extrae un DTO desde Data si el contrato venía desalineado.
        /// </summary>
        private bool TryExtractSingleFromData(IndPagedResponse<ActivityDetailDto> paged, out ActivityDetailDto dto)
        {
            dto = null;
            try
            {
                // Algunos contratos antiguos podían usar Data en vez de Items.
                var raw = JsonConvert.SerializeObject(paged);
                var envelope = JsonConvert.DeserializeObject<IndApiResponse<ActivityDetailDto>>(raw);
                if (envelope != null && envelope.Data != null)
                {
                    dto = envelope.Data;
                    return true;
                }
            }
            catch { /* ignore */ }

            return false;
        }

        /// <summary>
        /// Mapea el contenedor devuelto por AX a un DTO tipado para la actividad.
        /// </summary>
        private ActivityDetailDto MapActivityDetail(AxaptaCOMConnector.IAxaptaContainer root)
        {
            if (root == null || root.Length() == 0)
            {
                return null;
            }

            var row = root.Peek(1) as AxaptaCOMConnector.IAxaptaContainer;
            if (row == null || row.Length() < 13)
            {
                return null;
            }

            string SafeString(AxaptaCOMConnector.IAxaptaContainer c, int index)
            {
                try
                {
                    return c.Peek(index)?.ToString() ?? string.Empty;
                }
                catch
                {
                    return string.Empty;
                }
            }

            var dto = new ActivityDetailDto
            {
                ActividadId = SafeString(row, 1),
                RecId = SafeString(row, 2),
                Nombre = SafeString(row, 3),
                AccountNum = SafeString(row, 4),
                TransDate = SafeString(row, 5),
                Country = SafeString(row, 6),
                ActividadType = SafeString(row, 7),
                TipoVisita = SafeString(row, 8),
                Description = SafeString(row, 9),
                Comentarios = SafeString(row, 10),
                Antecedentes = SafeString(row, 11),
                Conclusiones = SafeString(row, 12),
                Asistentes = new List<ActivityAssistantDto>()
            };

            var asistentesCon = row.Length() >= 13 ? row.Peek(13) as AxaptaCOMConnector.IAxaptaContainer : null;
            if (asistentesCon != null)
            {
                try
                {
                    var length = asistentesCon.Length();
                    for (int i = 1; i <= length; i++)
                    {
                        var asistRow = asistentesCon.Peek(i) as AxaptaCOMConnector.IAxaptaContainer;
                        if (asistRow == null)
                            continue;

                        dto.Asistentes.Add(new ActivityAssistantDto
                        {
                            AsistenteId = SafeString(asistRow, 1),
                            AsistenteTipo = SafeString(asistRow, 2),
                            AsistenteCargo = SafeString(asistRow, 3)
                        });
                    }
                }
                catch
                {
                    // Ignorar fallos puntuales; se devuelve lista parcial
                }
            }

            return dto;
        }

        /// <summary>
        /// Parses dates in yyyyMMdd or yyyy-MM-dd deterministically.
        /// </summary>
        private static bool TryParseAxDate(string value, out DateTime date)
        {
            var formats = new[] { "yyyyMMdd", "yyyy-MM-dd" };
            return DateTime.TryParseExact(
                (value ?? string.Empty).Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        private IHttpActionResult BuildActivitiesListResponse(GetActivitiesRequest body, int page, int pageSize)
        {
            object resultObj = null;
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            DateTime fromDate = default(DateTime);
            DateTime toDate = default(DateTime);

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmActivityMissingFields);
            if (userError != null)
                return userError;

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Request body is required." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.fromDate) || !TryParseAxDate(body.fromDate, out fromDate))
                    validationErrors.Add(new IndValidationError { Field = "fromDate", Message = "fromDate debe ser yyyyMMdd o yyyy-MM-dd." });
                if (string.IsNullOrWhiteSpace(body.toDate) || !TryParseAxDate(body.toDate, out toDate))
                    validationErrors.Add(new IndValidationError { Field = "toDate", Message = "toDate debe ser yyyyMMdd o yyyy-MM-dd." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
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

                con.Append(company);
                con.Append(axUserId);
                con.Append(fromDate.ToString("yyyyMMdd"));
                con.Append(toDate.ToString("yyyyMMdd"));
                con.Append(body.accountNum?.Trim() ?? string.Empty);

                resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "getActivityContainer",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null)
                {
                    var nullResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
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
                    var usePaging = pageSize > 0;
                    var responseTotal = usePaging ? (int?)data.Length : null;
                    var responsePage = usePaging ? (int?)page : null;
                    var responsePageSize = usePaging ? (int?)pageSize : null;
                    return Ok(new IndPagedResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        Total = responseTotal,
                        Page = responsePage,
                        PageSize = responsePageSize,
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
                        Message = "Error al procesar la respuesta de AX.",
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
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
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

    }
}
