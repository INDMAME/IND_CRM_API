using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Net;
using System.Web.Http;

namespace IND_CRM_API.Controllers.System
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private static readonly DateTime _startTimeUtc = DateTime.UtcNow;

        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;
        private readonly IAxLogger _logger;

        public HealthController(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
        }

        /// <summary>
        /// Devuelve estado basico del servicio.
        /// </summary>
        [AllowAnonymous]
        [HttpGet, Route("ping")]
        [SwaggerOperation(Tags = new[] { "Salud" })]
        public IHttpActionResult Ping()
        {
            var traceId = Guid.NewGuid().ToString("N");
            var response = new IndApiResponse<object>
            {
                Success = true,
                Message = "Servicio en linea.",
                ErrorCode = null,
                Errors = null,
                Data = new { status = "Online", startedUtc = _startTimeUtc },
                TraceId = traceId
            };
            return Ok(response);
        }

        /// <summary>
        /// Comprueba la salud de la sesion de Axapta.
        /// </summary>
        [Authorize]
        [HttpGet, Route("health")]
        [SwaggerOperation(Tags = new[] { "Salud" })]
        public IHttpActionResult AxaptaHealth()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = User?.Identity?.Name ?? "health-check";

                _sessionManager.CreateOrGetSession(username, null, null);

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = "AX operativo.",
                    ErrorCode = null,
                    Errors = null,
                    Data = new { status = "Ok" },
                    TraceId = traceId
                };
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                _logger.Log("[HEALTH-ERROR] " + ex.Message);
                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error al validar AX.",
                    ErrorCode = IndErrorCodes.AxSessionError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, errorResponse);
            }
        }
    }
}
