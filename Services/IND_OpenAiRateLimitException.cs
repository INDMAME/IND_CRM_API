using System;
using System.Net;
using System.Net.Http;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Raised when OpenAI returns a throttling response.
    /// </summary>
    public sealed class IND_OpenAiRateLimitException : Exception
    {
        public IND_OpenAiRateLimitException(string message, int? retryAfterSeconds, string providerSummary)
            : base(message)
        {
            RetryAfterSeconds = retryAfterSeconds;
            ProviderSummary = providerSummary;
        }

        public int? RetryAfterSeconds { get; }

        public string ProviderSummary { get; }
    }

    internal static class IND_OpenAiErrorHandling
    {
        public static bool IsRateLimit(HttpStatusCode statusCode, string responseBody)
        {
            if (statusCode == (HttpStatusCode)429)
                return true;

            var body = (responseBody ?? string.Empty).ToLowerInvariant();
            return body.Contains("rate_limit")
                || body.Contains("rate limit")
                || body.Contains("too many requests")
                || body.Contains("insufficient_quota");
        }

        public static int? GetRetryAfterSeconds(HttpResponseMessage response)
        {
            if (response?.Headers?.RetryAfter == null)
                return null;

            var retryAfter = response.Headers.RetryAfter;
            if (retryAfter.Delta.HasValue)
            {
                var seconds = (int)Math.Ceiling(retryAfter.Delta.Value.TotalSeconds);
                return seconds > 0 ? seconds : 1;
            }

            if (retryAfter.Date.HasValue)
            {
                var delta = retryAfter.Date.Value - DateTimeOffset.UtcNow;
                var seconds = (int)Math.Ceiling(delta.TotalSeconds);
                return seconds > 0 ? seconds : 1;
            }

            return null;
        }
    }
}
