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
         
        public CrmVisitsController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        // CREAR ASISTENTE (Container)
        [HttpPost, Route("createVisitaAsistente")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Visitas CRM" })]
        public IHttpActionResult CreateVisitaAsistente([FromBody] CreateVisitaAsistenteRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new global::System.Collections.Generic.List<IndValidationError>();

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.refRecIdActividad))
                    validationErrors.Add(new IndValidationError { Field = "refRecIdActividad", Message = "refRecIdActividad es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.asistenteTipo))
                    validationErrors.Add(new IndValidationError { Field = "asistenteTipo", Message = "asistenteTipo es obligatorio." });
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

                Logger.Log($"[API-IN] CreateVisitaAsistente llamado por {username}");
                Logger.Log($" -> refRecIdActividad: {body.refRecIdActividad}");
                Logger.Log($" -> asistenteTipo: {body.asistenteTipo}");
                Logger.Log($" -> asistenteId: {body.asistenteId}");
                Logger.Log($" -> contactoRecId: {body.contactoRecId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.refRecIdActividad?.Trim() ?? string.Empty);
                con.Append(body.asistenteTipo?.Trim() ?? string.Empty);
                con.Append(body.asistenteId?.Trim() ?? string.Empty);
                con.Append(body.contactoRecId?.Trim() ?? string.Empty);

                Logger.Log("[CONTAINER] Enviado a AX (CreateVisitaAsistente):");
                for (int i = 1; i <= con.Length(); i++)
                    Logger.Log($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "createVisitaAsistente",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null || root.Length() == 0)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Contenedor vacio.",
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
                        Message = "Estructura inesperada en la respuesta.",
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

                var okResponse = new IndApiResponse<object>
                {
                    Success = successFlag,
                    Message = message,
                    ErrorCode = successFlag ? null : IndErrorCodes.AxComError,
                    Errors = null,
                    Data = new { Result = result, Message = message },
                    TraceId = traceId
                };

                if (successFlag)
                    return Content(HttpStatusCode.Created, okResponse);

                return Content(HttpStatusCode.BadRequest, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateVisitaAsistente API: {ex.Message}");
                var response = new IndApiResponse<object> {
                    Success = false,
                    Message = $"Error CreateVisitaAsistente: {ex.Message}",
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
        [SwaggerOperation(Tags = new[] { "Visitas CRM" })]
        public IHttpActionResult DeleteVisitaAsistente([FromBody] DeleteVisitaAsistenteRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new System.Collections.Generic.List<IndValidationError>();

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
                return Content((System.Net.HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();

                Logger.Log($"[API-IN] DeleteVisitaAsistente llamado por {username} refRecIdActividad={body.refRecIdActividad} asistenteId={body.asistenteId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.refRecIdActividad?.Trim() ?? string.Empty);
                con.Append(body.asistenteId?.Trim() ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "deleteVisitaAsistente",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null || root.Length() == 0)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Contenedor vacio.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(System.Net.HttpStatusCode.InternalServerError, errorResponse);
                }

                var row = root.Peek(1) as AxaptaCOMConnector.IAxaptaContainer;
                if (row == null || row.Length() < 2)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Estructura inesperada en la respuesta.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(System.Net.HttpStatusCode.InternalServerError, errorResponse);
                }

                string result = row.Peek(1)?.ToString() ?? string.Empty;
                string message = row.Peek(2)?.ToString() ?? string.Empty;

                bool successFlag =
                    string.Equals(result, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);

                var okResponse = new IndApiResponse<object>
                {
                    Success = successFlag,
                    Message = message,
                    ErrorCode = successFlag ? null : (message.IndexOf("no encontrada", System.StringComparison.OrdinalIgnoreCase) >= 0 ? IndErrorCodes.CrmActivityNotFound : IndErrorCodes.AxComError),
                    Errors = null,
                    Data = new { body.refRecIdActividad, body.asistenteId },
                    TraceId = traceId
                };

                if (successFlag)
                    return Content(System.Net.HttpStatusCode.NoContent, okResponse);

                if (okResponse.ErrorCode == IndErrorCodes.CrmActivityNotFound)
                    return Content(System.Net.HttpStatusCode.NotFound, okResponse);

                return Content(System.Net.HttpStatusCode.InternalServerError, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] DeleteVisitaAsistente API: {ex.Message}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = $"Error DeleteVisitaAsistente: {ex.Message}",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(System.Net.HttpStatusCode.InternalServerError, response);
            }
        }
    }
}






