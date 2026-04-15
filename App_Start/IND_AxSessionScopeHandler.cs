using IND_CRM_API.Services;
using IND_CRM_API.Helpers;
using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Formatting;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Routing;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Creates and tears down the per-request Axapta session scope.
    /// </summary>
    public class IND_AxSessionScopeHandler : DelegatingHandler
    {
        private readonly AxaptaSessionManager _sessionManager;
        private readonly IAxLogger _logger;

        public IND_AxSessionScopeHandler(AxaptaSessionManager sessionManager, IAxLogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? new FileAxLogger();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var correlationId = IndRequestDiagnosticsHelper.GetOrCreateCorrelationId(request);
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(request);
            var company = GetHeader(request, "X-IND-Company") ?? string.Empty;
            var axUserId = GetHeader(request, "X-IND-AxUserId") ?? string.Empty;
            var endpoint = request == null
                ? "unknown"
                : string.Format("{0} {1}", request.Method, request.RequestUri?.AbsolutePath ?? string.Empty);
            var user = request?.GetRequestContext()?.Principal?.Identity?.Name ?? string.Empty;
            var contentType = request?.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
            var contentLength = request?.Content?.Headers?.ContentLength?.ToString() ?? "0";
            var boundary = IndRequestDiagnosticsHelper.GetMultipartBoundary(request?.Content) ?? string.Empty;

            IndRequestDiagnosticsHelper.SetRequestIds(request, correlationId, traceId);

            _logger.Log(
                $"[API-PIPE-IN] timestamp={DateTime.UtcNow:o} correlationId={correlationId} traceId={traceId} " +
                $"method={request?.Method?.Method ?? "UNKNOWN"} path={request?.RequestUri?.AbsolutePath ?? string.Empty} " +
                $"company={company} axUserId={axUserId} authenticatedUser={user} contentLength={contentLength} " +
                $"contentType={contentType} multipartBoundary={boundary}");

            var preRouteData = TryResolveRouteData(request);
            var preRouteTemplate = preRouteData?.Route?.RouteTemplate ?? "unresolved";
            var preRouteValues = FormatRouteValues(preRouteData);
            _logger.Log(
                $"[API-PIPE-MATCH] correlationId={correlationId} traceId={traceId} endpoint={endpoint} " +
                $"routeTemplate={preRouteTemplate} routeValues={preRouteValues}");

            _sessionManager.BeginRequestScope(correlationId, traceId, endpoint, company);

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                AssignTraceIdToEnvelope(response, traceId);
                IndRequestDiagnosticsHelper.ApplyResponseIds(response, correlationId, traceId);
                var statusCode = response == null ? "null" : ((int)response.StatusCode).ToString();
                var postRouteData = request?.GetRequestContext()?.RouteData ?? preRouteData;
                var routeTemplate = postRouteData?.Route?.RouteTemplate ?? "unresolved";
                var routeValues = FormatRouteValues(postRouteData);
                _logger.Log(
                    $"[API-PIPE-OUT] correlationId={correlationId} traceId={traceId} endpoint={endpoint} status={statusCode} " +
                    $"preRouteTemplate={preRouteTemplate} preRouteValues={preRouteValues} " +
                    $"routeTemplate={routeTemplate} routeValues={routeValues}");

                if (response != null && (int)response.StatusCode >= 500)
                {
                    var responseType = response.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
                    var responseLength = response.Content?.Headers?.ContentLength?.ToString() ?? "unknown";
                    var reasonPhrase = response.ReasonPhrase ?? string.Empty;
                    _logger.Log(
                        $"[API-PIPE-500] correlationId={correlationId} traceId={traceId} endpoint={endpoint} status={statusCode} " +
                        $"reason={reasonPhrase} responseType={responseType} responseLength={responseLength} " +
                        $"preRouteTemplate={preRouteTemplate} preRouteValues={preRouteValues} " +
                        $"routeTemplate={routeTemplate} routeValues={routeValues}",
                        AxaptaSessionManager.LogLevel.Error);
                }
                return response;
            }
            catch (Exception ex)
            {
                var postRouteData = request?.GetRequestContext()?.RouteData ?? preRouteData;
                var routeTemplate = postRouteData?.Route?.RouteTemplate ?? "unresolved";
                var routeValues = FormatRouteValues(postRouteData);
                _logger.Log(
                    $"[API-PIPE-EX] correlationId={correlationId} traceId={traceId} endpoint={endpoint} routeTemplate={routeTemplate} " +
                    $"routeValues={routeValues} ex={ex.GetType().Name} {ex.Message}",
                    AxaptaSessionManager.LogLevel.Error);
                throw;
            }
            finally
            {
                _sessionManager.EndRequestScope();
            }
        }

        private static string GetHeader(HttpRequestMessage request, string name)
        {
            return IndRequestDiagnosticsHelper.GetHeaderValue(request, name);
        }

        private static IHttpRouteData TryResolveRouteData(HttpRequestMessage request)
        {
            if (request == null)
                return null;

            try
            {
                var config = request.GetConfiguration();
                var routeData = config?.Routes?.GetRouteData(request);
                return routeData ?? request.GetRequestContext()?.RouteData;
            }
            catch
            {
                return request.GetRequestContext()?.RouteData;
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

        private static void AssignTraceIdToEnvelope(HttpResponseMessage response, string traceId)
        {
            if (response?.Content == null || string.IsNullOrWhiteSpace(traceId))
                return;

            try
            {
                if (!(response.Content is ObjectContent objectContent) || objectContent.Value == null)
                    return;

                var property = objectContent.Value.GetType().GetProperty("TraceId", BindingFlags.Public | BindingFlags.Instance);
                if (property == null || property.PropertyType != typeof(string) || !property.CanWrite)
                    return;

                property.SetValue(objectContent.Value, traceId.Trim());
            }
            catch
            {
                // Best effort only.
            }
        }
    }
}
