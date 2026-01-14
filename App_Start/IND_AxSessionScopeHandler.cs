using IND_CRM_API.Services;
using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Creates and tears down the per-request Axapta session scope.
    /// </summary>
    public class IND_AxSessionScopeHandler : DelegatingHandler
    {
        private readonly AxaptaSessionManager _sessionManager;

        public IND_AxSessionScopeHandler(AxaptaSessionManager sessionManager)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var correlationId = GetHeader(request, "X-Correlation-Id") ?? Guid.NewGuid().ToString("N");
            var company = GetHeader(request, "X-IND-Company") ?? string.Empty;
            var endpoint = request == null
                ? "unknown"
                : string.Format("{0} {1}", request.Method, request.RequestUri?.AbsolutePath ?? string.Empty);

            _sessionManager.BeginRequestScope(correlationId, endpoint, company);

            try
            {
                return await base.SendAsync(request, cancellationToken);
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
