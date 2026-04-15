using IND_CRM_API.Helpers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Reflection;
using System.Web.Http;
using System.Web.Http.Controllers;
using System.Web.Http.Filters;
using System.Web.Http.Routing;
using DiagnosticsStopwatch = global::System.Diagnostics.Stopwatch;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Logs action execution and response envelopes with shared trace ids.
    /// </summary>
    public sealed class IndRouteDiagnosticsActionFilter : ActionFilterAttribute
    {
        public override void OnActionExecuting(HttpActionContext actionContext)
        {
            if (actionContext == null)
                return;

            var request = actionContext.Request;
            var correlationId = IndRequestDiagnosticsHelper.GetOrCreateCorrelationId(request);
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(request);
            request.Properties[IndRequestDiagnosticsHelper.ActionEnteredPropertyKey] = true;
            request.Properties[IndRequestDiagnosticsHelper.ActionStopwatchPropertyKey] = DiagnosticsStopwatch.StartNew();

            TryLogAction(
                actionContext,
                "[ACTION-IN]",
                correlationId,
                traceId,
                "n/a",
                null,
                null,
                null,
                null);
        }

        public override void OnActionExecuted(HttpActionExecutedContext actionExecutedContext)
        {
            if (actionExecutedContext == null)
                return;

            var actionContext = actionExecutedContext.ActionContext;
            var request = actionContext?.Request;
            var correlationId = IndRequestDiagnosticsHelper.GetOrCreateCorrelationId(request);
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(request);
            var durationMs = StopAndReadDuration(request);

            if (actionExecutedContext.Exception != null)
            {
                TryLogAction(
                    actionContext,
                    "[ACTION-EX]",
                    correlationId,
                    traceId,
                    ((int)HttpStatusCode.InternalServerError).ToString(),
                    durationMs,
                    null,
                    null,
                    actionExecutedContext.Exception.Message);
                return;
            }

            var response = actionExecutedContext.Response;
            IndRequestDiagnosticsHelper.ApplyResponseIds(response, correlationId, traceId);

            var envelope = GetResponseEnvelope(response);
            TryAssignTraceId(envelope, traceId);
            var envelopeSummary = SummarizeEnvelope(envelope, traceId);
            var statusText = response == null ? "null" : ((int)response.StatusCode).ToString();

            TryLogAction(
                actionContext,
                "[ACTION-OUT]",
                correlationId,
                traceId,
                statusText,
                durationMs,
                envelopeSummary.Message,
                envelopeSummary.ErrorCode,
                null);

            TryLogEnvelope(actionContext, correlationId, traceId, envelopeSummary);
        }

        private static long? StopAndReadDuration(System.Net.Http.HttpRequestMessage request)
        {
            if (request?.Properties == null ||
                !request.Properties.TryGetValue(IndRequestDiagnosticsHelper.ActionStopwatchPropertyKey, out var rawStopwatch) ||
                !(rawStopwatch is DiagnosticsStopwatch stopwatch))
            {
                return null;
            }

            if (stopwatch.IsRunning)
                stopwatch.Stop();

            return stopwatch.ElapsedMilliseconds;
        }

        private static void TryLogAction(
            HttpActionContext actionContext,
            string tag,
            string correlationId,
            string traceId,
            string statusCode,
            long? durationMs,
            string message,
            string errorCode,
            string exceptionMessage)
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

                var parts = new[]
                {
                    tag,
                    "correlationId=" + (correlationId ?? string.Empty),
                    "traceId=" + (traceId ?? string.Empty),
                    "method=" + method,
                    "path=" + path,
                    "controller=" + controllerName,
                    "action=" + actionName,
                    "routeTemplate=" + routeTemplate,
                    "routeValues=" + routeValues,
                    "status=" + (statusCode ?? "n/a"),
                    "durationMs=" + (durationMs.HasValue ? durationMs.Value.ToString() : "na"),
                    "message=" + ToLogValue(message),
                    "errorCode=" + ToLogValue(errorCode),
                    "exception=" + ToLogValue(exceptionMessage)
                };

                ResolveLogger(actionContext)?.Log(string.Join(" ", parts.Where(part => !string.IsNullOrWhiteSpace(part))));
            }
            catch
            {
                // Never break request execution due to diagnostics logging.
            }
        }

        private static void TryLogEnvelope(
            HttpActionContext actionContext,
            string correlationId,
            string traceId,
            EnvelopeSummary summary)
        {
            try
            {
                ResolveLogger(actionContext)?.Log(
                    $"[API-ENVELOPE] correlationId={correlationId} traceId={traceId} success={ToLogValue(summary.Success)} " +
                    $"message={ToLogValue(summary.Message)} errorCode={ToLogValue(summary.ErrorCode)} total={ToLogValue(summary.Total)} " +
                    $"page={ToLogValue(summary.Page)} pageSize={ToLogValue(summary.PageSize)} itemsCount={ToLogValue(summary.ItemsCount)} " +
                    $"dataKind={summary.DataKind}");
            }
            catch
            {
                // Diagnostics must not break the request flow.
            }
        }

        private static object GetResponseEnvelope(System.Net.Http.HttpResponseMessage response)
        {
            return response?.Content is ObjectContent objectContent ? objectContent.Value : null;
        }

        private static void TryAssignTraceId(object envelope, string traceId)
        {
            if (envelope == null || string.IsNullOrWhiteSpace(traceId))
                return;

            try
            {
                var property = envelope.GetType().GetProperty("TraceId", BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.PropertyType != typeof(string) || !property.CanWrite)
                    return;

                property.SetValue(envelope, traceId.Trim());
            }
            catch
            {
                // Best effort only.
            }
        }

        private static EnvelopeSummary SummarizeEnvelope(object envelope, string fallbackTraceId)
        {
            if (envelope == null)
            {
                return new EnvelopeSummary
                {
                    Success = null,
                    Message = null,
                    ErrorCode = null,
                    TraceId = fallbackTraceId,
                    Total = null,
                    Page = null,
                    PageSize = null,
                    ItemsCount = null,
                    DataKind = "none"
                };
            }

            var items = GetPropertyValue(envelope, "Items");
            var data = GetPropertyValue(envelope, "Data");

            return new EnvelopeSummary
            {
                Success = GetPropertyValue(envelope, "Success")?.ToString(),
                Message = GetPropertyValue(envelope, "Message")?.ToString(),
                ErrorCode = GetPropertyValue(envelope, "ErrorCode")?.ToString(),
                TraceId = GetPropertyValue(envelope, "TraceId")?.ToString() ?? fallbackTraceId,
                Total = ConvertValueToString(GetPropertyValue(envelope, "Total")),
                Page = ConvertValueToString(GetPropertyValue(envelope, "Page")),
                PageSize = ConvertValueToString(GetPropertyValue(envelope, "PageSize")),
                ItemsCount = CountEnumerable(items) ?? CountEnumerable(data),
                DataKind = DescribeDataKind(items ?? data)
            };
        }

        private static object GetPropertyValue(object instance, string propertyName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            try
            {
                var property = instance.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                return property == null ? null : property.GetValue(instance);
            }
            catch
            {
                return null;
            }
        }

        private static string ConvertValueToString(object value)
        {
            return value == null ? null : value.ToString();
        }

        private static string DescribeDataKind(object value)
        {
            if (value == null)
                return "null";

            if (value is string)
                return "string";

            if (value is IEnumerable)
                return "array";

            var type = value.GetType();
            return type.IsPrimitive || type.IsValueType ? "value" : "object";
        }

        private static string CountEnumerable(object value)
        {
            if (value == null || value is string || !(value is IEnumerable enumerable))
                return null;

            try
            {
                var count = 0;
                foreach (var _ in enumerable)
                    count++;

                return count.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static IAxLogger ResolveLogger(HttpActionContext actionContext)
        {
            try
            {
                var resolver = actionContext?.ControllerContext?.Configuration?.DependencyResolver
                               ?? GlobalConfiguration.Configuration.DependencyResolver;
                return resolver?.GetService(typeof(IAxLogger)) as IAxLogger ?? new FileAxLogger();
            }
            catch
            {
                return new FileAxLogger();
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

        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }

        private sealed class EnvelopeSummary
        {
            public string Success { get; set; }
            public string Message { get; set; }
            public string ErrorCode { get; set; }
            public string TraceId { get; set; }
            public string Total { get; set; }
            public string Page { get; set; }
            public string PageSize { get; set; }
            public string ItemsCount { get; set; }
            public string DataKind { get; set; }
        }
    }
}
