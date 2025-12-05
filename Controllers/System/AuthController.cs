using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Configuration;
using System.Net;
using System.Web.Http;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Controlador responsable de autenticar usuarios y emitir tokens JWT.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/auth")]
    public class AuthController : ApiController
    {
        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IJwtService _jwt;
        private readonly IAxLogger _logger;

        public AuthController(IAxaptaSessionManager sessionManager, IJwtService jwt, IAxLogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _jwt = jwt ?? throw new ArgumentNullException(nameof(jwt));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Autentica al usuario y devuelve un token JWT.
        /// </summary>
        /// <remarks>
        /// ErrorCode posibles: ValidationError cuando faltan campos, AuthInvalidCredentials si la sesion AX no se puede crear, InternalError en fallos inesperados.
        /// </remarks>
        /// <param name="dto">Credenciales de usuario.</param>
        [SwaggerResponse(HttpStatusCode.OK, "Login correcto", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.Unauthorized, "Credenciales invalidas", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        [AllowAnonymous]
        [HttpPost, Route("login")]
        [SwaggerOperation(Tags = new[] { "Autenticacion" })]
        public IHttpActionResult Login([FromBody] LoginRequest dto)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var errors = new global::System.Collections.Generic.List<IndValidationError>();

            if (dto == null)
            {
                errors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(dto.Username))
                    errors.Add(new IndValidationError { Field = "username", Message = "El usuario es obligatorio." });
                if (string.IsNullOrWhiteSpace(dto.Password))
                    errors.Add(new IndValidationError { Field = "password", Message = "La clave es obligatoria." });
            }

            if (errors.Count > 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = errors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                _logger.Log($"[AUTH] Login attempt for user {dto.Username}");

                var expirationSetting = ConfigurationManager.AppSettings["JwtSettings:ExpirationMinutes"];
                int expirationMinutes = int.TryParse(expirationSetting, out var exp) ? exp : 60;

                var tokenInfo = _jwt.GenerateToken(dto.Username, expirationMinutes);
                var sessionCreated = _sessionManager.CreateOrGetSession(dto.Username, dto.Password, tokenInfo);

                if (!sessionCreated)
                {
                    _logger.Log($"[AUTH-FAIL] Could not create Axapta session for {dto.Username}");
                    var failResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "No se pudo iniciar sesion en Axapta.",
                        ErrorCode = IndErrorCodes.AuthInvalidCredentials,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.Unauthorized, failResponse);
                }

                _logger.Log($"[AUTH-SUCCESS] Token issued for {dto.Username}");

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = "Token emitido.",
                    ErrorCode = null,
                    Errors = null,
                    Data = new { token = tokenInfo.Token, expires = tokenInfo.Expiration },
                    TraceId = traceId
                };
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                _logger.Log($"[AUTH-ERROR] {dto?.Username} -> {ex.Message}");
                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno de autenticacion.",
                    ErrorCode = IndErrorCodes.InternalError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, errorResponse);
            }
        }

        /// <summary>
        /// Renueva el token JWT para el usuario autenticado.
        /// </summary>
        /// <remarks>
        /// ErrorCode posibles: AuthRequired cuando no hay identidad, InternalError en fallos inesperados.
        /// </remarks>
        [SwaggerResponse(HttpStatusCode.OK, "Token renovado", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.Unauthorized, "Autenticacion requerida", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        [Authorize]
        [HttpPost, Route("refresh")]
        [SwaggerOperation(Tags = new[] { "Autenticacion" })]
        public IHttpActionResult Refresh()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = User?.Identity?.Name;
                if (string.IsNullOrWhiteSpace(username))
                {
                    var authResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Autenticacion requerida.",
                        ErrorCode = IndErrorCodes.AuthRequired,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.Unauthorized, authResponse);
                }

                var authHeader = Request.Headers.Authorization;
                var oldToken = authHeader != null && authHeader.Scheme == "Bearer" ? authHeader.Parameter : null;

                var expirationSetting = ConfigurationManager.AppSettings["JwtSettings:ExpirationMinutes"];
                int expirationMinutes = int.TryParse(expirationSetting, out var exp) ? exp : 60;

                var tokenInfo = _jwt.GenerateToken(username, expirationMinutes);
                _sessionManager.RefreshSessionToken(username, tokenInfo, oldToken);

                _logger.Log("[AUTH-REFRESH] Token refreshed for " + username);

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = "Token renovado.",
                    ErrorCode = null,
                    Errors = null,
                    Data = new { token = tokenInfo.Token, expires = tokenInfo.Expiration },
                    TraceId = traceId
                };
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                _logger.Log("[AUTH-ERROR] Refresh -> " + ex.Message);
                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno al refrescar token.",
                    ErrorCode = IndErrorCodes.InternalError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, errorResponse);
            }
        }
    }
}
