using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Proveedor primario que consume el XML oficial diario del ECB.
    /// </summary>
    public class EcbExchangeRateProvider : IRawExchangeRateProvider
    {
        private const string DailyFeedUrl = "https://www.ecb.europa.eu/stats/eurofxref/eurofxref-daily.xml";
        private const string EurCurrencyCode = "EUR";
        private static readonly Regex IsoCurrencyRegex = new Regex("^[A-Z]{3}$", RegexOptions.Compiled);
        private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

        private readonly IAxLogger _logger;

        public EcbExchangeRateProvider(IAxLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public string ProviderName => "ECB";

        public ExchangeRateResult GetRate(string baseCurrency, string targetCurrency, DateTime date)
        {
            var normalizedBase = NormalizeCurrency(baseCurrency);
            var normalizedTarget = NormalizeCurrency(targetCurrency);

            if (!IsIsoCurrency(normalizedBase) || !IsIsoCurrency(normalizedTarget))
            {
                return BuildFailure(ExchangeRateProviderErrorCodes.CurrencyNotFound, date.Date);
            }

            if (normalizedBase == normalizedTarget)
            {
                return BuildSuccess(1m, date.Date);
            }

            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, DailyFeedUrl))
                using (var response = SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult())
                {
                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Log($"[EXCHANGE-ECB] GET {DailyFeedUrl} -> {(int)response.StatusCode}", AxaptaSessionManager.LogLevel.Warning);
                        return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
                    }

                    var payload = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!TryParseDailyFeed(payload, out var feedDate, out var eurRates, out var parseError))
                    {
                        _logger.Log($"[EXCHANGE-ECB] Parse error reason={parseError}", AxaptaSessionManager.LogLevel.Warning);
                        return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
                    }

                    if (!eurRates.ContainsKey(EurCurrencyCode))
                        eurRates[EurCurrencyCode] = 1m;

                    if (!eurRates.TryGetValue(normalizedBase, out var basePerEur) ||
                        !eurRates.TryGetValue(normalizedTarget, out var targetPerEur) ||
                        basePerEur <= 0m ||
                        targetPerEur <= 0m)
                    {
                        return BuildFailure(ExchangeRateProviderErrorCodes.CurrencyNotFound, feedDate);
                    }

                    var computedRate = normalizedBase == EurCurrencyCode
                        ? targetPerEur
                        : (normalizedTarget == EurCurrencyCode
                            ? SafeDivide(1m, basePerEur)
                            : SafeDivide(targetPerEur, basePerEur));

                    if (computedRate <= 0m)
                    {
                        return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, feedDate);
                    }

                    return BuildSuccess(computedRate, feedDate);
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.Log($"[EXCHANGE-ECB] Timeout {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
            }
            catch (HttpRequestException ex)
            {
                _logger.Log($"[EXCHANGE-ECB] Request error {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date.Date);
            }
            catch (Exception ex)
            {
                _logger.Log($"[EXCHANGE-ECB] Unexpected error {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
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

        private static HttpClient CreateSharedHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var timeoutSeconds = ReadTimeoutSeconds("ExchangeRate:EcbTimeoutSeconds", 5);
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            };

            var client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(timeoutSeconds)
            };

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/xml"));
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("text/xml"));
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

        private static bool TryParseDailyFeed(
            string payload,
            out DateTime feedDate,
            out IDictionary<string, decimal> eurRates,
            out string reason)
        {
            feedDate = DateTime.UtcNow.Date;
            eurRates = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            reason = null;

            if (string.IsNullOrWhiteSpace(payload))
            {
                reason = "Empty payload.";
                return false;
            }

            XDocument document;
            try
            {
                document = XDocument.Parse(payload, LoadOptions.None);
            }
            catch (Exception ex)
            {
                reason = $"Invalid XML: {ex.Message}";
                return false;
            }

            var dayNode = document
                .Descendants()
                .FirstOrDefault(node => string.Equals(node.Name.LocalName, "Cube", StringComparison.OrdinalIgnoreCase) &&
                                        node.Attribute("time") != null);
            if (dayNode == null)
            {
                reason = "Missing day node.";
                return false;
            }

            var dateRaw = dayNode.Attribute("time")?.Value;
            if (!DateTime.TryParseExact(
                dateRaw,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
            {
                reason = "Invalid date in ECB payload.";
                return false;
            }

            feedDate = parsedDate.Date;
            foreach (var rateNode in dayNode.Elements())
            {
                if (!string.Equals(rateNode.Name.LocalName, "Cube", StringComparison.OrdinalIgnoreCase))
                    continue;

                var currency = NormalizeCurrency(rateNode.Attribute("currency")?.Value);
                var rateRaw = (rateNode.Attribute("rate")?.Value ?? string.Empty).Trim();
                if (!IsIsoCurrency(currency))
                    continue;

                if (!decimal.TryParse(rateRaw, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRate))
                    continue;

                if (parsedRate <= 0m)
                    continue;

                eurRates[currency] = parsedRate;
            }

            return eurRates.Count > 0;
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

        private static decimal SafeDivide(decimal numerator, decimal denominator)
        {
            if (denominator == 0m)
                return 0m;

            return numerator / denominator;
        }
    }
}
