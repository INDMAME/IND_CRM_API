using System;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using AxaptaCOMConnector;
using IND_CRM_API.Models.Responses;
using Swashbuckle.Swagger.Annotations;
using System.Collections.Generic; // <-- Agregar esta línea

namespace IND_CRM_API.Controllers.System
{
    [Authorize]
    [RoutePrefix("api/system")]
    public class EnvironmentController : ApiController
    {
        private readonly IAxaptaSessionManager _sessionManager;

        public EnvironmentController(IAxaptaSessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        private string GetAuthenticatedUsername()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no autenticado o token invalido.");
            return username;
        }

        // ---------------------------------------------------------
        // OBTENER ENTORNO (TEST / PROD) via container
        // X++: static container getEnvironmentName() returns [["TEST"]]
        // ---------------------------------------------------------
        [HttpGet, Route("getEnvironmentName")]
        [SwaggerOperation(Tags = new[] { "Sistema" })]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerResponse(HttpStatusCode.OK, "Entorno del sistema", typeof(IndPagedResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetEnvironmentName()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = GetAuthenticatedUsername();
                AxaptaSessionManager.LogStatic($"[API-IN] GetEnvironmentName llamado por {username}");

                object resultObj = _sessionManager.CallMethodByUser(
                    username,
                    "INDCRMUtilityService",
                    "getEnvironmentName"
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                {
                    var emptyResponse = new IndPagedResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        Items = new List<object> { new { environment = "Unknown" } },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                var row = root.Peek(1) as IAxaptaContainer;
                if (row == null || row.Length() < 1)
                {
                    var emptyResponse = new IndPagedResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        Items = new List<object> { new { environment = "Unknown" } },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                string env = row.Peek(1)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(env))
                    env = "Unknown";

                var okResponse = new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Items = new List<object> { new { environment = env } },
                    TraceId = traceId
                };
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetEnvironmentName API: {ex.Message}");
                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, errorResponse);
            }
        }

        // ---------------------------------------------------------
        // OBTENER NOMBRE DE COMPANIA via container
        // X++: static container getCompanyName()
        //      returns [[ dataAreaId, Name ]]
        // ---------------------------------------------------------
        [HttpGet, Route("getCompanyName")]
        [SwaggerOperation(Tags = new[] { "Sistema" })]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerResponse(HttpStatusCode.OK, "Compania del sistema", typeof(IndPagedResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetCompanyName()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = GetAuthenticatedUsername();
                AxaptaSessionManager.LogStatic($"[API-IN] GetCompanyName llamado por {username}");

                object resultObj = _sessionManager.CallMethodByUser(
                    username,
                    "INDCRMUtilityService",
                    "getCompanyName"
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                {
                    var emptyResponse = new IndPagedResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        Items = new List<object> { new { companyId = "", companyName = "", company = "" } },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                var row = root.Peek(1) as IAxaptaContainer;
                if (row == null || row.Length() < 2)
                {
                    var emptyResponse = new IndPagedResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        Items = new List<object> { new { companyId = "", companyName = "", company = "" } },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                string companyId = row.Peek(1)?.ToString() ?? string.Empty;
                string companyName = row.Peek(2)?.ToString() ?? string.Empty;

                var okResponse = new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Items = new List<object> { new { companyId, companyName, company = companyName } },
                    TraceId = traceId
                };
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetCompanyName API: {ex.Message}");
                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, errorResponse);
            }
        }
    }
}
