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
            var company = GetHeaderValue("X-IND-Company");
            var axUserId = GetHeaderValue("X-IND-AxUserId");
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(company))
            {
                LogCompanyAuthorization("deny", "missing-company-header", null, axUserId, username, null, effectiveTraceId);
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
                LogCompanyAuthorization("deny", "missing-authenticated-user", company, axUserId, username, null, effectiveTraceId);
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

            var accessEvaluation = UserCompanyAccessCache.EvaluateCompanyAccess(username, company);
            var cacheSnapshot = accessEvaluation.Snapshot ?? UserCompanyAccessCache.GetSnapshot(username);
            if (!accessEvaluation.Allowed)
            {
                var message = accessEvaluation.CacheMissing
                    ? "Contexto de companias no inicializado. Consulte /api/auth/entra/context."
                    : accessEvaluation.CacheExpired
                        ? "Contexto de companias expirado. Consulte /api/auth/entra/context."
                        : "Compania no permitida para el usuario.";

                var reason = accessEvaluation.CacheMissing
                    ? "company-cache-missing"
                    : accessEvaluation.CacheExpired
                        ? "company-cache-expired"
                        : "company-not-allowed";

                LogCompanyAuthorization("deny", reason, company, axUserId, username, cacheSnapshot, effectiveTraceId);

                var forbiddenResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = message,
                    ErrorCode = IndErrorCodes.AuthForbidden,
                    Errors = null,
                    Data = null,
                    TraceId = effectiveTraceId
                };
                errorResult = Content(HttpStatusCode.Forbidden, forbiddenResponse);
                return null;
            }

            LogCompanyAuthorization(
                "allow",
                accessEvaluation.UsedGraceWindow ? "company-allowed-grace-window" : "company-allowed",
                company,
                axUserId,
                username,
                accessEvaluation.Snapshot ?? cacheSnapshot,
                effectiveTraceId);
            return company.Trim();
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

        private void LogCompanyAuthorization(
            string result,
            string reason,
            string company,
            string axUserId,
            string username,
            UserCompanyAccessCache.Snapshot cacheSnapshot,
            string traceId)
        {
            var cacheCompanies = cacheSnapshot?.Companies == null || cacheSnapshot.Companies.Length == 0
                ? "-"
                : string.Join("|", cacheSnapshot.Companies);
            var cacheExpiresUtc = cacheSnapshot?.ExpiresUtc.HasValue == true
                ? cacheSnapshot.ExpiresUtc.Value.ToString("o")
                : "-";
            var cacheGraceUntilUtc = cacheSnapshot?.GraceUntilUtc.HasValue == true
                ? cacheSnapshot.GraceUntilUtc.Value.ToString("o")
                : "-";

            Logger.Log(
                $"[AUTHZ-COMPANY] gate=BaseCrmController.RequireCompanyOrReturn422 result={result} reason={reason} " +
                $"company={ToLogValue(company)} axUserId={ToLogValue(axUserId)} authenticatedUser={ToLogValue(username)} " +
                $"cacheExists={(cacheSnapshot?.Exists ?? false)} cacheExpired={(cacheSnapshot?.Expired ?? false)} " +
                $"cacheCompanies={cacheCompanies} cacheExpiresUtc={cacheExpiresUtc} cacheGraceUntilUtc={cacheGraceUntilUtc} traceId={traceId}");
        }

        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
