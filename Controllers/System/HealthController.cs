using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.System
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private static readonly DateTime _startTimeUtc = DateTime.UtcNow;

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IAxLogger _logger;

        public HealthController(IAxaptaSessionManager sessionManager, IAxLogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? new FileAxLogger();
        }

        /// <summary>
        /// Devuelve estado basico del servicio.
        /// </summary>
        [AllowAnonymous]
        [HttpGet, Route("ping")]
        [SwaggerOperation(Tags = new[] { "Salud" })]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerResponse(HttpStatusCode.OK, "Estado del servicio", typeof(IndPagedResponse<object>))]
        public IHttpActionResult Ping()
        {
            var traceId = Guid.NewGuid().ToString("N");
            var response = new IndPagedResponse<object>
            {
                Success = true,
                Message = "OK",
                Items = new global::System.Collections.Generic.List<object>
                {
                    new { status = "Online", startedUtc = _startTimeUtc }
                },
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
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerResponse(HttpStatusCode.OK, "Estado de Axapta", typeof(IndPagedResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "No disponible", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult AxaptaHealth()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = User?.Identity?.Name ?? "health-check";
                var sessionReady = _sessionManager.CreateOrGetSession(username, null, null);
                if (!sessionReady)
                {
                    _logger.Log("[HEALTH-WARN] No se pudo validar sesion AX para " + username);
                    var unavailableResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "No se pudo validar la sesion de Axapta.",
                        ErrorCode = IndErrorCodes.AxSessionError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.ServiceUnavailable, unavailableResponse);
                }

                var okResponse = new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Items = new global::System.Collections.Generic.List<object> { new { status = "Ok" } },
                    TraceId = traceId
                };
                return Ok(okResponse);
            }
            catch (IND_AxCallTimeoutException ex)
            {
                _logger.Log("[HEALTH-TIMEOUT] " + ex.Message);
                var timeoutResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = ex.UserMessage,
                    ErrorCode = IndErrorCodes.AxTimeout,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.ServiceUnavailable, timeoutResponse);
            }
            catch (Exception ex)
            {
                _logger.Log("[HEALTH-ERROR] " + ex);
                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
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
