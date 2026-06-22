using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Net;
using System.Web.Http.Description;
using System.Web.Http;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/visits")]
    public class CrmVisitsController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;
        private const string ControlDataVisibilityAppCode = "CRM";
        private const string ControlDataVisibilityVisitsModuleCode = "VISITAS_GESTION";
        private const string AxEnumNumericValidationMessage = "Debe ser un valor numerico de enum AX mayor o igual que 0. Consulte /api/crm/enums para las opciones activas.";
         
        public CrmVisitsController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        // CREAR ASISTENTE (Container)
        [HttpPost, Route("createVisitaAsistente")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Visitas" })]
        [SwaggerResponse(HttpStatusCode.Created, "Asistente creado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion o negocio", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Registro no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult CreateVisitaAsistente([FromBody] CreateVisitaAsistenteRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new global::System.Collections.Generic.List<IndValidationError>();

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.ValidationError);
            if (userError != null)
                return userError;

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.refRecIdActividad))
                    validationErrors.Add(new IndValidationError { Field = "refRecIdActividad", Message = "refRecIdActividad es obligatorio." });
                if (!body.asistenteTipo.HasValue)
                    validationErrors.Add(new IndValidationError { Field = "asistenteTipo", Message = "asistenteTipo es obligatorio." });
                else if (body.asistenteTipo.Value < 0)
                    validationErrors.Add(new IndValidationError { Field = "asistenteTipo", Message = AxEnumNumericValidationMessage });
                if (string.IsNullOrWhiteSpace(body.asistenteId))
                    validationErrors.Add(new IndValidationError { Field = "asistenteId", Message = "asistenteId es obligatorio." });
            }

            if (validationErrors.Count > 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();

                if (!string.IsNullOrWhiteSpace(body.createdByUserId) &&
                    !string.Equals(body.createdByUserId.Trim(), axUserId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"[WARN] CreateVisitaAsistente createdByUserId mismatch body={body.createdByUserId} header={axUserId} token={username}");
                }

                Logger.Log($"[API-IN] CreateVisitaAsistente llamado por {username} axUserId={axUserId} company={company}");
                Logger.Log($" -> refRecIdActividad: {body.refRecIdActividad}");
                Logger.Log($" -> asistenteTipo: {body.asistenteTipo}");
                Logger.Log($" -> asistenteId: {body.asistenteId}");
                Logger.Log($" -> contactoRecId: {body.contactoRecId}");
                Logger.Log($" -> createdByUserId(header): {axUserId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(company);
                con.Append(body.refRecIdActividad?.Trim() ?? string.Empty);
                con.Append(body.asistenteTipo.Value);
                con.Append(body.asistenteId?.Trim() ?? string.Empty);
                con.Append(body.contactoRecId?.Trim() ?? string.Empty);
                con.Append(axUserId);
                con.Append(ControlDataVisibilityAppCode);
                con.Append(ControlDataVisibilityVisitsModuleCode);

                Logger.Log("[CONTAINER] Enviado a AX (CreateVisitaAsistente):");
                for (int i = 1; i <= con.Length(); i++)
                    Logger.Log($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "createVisitaAsistente",
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

                Logger.Log($"[API-OUT] Resultado CreateVisitaAsistente: {result} - {message}");

                var isNotFound = message.IndexOf("no encontrada", StringComparison.OrdinalIgnoreCase) >= 0;
                var okResponse = new IndApiResponse<object>
                {
                    Success = successFlag,
                    Message = successFlag ? (string.IsNullOrWhiteSpace(message) ? "OK" : message) : (isNotFound ? "Registro no encontrado." : (string.IsNullOrWhiteSpace(message) ? "No se pudo crear el asistente." : message)),
                    ErrorCode = successFlag ? null : (isNotFound ? IndErrorCodes.CrmActivityNotFound : IndErrorCodes.ValidationError),
                    Errors = null,
                    Data = successFlag ? new { Result = result, Message = message } : null,
                    TraceId = traceId
                };

                if (successFlag)
                    return Content(HttpStatusCode.Created, okResponse);

                if (okResponse.ErrorCode == IndErrorCodes.CrmActivityNotFound)
                    return Content(HttpStatusCode.NotFound, okResponse);

                return Content((HttpStatusCode)422, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateVisitaAsistente API: {ex}");
                var response = new IndApiResponse<object> {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Elimina un asistente vinculado a una actividad.
        /// </summary>
        /// <remarks>
        /// Llama al metodo X++ deleteVisitaAsistente.  
        /// ErrorCode posibles: ValidationError si faltan datos, AxComError en errores AX, CrmActivityNotFound si la combinacion no existe.
        /// </remarks>
        /// <param name="body">Datos del asistente a eliminar.</param>
        [HttpDelete, Route("deleteVisitaAsistente")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Visitas" })]
        [SwaggerResponse(HttpStatusCode.OK, "Asistente eliminado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion o negocio", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Registro no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteVisitaAsistente([FromBody] DeleteVisitaAsistenteRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new global::System.Collections.Generic.List<IndValidationError>();

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.ValidationError);
            if (userError != null)
                return userError;

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.refRecIdActividad))
                    validationErrors.Add(new IndValidationError { Field = "refRecIdActividad", Message = "refRecIdActividad es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.asistenteId))
                    validationErrors.Add(new IndValidationError { Field = "asistenteId", Message = "asistenteId es obligatorio." });
            }

            if (validationErrors.Count > 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((global::System.Net.HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();

                Logger.Log($"[API-IN] DeleteVisitaAsistente llamado por {username} axUserId={axUserId} refRecIdActividad={body.refRecIdActividad} asistenteId={body.asistenteId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(company);
                con.Append(body.refRecIdActividad?.Trim() ?? string.Empty);
                con.Append(body.asistenteId?.Trim() ?? string.Empty);
                con.Append(axUserId);
                con.Append(ControlDataVisibilityAppCode);
                con.Append(ControlDataVisibilityVisitsModuleCode);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
                    "deleteVisitaAsistente",
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
                    return Content(global::System.Net.HttpStatusCode.InternalServerError, errorResponse);
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
                return Content(global::System.Net.HttpStatusCode.InternalServerError, errorResponse);
                }

                string result = row.Peek(1)?.ToString() ?? string.Empty;
                string message = row.Peek(2)?.ToString() ?? string.Empty;

                bool successFlag =
                    string.Equals(result, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);

                var isNotFound = message.IndexOf("no encontrada", global::System.StringComparison.OrdinalIgnoreCase) >= 0;
                var okResponse = new IndApiResponse<object>
                {
                    Success = successFlag,
                    Message = successFlag ? (string.IsNullOrWhiteSpace(message) ? "OK" : message) : (isNotFound ? "Registro no encontrado." : (string.IsNullOrWhiteSpace(message) ? "No se pudo eliminar el asistente." : message)),
                    ErrorCode = successFlag ? null : (isNotFound ? IndErrorCodes.CrmActivityNotFound : IndErrorCodes.ValidationError),
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };

                // Devolver el mensaje de AX cuando la eliminacion es exitosa.
                if (successFlag)
                    return Ok(okResponse);

                if (okResponse.ErrorCode == IndErrorCodes.CrmActivityNotFound)
                    return Content(global::System.Net.HttpStatusCode.NotFound, okResponse);

                return Content((global::System.Net.HttpStatusCode)422, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] DeleteVisitaAsistente API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(global::System.Net.HttpStatusCode.InternalServerError, response);
            }
        }
    }
}






