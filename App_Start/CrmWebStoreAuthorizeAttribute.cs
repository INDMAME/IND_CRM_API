using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Web.Http;
using System.Web.Http.Controllers;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Authorization of the api/crm/webstore/* routes. It reads the bearer itself and accepts only a
    /// token issued by CrmWebStoreTokenService to the configured AX_WEBSTORE_USER; the principal set by
    /// the global JWT middleware (legacy CRM tokens) is ignored. Answers 401, 403 or 503 with the
    /// standard envelope.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class CrmWebStoreAuthorizeAttribute : AuthorizeAttribute
    {
        /// <summary>Authentication type of the principal created for the web store user.</summary>
        public const string AuthenticationType = "CrmWebStore";

        private readonly CrmWebStoreTokenService _tokens;

        /// <summary>Attribute constructor used by Web API (production settings).</summary>
        public CrmWebStoreAuthorizeAttribute()
            : this(new CrmWebStoreTokenService())
        {
        }

        /// <summary>Constructor with an explicit token service (tests).</summary>
        internal CrmWebStoreAuthorizeAttribute(CrmWebStoreTokenService tokens)
        {
            _tokens = tokens ?? throw new ArgumentNullException(nameof(tokens));
        }

        /// <summary>
        /// Validates the bearer and either sets the web store principal or writes the error response.
        /// </summary>
        public override void OnAuthorization(HttpActionContext actionContext)
        {
            if (actionContext?.Request == null)
                throw new ArgumentNullException(nameof(actionContext));

            var check = _tokens.Validate(ReadBearer(actionContext.Request));
            switch (check.Status)
            {
                case CrmWebStoreTokenStatus.Valid:
                    actionContext.RequestContext.Principal = BuildPrincipal(check.Username);
                    return;
                case CrmWebStoreTokenStatus.Disabled:
                    Deny(actionContext, HttpStatusCode.ServiceUnavailable, CrmWebStoreErrorCodes.Disabled,
                        "Las rutas del almacen web no estan habilitadas en este servidor.");
                    return;
                case CrmWebStoreTokenStatus.Forbidden:
                    Deny(actionContext, HttpStatusCode.Forbidden, CrmWebStoreErrorCodes.Forbidden,
                        "El token no corresponde al usuario autorizado para estas rutas.");
                    return;
                case CrmWebStoreTokenStatus.Expired:
                    Deny(actionContext, HttpStatusCode.Unauthorized, IndErrorCodes.AuthTokenExpired,
                        "El token ha caducado.");
                    return;
                default:
                    Deny(actionContext, HttpStatusCode.Unauthorized, IndErrorCodes.AuthRequired,
                        "Se requiere un token valido de api/crm/webstore/auth/login.");
                    return;
            }
        }

        // Reads the Authorization: Bearer value; anything else counts as missing.
        private static string ReadBearer(HttpRequestMessage request)
        {
            var header = request.Headers.Authorization;
            if (header == null || !string.Equals(header.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                return null;

            return header.Parameter;
        }

        // Principal with only the configured AX user name and the web store channel claim.
        private static ClaimsPrincipal BuildPrincipal(string username)
        {
            var identity = new ClaimsIdentity(AuthenticationType, ClaimTypes.Name, ClaimTypes.Role);
            identity.AddClaim(new Claim(ClaimTypes.Name, username));
            identity.AddClaim(new Claim(CrmWebStoreTokenService.ChannelClaimType, CrmWebStoreTokenService.ChannelValue));
            return new ClaimsPrincipal(identity);
        }

        // Writes the standard envelope; 401 also carries the Bearer challenge.
        private static void Deny(HttpActionContext actionContext, HttpStatusCode status, string errorCode, string message)
        {
            var request = actionContext.Request;
            var response = request.CreateResponse(status, new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Errors = null,
                Data = null,
                TraceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(request)
            });

            if (status == HttpStatusCode.Unauthorized)
                response.Headers.WwwAuthenticate.Add(new AuthenticationHeaderValue("Bearer"));

            actionContext.Response = response;
        }
    }
}
