using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Http.Routing;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Logs route/controller/action resolution around action execution.
    /// </summary>
    public sealed class IndRouteDiagnosticsActionFilter : ActionFilterAttribute
    {
        private const string TraceIdPropertyKey = "IND_ROUTE_TRACE_ID";

        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext == null)
                return;

            var traceId = Guid.NewGuid().ToString("N");
            if (actionContext.Request != null)
                actionContext.Request.Properties[TraceIdPropertyKey] = traceId;

            TryLog(actionContext, "[API-ROUTE-IN]", traceId, null, null);
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (actionExecutedContext == null)
                return;

            var actionContext = actionExecutedContext.ActionContext;
            var traceId = ResolveTraceId(actionExecutedContext);

            if (actionExecutedContext.Exception != null)
            {
                TryLog(actionContext, "[API-ROUTE-EX]", traceId, null, actionExecutedContext.Exception);
                return;
            }

            var statusCode = actionExecutedContext.Response?.StatusCode;
            TryLog(actionContext, "[API-ROUTE-OUT]", traceId, statusCode, null);
        }

        private static string ResolveTraceId(HttpActionExecutedContext context)
        {
            if (context?.Request?.Properties != null &&
                context.Request.Properties.TryGetValue(TraceIdPropertyKey, out var rawTrace) &&
                rawTrace is string trace &&
                !string.IsNullOrWhiteSpace(trace))
            {
                return trace;
            }

            return Guid.NewGuid().ToString("N");
        }

        private static void TryLog(
            HttpActionContext actionContext,
            string tag,
            string traceId,
            System.Net.HttpStatusCode? statusCode,
            Exception exception)
        {
            try
            {
                var request = actionContext?.Request;
                var routeData = actionContext?.ControllerContext?.RouteData;
                var controllerName = actionContext?.ControllerContext?.ControllerDescriptor?.ControllerName ?? "unknown";
                var actionName = actionContext?.ActionDescriptor?.ActionName ?? "unknown";
                var method = request?.Method?.Method ?? "UNKNOWN";
                var path = request?.RequestUri?.PathAndQuery ?? "unknown";
                var routeTemplate = routeData?.Route?.RouteTemplate ?? "unknown";
                var routeValues = FormatRouteValues(routeData);

                var statusText = statusCode.HasValue ? ((int)statusCode.Value).ToString() : "n/a";
                var exceptionText = exception == null
                    ? string.Empty
                    : " ex=" + exception.GetType().Name + " " + exception.Message;

                var message =
                    $"{tag} method={method} path={path} controller={controllerName} action={actionName} " +
                    $"routeTemplate={routeTemplate} routeValues={routeValues} status={statusText} traceId={traceId}{exceptionText}";

                var resolver = actionContext?.ControllerContext?.Configuration?.DependencyResolver
                               ?? GlobalConfiguration.Configuration.DependencyResolver;
                var logger = resolver?.GetService(typeof(IAxLogger)) as IAxLogger;

                if (logger != null)
                {
                    logger.Log(message);
                    return;
                }

                AxaptaSessionManager.LogStatic(message);
            }
            catch
            {
                // Never break request execution due to diagnostics logging.
            }
        }

        private static string FormatRouteValues(IHttpRouteData routeData)
        {
            if (routeData?.Values == null || routeData.Values.Count == 0)
                return "-";

            return string.Join(
                ",",
                routeData.Values
                    .Where(kvp => kvp.Key != null)
                    .Select(kvp => kvp.Key + "=" + (kvp.Value == null ? "null" : kvp.Value.ToString()))
                    .ToArray());
        }
    }
}
