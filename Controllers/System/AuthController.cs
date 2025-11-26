using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Configuration;
using System.Web.Http;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Controlador responsable de la autenticación de usuarios.
    /// Genera y devuelve tokens JWT válidos para acceder a los endpoints de Axapta.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IJwtService _jwt;

        public AuthController(IAxaptaSessionManager sessionManager, IJwtService jwt)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _jwt = jwt ?? throw new ArgumentNullException(nameof(jwt));
        }

        // LOGIN PRINCIPAL
        [AllowAnonymous]
        [HttpPost, Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequest dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Faltan credenciales.");

            try
            {
                AxaptaSessionManager.LogStatic($"[AUTH] Login attempt for user {dto.Username}");

                var tokenInfo = _jwt.GenerateToken(dto.Username, 60);
                var sessionCreated = _sessionManager.CreateOrGetSession(dto.Username, dto.Password, tokenInfo);

                if (!sessionCreated)
                {
                    AxaptaSessionManager.LogStatic($"[AUTH-FAIL] Could not create Axapta session for {dto.Username}");
                    return InternalServerError(new Exception("No se pudo iniciar sesión en Axapta (ver log)."));
                }

                AxaptaSessionManager.LogStatic($"[AUTH-SUCCESS] Token issued for {dto.Username}");

                return Ok(new
                {
                    token = tokenInfo.Token,
                    expires = tokenInfo.Expiration
                });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[AUTH-ERROR] {dto?.Username} -> {ex.Message}");
                return InternalServerError(ex);
            }
        }

        // REFRESH TOKEN
        [Authorize]
        [HttpPost, Route("refresh")]
        public IHttpActionResult Refresh()
        {
            try
            {
                var username = User?.Identity?.Name;
                if (string.IsNullOrWhiteSpace(username))
                    return Unauthorized();

                var authHeader = Request.Headers.Authorization;
                var oldToken = authHeader != null && authHeader.Scheme == "Bearer" ? authHeader.Parameter : null;

                var expirationSetting = ConfigurationManager.AppSettings["JwtSettings:ExpirationMinutes"];
                int expirationMinutes = int.TryParse(expirationSetting, out var exp) ? exp : 60;

                var tokenInfo = _jwt.GenerateToken(username, expirationMinutes);
                _sessionManager.RefreshSessionToken(username, tokenInfo, oldToken);

                AxaptaSessionManager.LogStatic("[AUTH-REFRESH] Token refreshed for " + username);

                return Ok(new
                {
                    token = tokenInfo.Token,
                    expires = tokenInfo.Expiration
                });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic("[AUTH-ERROR] Refresh -> " + ex.Message);
                return InternalServerError(ex);
            }
        }
    }
}

