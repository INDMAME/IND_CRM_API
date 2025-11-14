using IND_CRM_APIs.Services;
using System;
using System.Web.Http;

namespace IND_CRM_APIs.Controllers
{
    /// <summary>
    /// Controlador responsable de la autenticación de usuarios.
    /// Genera y devuelve tokens JWT válidos para acceder a los endpoints de Axapta.
    /// </summary>
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        // Instancia directa, sin ServiceLocator
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;

        private readonly JwtService _jwt = new JwtService();

        /// <summary>
        /// Modelo básico del cuerpo del login.
        /// </summary>
        public class LoginRequest
        {
            public string Username { get; set; }
            public string Password { get; set; }
        }

        public static class AxSession
        {
            public static readonly AxaptaSessionManager Manager = new AxaptaSessionManager();
        }


        // -------------------------------------------------------------
        // 🔹 LOGIN PRINCIPAL
        // -------------------------------------------------------------
        /// <summary>
        /// Endpoint de autenticación.
        /// Valida credenciales contra Axapta y genera un token JWT de sesión.
        /// </summary>
        /// <param name="dto">Usuario y contraseña.</param>
        /// <returns>Token JWT y su fecha de expiración.</returns>
        [AllowAnonymous]
        [HttpPost, Route("login")]
        public IHttpActionResult Login([FromBody] LoginRequest dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.Username) || string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest("Faltan credenciales.");

            try
            {
                //  Generar token válido por 15 minutos
                var tokenInfo = _jwt.GenerateToken(dto.Username, 15);

                //  Crear sesión Axapta (conexión COM)
                var sessionCreated = _sessionManager.CreateOrGetSession(dto.Username, dto.Password, tokenInfo);

                if (!sessionCreated)
                    return InternalServerError(new Exception("No se pudo iniciar sesión en Axapta (ver log)."));

                //  Retornar el token y fecha de expiración
                return Ok(new
                {
                    token = tokenInfo.Token,
                    expires = tokenInfo.Expiration
                });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }
    }
}
