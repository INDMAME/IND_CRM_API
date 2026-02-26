using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Proveedor secundario de tipos de cambio usando ExchangeRate.host.
    /// </summary>
    public class ExchangeRateHostProvider : IRawExchangeRateProvider
    {
        private const string Endpoint = "https://api.exchangerate.host/latest";
        private static readonly Regex IsoCurrencyRegex = new Regex("^[A-Z]{3}$", RegexOptions.Compiled);
        private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

        private readonly IAxLogger _logger;

        public ExchangeRateHostProvider(IAxLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string ProviderName => "EXR_HOST";

        public ExchangeRateResult GetRate(string baseCurrency, string targetCurrency, DateTime date)
        {
            var normalizedBase = NormalizeCurrency(baseCurrency);
            var normalizedTarget = NormalizeCurrency(targetCurrency);
            if (!IsIsoCurrency(normalizedBase) || !IsIsoCurrency(normalizedTarget))
                return BuildFailure(ExchangeRateProviderErrorCodes.CurrencyNotFound, date.Date);

            if (normalizedBase == normalizedTarget)
                return BuildSuccess(1m, date.Date);

            var requestUrl = $"{Endpoint}?base={Uri.EscapeDataString(normalizedBase)}&symbols={Uri.EscapeDataString(normalizedTarget)}";

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                using (var response = SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Log($"[EXCHANGE-EXRHOST] GET {requestUrl} -> {(int)response.StatusCode}", AxaptaSessionManager.LogLevel.Warning);
                        return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
                    }

                    var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!TryParsePayload(payload, normalizedTarget, out var rate, out var rateDate, out var errorCode, out var parseError))
                    {
                        _logger.Log($"[EXCHANGE-EXRHOST] Parse error reason={parseError}", AxaptaSessionManager.LogLevel.Warning);
                        return BuildFailure(errorCode, date.Date);
                    }

                    return BuildSuccess(rate, rateDate);
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.Log($"[EXCHANGE-EXRHOST] Timeout {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
            }
            catch (HttpRequestException ex)
            {
                _logger.Log($"[EXCHANGE-EXRHOST] Request error {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
            }
            catch (Exception ex)
            {
                _logger.Log($"[EXCHANGE-EXRHOST] Unexpected error {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
            }
        }

        private ExchangeRateResult BuildSuccess(decimal rate, DateTime date)
        {
            return new ExchangeRateResult
            {
                Success = true,
                Rate = rate,
                Source = ProviderName,
                ErrorCode = null,
                Date = date.Date,
                ProviderUsed = ProviderName,
                FallbackActivated = false
            };
        }

        private ExchangeRateResult BuildFailure(string errorCode, DateTime date)
        {
            return new ExchangeRateResult
            {
                Success = false,
                Rate = 0m,
                Source = ProviderName,
                ErrorCode = errorCode,
                Date = date.Date,
                ProviderUsed = ProviderName,
                FallbackActivated = false
            };
        }

        private static bool TryParsePayload(
            string payload,
            string targetCurrency,
            out decimal rate,
            out DateTime date,
            out string errorCode,
            out string reason)
        {
            rate = 0m;
            date = DateTime.UtcNow.Date;
            errorCode = ExchangeRateProviderErrorCodes.ProviderError;
            reason = null;

            if (string.IsNullOrWhiteSpace(payload))
            {
                reason = "Empty payload.";
                return false;
            }

            JObject root;
            try
            {
                root = JObject.Parse(payload);
            }
            catch (Exception ex)
            {
                reason = $"Invalid JSON: {ex.Message}";
                return false;
            }

            var successToken = root["success"];
            if (successToken != null && successToken.Type == JTokenType.Boolean && !successToken.Value<bool>())
            {
                reason = "Provider returned success=false.";
                return false;
            }

            var rateToken = root["rates"]?[targetCurrency];
            if (rateToken == null || rateToken.Type == JTokenType.Null)
            {
                errorCode = ExchangeRateProviderErrorCodes.CurrencyNotFound;
                reason = "Target currency not found.";
                return false;
            }

            if (!TryParseDecimal(rateToken.ToString(), out rate) || rate <= 0m)
            {
                reason = "Target rate is invalid.";
                return false;
            }

            var dateRaw = (root["date"]?.ToString() ?? string.Empty).Trim();
            if (DateTime.TryParseExact(
                dateRaw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
            {
                date = parsedDate.Date;
            }

            errorCode = null;
            return true;
        }

        private static HttpClient CreateSharedHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var timeoutSeconds = ReadTimeoutSeconds("ExchangeRate:HostTimeoutSeconds", 5);
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            client.DefaultRequestHeaders.UserAgent.ParseAdd("IND_CRM_API/1.0");
            return client;
        }

        private static int ReadTimeoutSeconds(string key, int fallback)
        {
            try
            {
                var value = ConfigurationManager.AppSettings[key];
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
                    return seconds;
            }
            catch
            {
                // Ignorar lectura de config y usar fallback defensivo.
            }

            return fallback;
        }

        private static string NormalizeCurrency(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        private static bool IsIsoCurrency(string value)
        {
            return !string.IsNullOrWhiteSpace(value) && IsoCurrencyRegex.IsMatch(value);
        }

        private static bool TryParseDecimal(string raw, out decimal value)
        {
            value = 0m;
            if (string.IsNullOrWhiteSpace(raw))
                return false;

            if (decimal.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return true;

            var normalized = raw.Trim().Replace(',', '.');
            return decimal.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }
    }
}
