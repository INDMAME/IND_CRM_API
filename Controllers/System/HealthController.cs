using IND_CRM_API.Services;
using System;
using System.Web.Http;
using IND_CRM_API.Services.Interfaces;
using System.Configuration;

namespace IND_CRM_API.Controllers.System
{
    [RoutePrefix("api/health")]
    public class HealthController : ApiController
    {
        private static readonly DateTime _startTimeUtc = DateTime.UtcNow;

        // Use global AxSession singleton
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;
        private readonly IAxLogger _logger;

        public HealthController(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
        }

        [AllowAnonymous]
        [HttpGet, Route("ping")]
        public IHttpActionResult Ping()
        {
            return Ok(new
            {
                status = "Online",
                startedUtc = _startTimeUtc
            });
        }

        [Authorize]
        [HttpGet, Route("health")]
        public IHttpActionResult AxaptaHealth()
        {
            try
            {
                var username = User?.Identity?.Name ?? "health-check";

                // Simple: solo intentar crear o recuperar la sesion
                _sessionManager.CreateOrGetSession(username, null, null);

                return Ok(new { status = "Ok" });
            }
            catch (Exception ex)
            {
                _logger.Log("[HEALTH-ERROR] " + ex.Message);
                return InternalServerError(ex);
            }
        }
    }
}
