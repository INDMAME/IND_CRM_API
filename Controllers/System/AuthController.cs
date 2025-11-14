using IND_CRM_API.Services;
using System;
using System.Configuration;
using System.Web.Http;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Controlador responsable de la autenticacion de usuarios.
    /// Genera y devuelve tokens JWT validos para acceder a los endpoints de Axapta.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        // Use global AxSession singleton
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;

        // Servicio JWT
        private readonly JwtService _jwt = new JwtService();

        /// <summary>
        /// Modelo basico del cuerpo del login.
        /// </summary>
        public class LoginRequest
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        // -------------------------------------------------------------
        // LOGIN PRINCIPAL
        // -------------------------------------------------------------
        /// <summary>
        /// Endpoint de autenticacion.
        /// Valida credenciales contra Axapta y genera un token JWT de sesion.
        /// </summary>
        /// <param name="dto">Usuario y contrasena.</param>
        /// <returns>Token JWT y su fecha de expiracion.</returns>
        [AllowAnonymous]
        [HttpPost, Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequest dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Faltan credenciales.");

            try
            {
                AxaptaSessionManager.LogStatic($"[AUTH] Login attempt for user {dto.Username}");

                // Generar token valido por 60 minutos (o configuracion interna)
                var tokenInfo = _jwt.GenerateToken(dto.Username, 60);

                // Crear sesion Axapta (conexion COM) usando singleton
                var sessionCreated = _sessionManager.CreateOrGetSession(dto.Username, dto.Password, tokenInfo);

                if (!sessionCreated)
                {
                    AxaptaSessionManager.LogStatic($"[AUTH-FAIL] Could not create Axapta session for {dto.Username}");
                    return InternalServerError(new Exception("No se pudo iniciar sesion en Axapta (ver log)."));
                }

                AxaptaSessionManager.LogStatic($"[AUTH-OK] Axapta session created for {dto.Username}");

                return Ok(new
                {
                    token = tokenInfo.Token,
                    expires = tokenInfo.Expiration
                });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[AUTH-ERROR] {dto.Username} -> {ex.Message}");
                return InternalServerError(ex);
            }
        }

        [Authorize]
        [HttpPost, Route("refresh")]
        public IHttpActionResult Refresh()
        {
            try
            {
                // Recuperar el usuario autenticado desde el token actual
                var username = User?.Identity?.Name;
                if (string.IsNullOrWhiteSpace(username))
                    return Unauthorized();

                // Leer el token actual desde el header Authorization: Bearer xxx
                var authHeader = Request.Headers.Authorization;
                var oldToken = authHeader != null && authHeader.Scheme == "Bearer"
                    ? authHeader.Parameter
                    : null;

                // Leer minutos de expiracion desde App.config
                var expirationSetting = ConfigurationManager.AppSettings["JwtSettings:ExpirationMinutes"];

                // Intentar convertir el valor a int, si falla usar 60 como valor por defecto
                int expirationMinutes;
                if (!int.TryParse(expirationSetting, out expirationMinutes))
                {
                    expirationMinutes = 60;
                }

                // Generar el token usando el valor de configuracion
                var tokenInfo = _jwt.GenerateToken(username, expirationMinutes);


                // Actualizar la sesion en Axapta para usar el nuevo token y nueva expiracion
                _sessionManager.RefreshSessionToken(username, tokenInfo, oldToken);

                // Log de control
                AxaptaSessionManager.LogStatic("[AUTH-REFRESH] Token refreshed for " + username);

                // Devolver el nuevo token y su expiracion al cliente
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
