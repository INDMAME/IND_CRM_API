using System;
using System.Linq;
using System.Net.Http;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Shared helpers for correlation and trace ids across the request pipeline.
    /// </summary>
    public static class IndRequestDiagnosticsHelper
    {
        public const string CorrelationIdHeaderName = "X-Correlation-Id";
        public const string TraceIdHeaderName = "X-Trace-Id";
        public const string CorrelationIdPropertyKey = "IND_DIAG_CORRELATION_ID";
        public const string TraceIdPropertyKey = "IND_DIAG_TRACE_ID";
        public const string ActionEnteredPropertyKey = "IND_DIAG_ACTION_ENTERED";
        public const string ActionStopwatchPropertyKey = "IND_DIAG_ACTION_STOPWATCH";

        public static string GetOrCreateCorrelationId(HttpRequestMessage request)
        {
            return GetOrCreateId(request, CorrelationIdPropertyKey, CorrelationIdHeaderName);
        }

        public static string GetOrCreateTraceId(HttpRequestMessage request)
        {
            return GetOrCreateId(request, TraceIdPropertyKey, TraceIdHeaderName);
        }

        public static void SetRequestIds(HttpRequestMessage request, string correlationId, string traceId)
        {
            if (request?.Properties == null)
                return;

            if (!string.IsNullOrWhiteSpace(correlationId))
                request.Properties[CorrelationIdPropertyKey] = correlationId.Trim();

            if (!string.IsNullOrWhiteSpace(traceId))
                request.Properties[TraceIdPropertyKey] = traceId.Trim();
        }

        public static void ApplyResponseIds(HttpResponseMessage response, string correlationId, string traceId)
        {
            if (response?.Headers == null)
                return;

            ReplaceHeader(response, CorrelationIdHeaderName, correlationId);
            ReplaceHeader(response, TraceIdHeaderName, traceId);
        }

        public static string GetHeaderValue(HttpRequestMessage request, string headerName)
        {
            if (request?.Headers == null || string.IsNullOrWhiteSpace(headerName))
                return null;

            try
            {
                if (request.Headers.TryGetValues(headerName, out var values))
                {
                    var value = values?.FirstOrDefault();
                    return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                }
            }
            catch
            {
                // Diagnostics must never break the request flow.
            }

            return null;
        }

        public static string GetMultipartBoundary(HttpContent content)
        {
            try
            {
                var boundary = content?.Headers?.ContentType?.Parameters?
                    .FirstOrDefault(parameter => string.Equals(parameter.Name, "boundary", StringComparison.OrdinalIgnoreCase))
                    ?.Value;

                return string.IsNullOrWhiteSpace(boundary) ? null : boundary.Trim().Trim('"');
            }
            catch
            {
                return null;
            }
        }

        private static string GetOrCreateId(HttpRequestMessage request, string propertyKey, string headerName)
        {
            if (request == null)
                return Guid.NewGuid().ToString("N");

            if (request.Properties != null &&
                request.Properties.TryGetValue(propertyKey, out var rawPropertyValue) &&
                rawPropertyValue is string propertyValue &&
                !string.IsNullOrWhiteSpace(propertyValue))
            {
                return propertyValue.Trim();
            }

            var headerValue = GetHeaderValue(request, headerName);
            var resolvedValue = string.IsNullOrWhiteSpace(headerValue)
                ? Guid.NewGuid().ToString("N")
                : headerValue.Trim();

            if (request.Properties != null)
                request.Properties[propertyKey] = resolvedValue;

            return resolvedValue;
        }

        private static void ReplaceHeader(HttpResponseMessage response, string headerName, string value)
        {
            if (string.IsNullOrWhiteSpace(headerName) || string.IsNullOrWhiteSpace(value))
                return;

            try
            {
                if (response.Headers.Contains(headerName))
                    response.Headers.Remove(headerName);

                response.Headers.TryAddWithoutValidation(headerName, value.Trim());
            }
            catch
            {
                // Keep response stable even if a header cannot be added.
            }
        }
    }
}
