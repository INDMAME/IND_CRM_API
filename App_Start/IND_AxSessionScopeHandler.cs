using IND_CRM_API.Services;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

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
            var correlationId = GetHeader(request, "X-Correlation-Id") ?? Guid.NewGuid().ToString("N");
            var company = GetHeader(request, "X-IND-Company") ?? string.Empty;
            var endpoint = request == null
                ? "unknown"
                : string.Format("{0} {1}", request.Method, request.RequestUri?.AbsolutePath ?? string.Empty);
            var user = request?.GetRequestContext()?.Principal?.Identity?.Name ?? string.Empty;
            var contentType = request?.Content?.Headers?.ContentType?.MediaType ?? string.Empty;
            var contentLength = request?.Content?.Headers?.ContentLength?.ToString() ?? "0";

            _logger.Log(
                $"[API-PIPE-IN] correlationId={correlationId} endpoint={endpoint} company={company} user={user} contentType={contentType} contentLength={contentLength}");

            _sessionManager.BeginRequestScope(correlationId, endpoint, company);

            try
            {
                var response = await base.SendAsync(request, cancellationToken);
                var statusCode = response == null ? "null" : ((int)response.StatusCode).ToString();
                _logger.Log($"[API-PIPE-OUT] correlationId={correlationId} endpoint={endpoint} status={statusCode}");
                return response;
            }
            catch (Exception ex)
            {
                _logger.Log(
                    $"[API-PIPE-EX] correlationId={correlationId} endpoint={endpoint} ex={ex.GetType().Name} {ex.Message}",
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
            if (request == null || request.Headers == null)
                return null;

            try
            {
                if (request.Headers.TryGetValues(name, out var values))
                    return values == null ? null : string.Join(",", values);
            }
            catch
            {
                // ignore header errors
            }

            return null;
        }
    }
}
