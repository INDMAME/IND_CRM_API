using IND_CRM_API.Helpers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Logs authorization decisions before the controller action runs.
    /// </summary>
    public sealed class IndAuthorizationDiagnosticsFilter : FilterAttribute, IAuthorizationFilter
    {
        public override bool AllowMultiple => false;

        public async Task<HttpResponseMessage> ExecuteAuthorizationFilterAsync(
            HttpActionContext actionContext,
            CancellationToken cancellationToken,
            Func<Task<HttpResponseMessage>> continuation)
        {
            if (actionContext == null)
                return await continuation().ConfigureAwait(false);

            var request = actionContext.Request;
            var correlationId = IndRequestDiagnosticsHelper.GetOrCreateCorrelationId(request);
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(request);
            var company = IndRequestDiagnosticsHelper.GetHeaderValue(request, "X-IND-Company") ?? string.Empty;
            var axUserId = IndRequestDiagnosticsHelper.GetHeaderValue(request, "X-IND-AxUserId") ?? string.Empty;
            var principal = request?.GetRequestContext()?.Principal ?? Thread.CurrentPrincipal;
            var authenticatedUser = principal?.Identity?.Name ?? string.Empty;
            var isAuthenticated = principal?.Identity?.IsAuthenticated ?? false;
            var allowAnonymous = HasAllowAnonymous(actionContext);
            var filterNames = ResolveAuthorizationFilterNames(actionContext, allowAnonymous);
            var logger = ResolveLogger(actionContext);

            HttpResponseMessage response = null;
            try
            {
                response = await continuation().ConfigureAwait(false);
                return response;
            }
            finally
            {
                var actionEntered = request?.Properties != null &&
                    request.Properties.TryGetValue(IndRequestDiagnosticsHelper.ActionEnteredPropertyKey, out var rawActionEntered) &&
                    rawActionEntered is bool actionEnteredValue &&
                    actionEnteredValue;

                var statusCode = response?.StatusCode;
                var statusText = statusCode.HasValue ? ((int)statusCode.Value).ToString() : "null";
                var result = "allow";
                var reason = "controller-entered";

                if (allowAnonymous)
                {
                    result = "allow";
                    reason = "allow-anonymous";
                }
                else if (!actionEntered)
                {
                    if (!isAuthenticated)
                    {
                        result = "deny";
                        reason = "principal-missing-or-unauthenticated";
                    }
                    else if (statusCode == HttpStatusCode.Forbidden || statusCode == HttpStatusCode.Unauthorized)
                    {
                        result = "deny";
                        reason = "authorization-filter-blocked-before-controller";
                    }
                    else
                    {
                        result = "pass-through";
                        reason = "awaiting-controller-entry";
                    }
                }

                logger?.Log(
                    $"[AUTHZ-PRE] correlationId={correlationId} traceId={traceId} filters={filterNames} result={result} " +
                    $"reason={reason} status={statusText} method={request?.Method?.Method ?? "UNKNOWN"} " +
                    $"path={request?.RequestUri?.AbsolutePath ?? string.Empty} company={company} axUserId={axUserId} " +
                    $"authenticatedUser={authenticatedUser} companyCacheContext=not-evaluated-pre-controller");
            }
        }

        private static bool HasAllowAnonymous(HttpActionContext actionContext)
        {
            return actionContext.ActionDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Any() ||
                   actionContext.ControllerContext.ControllerDescriptor.GetCustomAttributes<AllowAnonymousAttribute>().Any();
        }

        private static string ResolveAuthorizationFilterNames(HttpActionContext actionContext, bool allowAnonymous)
        {
            if (allowAnonymous)
                return typeof(AllowAnonymousAttribute).FullName;

            var names = new List<string>();

            names.AddRange(
                actionContext.ControllerContext.ControllerDescriptor
                    .GetCustomAttributes<AuthorizeAttribute>()
                    .Select(attribute => attribute.GetType().FullName));

            names.AddRange(
                actionContext.ActionDescriptor
                    .GetCustomAttributes<AuthorizeAttribute>()
                    .Select(attribute => attribute.GetType().FullName));

            var distinctNames = names
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            return distinctNames.Count == 0 ? "none" : string.Join("|", distinctNames);
        }

        private static IAxLogger ResolveLogger(HttpActionContext actionContext)
        {
            try
            {
                var resolver = actionContext?.ControllerContext?.Configuration?.DependencyResolver
                               ?? GlobalConfiguration.Configuration?.DependencyResolver;
                return resolver?.GetService(typeof(IAxLogger)) as IAxLogger ?? new FileAxLogger();
            }
            catch
            {
                return new FileAxLogger();
            }
        }
    }
}
