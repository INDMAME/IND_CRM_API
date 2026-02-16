using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Wrapper HTTP aislado para consumir el WS REST del Banco Central Europeo.
    /// </summary>
    public class EcbHttpClient : IEcbHttpClient
    {
        private static readonly string[] HostPriority =
        {
            "https://sdw-wsrest.ecb.europa.eu",
            "https://data-api.ecb.europa.eu"
        };

        private static readonly HttpClient SharedHttpClient = CreateSharedHttpClient();

        private readonly IAxLogger _logger;

        public EcbHttpClient(IAxLogger logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Consulta una observacion ECB para la serie D.{target}.{base}.SP00.A.
        /// </summary>
        public async Task<EcbObservationResult> GetObservationAsync(
            string targetCurrency,
            string baseCurrency,
            DateTime? requestedDate,
            bool fallbackToPreviousBusinessDay,
            CancellationToken cancellationToken)
        {
            var normalizedTarget = NormalizeCurrency(targetCurrency);
            var normalizedBase = NormalizeCurrency(baseCurrency);
            var normalizedDate = requestedDate?.Date;

            foreach (var host in HostPriority)
            {
                var requestUrl = BuildRequestUrl(host, normalizedTarget, normalizedBase, normalizedDate, fallbackToPreviousBusinessDay);
                try
                {
                    using (var request = new HttpRequestMessage(HttpMethod.Get, requestUrl))
                    using (var response = await SharedHttpClient
                        .SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false))
                    {
                        if (!response.IsSuccessStatusCode)
                        {
                            _logger.Log($"[ECB-HTTP] GET {requestUrl} -> {(int)response.StatusCode}");
                            continue;
                        }

                        var payload = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (string.IsNullOrWhiteSpace(payload))
                        {
                            _logger.Log($"[ECB-HTTP] Empty payload for {requestUrl}");
                            continue;
                        }

                        if (TryParseObservation(payload, out var parsed))
                            return parsed;

                        _logger.Log($"[ECB-HTTP] Invalid ECB payload for {requestUrl}", AxaptaSessionManager.LogLevel.Warning);
                    }
                }
                catch (TaskCanceledException)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw;

                    _logger.Log($"[ECB-HTTP] Timeout for {requestUrl}", AxaptaSessionManager.LogLevel.Warning);
                }
                catch (HttpRequestException ex)
                {
                    _logger.Log($"[ECB-HTTP] Request error for {requestUrl}: {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                }
                catch (Exception ex)
                {
                    _logger.Log($"[ECB-HTTP] Unexpected error for {requestUrl}: {ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                }
            }

            return EcbObservationResult.NotFound();
        }

        private static HttpClient CreateSharedHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };

            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static string NormalizeCurrency(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim().ToUpperInvariant();
        }

        private static string BuildRequestUrl(
            string host,
            string targetCurrency,
            string baseCurrency,
            DateTime? requestedDate,
            bool fallbackToPreviousBusinessDay)
        {
            var seriesKey = $"D.{targetCurrency}.{baseCurrency}.SP00.A";
            var parameters = new List<string>
            {
                "format=jsondata",
                "detail=dataonly"
            };

            if (requestedDate.HasValue)
            {
                var dateValue = requestedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (fallbackToPreviousBusinessDay)
                {
                    parameters.Add("endPeriod=" + dateValue);
                    parameters.Add("lastNObservations=1");
                }
                else
                {
                    parameters.Add("startPeriod=" + dateValue);
                    parameters.Add("endPeriod=" + dateValue);
                }
            }
            else
            {
                parameters.Add("lastNObservations=1");
            }

            return $"{host}/service/data/EXR/{seriesKey}?{string.Join("&", parameters)}";
        }

        private static bool TryParseObservation(string payload, out EcbObservationResult observation)
        {
            observation = EcbObservationResult.NotFound();

            JObject root;
            try
            {
                root = JObject.Parse(payload);
            }
            catch
            {
                return false;
            }

            var series = root["dataSets"]?[0]?["series"] as JObject;
            if (series == null || !series.Properties().Any())
                return false;

            var firstSeries = series.Properties().FirstOrDefault();
            var observations = firstSeries?.Value?["observations"] as JObject;
            if (observations == null || !observations.Properties().Any())
                return false;

            var firstObservation = observations.Properties()
                .OrderBy(p => ParseNonNegativeInteger(p.Name))
                .FirstOrDefault();
            if (firstObservation == null)
                return false;

            var observationValues = firstObservation.Value as JArray;
            if (observationValues == null || observationValues.Count == 0)
                return false;

            if (!TryParseDecimal(observationValues[0], out var rate))
                return false;

            if (rate <= 0m)
                return false;

            var observationIndex = ParseNonNegativeInteger(firstObservation.Name);
            var observationDate = TryResolveObservationDate(root, observationIndex);
            if (!observationDate.HasValue)
                return false;

            observation = new EcbObservationResult
            {
                Found = true,
                Rate = rate,
                ObservationDate = observationDate.Value.Date
            };

            return true;
        }

        private static DateTime? TryResolveObservationDate(JObject root, int observationIndex)
        {
            var values = root["structure"]?["dimensions"]?["observation"]?[0]?["values"] as JArray;
            if (values == null || values.Count == 0)
                return null;

            var safeIndex = observationIndex;
            if (safeIndex < 0 || safeIndex >= values.Count)
                safeIndex = values.Count - 1;

            var dateText = values[safeIndex]?["id"]?.ToString();
            if (string.IsNullOrWhiteSpace(dateText))
                return null;

            if (DateTime.TryParseExact(
                dateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsedDate))
            {
                return parsedDate.Date;
            }

            return null;
        }

        private static bool TryParseDecimal(JToken token, out decimal value)
        {
            value = 0m;
            if (token == null)
                return false;

            return decimal.TryParse(
                token.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static int ParseNonNegativeInteger(string value)
        {
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed >= 0)
                return parsed;

            return int.MaxValue;
        }
    }
}
