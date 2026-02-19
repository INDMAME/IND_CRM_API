using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Applies request rate and concurrency limits for OpenAI endpoints.
    /// </summary>
    public sealed class IND_OpenAiRateLimitHandler : DelegatingHandler
    {
        private const string SpeechPath = "/api/ia/service/speech";
        private const string ExpenseFromTicketPath = "/api/ia/service/expensefromticket";

        private const string SpeechMaxRequestsSettingKey = "OpenAI:RateLimitSpeechMaxRequests";
        private const string SpeechWindowSecondsSettingKey = "OpenAI:RateLimitSpeechWindowSeconds";
        private const string ExpenseMaxRequestsSettingKey = "OpenAI:RateLimitExpenseTicketMaxRequests";
        private const string ExpenseWindowSecondsSettingKey = "OpenAI:RateLimitExpenseTicketWindowSeconds";
        private const string MaxConcurrentPerUserSettingKey = "OpenAI:RateLimitMaxConcurrentPerUser";

        private const int DefaultSpeechMaxRequests = 5;
        private const int DefaultSpeechWindowSeconds = 300;
        private const int DefaultExpenseMaxRequests = 10;
        private const int DefaultExpenseWindowSeconds = 600;
        private const int DefaultMaxConcurrentPerUser = 1;

        private static readonly EndpointLimit SpeechLimit = new EndpointLimit(
            "speech",
            NormalizePath(SpeechPath),
            ReadPositiveIntFromConfig(SpeechMaxRequestsSettingKey, DefaultSpeechMaxRequests),
            TimeSpan.FromSeconds(ReadPositiveIntFromConfig(SpeechWindowSecondsSettingKey, DefaultSpeechWindowSeconds)));

        private static readonly EndpointLimit ExpenseFromTicketLimit = new EndpointLimit(
            "expensefromticket",
            NormalizePath(ExpenseFromTicketPath),
            ReadPositiveIntFromConfig(ExpenseMaxRequestsSettingKey, DefaultExpenseMaxRequests),
            TimeSpan.FromSeconds(ReadPositiveIntFromConfig(ExpenseWindowSecondsSettingKey, DefaultExpenseWindowSeconds)));

        private readonly ConcurrentDictionary<string, RateWindowState> _rateWindows =
            new ConcurrentDictionary<string, RateWindowState>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, int> _activeRequests =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly IAxLogger _logger;
        private readonly int _maxConcurrentPerUser;

        public IND_OpenAiRateLimitHandler(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _maxConcurrentPerUser = ReadPositiveIntFromConfig(MaxConcurrentPerUserSettingKey, DefaultMaxConcurrentPerUser);
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!TryResolveEndpoint(request, out var endpoint))
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var userKey = ResolveUserKey(request);
            if (string.IsNullOrWhiteSpace(userKey))
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!TryAcquireConcurrency(userKey, out var activeCount))
            {
                var traceId = Guid.NewGuid().ToString("N");
                _logger.Log(
                    $"[AI-LIMIT] Concurrent limit exceeded user={userKey} endpoint={endpoint.Name} active={activeCount} max={_maxConcurrentPerUser} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Warning);

                return BuildTooManyRequestsResponse(
                    request,
                    traceId,
                    "Solo se permite una ejecucion IA simultanea por usuario.",
                    IndErrorCodes.AiConcurrencyLimitExceeded,
                    1);
            }

            try
            {
                if (!TryConsumeRequest(userKey, endpoint, out var retryAfterSeconds))
                {
                    var traceId = Guid.NewGuid().ToString("N");
                    _logger.Log(
                        $"[AI-LIMIT] Rate limit exceeded user={userKey} endpoint={endpoint.Name} maxRequests={endpoint.MaxRequests} windowSeconds={(int)endpoint.Window.TotalSeconds} retryAfterSeconds={retryAfterSeconds} traceId={traceId}",
                        AxaptaSessionManager.LogLevel.Warning);

                    return BuildTooManyRequestsResponse(
                        request,
                        traceId,
                        $"Se excedio el limite de {endpoint.MaxRequests} solicitudes en {(int)endpoint.Window.TotalMinutes} minutos.",
                        IndErrorCodes.AiRateLimitExceeded,
                        retryAfterSeconds);
                }

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ReleaseConcurrency(userKey);
            }
        }

        private bool TryConsumeRequest(string userKey, EndpointLimit endpoint, out int retryAfterSeconds)
        {
            var nowUtc = DateTime.UtcNow;
            var key = userKey + "|" + endpoint.Name;
            var state = _rateWindows.GetOrAdd(key, _ => new RateWindowState(nowUtc));

            lock (state.SyncRoot)
            {
                var elapsed = nowUtc - state.WindowStartUtc;
                if (elapsed >= endpoint.Window)
                {
                    state.WindowStartUtc = nowUtc;
                    state.Count = 0;
                }

                if (state.Count >= endpoint.MaxRequests)
                {
                    var resetAt = state.WindowStartUtc.Add(endpoint.Window);
                    retryAfterSeconds = (int)Math.Ceiling((resetAt - nowUtc).TotalSeconds);
                    if (retryAfterSeconds <= 0)
                        retryAfterSeconds = 1;

                    return false;
                }

                state.Count++;
                retryAfterSeconds = 0;
                return true;
            }
        }

        private bool TryAcquireConcurrency(string userKey, out int activeCount)
        {
            activeCount = _activeRequests.AddOrUpdate(userKey, 1, (_, current) => current + 1);
            if (activeCount <= _maxConcurrentPerUser)
                return true;

            _activeRequests.AddOrUpdate(userKey, 0, (_, current) => current > 0 ? current - 1 : 0);
            CleanupConcurrencyEntry(userKey);
            return false;
        }

        private void ReleaseConcurrency(string userKey)
        {
            _activeRequests.AddOrUpdate(userKey, 0, (_, current) => current > 0 ? current - 1 : 0);
            CleanupConcurrencyEntry(userKey);
        }

        private void CleanupConcurrencyEntry(string userKey)
        {
            if (_activeRequests.TryGetValue(userKey, out var active) && active <= 0)
                _activeRequests.TryRemove(userKey, out _);
        }

        private static bool TryResolveEndpoint(HttpRequestMessage request, out EndpointLimit endpoint)
        {
            endpoint = null;
            if (request == null || request.Method != HttpMethod.Post)
                return false;

            var path = NormalizePath(request.RequestUri?.AbsolutePath);
            if (string.Equals(path, SpeechLimit.Path, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = SpeechLimit;
                return true;
            }

            if (string.Equals(path, ExpenseFromTicketLimit.Path, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = ExpenseFromTicketLimit;
                return true;
            }

            return false;
        }

        private static string ResolveUserKey(HttpRequestMessage request)
        {
            var principal = request?.GetRequestContext()?.Principal ?? Thread.CurrentPrincipal;
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            var name = principal.Identity.Name;
            if (string.IsNullOrWhiteSpace(name))
                return null;

            return name.Trim().ToLowerInvariant();
        }

        private static HttpResponseMessage BuildTooManyRequestsResponse(
            HttpRequestMessage request,
            string traceId,
            string message,
            string errorCode,
            int retryAfterSeconds)
        {
            var payload = new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Data = null,
                Errors = null,
                TraceId = traceId
            };

            var response = request.CreateResponse((HttpStatusCode)429, payload);
            if (retryAfterSeconds > 0)
                response.Headers.Add("Retry-After", retryAfterSeconds.ToString(CultureInfo.InvariantCulture));

            return response;
        }

        private static int ReadPositiveIntFromConfig(string key, int defaultValue)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                    return parsed;
            }
            catch
            {
                // Ignore and use default value.
            }

            return defaultValue;
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var normalized = path.Trim().ToLowerInvariant();
            if (normalized.Length > 1)
                normalized = normalized.TrimEnd('/');

            return normalized;
        }

        private sealed class EndpointLimit
        {
            public EndpointLimit(string name, string path, int maxRequests, TimeSpan window)
            {
                Name = name;
                Path = path;
                MaxRequests = maxRequests;
                Window = window;
            }

            public string Name { get; }

            public string Path { get; }

            public int MaxRequests { get; }

            public TimeSpan Window { get; }
        }

        private sealed class RateWindowState
        {
            public RateWindowState(DateTime windowStartUtc)
            {
                WindowStartUtc = windowStartUtc;
                Count = 0;
            }

            public object SyncRoot { get; } = new object();

            public DateTime WindowStartUtc { get; set; }

            public int Count { get; set; }
        }
    }
}
