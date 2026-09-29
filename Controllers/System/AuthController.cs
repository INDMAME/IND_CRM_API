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
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Net;
using System.Threading;
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
            var correlationId = IndRequestDiagnosticsHelper.GetOrCreateCorrelationId(Request);
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
            var authSw = Stopwatch.StartNew();
            var errors = new global::System.Collections.Generic.List<IndValidationError>();
            var requestedUser = dto?.Username?.Trim() ?? string.Empty;

            LogAuthTrace(
                "login",
                "request-received",
                traceId,
                correlationId,
                requestedUser,
                null,
                $"dtoNull={dto == null} passwordProvided={!string.IsNullOrWhiteSpace(dto?.Password)} passwordLength={dto?.Password?.Length ?? 0}",
                authSw);

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
                LogAuthTrace(
                    "login",
                    "validation-failed",
                    traceId,
                    correlationId,
                    requestedUser,
                    null,
                    $"errorCount={errors.Count}",
                    authSw,
                    AxaptaSessionManager.LogLevel.Warning);

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
                LogAuthTrace("login", "validation-ok", traceId, correlationId, dto.Username, null, null, authSw);
                _logger.Log($"[AUTH] Login attempt for user {dto.Username}");

                var expirationSetting = AppSettingsHelper.GetSetting(
                    "JwtSettings:ExpirationMinutes",
                    "INDCRM_JWT_EXPIRATION_MINUTES");
                int expirationMinutes = int.TryParse(expirationSetting, out var exp) ? exp : 60;
                LogAuthTrace(
                    "login",
                    "expiration-resolved",
                    traceId,
                    correlationId,
                    dto.Username,
                    null,
                    $"expirationSetting={expirationSetting ?? string.Empty} expirationMinutes={expirationMinutes}",
                    authSw);

                // Primero validar credenciales contra Axapta, luego emitir el token.
                LogAuthTrace("login", "before-create-or-get-session", traceId, correlationId, dto.Username, null, "tokenInfoPresent=false", authSw);
                var sessionCreated = _sessionManager.CreateOrGetSession(dto.Username, dto.Password, null);
                LogAuthTrace(
                    "login",
                    "after-create-or-get-session",
                    traceId,
                    correlationId,
                    dto.Username,
                    null,
                    "sessionCreated=" + sessionCreated,
                    authSw,
                    sessionCreated ? AxaptaSessionManager.LogLevel.Info : AxaptaSessionManager.LogLevel.Warning);

                if (!sessionCreated)
                {
                    LogAuthTrace("login", "session-create-failed", traceId, correlationId, dto.Username, null, null, authSw, AxaptaSessionManager.LogLevel.Warning);
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

                LogAuthTrace("login", "before-generate-token", traceId, correlationId, dto.Username, null, null, authSw);
                var tokenInfo = _jwt.GenerateToken(dto.Username, expirationMinutes);
                LogAuthTrace(
                    "login",
                    "after-generate-token",
                    traceId,
                    correlationId,
                    dto.Username,
                    null,
                    $"tokenExpiresUtc={tokenInfo.Expiration:o}",
                    authSw);

                LogAuthTrace("login", "before-refresh-session-token", traceId, correlationId, dto.Username, null, "oldTokenPresent=false", authSw);
                if (!_sessionManager.RefreshSessionToken(dto.Username, tokenInfo, null))
                {
                    LogAuthTrace("login", "refresh-session-token-failed", traceId, correlationId, dto.Username, null, null, authSw, AxaptaSessionManager.LogLevel.Error);
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
                LogAuthTrace("login", "after-refresh-session-token", traceId, correlationId, dto.Username, null, "refreshResult=true", authSw);

                _logger.Log($"[AUTH-SUCCESS] Token issued for {dto.Username}");
                LogAuthTrace("login", "completed", traceId, correlationId, dto.Username, null, null, authSw);

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
            catch (IND_AxCallTimeoutException ex)
            {
                LogAuthTrace("login", "timeout", traceId, correlationId, dto?.Username, null, ex.Detail, authSw, AxaptaSessionManager.LogLevel.Error, ex);
                _logger.Log($"[AUTH-TIMEOUT] {dto?.Username} -> {ex.Message}");
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
                LogAuthTrace("login", "exception", traceId, correlationId, dto?.Username, null, null, authSw, AxaptaSessionManager.LogLevel.Error, ex);
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
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Renovacion temporalmente no disponible", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        [Authorize]
        [HttpPost, Route("refresh")]
        [SwaggerOperation(Tags = new[] { "Autenticacion" })]
        public IHttpActionResult Refresh()
        {
            var correlationId = IndRequestDiagnosticsHelper.GetOrCreateCorrelationId(Request);
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
            var authSw = Stopwatch.StartNew();
            try
            {
                var username = User?.Identity?.Name;
                LogAuthTrace("refresh", "request-received", traceId, correlationId, username, null, null, authSw);
                if (string.IsNullOrWhiteSpace(username))
                {
                    LogAuthTrace("refresh", "auth-required", traceId, correlationId, username, null, null, authSw, AxaptaSessionManager.LogLevel.Warning);
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

                var expirationSetting = AppSettingsHelper.GetSetting(
                    "JwtSettings:ExpirationMinutes",
                    "INDCRM_JWT_EXPIRATION_MINUTES");
                int expirationMinutes = int.TryParse(expirationSetting, out var exp) ? exp : 60;

                var tokenInfo = _jwt.GenerateToken(username, expirationMinutes);
                LogAuthTrace(
                    "refresh",
                    "token-generated",
                    traceId,
                    correlationId,
                    username,
                    null,
                    $"oldTokenPresent={!string.IsNullOrWhiteSpace(oldToken)} tokenExpiresUtc={tokenInfo.Expiration:o}",
                    authSw);

                if (!_sessionManager.RefreshSessionToken(username, tokenInfo, oldToken))
                {
                    return Content(HttpStatusCode.ServiceUnavailable, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "No se pudo renovar la sesion temporalmente. Vuelva a intentarlo.",
                        ErrorCode = IndErrorCodes.InternalError,
                        TraceId = traceId
                    });
                }
                LogAuthTrace("refresh", "session-token-refreshed", traceId, correlationId, username, null, null, authSw);

                _logger.Log("[AUTH-REFRESH] Token refreshed for " + username);
                LogAuthTrace("refresh", "completed", traceId, correlationId, username, null, null, authSw);

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
                LogAuthTrace("refresh", "exception", traceId, correlationId, User?.Identity?.Name, null, null, authSw, AxaptaSessionManager.LogLevel.Error, ex);
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
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Contexto pendiente de revalidacion o capacidad temporalmente agotada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        [Authorize]
        [HttpPost, Route("entra/context")]
        [SwaggerOperation(Tags = new[] { "Autenticacion" })]
        [ResponseType(typeof(IndPagedResponse<EntraContextDto>))]
        public IHttpActionResult EntraContext([FromBody] EntraContextRequest body)
        {
            var correlationId = IndRequestDiagnosticsHelper.GetOrCreateCorrelationId(Request);
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
            var authSw = Stopwatch.StartNew();
            var errors = new List<IndValidationError>();
            var currentUsername = User?.Identity?.Name ?? string.Empty;

            LogAuthTrace(
                "entra-context",
                "request-received",
                traceId,
                correlationId,
                currentUsername,
                body?.appCode,
                $"bodyNull={body == null} entraOidProvided={!string.IsNullOrWhiteSpace(body?.entraOid)} entraOidLength={body?.entraOid?.Trim().Length ?? 0}",
                authSw);

            void LogOut(HttpStatusCode statusCode)
            {
                LogAuthTrace(
                    "entra-context",
                    "response",
                    traceId,
                    correlationId,
                    currentUsername,
                    body?.appCode,
                    "status=" + (int)statusCode,
                    authSw,
                    statusCode == HttpStatusCode.OK ? AxaptaSessionManager.LogLevel.Info : AxaptaSessionManager.LogLevel.Warning);
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
                LogAuthTrace(
                    "entra-context",
                    "validation-failed",
                    traceId,
                    correlationId,
                    currentUsername,
                    body?.appCode,
                    $"errorCount={errors.Count}",
                    authSw,
                    AxaptaSessionManager.LogLevel.Warning);

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
                currentUsername = username ?? string.Empty;
                if (string.IsNullOrWhiteSpace(username))
                {
                    LogAuthTrace("entra-context", "auth-required", traceId, correlationId, currentUsername, body?.appCode, null, authSw, AxaptaSessionManager.LogLevel.Warning);
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
                LogAuthTrace("entra-context", "before-get-ax-instance", traceId, correlationId, username, body.appCode, null, authSw);

                var ax = _sessionManager.GetAxInstanceForUser(username);
                LogAuthTrace(
                    "entra-context",
                    "after-get-ax-instance",
                    traceId,
                    correlationId,
                    username,
                    body.appCode,
                    "axType=" + (ax == null ? "null" : ax.GetType().FullName),
                    authSw);

                LogAuthTrace("entra-context", "before-create-container", traceId, correlationId, username, body.appCode, null, authSw);
                var con = ax.CreateContainer();
                LogAuthTrace("entra-context", "after-create-container", traceId, correlationId, username, body.appCode, "containerType=" + (con == null ? "null" : con.GetType().FullName), authSw);
                con.Append(body.entraOid?.Trim() ?? string.Empty);
                con.Append(body.appCode?.Trim() ?? string.Empty);
                LogAuthTrace("entra-context", "container-populated", traceId, correlationId, username, body.appCode, "appendCount=2", authSw);

                LogAuthTrace("entra-context", "before-login-entra-context-call", traceId, correlationId, username, body.appCode, null, authSw);
                // Reserve the observation version while the request owns serialized COM access.
                var contextVersion = UserCompanyAccessCache.CreateContextVersion();
                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMUtilityService",
                    "loginEntraContext",
                    con
                );
                LogAuthTrace(
                    "entra-context",
                    "after-login-entra-context-call",
                    traceId,
                    correlationId,
                    username,
                    body.appCode,
                    "resultType=" + (resultObj == null ? "null" : resultObj.GetType().FullName),
                    authSw);

                var root = resultObj as IAxaptaContainer;
                if (root == null)
                {
                    LogAuthTrace("entra-context", "invalid-root-container", traceId, correlationId, username, body.appCode, null, authSw, AxaptaSessionManager.LogLevel.Error);
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

                var contextRead = ReadEntraContextForRefresh(root, ResolveTenantId(), body.entraOid, body.appCode, contextVersion);
                if (contextRead.RequiresRevalidation)
                {
                    LogOut(HttpStatusCode.ServiceUnavailable);
                    return Content(HttpStatusCode.ServiceUnavailable, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "No se pudo confirmar el contexto de empresas. Vuelva a intentarlo.",
                        ErrorCode = IndErrorCodes.AuthContextStale,
                        TraceId = traceId
                    });
                }

                var header = contextRead.Header;
                if (header == null)
                {
                    LogAuthTrace("entra-context", "invalid-header", traceId, correlationId, username, body.appCode, null, authSw, AxaptaSessionManager.LogLevel.Error);
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
                LogAuthTrace(
                    "entra-context",
                    "header-mapped",
                    traceId,
                    correlationId,
                    username,
                    body.appCode,
                    $"headerSuccess={header.Success} appActive={header.AppActive} userActive={header.UserActive} defaultCompany={header.DefaultCompany}",
                    authSw,
                    header.Success ? AxaptaSessionManager.LogLevel.Info : AxaptaSessionManager.LogLevel.Warning);

                if (!header.Success)
                {
                    // Full AX authorization headers distinguish a denial from a short generic failure result.
                    if (contextRead.ExplicitDenial)
                        UserCompanyAccessCache.Revoke(ResolveTenantId(), body.entraOid, body.appCode, contextVersion);
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

                var companies = contextRead.Companies;
                var normalizedEntraOid = (body.entraOid ?? string.Empty).Trim();
                var tenantId = ResolveTenantId();
                var snapshot = UserCompanyAccessCache.SetSnapshot(
                    tenantId,
                    normalizedEntraOid,
                    header.AxUserId,
                    header.DefaultCompany,
                    body.appCode,
                    companies == null ? null : companies.ConvertAll(c => c.CompanyId),
                    contextVersion);
                var contextToken = snapshot.Exists && !snapshot.IsRevoked
                    ? UserContextTokenService.CreateToken(snapshot)
                    : string.Empty;

                var context = new EntraContextDto
                {
                    TenantId = snapshot.TenantId,
                    EntraOid = normalizedEntraOid,
                    ContextVersion = snapshot.ContextVersion,
                    PermissionsRevision = snapshot.PermissionsRevision,
                    ContextIssuedUtc = snapshot.IssuedUtc ?? DateTime.UtcNow,
                    ContextExpiresUtc = snapshot.ExpiresUtc ?? DateTime.UtcNow,
                    ContextToken = contextToken,
                    Header = header,
                    Companies = companies
                };
                LogAuthTrace(
                    "entra-context",
                    "companies-mapped",
                    traceId,
                    correlationId,
                    username,
                    body.appCode,
                    "companyCount=" + (context.Companies == null ? 0 : context.Companies.Count) + " contextVersion=" + context.ContextVersion + " permissionsRevision=" + (context.PermissionsRevision ?? string.Empty),
                    authSw);
                LogAuthTrace("entra-context", "company-cache-updated", traceId, correlationId, username, body.appCode, "snapshotKey=" + (snapshot.SnapshotKey ?? string.Empty), authSw);

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
            catch (UserCompanyAccessCache.CapacityException)
            {
                var capacityResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Contexto temporalmente no disponible. Vuelva a intentarlo.",
                    ErrorCode = IndErrorCodes.AuthContextRequired,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.ServiceUnavailable);
                return Content(HttpStatusCode.ServiceUnavailable, capacityResponse);
            }
            catch (UserCompanyAccessCache.RefreshConflictException)
            {
                var conflictResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "El contexto se ha actualizado simultaneamente. Vuelva a intentarlo.",
                    ErrorCode = IndErrorCodes.AuthContextStale,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.ServiceUnavailable);
                return Content(HttpStatusCode.ServiceUnavailable, conflictResponse);
            }
            catch (IND_AxCallTimeoutException ex)
            {
                LogAuthTrace("entra-context", "timeout", traceId, correlationId, currentUsername, body?.appCode, ex.Detail, authSw, AxaptaSessionManager.LogLevel.Error, ex);
                _logger.Log($"[AUTH-ENTRA-TIMEOUT] {ex.Message}");
                var timeoutResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = ex.UserMessage,
                    ErrorCode = IndErrorCodes.AxTimeout,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.ServiceUnavailable);
                return Content(HttpStatusCode.ServiceUnavailable, timeoutResponse);
            }
            catch (Exception ex)
            {
                LogAuthTrace("entra-context", "exception", traceId, correlationId, currentUsername, body?.appCode, null, authSw, AxaptaSessionManager.LogLevel.Error, ex);
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

        // Resolves the tenant id used to isolate user authorization snapshots.
        private static string ResolveTenantId()
        {
            var tenantId = AppSettingsHelper.GetMachineEnvironmentVariable("CRM_TENANT_ID");
            if (!string.IsNullOrWhiteSpace(tenantId))
                return tenantId.Trim();

            var issuer = AppSettingsHelper.GetSetting("JwtSettings:Issuer", "INDCRM_JWT_ISSUER");
            if (!string.IsNullOrWhiteSpace(issuer))
                return issuer.Trim();

            return "default-tenant";
        }

        // Emits structured auth traces so stuck COM calls leave a clear last stage in the log.
        private void LogAuthTrace(
            string flow,
            string stage,
            string traceId,
            string correlationId,
            string username,
            string appCode,
            string detail,
            Stopwatch stopwatch,
            AxaptaSessionManager.LogLevel level = AxaptaSessionManager.LogLevel.Info,
            Exception ex = null)
        {
            var parts = new List<string>
            {
                "[AUTH-TRACE]",
                "flow=" + (flow ?? string.Empty),
                "stage=" + (stage ?? string.Empty),
                "correlationId=" + (correlationId ?? "-"),
                "traceId=" + (traceId ?? "-"),
                "requestUser=" + (User?.Identity?.Name ?? string.Empty),
                "targetUser=" + (username ?? string.Empty),
                "appCode=" + (appCode ?? string.Empty),
                "method=" + (Request?.Method?.Method ?? "UNKNOWN"),
                "path=" + (Request?.RequestUri?.AbsolutePath ?? string.Empty),
                "threadId=" + Environment.CurrentManagedThreadId,
                "apartment=" + Thread.CurrentThread.GetApartmentState(),
                "processId=" + Process.GetCurrentProcess().Id,
                "elapsedMs=" + (stopwatch == null ? 0 : stopwatch.ElapsedMilliseconds)
            };

            if (!string.IsNullOrWhiteSpace(detail))
                parts.Add("detail=" + Truncate(detail, 3000));

            if (ex != null)
            {
                parts.Add("error=" + ex.GetType().Name);
                parts.Add("message=" + Truncate(ex.Message, 2000));
                if (!string.IsNullOrWhiteSpace(ex.StackTrace))
                    parts.Add("stack=" + Truncate(ex.StackTrace, 4000));
            }

            _logger.Log(string.Join(" ", parts), level);
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
                return value;

            return value.Substring(0, maxLength);
        }

        // A failed read never exposes a partially mapped context to authorization callers.
        internal sealed class EntraContextReadResult
        {
            internal EntraContextHeaderDto Header { get; set; }
            internal List<EntraCompanyDto> Companies { get; set; }
            internal bool ExplicitDenial { get; set; }
            internal bool RequiresRevalidation { get; set; }
        }

        // Separates unreadable COM data from authoritative empty results during context refresh.
        internal static EntraContextReadResult ReadEntraContextForRefresh(IAxaptaContainer root,
            string tenantId, string entraOid, string appCode, long contextVersion)
        {
            try
            {
                var header = MapEntraHeader(root);
                if (header == null) return new EntraContextReadResult();
                var explicitDenial = !header.Success && HasExplicitContextDenial(root);
                // A complete inactive header is authoritative without reading optional company data.
                var companies = explicitDenial ? new List<EntraCompanyDto>() : MapEntraCompanies(root);
                if (header.Success && (ContainerLength(root) < 2 || string.IsNullOrWhiteSpace(header.AxUserId)))
                    throw new EntraContextReadException("Incomplete successful AX context.");
                if (!explicitDenial && HasAmbiguousContextDenial(root))
                {
                    UserCompanyAccessCache.RequireRevalidation(tenantId, entraOid, appCode, contextVersion);
                    return new EntraContextReadResult { RequiresRevalidation = true };
                }
                return new EntraContextReadResult { Header = header, Companies = companies, ExplicitDenial = explicitDenial };
            }
            catch (EntraContextReadException)
            {
                UserCompanyAccessCache.RequireRevalidation(tenantId, entraOid, appCode, contextVersion);
                return new EntraContextReadResult { RequiresRevalidation = true };
            }
        }

        // Lets live permission checks return their existing transient failure instead of using partial rights.
        internal sealed class EntraContextReadException : InvalidOperationException
        {
            internal EntraContextReadException(string message, Exception inner = null) : base(message, inner) { }
        }

        // Shared with destructive operations that recheck current AX permissions without renewing tokens.
        // Only an explicit inactive flag in a complete AX header invalidates an existing authorization snapshot.
        internal static bool HasExplicitContextDenial(IAxaptaContainer root)
        {
            var container = PeekContainer(root, 1);
            if (container == null) return false;
            var row = PeekContainer(container, 1) ?? container;
            if (ContainerLength(row) < 6 || ToBool(ReadString(row, 1))) return false;
            return string.Equals(ReadString(row, 4), "0", StringComparison.Ordinal) ||
                   string.Equals(ReadString(row, 5), "0", StringComparison.Ordinal);
        }

        // AX uses this shape both for no allowed companies and for failed per-company lookups.
        internal static bool HasAmbiguousContextDenial(IAxaptaContainer root)
        {
            var container = PeekContainer(root, 1);
            if (container == null) return false;
            var row = PeekContainer(container, 1) ?? container;
            return ContainerLength(row) >= 6 && !ToBool(ReadString(row, 1)) &&
                string.Equals(ReadString(row, 4), "1", StringComparison.Ordinal) &&
                string.Equals(ReadString(row, 5), "1", StringComparison.Ordinal) && MapEntraCompanies(root).Count == 0;
        }

        internal static EntraContextHeaderDto MapEntraHeader(IAxaptaContainer root)
        {
            var headerContainer = PeekContainer(root, 1);
            if (headerContainer == null)
                return null;

            var headerRow = PeekContainer(headerContainer, 1);
            if (headerRow == null && ContainerLength(headerContainer) >= 2)
                headerRow = headerContainer;

            if (headerRow == null)
                return null;

            var header = new EntraContextHeaderDto
            {
                Success = ToBool(ReadString(headerRow, 1)),
                Message = ReadString(headerRow, 2),
                AxUserId = string.Empty,
                UserActive = false,
                AppActive = false,
                DefaultCompany = string.Empty,
                DefaultCurrencyCode = string.Empty,
                UserName = string.Empty
            };

            if (ContainerLength(headerRow) >= 6)
            {
                header.AxUserId = ReadString(headerRow, 3);
                header.UserActive = ToBool(ReadString(headerRow, 4));
                header.AppActive = ToBool(ReadString(headerRow, 5));
                header.DefaultCompany = ReadString(headerRow, 6);

                // Nuevo contrato AX: Header[7] = DefaultCurrencyCode.
                if (ContainerLength(headerRow) >= 7)
                    header.DefaultCurrencyCode = ReadString(headerRow, 7);

                // Nuevo contrato AX: Header[8] = UserName.
                if (ContainerLength(headerRow) >= 8)
                    header.UserName = ReadString(headerRow, 8);
            }

            if (string.IsNullOrWhiteSpace(header.Message))
                header.Message = header.Success ? "OK" : "Acceso denegado.";

            return header;
        }

        // Preserves every supported AX company row shape for authorization callers.
        internal static List<EntraCompanyDto> MapEntraCompanies(IAxaptaContainer root)
        {
            var companies = new List<EntraCompanyDto>();
            var companiesCon = PeekContainer(root, 2);
            if (companiesCon == null && ReadValue(root, 2) != null)
                throw new EntraContextReadException("Invalid AX company inventory.");
            var count = ContainerLength(companiesCon);

            for (int i = 1; i <= count; i++)
            {
                var companyCon = PeekContainer(companiesCon, i);
                if (companyCon == null || ContainerLength(companyCon) < 4)
                    throw new EntraContextReadException("Incomplete AX company row.");

                // AX contract vNext:
                // [companyId, isDefault, companyName, currencyCode, allowSelfManagement, crmUserId, modulesCon].
                var modulesCon = PeekContainer(companyCon, 7);
                var currencyCode = ReadString(companyCon, 4);
                var allowSelfManagement = ToBool(ReadString(companyCon, 5));
                var crmUserId = ReadString(companyCon, 6);

                // Backward compatibility:
                // [companyId, isDefault, companyName, currencyCode, allowSelfManagement, modulesCon].
                if (modulesCon == null)
                {
                    modulesCon = PeekContainer(companyCon, 6);
                    crmUserId = string.Empty;
                }

                // Backward compatibility:
                // [companyId, isDefault, companyName, currencyCode, modulesCon].
                if (modulesCon == null)
                {
                    modulesCon = PeekContainer(companyCon, 5);
                    allowSelfManagement = false;
                    crmUserId = string.Empty;
                }

                // Legacy compatibility:
                // [companyId, isDefault, companyName, modulesCon].
                if (modulesCon == null)
                {
                    modulesCon = PeekContainer(companyCon, 4);
                    currencyCode = string.Empty;
                    allowSelfManagement = false;
                    crmUserId = string.Empty;
                }

                if (modulesCon == null || string.IsNullOrWhiteSpace(ReadString(companyCon, 1)))
                    throw new EntraContextReadException("Incomplete AX company permissions.");

                companies.Add(new EntraCompanyDto
                {
                    CompanyId = ReadString(companyCon, 1),
                    IsDefault = ToBool(ReadString(companyCon, 2)),
                    CompanyName = ReadString(companyCon, 3),
                    CurrencyCode = currencyCode,
                    AllowSelfManagement = allowSelfManagement,
                    CrmUserId = crmUserId,
                    Modules = MapEntraModules(modulesCon)
                });
            }

            return companies;
        }

        private static List<EntraModuleDto> MapEntraModules(IAxaptaContainer modulesCon)
        {
            var modules = new List<EntraModuleDto>();
            var count = ContainerLength(modulesCon);

            for (int i = 1; i <= count; i++)
            {
                var moduleCon = PeekContainer(modulesCon, i);
                if (moduleCon == null || ContainerLength(moduleCon) < 4)
                    throw new EntraContextReadException("Incomplete AX module row.");

                modules.Add(new EntraModuleDto
                {
                    ModuleCode = ReadString(moduleCon, 1),
                    Description = ReadString(moduleCon, 2),
                    IsActive = ToBool(ReadString(moduleCon, 3)),
                    AccessRightsInt = ToInt(ReadString(moduleCon, 4))
                });
            }

            return modules;
        }

        private static IAxaptaContainer PeekContainer(IAxaptaContainer container, int index)
        {
            return ReadValue(container, index) as IAxaptaContainer;
        }

        // Optional legacy fields are absent by length; a failed COM read is never an absent field.
        private static object ReadValue(IAxaptaContainer container, int index)
        {
            if (container == null || index > ContainerLength(container)) return null;
            try { return container.Peek(index); }
            catch (Exception ex) { throw new EntraContextReadException("AX context field could not be read.", ex); }
        }

        // Propagates unreadable inventories instead of classifying them as empty authorization results.
        private static int ContainerLength(IAxaptaContainer container)
        {
            try
            {
                return container?.Length() ?? 0;
            }
            catch (Exception ex)
            {
                throw new EntraContextReadException("AX context length could not be read.", ex);
            }
        }

        // Keeps legacy scalar conversion while rejecting a conversion failure as incomplete data.
        private static string ReadString(IAxaptaContainer container, int index)
        {
            try
            {
                return ReadValue(container, index)?.ToString() ?? string.Empty;
            }
            catch (Exception ex)
            {
                throw new EntraContextReadException("AX context scalar could not be read.", ex);
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
