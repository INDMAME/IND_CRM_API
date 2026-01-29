using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Helpers;
using AxaptaCOMConnector;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Runtime.InteropServices;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;
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

                // Primero validar credenciales contra Axapta, luego emitir el token.
                var sessionCreated = _sessionManager.CreateOrGetSession(dto.Username, dto.Password, null);

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

                var tokenInfo = _jwt.GenerateToken(dto.Username, expirationMinutes);
                if (!_sessionManager.RefreshSessionToken(dto.Username, tokenInfo, null))
                {
                    _logger.Log($"[AUTH-ERROR] Could not bind token to session for {dto.Username}");
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

        /// <summary>
        /// Obtiene el contexto Entra (usuario, companias y modulos).
        /// </summary>
        /// <remarks>
        /// ErrorCode posibles: ValidationError en datos faltantes, AuthForbidden en permisos, AxComError en AX.
        /// </remarks>
        [SwaggerResponse(HttpStatusCode.OK, "Contexto Entra", typeof(IndPagedResponse<EntraContextDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.Forbidden, "Acceso denegado", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        [Authorize]
        [HttpPost, Route("entra/context")]
        [SwaggerOperation(Tags = new[] { "Autenticacion" })]
        [ResponseType(typeof(IndPagedResponse<EntraContextDto>))]
        public IHttpActionResult EntraContext([FromBody] EntraContextRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var errors = new List<IndValidationError>();

            void LogOut(HttpStatusCode statusCode)
            {
                _logger.Log($"[AUTH-ENTRA-OUT] {(int)statusCode} traceId={traceId}");
            }

            if (body == null)
            {
                errors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                var entraOidTrim = body.entraOid?.Trim();
                if (string.IsNullOrWhiteSpace(entraOidTrim))
                    errors.Add(new IndValidationError { Field = "entraOid", Message = "entraOid es obligatorio." });
                else if (!Guid.TryParse(entraOidTrim, out _))
                    errors.Add(new IndValidationError { Field = "entraOid", Message = "entraOid debe ser un GUID valido." });

                if (string.IsNullOrWhiteSpace(body.appCode))
                    errors.Add(new IndValidationError { Field = "appCode", Message = "appCode es obligatorio." });
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
                LogOut((HttpStatusCode)422);
                return Content((HttpStatusCode)422, validationResponse);
            }

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
                    LogOut(HttpStatusCode.Unauthorized);
                    return Content(HttpStatusCode.Unauthorized, authResponse);
                }

                _logger.Log($"[AUTH-ENTRA-IN] user={username} appCode={body.appCode} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(body.entraOid?.Trim() ?? string.Empty);
                con.Append(body.appCode?.Trim() ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMUtilityService",
                    "loginEntraContext",
                    con
                );

                var root = resultObj as IAxaptaContainer;
                if (root == null)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                var header = MapEntraHeader(root);
                if (header == null)
                {
                    var invalidResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, invalidResponse);
                }

                if (!header.Success)
                {
                    var forbiddenResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = string.IsNullOrWhiteSpace(header.Message) ? "Acceso denegado." : header.Message,
                        ErrorCode = IndErrorCodes.AuthForbidden,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.Forbidden);
                    return Content(HttpStatusCode.Forbidden, forbiddenResponse);
                }

                var context = new EntraContextDto
                {
                    Header = header,
                    Companies = MapEntraCompanies(root)
                };

                UserCompanyAccessCache.SetAllowedCompanies(
                    username,
                    context.Companies == null ? null : context.Companies.ConvertAll(c => c.CompanyId)
                );

                var okResponse = new IndPagedResponse<EntraContextDto>
                {
                    Success = true,
                    Message = "OK",
                    Items = new List<EntraContextDto> { context },
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                _logger.Log($"[AUTH-ENTRA-ERROR] {ex}");
                var errorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError;
                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = errorCode,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, errorResponse);
            }
        }

        // Mapeo defensivo del contenedor AX a DTOs tipados.
        private EntraContextHeaderDto MapEntraHeader(IAxaptaContainer root)
        {
            var headerContainer = SafePeekContainer(root, 1);
            if (headerContainer == null)
                return null;

            var headerRow = SafePeekContainer(headerContainer, 1);
            if (headerRow == null && SafeLength(headerContainer) >= 2)
                headerRow = headerContainer;

            if (headerRow == null)
                return null;

            var header = new EntraContextHeaderDto
            {
                Success = ToBool(SafeString(headerRow, 1)),
                Message = SafeString(headerRow, 2),
                AxUserId = string.Empty,
                UserActive = false,
                AppActive = false,
                DefaultCompany = string.Empty
            };

            if (SafeLength(headerRow) >= 6)
            {
                header.AxUserId = SafeString(headerRow, 3);
                header.UserActive = ToBool(SafeString(headerRow, 4));
                header.AppActive = ToBool(SafeString(headerRow, 5));
                header.DefaultCompany = SafeString(headerRow, 6);
            }

            if (string.IsNullOrWhiteSpace(header.Message))
                header.Message = header.Success ? "OK" : "Acceso denegado.";

            return header;
        }

        private List<EntraCompanyDto> MapEntraCompanies(IAxaptaContainer root)
        {
            var companies = new List<EntraCompanyDto>();
            var companiesCon = SafePeekContainer(root, 2);
            var count = SafeLength(companiesCon);

            for (int i = 1; i <= count; i++)
            {
                var companyCon = SafePeekContainer(companiesCon, i);
                if (companyCon == null)
                    continue;

                var modulesCon = SafePeekContainer(companyCon, 4);

                companies.Add(new EntraCompanyDto
                {
                    CompanyId = SafeString(companyCon, 1),
                    IsDefault = ToBool(SafeString(companyCon, 2)),
                    CompanyName = SafeString(companyCon, 3),
                    Modules = MapEntraModules(modulesCon)
                });
            }

            return companies;
        }

        private List<EntraModuleDto> MapEntraModules(IAxaptaContainer modulesCon)
        {
            var modules = new List<EntraModuleDto>();
            var count = SafeLength(modulesCon);

            for (int i = 1; i <= count; i++)
            {
                var moduleCon = SafePeekContainer(modulesCon, i);
                if (moduleCon == null)
                    continue;

                modules.Add(new EntraModuleDto
                {
                    ModuleCode = SafeString(moduleCon, 1),
                    Description = SafeString(moduleCon, 2),
                    IsActive = ToBool(SafeString(moduleCon, 3)),
                    AccessRightsInt = ToInt(SafeString(moduleCon, 4))
                });
            }

            return modules;
        }

        private static IAxaptaContainer SafePeekContainer(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index) as IAxaptaContainer;
            }
            catch
            {
                return null;
            }
        }

        private static int SafeLength(IAxaptaContainer container)
        {
            try
            {
                return container?.Length() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        private static string SafeString(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static bool ToBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            return normalized == "1" ||
                   normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
        }

        private static int ToInt(string value)
        {
            if (int.TryParse(value, out var result))
                return result;
            return 0;
        }
    }
}
