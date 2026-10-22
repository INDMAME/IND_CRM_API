using System;
using System.Net;
using System.Net.Http;
using IND_CRM_API.Models.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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

    /// <summary>
    /// Raised when an external dependency is unavailable or returns an invalid response.
    /// </summary>
    public sealed class IND_ExternalServiceException : Exception
    {
        public IND_ExternalServiceException(
            string serviceName,
            string userMessage,
            string errorCode,
            HttpStatusCode statusCode,
            string providerSummary = null,
            Exception innerException = null)
            : base(userMessage, innerException)
        {
            ServiceName = serviceName;
            UserMessage = userMessage;
            ErrorCode = errorCode;
            StatusCode = statusCode;
            ProviderSummary = providerSummary;
        }

        public string ServiceName { get; }

        public string UserMessage { get; }

        public string ErrorCode { get; }

        public HttpStatusCode StatusCode { get; }

        public string ProviderSummary { get; }
    }

    /// <summary>
    /// Raised when an Axapta COM call exceeds the configured timeout.
    /// </summary>
    public sealed class IND_AxCallTimeoutException : TimeoutException
    {
        public IND_AxCallTimeoutException(string operationName, int timeoutSeconds, string detail = null)
            : base("Axapta timeout in operation " + (operationName ?? "unknown") + ".")
        {
            OperationName = operationName;
            TimeoutSeconds = timeoutSeconds;
            Detail = detail;
        }

        public string OperationName { get; }

        public int TimeoutSeconds { get; }

        public string Detail { get; }

        public string UserMessage
        {
            get
            {
                return "El servicio de Axapta no respondio dentro del tiempo esperado. Intente de nuevo en unos momentos.";
            }
        }
    }

    internal static class IND_KnownExceptionMapper
    {
        public static bool TryMap(
            Exception ex,
            out HttpStatusCode statusCode,
            out string message,
            out string errorCode,
            out int? retryAfterSeconds)
        {
            retryAfterSeconds = null;

            if (ex is IND_OpenAiRateLimitException rateLimit)
            {
                statusCode = (HttpStatusCode)429;
                message = "Se excedio el limite de solicitudes de IA. Intente de nuevo en unos segundos.";
                errorCode = IndErrorCodes.AiRateLimitExceeded;
                retryAfterSeconds = rateLimit.RetryAfterSeconds;
                return true;
            }

            if (ex is IND_AxCallTimeoutException axTimeout)
            {
                statusCode = HttpStatusCode.ServiceUnavailable;
                message = axTimeout.UserMessage;
                errorCode = IndErrorCodes.AxTimeout;
                return true;
            }

            if (ex is IND_ExternalServiceException external)
            {
                statusCode = external.StatusCode;
                message = string.IsNullOrWhiteSpace(external.UserMessage)
                    ? "No se pudo completar la operacion con un servicio externo."
                    : external.UserMessage;
                errorCode = string.IsNullOrWhiteSpace(external.ErrorCode)
                    ? IndErrorCodes.ExternalServiceUnavailable
                    : external.ErrorCode;
                return true;
            }

            statusCode = HttpStatusCode.InternalServerError;
            message = null;
            errorCode = null;
            return false;
        }
    }

    internal static class IND_OpenAiErrorHandling
    {
        // Reports provider billing failures separately from temporary throttling.
        public static void ThrowIfQuotaExceeded(string responseBody)
        {
            if (!TryReadQuotaError(responseBody, out var type, out var code))
                return;

            var message = string.Equals(code, "credit_balance_exhausted", StringComparison.OrdinalIgnoreCase)
                ? "El proveedor de IA informa de saldo agotado. Contacte con el administrador para revisar la facturacion."
                : "El proveedor de IA informa de una cuota de uso agotada. Contacte con el administrador para revisar la facturacion y los limites.";

            throw new IND_ExternalServiceException(
                "OpenAI",
                message,
                IndErrorCodes.AiQuotaExceeded,
                HttpStatusCode.ServiceUnavailable,
                "type=" + type + " code=" + code);
        }

        // Only structured provider error fields can establish a billing failure.
        private static bool TryReadQuotaError(string responseBody, out string type, out string code)
        {
            type = null;
            code = null;
            if (string.IsNullOrWhiteSpace(responseBody))
                return false;

            try
            {
                var error = JObject.Parse(responseBody)["error"] as JObject;
                type = ReadErrorIdentifier(error?["type"]);
                code = ReadErrorIdentifier(error?["code"]);
                return IsQuotaIdentifier(type) || IsQuotaIdentifier(code);
            }
            catch (JsonException)
            {
                return false;
            }
        }

        // Ignore unexpected object or array values in provider error identifiers.
        private static string ReadErrorIdentifier(JToken token)
        {
            return token?.Type == JTokenType.String ? token.Value<string>().Trim() : null;
        }

        // These failures require a billing or quota change, not a short retry.
        private static bool IsQuotaIdentifier(string value)
        {
            switch (value?.ToLowerInvariant())
            {
                case "insufficient_quota":
                case "credit_balance_exhausted":
                case "billing_hard_limit_reached":
                case "organization_spend_limit_exceeded":
                case "project_spend_limit_exceeded":
                case "organization_usage_limit_exceeded":
                    return true;
                default:
                    return false;
            }
        }

        // Preserve throttling behavior while excluding known billing failures.
        public static bool IsRateLimit(HttpStatusCode statusCode, string responseBody)
        {
            if (TryReadQuotaError(responseBody, out _, out _))
                return false;

            if (statusCode == (HttpStatusCode)429)
                return true;

            var body = (responseBody ?? string.Empty).ToLowerInvariant();
            return body.Contains("rate_limit")
                || body.Contains("rate limit")
                || body.Contains("too many requests");
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
