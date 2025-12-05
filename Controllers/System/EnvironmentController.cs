using System;
using System.Net;
using System.Web.Http;
using IND_CRM_API.Services;
using AxaptaCOMConnector;
using IND_CRM_API.Models.Responses;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.System
{
    [Authorize]
    [RoutePrefix("api/system")]
    public class EnvironmentController : ApiController
    {
        // Use global AxSession singleton
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;

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
        public IHttpActionResult GetEnvironmentName()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = GetAuthenticatedUsername();
                AxaptaSessionManager.LogStatic($"[API-IN] GetEnvironmentName llamado por {username}");

                object resultObj = _sessionManager.CallMethodByUser(
                    username,
                    "INDCRMApiClass",
                    "getEnvironmentName"
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                {
                    var emptyResponse = new IndApiResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        ErrorCode = null,
                        Errors = null,
                        Data = new { environment = "Unknown" },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                var row = root.Peek(1) as IAxaptaContainer;
                if (row == null || row.Length() < 1)
                {
                    var emptyResponse = new IndApiResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        ErrorCode = null,
                        Errors = null,
                        Data = new { environment = "Unknown" },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                string env = row.Peek(1)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(env))
                    env = "Unknown";

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Errors = null,
                    Data = new { environment = env },
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
                    Message = $"Error GetEnvironmentName: {ex.Message}",
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
        public IHttpActionResult GetCompanyName()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = GetAuthenticatedUsername();
                AxaptaSessionManager.LogStatic($"[API-IN] GetCompanyName llamado por {username}");

                object resultObj = _sessionManager.CallMethodByUser(
                    username,
                    "INDCRMApiClass",
                    "getCompanyName"
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                {
                    var emptyResponse = new IndApiResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        ErrorCode = null,
                        Errors = null,
                        Data = new { companyId = "", companyName = "", company = "" },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                var row = root.Peek(1) as IAxaptaContainer;
                if (row == null || row.Length() < 2)
                {
                    var emptyResponse = new IndApiResponse<object>
                    {
                        Success = true,
                        Message = "OK",
                        ErrorCode = null,
                        Errors = null,
                        Data = new { companyId = "", companyName = "", company = "" },
                        TraceId = traceId
                    };
                    return Ok(emptyResponse);
                }

                string companyId = row.Peek(1)?.ToString() ?? string.Empty;
                string companyName = row.Peek(2)?.ToString() ?? string.Empty;

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Errors = null,
                    Data = new { companyId, companyName, company = companyName },
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
                    Message = $"Error GetCompanyName: {ex.Message}",
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

