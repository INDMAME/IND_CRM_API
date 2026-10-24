using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Controllers;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Restricts an endpoint to the CRM_INSERTEC sync worker: a bearer issued by the login
    /// to the configured user (INDCRM_SYNC_USER) that carries the ind:role=sync-worker claim.
    /// Other authenticated users receive 403 with the standard envelope.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = false)]
    public sealed class SyncWorkerAuthorizeAttribute : AuthorizeAttribute
    {
        protected override bool IsAuthorized(HttpActionContext actionContext)
        {
            if (!base.IsAuthorized(actionContext))
                return false;

            var principal = actionContext?.RequestContext?.Principal;
            return SyncWorkerIdentity.IsSyncWorkerPrincipal(principal);
        }

        protected override void HandleUnauthorizedRequest(HttpActionContext actionContext)
        {
            if (actionContext?.Request == null)
                return;

            var principal = actionContext.RequestContext?.Principal;
            var authenticated = principal?.Identity?.IsAuthenticated ?? false;
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(actionContext.Request);

            var response = new IndApiResponse<object>
            {
                Success = false,
                Message = authenticated
                    ? "Endpoint reservado al worker de sincronizacion."
                    : "Autenticacion requerida.",
                ErrorCode = authenticated ? IndErrorCodes.SyncWorkerForbidden : IndErrorCodes.AuthRequired,
                Errors = null,
                Data = null,
                TraceId = traceId
            };

            actionContext.Response = actionContext.Request.CreateResponse(
                authenticated ? HttpStatusCode.Forbidden : HttpStatusCode.Unauthorized,
                response);
        }
    }
}
