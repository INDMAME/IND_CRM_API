using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Linq;
using System.Web.Http;
using System.Web.Http.ExceptionHandling;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Global Web API exception logger (captures errors outside action filters).
    /// </summary>
    public sealed class IndGlobalExceptionLogger : ExceptionLogger
    {
        public override void Log(ExceptionLoggerContext context)
        {
            if (context == null)
                return;

            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var exceptionContext = context.ExceptionContext;
                var request = exceptionContext?.Request;
                var method = request?.Method?.Method ?? "UNKNOWN";
                var path = request?.RequestUri?.PathAndQuery ?? "unknown";
                var routeTemplate = request?.GetRouteData()?.Route?.RouteTemplate ?? "unresolved";
                var routeValues = FormatRouteValues(request?.GetRouteData());
                var catchBlock = context.CatchBlock?.Name ?? "unknown";
                var ex = exceptionContext?.Exception;
                var exText = ex == null ? "Excepcion no especificada" : ex.GetType().FullName + " " + ex.Message;

                var logger = ResolveLogger(context);
                var message =
                    "[ERROR-WEBAPI] " +
                    "catchBlock=" + catchBlock +
                    " method=" + method +
                    " path=" + path +
                    " routeTemplate=" + routeTemplate +
                    " routeValues=" + routeValues +
                    " exception=" + exText +
                    " traceId=" + traceId;

                if (logger != null)
                {
                    logger.Log(message, AxaptaSessionManager.LogLevel.Error);
                    return;
                }

                AxaptaSessionManager.LogStatic(message);
            }
            catch
            {
                // Never throw from logger.
            }
        }

        private static IAxLogger ResolveLogger(ExceptionLoggerContext context)
        {
            var requestResolver = context?.ExceptionContext?.RequestContext?.Configuration?.DependencyResolver;
            var globalResolver = GlobalConfiguration.Configuration?.DependencyResolver;
            var resolver = requestResolver ?? globalResolver;
            return resolver?.GetService(typeof(IAxLogger)) as IAxLogger;
        }

        private static string FormatRouteValues(System.Web.Http.Routing.IHttpRouteData routeData)
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
