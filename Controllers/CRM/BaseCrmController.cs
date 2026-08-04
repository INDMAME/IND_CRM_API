using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Web.Http;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Services;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Helpers;

namespace IND_CRM_API.Controllers
{
    public abstract class BaseCrmController : ApiController
    {
        private const string ValidatedSnapshotAxUserIdRequestPropertyKey = "IND.ValidatedSnapshotAxUserId";

        protected readonly IAxaptaSessionManager SessionManager;
        protected readonly IAxLogger Logger;

        protected BaseCrmController(IAxaptaSessionManager sessionManager, IAxLogger logger)
        {
            SessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        protected string GetAuthenticatedUsername()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("User not authenticated or invalid token.");
            return username;
        }

        protected string GetOrCreateTraceId()
        {
            return IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
        }

        /// <summary>
        /// Validates company header and user access (422 if missing, 403 if forbidden).
        /// </summary>
        protected string RequireCompanyOrReturn422(out IHttpActionResult errorResult, string traceId)
        {
            var effectiveTraceId = GetOrCreateTraceId();
            errorResult = null;
            StoreValidatedSnapshotAxUserId(null);
            var company = GetHeaderValue("X-IND-Company");
            var axUserId = GetHeaderValue("X-IND-AxUserId");
            var entraOid = GetHeaderValue("X-IND-EntraOid");
            var rawContextVersion = GetHeaderValue("X-IND-Context-Version");
            var permissionsRevision = GetHeaderValue("X-IND-Permissions-Revision");
            var contextToken = GetHeaderValue("X-IND-Context-Token");
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(company))
            {
                LogCompanyAuthorization("deny", "missing-company-header", null, axUserId, username, entraOid, 0, null, effectiveTraceId);
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "company es obligatorio.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = new List<IndValidationError>
                    {
                        new IndValidationError { Field = "company", Message = "Header X-IND-Company requerido." }
                    },
                    Data = null,
                    TraceId = effectiveTraceId
                };
                errorResult = Content((HttpStatusCode)422, response);
                return null;
            }

            // Validate that the user has access to the selected company.
            if (string.IsNullOrWhiteSpace(username))
            {
                LogCompanyAuthorization("deny", "missing-authenticated-user", company, axUserId, username, entraOid, 0, null, effectiveTraceId);
                var authResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Autenticacion requerida.",
                    ErrorCode = IndErrorCodes.AuthRequired,
                    Errors = null,
                    Data = null,
                    TraceId = effectiveTraceId
                };
                errorResult = Content(HttpStatusCode.Unauthorized, authResponse);
                return null;
            }

            if (!TryParseContextVersion(rawContextVersion, out var contextVersion) ||
                string.IsNullOrWhiteSpace(entraOid) ||
                string.IsNullOrWhiteSpace(permissionsRevision) ||
                string.IsNullOrWhiteSpace(contextToken))
            {
                var contextRequiredResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Contexto de companias no inicializado. Consulte /api/auth/entra/context.",
                    ErrorCode = IndErrorCodes.AuthContextRequired,
                    Errors = null,
                    Data = null,
                    TraceId = effectiveTraceId
                };
                LogCompanyAuthorization("deny", "missing-context-headers", company, axUserId, username, entraOid, contextVersion, null, effectiveTraceId);
                errorResult = Content(HttpStatusCode.Forbidden, contextRequiredResponse);
                return null;
            }

            var tenantId = ResolveTenantId();
            var latestSnapshot = UserCompanyAccessCache.GetSnapshot(tenantId, entraOid);
            var validation = UserContextTokenService.Validate(
                contextToken,
                tenantId,
                entraOid,
                contextVersion,
                permissionsRevision,
                company,
                latestSnapshot);

            if (!validation.IsValid)
            {
                var snapshot = validation.Snapshot ?? latestSnapshot;
                string message;
                string errorCode;

                if (validation.IsMissing)
                {
                    message = "Contexto de companias no inicializado. Consulte /api/auth/entra/context.";
                    errorCode = IndErrorCodes.AuthContextRequired;
                }
                else if (validation.IsExpired || validation.IsStale)
                {
                    message = "Contexto de companias expirado o desincronizado. Consulte /api/auth/entra/context.";
                    errorCode = IndErrorCodes.AuthContextStale;
                }
                else
                {
                    message = "Compania no permitida para el usuario.";
                    errorCode = IndErrorCodes.AuthForbidden;
                }

                LogCompanyAuthorization("deny", validation.Reason, company, axUserId, username, entraOid, contextVersion, snapshot, effectiveTraceId);

                var forbiddenResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = message,
                    ErrorCode = errorCode,
                    Errors = null,
                    Data = null,
                    TraceId = effectiveTraceId
                };
                errorResult = Content(HttpStatusCode.Forbidden, forbiddenResponse);
                return null;
            }

            StoreValidatedSnapshotAxUserId(validation.Snapshot?.AxUserId);
            LogCompanyAuthorization(
                "allow",
                validation.Reason,
                company,
                axUserId,
                username,
                entraOid,
                contextVersion,
                validation.Snapshot ?? latestSnapshot,
                effectiveTraceId);
            return company.Trim();
        }

        /// <summary>
        /// Returns the AX actor from the signed context snapshot validated for this request.
        /// </summary>
        // MMS - Exposes only the AX actor validated from the signed snapshot. - 2026.08.04
        protected string RequireValidatedSnapshotAxUserIdOrReturn403(out IHttpActionResult errorResult, string traceId)
        {
            var effectiveTraceId = GetOrCreateTraceId();
            errorResult = null;
            object rawAxUserId = null;
            if (Request?.Properties != null)
                Request.Properties.TryGetValue(ValidatedSnapshotAxUserIdRequestPropertyKey, out rawAxUserId);
            var axUserId = rawAxUserId as string;

            if (string.IsNullOrWhiteSpace(axUserId))
            {
                Logger.Log(
                    $"[AUTHZ-VIEWER-AXUSER] gate=BaseCrmController.RequireValidatedSnapshotAxUserIdOrReturn403 " +
                    $"result=deny reason=missing-signed-axuserid authenticatedUser={ToLogValue(User?.Identity?.Name)} traceId={effectiveTraceId}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Contexto de autorizacion sin usuario AX firmado. Consulte /api/auth/entra/context.",
                    ErrorCode = IndErrorCodes.AuthContextStale,
                    Errors = null,
                    Data = null,
                    TraceId = effectiveTraceId
                };
                errorResult = Content(HttpStatusCode.Forbidden, response);
                return null;
            }

            Logger.Log(
                $"[AUTHZ-VIEWER-AXUSER] gate=BaseCrmController.RequireValidatedSnapshotAxUserIdOrReturn403 " +
                $"result=allow viewerAxUserId={ToLogValue(axUserId)} authenticatedUser={ToLogValue(User?.Identity?.Name)} traceId={effectiveTraceId}");
            return axUserId.Trim();
        }

        /// <summary>
        /// Valida header X-IND-AxUserId (422 si falta).
        /// </summary>
        protected string RequireAxUserIdOrReturn422(out IHttpActionResult errorResult, string traceId, string errorCode = null)
        {
            var effectiveTraceId = GetOrCreateTraceId();
            errorResult = null;
            var axUserId = GetHeaderValue("X-IND-AxUserId");
            if (string.IsNullOrWhiteSpace(axUserId))
            {
                Logger.Log(
                    $"[AUTHZ-AXUSER] gate=BaseCrmController.RequireAxUserIdOrReturn422 result=deny reason=missing-axuserid-header " +
                    $"axUserId=- authenticatedUser={ToLogValue(User?.Identity?.Name)} traceId={effectiveTraceId}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "axUserId es obligatorio.",
                    ErrorCode = string.IsNullOrWhiteSpace(errorCode) ? IndErrorCodes.ValidationError : errorCode,
                    Errors = new List<IndValidationError>
                    {
                        new IndValidationError { Field = "axUserId", Message = "Header X-IND-AxUserId requerido." }
                    },
                    Data = null,
                    TraceId = effectiveTraceId
                };
                errorResult = Content((HttpStatusCode)422, response);
                return null;
            }

            Logger.Log(
                $"[AUTHZ-AXUSER] gate=BaseCrmController.RequireAxUserIdOrReturn422 result=allow reason=axuserid-present " +
                $"axUserId={axUserId.Trim()} authenticatedUser={ToLogValue(User?.Identity?.Name)} traceId={effectiveTraceId}");
            return axUserId.Trim();
        }

        /// <summary>
        /// Validates line RecIds while allowing temporary negative values returned by AX.
        /// </summary>
        protected static void AddLineRecIdValidation(List<IndValidationError> validationErrors, long lineRecId)
        {
            if (lineRecId == 0)
                validationErrors.Add(new IndValidationError { Field = "lineRecId", Message = "lineRecId debe ser distinto de cero." });
        }

        /// <summary>
        /// Validates optional line RecIds while allowing temporary negative values returned by AX.
        /// </summary>
        protected static void AddOptionalLineRecIdValidation(List<IndValidationError> validationErrors, long? lineRecId)
        {
            if (lineRecId.HasValue && lineRecId.Value == 0)
                validationErrors.Add(new IndValidationError { Field = "lineRecId", Message = "lineRecId debe ser distinto de cero." });
        }

        private string GetHeaderValue(string headerName)
        {
            try
            {
                IEnumerable<string> values;
                if (Request?.Headers != null && Request.Headers.TryGetValues(headerName, out values))
                    return values?.FirstOrDefault();
            }
            catch
            {
                // Ignorar errores de lectura de headers.
            }

            return null;
        }

        private void StoreValidatedSnapshotAxUserId(string axUserId)
        {
            if (Request?.Properties == null)
                return;

            Request.Properties.Remove(ValidatedSnapshotAxUserIdRequestPropertyKey);
            if (!string.IsNullOrWhiteSpace(axUserId))
                Request.Properties[ValidatedSnapshotAxUserIdRequestPropertyKey] = axUserId.Trim();
        }

        private static bool TryParseContextVersion(string rawContextVersion, out long contextVersion)
        {
            contextVersion = 0;
            var normalized = (rawContextVersion ?? string.Empty).Trim();
            return !string.IsNullOrWhiteSpace(normalized) && long.TryParse(normalized, out contextVersion) && contextVersion > 0;
        }

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

        private void LogCompanyAuthorization(
            string result,
            string reason,
            string company,
            string axUserId,
            string username,
            string entraOid,
            long contextVersion,
            UserCompanyAccessCache.Snapshot cacheSnapshot,
            string traceId)
        {
            LogCompanyAuthorization(
                result,
                reason,
                company,
                axUserId,
                username,
                entraOid,
                contextVersion,
                cacheSnapshot?.PermissionsRevision,
                cacheSnapshot,
                traceId);
        }

        private void LogCompanyAuthorization(
            string result,
            string reason,
            string company,
            string axUserId,
            string username,
            string entraOid,
            long contextVersion,
            string permissionsRevision,
            UserCompanyAccessCache.Snapshot cacheSnapshot,
            string traceId)
        {
            var cacheCompanies = cacheSnapshot?.Companies == null || cacheSnapshot.Companies.Length == 0
                ? "-"
                : string.Join("|", cacheSnapshot.Companies);
            var cacheExpiresUtc = cacheSnapshot?.ExpiresUtc.HasValue == true
                ? cacheSnapshot.ExpiresUtc.Value.ToString("o")
                : "-";
            var cacheIssuedUtc = cacheSnapshot?.IssuedUtc.HasValue == true
                ? cacheSnapshot.IssuedUtc.Value.ToString("o")
                : "-";

            Logger.Log(
                $"[AUTHZ-COMPANY] gate=BaseCrmController.RequireCompanyOrReturn422 result={result} reason={reason} " +
                $"company={ToLogValue(company)} axUserId={ToLogValue(axUserId)} authenticatedUser={ToLogValue(username)} " +
                $"entraOid={ToLogValue(entraOid)} contextVersion={contextVersion} permissionsRevision={ToLogValue(permissionsRevision)} snapshotKey={ToLogValue(cacheSnapshot?.SnapshotKey)} " +
                $"cacheExists={(cacheSnapshot?.Exists ?? false)} cacheExpired={(cacheSnapshot?.Expired ?? false)} " +
                $"cachePermissionsRevision={ToLogValue(cacheSnapshot?.PermissionsRevision)} cacheCompanies={cacheCompanies} cacheIssuedUtc={cacheIssuedUtc} cacheExpiresUtc={cacheExpiresUtc} traceId={traceId}");
        }

        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
