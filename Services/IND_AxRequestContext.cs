using System;
using System.Threading;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Request-scope context for Axapta sessions.
    /// Stores correlation data and the per-request Axapta COM session.
    /// </summary>
    public sealed class IND_AxRequestContext
    {
        private static readonly AsyncLocal<IND_AxRequestContext> _current = new AsyncLocal<IND_AxRequestContext>();

        public static IND_AxRequestContext Current => _current.Value;

        public static void Start(string correlationId, string traceId, string endpoint, string company)
        {
            _current.Value = new IND_AxRequestContext(correlationId, traceId, endpoint, company);
        }

        public static void Clear()
        {
            _current.Value = null;
        }

        private IND_AxRequestContext(string correlationId, string traceId, string endpoint, string company)
        {
            CorrelationId = string.IsNullOrWhiteSpace(correlationId) ? Guid.NewGuid().ToString("N") : correlationId.Trim();
            TraceId = string.IsNullOrWhiteSpace(traceId) ? Guid.NewGuid().ToString("N") : traceId.Trim();
            Endpoint = endpoint ?? string.Empty;
            Company = company ?? string.Empty;
            StartedUtc = DateTime.UtcNow;
        }

        public string CorrelationId { get; }
        public string TraceId { get; }
        public string Endpoint { get; }
        public string Company { get; }
        public string Username { get; set; }
        public AxaptaComSession ComSession { get; set; }
        public IDisposable ComAccessLease { get; set; }
        public DateTime StartedUtc { get; }
    }
}
