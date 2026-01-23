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

        /// <summary>
        /// Validates company header and user access (422 if missing, 403 if forbidden).
        /// </summary>
        protected string RequireCompanyOrReturn422(out IHttpActionResult errorResult, string traceId)
        {
            errorResult = null;
            var company = GetHeaderValue("X-IND-Company");
            if (string.IsNullOrWhiteSpace(company))
            {
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
                    TraceId = traceId
                };
                errorResult = Content((HttpStatusCode)422, response);
                return null;
            }

            // Validate that the user has access to the selected company.
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
                errorResult = Content(HttpStatusCode.Unauthorized, authResponse);
                return null;
            }

            if (!UserCompanyAccessCache.IsCompanyAllowed(username, company, out var cacheMissing))
            {
                var message = cacheMissing
                    ? "Contexto de companias no inicializado. Consulte /api/auth/entra/context."
                    : "Compania no permitida para el usuario.";

                var forbiddenResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = message,
                    ErrorCode = IndErrorCodes.AuthForbidden,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                errorResult = Content(HttpStatusCode.Forbidden, forbiddenResponse);
                return null;
            }

            return company.Trim();
        }

        /// <summary>
        /// Valida header X-IND-AxUserId (422 si falta).
        /// </summary>
        protected string RequireAxUserIdOrReturn422(out IHttpActionResult errorResult, string traceId, string errorCode = null)
        {
            errorResult = null;
            var axUserId = GetHeaderValue("X-IND-AxUserId");
            if (string.IsNullOrWhiteSpace(axUserId))
            {
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
                    TraceId = traceId
                };
                errorResult = Content((HttpStatusCode)422, response);
                return null;
            }

            return axUserId.Trim();
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
    }
}
