using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// Own login of the api/crm/webstore/* routes. Only the AX user named by AX_WEBSTORE_USER can
    /// sign in: the credentials are validated against Axapta with the existing session manager
    /// (the same per-user credential cache that the per-request COM session uses) and the answer is
    /// a web store token from CrmWebStoreTokenService. AuthController and JwtService are not used.
    /// </summary>
    [RoutePrefix("api/crm/webstore/auth")]
    public class CrmWebStoreAuthController : ApiController
    {
        private const string SwaggerTag = "Almacen web CRM_INSERTEC";

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IAxLogger _logger;
        private readonly CrmWebStoreTokenService _tokens;

        /// <summary>Constructor resolved with services that are already registered.</summary>
        public CrmWebStoreAuthController(IAxaptaSessionManager sessionManager, IAxLogger logger)
            : this(sessionManager, logger, new CrmWebStoreTokenService())
        {
        }

        /// <summary>Constructor with an explicit token service (tests).</summary>
        internal CrmWebStoreAuthController(IAxaptaSessionManager sessionManager, IAxLogger logger, CrmWebStoreTokenService tokens)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        /// <summary>
        /// Validates the technical user against Axapta and returns a token for api/crm/webstore/*.
        /// </summary>
        /// <remarks>
        /// 200 token; 422 sin usuario o clave; 403 usuario distinto de AX_WEBSTORE_USER; 401 credenciales
        /// rechazadas por Axapta; 503 rutas deshabilitadas (WEBSTORE_DISABLED) o tiempo de espera de Axapta.
        /// </remarks>
        [AllowAnonymous]
        [HttpPost, Route("login")]
        [ResponseType(typeof(IndApiResponse<WebStoreTokenDto>))]
        [SwaggerOperation(Tags = new[] { SwaggerTag })]
        [SwaggerResponse(HttpStatusCode.OK, "Token emitido", typeof(IndApiResponse<WebStoreTokenDto>))]
        [SwaggerResponse(HttpStatusCode.Unauthorized, "Credenciales rechazadas por Axapta", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.Forbidden, "Usuario no autorizado para estas rutas", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Faltan usuario o clave", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Rutas deshabilitadas o Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult Login([FromBody] WebStoreLoginRequest body)
        {
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
            if (!_tokens.IsEnabled)
            {
                _logger.Log("[WEBSTORE-AUTH] login disabled userConfigured=false traceId=" + traceId, AxaptaSessionManager.LogLevel.Warning);
                return Fail(HttpStatusCode.ServiceUnavailable, CrmWebStoreErrorCodes.Disabled,
                    "Las rutas del almacen web no estan habilitadas en este servidor.", null, traceId);
            }

            var errors = new List<IndValidationError>();
            if (string.IsNullOrWhiteSpace(body?.username))
                errors.Add(new IndValidationError { Field = "username", Message = "username es obligatorio." });
            if (string.IsNullOrEmpty(body?.password))
                errors.Add(new IndValidationError { Field = "password", Message = "password es obligatorio." });
            if (errors.Count > 0)
                return Fail((HttpStatusCode)422, IndErrorCodes.ValidationError, "Error de validacion.", errors, traceId);

            var requested = body.username.Trim();
            if (!_tokens.IsConfiguredUser(requested))
            {
                // The password of another user is never sent to Axapta from this route.
                _logger.Log("[WEBSTORE-AUTH] login rejected user=" + SafeLogValue(requested) + " allowedUser=false traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Warning);
                return Fail(HttpStatusCode.Forbidden, CrmWebStoreErrorCodes.Forbidden,
                    "Usuario no autorizado para estas rutas.", null, traceId);
            }

            var username = _tokens.ConfiguredUser;
            try
            {
                _logger.Log("[WEBSTORE-AUTH] login attempt user=" + username + " passwordProvided=true traceId=" + traceId);
                if (!_sessionManager.CreateOrGetSession(username, body.password, null))
                {
                    _logger.Log("[WEBSTORE-AUTH] login failed user=" + username + " traceId=" + traceId, AxaptaSessionManager.LogLevel.Warning);
                    return Fail(HttpStatusCode.Unauthorized, IndErrorCodes.AuthInvalidCredentials,
                        "No se pudo iniciar sesion en Axapta.", null, traceId);
                }

                var issued = _tokens.Issue(username);

                // Keeps the validated credentials cached while this token lives, like the common login.
                var binding = new JwtService.JwtTokenInfo { Token = issued.Token, Expiration = issued.ExpiresUtc };
                if (!_sessionManager.RefreshSessionToken(username, binding, null))
                {
                    _logger.Log("[WEBSTORE-AUTH] token binding failed user=" + username + " traceId=" + traceId, AxaptaSessionManager.LogLevel.Error);
                    return Fail(HttpStatusCode.InternalServerError, IndErrorCodes.InternalError,
                        "Error interno de autenticacion.", null, traceId);
                }

                _logger.Log("[WEBSTORE-AUTH] token issued user=" + username + " expiresUtc=" + issued.ExpiresUtc.ToString("o") + " traceId=" + traceId);
                return Ok(new IndApiResponse<WebStoreTokenDto>
                {
                    Success = true,
                    Message = "Token emitido.",
                    Data = new WebStoreTokenDto { Token = issued.Token, Expires = issued.ExpiresUtc },
                    TraceId = traceId
                });
            }
            catch (IND_AxCallTimeoutException ex)
            {
                _logger.Log("[WEBSTORE-AUTH] login timeout user=" + username + " traceId=" + traceId, AxaptaSessionManager.LogLevel.Error);
                return Fail(HttpStatusCode.ServiceUnavailable, IndErrorCodes.AxTimeout, ex.UserMessage, null, traceId);
            }
            catch (Exception ex)
            {
                _logger.Log("[WEBSTORE-AUTH] login error user=" + username + " failureType=" + ex.GetType().Name + " traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Error);
                return Fail(HttpStatusCode.InternalServerError, IndErrorCodes.InternalError,
                    "Error interno de autenticacion.", null, traceId);
            }
        }

        // Untrusted user names are logged on one line and with a bounded length.
        private static string SafeLogValue(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "-";

            var chars = value.ToCharArray();
            for (var i = 0; i < chars.Length; i++)
            {
                if (char.IsControl(chars[i]))
                    chars[i] = '?';
            }

            var text = new string(chars);
            return text.Length > 64 ? text.Substring(0, 64) + "..." : text;
        }

        // Standard envelope for every non-200 answer of the login.
        private IHttpActionResult Fail(HttpStatusCode status, string errorCode, string message, List<IndValidationError> errors, string traceId)
        {
            return Content(status, new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Errors = errors,
                Data = null,
                TraceId = traceId
            });
        }
    }
}
