using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Helpers;
using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Security.Claims;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Applies request rate and concurrency limits for OpenAI endpoints.
    /// </summary>
    public sealed class IND_OpenAiRateLimitHandler : DelegatingHandler
    {
        private const string SpeechPath = "/api/ia/service/speech";
        private const string ExpenseFromTicketPath = "/api/ia/service/expensefromticket";
        private const string ExpenseSheetAskPath = "/api/ia/service/expensesheets/ask";
        private const string QuickCreateTicketPath = "/api/crm/expensesheets/tickets/quick-create";
        private const string TextFormattingPath = "/api/ia/service/text/format";
        private const string HelpAskPath = "/api/ia/service/help/ask";

        private const string SpeechMaxRequestsSettingKey = "OpenAI:RateLimitSpeechMaxRequests";
        private const string SpeechWindowSecondsSettingKey = "OpenAI:RateLimitSpeechWindowSeconds";
        private const string ExpenseMaxRequestsSettingKey = "OpenAI:RateLimitExpenseTicketMaxRequests";
        private const string ExpenseWindowSecondsSettingKey = "OpenAI:RateLimitExpenseTicketWindowSeconds";
        private const string TextFormattingMaxRequestsSettingKey = "OpenAI:RateLimitTextFormattingMaxRequests";
        private const string TextFormattingWindowSecondsSettingKey = "OpenAI:RateLimitTextFormattingWindowSeconds";
        private const string MaxConcurrentPerUserSettingKey = "OpenAI:RateLimitMaxConcurrentPerUser";
        private const string ValidationMultiplierSettingKey = "OpenAI:RateLimitValidationMultiplier";
        private const string RateLimitEnabledSettingKey = "OpenAI:RateLimitEnabled";
        private const string AssistantQueryRateLimitEnabledSettingKey = "AssistantQueries:RateLimitEnabled";
        private const string AssistantQueryMaxRequestsSettingKey = "AssistantQueries:RateLimitMaxRequests";
        private const string AssistantQueryWindowSecondsSettingKey = "AssistantQueries:RateLimitWindowSeconds";
        private const string AssistantQueryValidationMultiplierSettingKey = "AssistantQueries:RateLimitValidationMultiplier";
        private const string HelpRateLimitEnabledSettingKey = "HelpAssistant:RateLimitEnabled";
        private const string HelpMaxRequestsSettingKey = "HelpAssistant:RateLimitMaxRequests";
        private const string HelpWindowSecondsSettingKey = "HelpAssistant:RateLimitWindowSeconds";
        private const string HelpValidationMultiplierSettingKey = "HelpAssistant:RateLimitValidationMultiplier";

        private const int DefaultSpeechMaxRequests = 5;
        private const int DefaultSpeechWindowSeconds = 300;
        private const int DefaultExpenseMaxRequests = 10;
        private const int DefaultExpenseWindowSeconds = 600;
        private const int DefaultTextFormattingMaxRequests = 20;
        private const int DefaultTextFormattingWindowSeconds = 600;
        private const int DefaultMaxConcurrentPerUser = 1;
        private const int DefaultValidationMultiplier = 1;
        private const bool DefaultRateLimitEnabled = true;
        private const int DefaultAssistantQueryMaxRequests = 30;
        private const int DefaultAssistantQueryWindowSeconds = 900;
        private const int DefaultAssistantQueryValidationMultiplier = 1;

        private static readonly int AssistantQueryMaxRequests = ReadPositiveIntFromConfig(
            AssistantQueryMaxRequestsSettingKey,
            HelpMaxRequestsSettingKey,
            DefaultAssistantQueryMaxRequests);

        private static readonly TimeSpan AssistantQueryWindow = TimeSpan.FromSeconds(ReadPositiveIntFromConfig(
            AssistantQueryWindowSecondsSettingKey,
            HelpWindowSecondsSettingKey,
            DefaultAssistantQueryWindowSeconds));

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

        private static readonly EndpointLimit ExpenseSheetAskLimit = new EndpointLimit(
            "expensesheets-ask",
            NormalizePath(ExpenseSheetAskPath),
            AssistantQueryMaxRequests,
            AssistantQueryWindow);

        private static readonly EndpointLimit QuickCreateTicketLimit = new EndpointLimit(
            "ticket-quick-create",
            NormalizePath(QuickCreateTicketPath),
            ReadPositiveIntFromConfig(ExpenseMaxRequestsSettingKey, DefaultExpenseMaxRequests),
            TimeSpan.FromSeconds(ReadPositiveIntFromConfig(ExpenseWindowSecondsSettingKey, DefaultExpenseWindowSeconds)));

        private static readonly EndpointLimit TextFormattingLimit = new EndpointLimit(
            "text-formatting",
            NormalizePath(TextFormattingPath),
            ReadPositiveIntFromConfig(TextFormattingMaxRequestsSettingKey, DefaultTextFormattingMaxRequests),
            TimeSpan.FromSeconds(ReadPositiveIntFromConfig(
                TextFormattingWindowSecondsSettingKey,
                DefaultTextFormattingWindowSeconds)));

        private static readonly EndpointLimit HelpAskLimit = new EndpointLimit(
            "crm-help-ask",
            NormalizePath(HelpAskPath),
            AssistantQueryMaxRequests,
            AssistantQueryWindow);

        private readonly ConcurrentDictionary<string, RateWindowState> _rateWindows =
            new ConcurrentDictionary<string, RateWindowState>(StringComparer.OrdinalIgnoreCase);

        private readonly ConcurrentDictionary<string, int> _activeRequests =
            new ConcurrentDictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private readonly IAxLogger _logger;
        private readonly int _maxConcurrentPerUser;
        private readonly int _validationMultiplier;
        private readonly int _assistantQueryValidationMultiplier;
        private readonly bool _isEnabled;
        private readonly bool _isAssistantQueryEnabled;
        private readonly bool _isHelpEnabled;
        private readonly bool _isHelpFeatureEnabled;

        public IND_OpenAiRateLimitHandler(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _maxConcurrentPerUser = ReadPositiveIntFromConfig(MaxConcurrentPerUserSettingKey, DefaultMaxConcurrentPerUser);
            _validationMultiplier = ReadPositiveIntFromConfig(ValidationMultiplierSettingKey, DefaultValidationMultiplier);
            _assistantQueryValidationMultiplier = ReadPositiveIntFromConfig(
                AssistantQueryValidationMultiplierSettingKey,
                HelpValidationMultiplierSettingKey,
                DefaultAssistantQueryValidationMultiplier);
            _isEnabled = ReadBoolFromConfig(RateLimitEnabledSettingKey, DefaultRateLimitEnabled);
            _isAssistantQueryEnabled = ReadBoolFromConfig(AssistantQueryRateLimitEnabledSettingKey, true);
            _isHelpEnabled = ReadBoolFromConfig(HelpRateLimitEnabledSettingKey, true);
            _isHelpFeatureEnabled = AppSettingsHelper.GetBoolSetting(
                "HelpAssistant:Enabled",
                false,
                "INDCRM_HELP_ENABLED");
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (!TryResolveEndpoint(request, out var endpoint))
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);

            var isHelpEndpoint = ReferenceEquals(endpoint, HelpAskLimit);
            var isAssistantQueryEndpoint = isHelpEndpoint || ReferenceEquals(endpoint, ExpenseSheetAskLimit);
            if (isHelpEndpoint && !_isHelpFeatureEnabled)
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (isAssistantQueryEndpoint)
            {
                if (!_isAssistantQueryEnabled || (isHelpEndpoint && !_isHelpEnabled))
                    return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            else if (!_isEnabled)
            {
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }

            var userKey = ResolveUserKey(request);
            if (string.IsNullOrWhiteSpace(userKey))
                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var logUser = isAssistantQueryEndpoint ? "redacted" : userKey;

            if (!TryAcquireConcurrency(userKey, out var activeCount))
            {
                var traceId = Guid.NewGuid().ToString("N");
                _logger.Log(
                    $"[AI-LIMIT] Concurrent limit exceeded user={logUser} endpoint={endpoint.Name} active={activeCount} max={_maxConcurrentPerUser} traceId={traceId}",
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
                // Keeps chatbot query throttling independent from shared OpenAI tests.
                var validationMultiplier = isAssistantQueryEndpoint
                    ? _assistantQueryValidationMultiplier
                    : _validationMultiplier;
                if (!TryConsumeRequest(
                    userKey,
                    endpoint,
                    validationMultiplier,
                    out var retryAfterSeconds,
                    out var effectiveMaxRequests))
                {
                    var traceId = Guid.NewGuid().ToString("N");
                    _logger.Log(
                        $"[AI-LIMIT] Rate limit exceeded user={logUser} endpoint={endpoint.Name} configuredMaxRequests={endpoint.MaxRequests} effectiveMaxRequests={effectiveMaxRequests} validationMultiplier={validationMultiplier} windowSeconds={(int)endpoint.Window.TotalSeconds} retryAfterSeconds={retryAfterSeconds} traceId={traceId}",
                        AxaptaSessionManager.LogLevel.Warning);

                    var limitMessage = isAssistantQueryEndpoint
                        ? BuildAssistantQueryRateLimitMessage(retryAfterSeconds)
                        : validationMultiplier > 1
                            ? $"Se excedio el limite de {effectiveMaxRequests} solicitudes en {(int)endpoint.Window.TotalMinutes} minutos (modo pruebas x{validationMultiplier})."
                            : $"Se excedio el limite de {effectiveMaxRequests} solicitudes en {(int)endpoint.Window.TotalMinutes} minutos.";

                    return BuildTooManyRequestsResponse(
                        request,
                        traceId,
                        limitMessage,
                        isAssistantQueryEndpoint
                            ? IndErrorCodes.AssistantQueryRateLimitExceeded
                            : IndErrorCodes.AiRateLimitExceeded,
                        retryAfterSeconds);
                }

                return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                ReleaseConcurrency(userKey);
            }
        }

        private bool TryConsumeRequest(
            string userKey,
            EndpointLimit endpoint,
            int validationMultiplier,
            out int retryAfterSeconds,
            out int effectiveMaxRequests)
        {
            var nowUtc = DateTime.UtcNow;
            var key = userKey + "|" + endpoint.Name;
            var state = _rateWindows.GetOrAdd(key, _ => new RateWindowState(nowUtc));
            effectiveMaxRequests = GetEffectiveMaxRequests(endpoint.MaxRequests, validationMultiplier);

            lock (state.SyncRoot)
            {
                var elapsed = nowUtc - state.WindowStartUtc;
                if (elapsed >= endpoint.Window)
                {
                    state.WindowStartUtc = nowUtc;
                    state.Count = 0;
                }

                if (state.Count >= effectiveMaxRequests)
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

        private static int GetEffectiveMaxRequests(int configuredMaxRequests, int validationMultiplier)
        {
            if (validationMultiplier <= 1)
                return configuredMaxRequests;

            var effective = (int)Math.Ceiling((double)configuredMaxRequests / validationMultiplier);
            return effective <= 0 ? 1 : effective;
        }

        /// <summary>
        /// Builds a clear assistant query limit message using the remaining server wait time.
        /// </summary>
        private static string BuildAssistantQueryRateLimitMessage(int retryAfterSeconds)
        {
            var retryAfterMinutes = Math.Max(1, (int)Math.Ceiling(retryAfterSeconds / 60d));
            var minuteLabel = retryAfterMinutes == 1 ? "minuto" : "minutos";
            return $"Se ha superado el límite de consultas. Por favor, vuelva a intentarlo dentro de {retryAfterMinutes} {minuteLabel}.";
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

            if (string.Equals(path, ExpenseSheetAskLimit.Path, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = ExpenseSheetAskLimit;
                return true;
            }

            if (string.Equals(path, QuickCreateTicketLimit.Path, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = QuickCreateTicketLimit;
                return true;
            }

            if (string.Equals(path, TextFormattingLimit.Path, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = TextFormattingLimit;
                return true;
            }

            if (string.Equals(path, HelpAskLimit.Path, StringComparison.OrdinalIgnoreCase))
            {
                endpoint = HelpAskLimit;
                return true;
            }

            return false;
        }

        private static string ResolveUserKey(HttpRequestMessage request)
        {
            var principal = request?.GetRequestContext()?.Principal ?? Thread.CurrentPrincipal;
            if (principal?.Identity?.IsAuthenticated != true)
                return null;

            string name = null;
            if (principal is ClaimsPrincipal claims)
                name = claims.FindFirst("oid")?.Value ?? claims.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? claims.FindFirst("sub")?.Value;
            if (string.IsNullOrWhiteSpace(name))
                name = principal.Identity.Name;
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
            return TryReadPositiveIntFromConfig(key, out var value) ? value : defaultValue;
        }

        /// <summary>
        /// Reads a primary assistant setting while preserving the legacy CRM help fallback.
        /// </summary>
        private static int ReadPositiveIntFromConfig(string key, string fallbackKey, int defaultValue)
        {
            return TryReadPositiveIntFromConfig(key, out var value)
                ? value
                : ReadPositiveIntFromConfig(fallbackKey, defaultValue);
        }

        /// <summary>
        /// Tries to read one positive integer without accepting unresolved placeholders.
        /// </summary>
        private static bool TryReadPositiveIntFromConfig(string key, out int value)
        {
            value = 0;
            try
            {
                var configuredValue = AppSettingsHelper.GetSetting(key);
                return int.TryParse(
                    configuredValue,
                    NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out value) && value > 0;
            }
            catch
            {
                return false;
            }
        }

        private static bool ReadBoolFromConfig(string key, bool defaultValue)
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(key);
                if (bool.TryParse(value, out var parsed))
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
