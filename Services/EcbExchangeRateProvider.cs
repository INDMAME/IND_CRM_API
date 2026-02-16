using IND_CRM_API.Services.Interfaces;
using System;
using System.Globalization;
using System.Runtime.Caching;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Proveedor que orquesta cache y conversiones de tipos de cambio via ECB.
    /// </summary>
    public class EcbExchangeRateProvider : IExchangeRateProvider
    {
        private const string EurCurrencyCode = "EUR";
        private const string SourceName = "ECB";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
        private static readonly Regex IsoCurrencyRegex = new Regex("^[A-Z]{3}$", RegexOptions.Compiled);

        private readonly IEcbHttpClient _ecbHttpClient;
        private readonly IAxLogger _logger;
        private readonly MemoryCache _cache;

        public EcbExchangeRateProvider(IEcbHttpClient ecbHttpClient, IAxLogger logger)
        {
            _ecbHttpClient = ecbHttpClient ?? throw new ArgumentNullException(nameof(ecbHttpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = MemoryCache.Default;
        }

        /// <summary>
        /// Obtiene tipo de cambio base -> destino aplicando conversion via EUR cuando corresponde.
        /// </summary>
        public async Task<ExchangeRateProviderResult> GetExchangeRateAsync(
            string baseCurrency,
            string targetCurrency,
            DateTime? requestedDate,
            CancellationToken cancellationToken)
        {
            var normalizedBase = NormalizeCurrency(baseCurrency);
            var normalizedTarget = NormalizeCurrency(targetCurrency);
            var normalizedDate = requestedDate?.Date;
            var dateToken = normalizedDate.HasValue
                ? normalizedDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : "latest";
            var cacheKey = BuildCacheKey(normalizedBase, normalizedTarget, dateToken);

            if (TryGetFromCache(cacheKey, out var cachedResult))
                return cachedResult;

            if (!IsIsoCurrency(normalizedBase) || !IsIsoCurrency(normalizedTarget))
                return ExchangeRateProviderResult.NotFound(normalizedBase, normalizedTarget);

            if (normalizedBase == normalizedTarget)
            {
                var sameCurrencyResult = new ExchangeRateProviderResult
                {
                    Found = true,
                    BaseCurrency = normalizedBase,
                    TargetCurrency = normalizedTarget,
                    Rate = 1m,
                    Date = normalizedDate ?? DateTime.UtcNow.Date,
                    Source = SourceName
                };

                SetInCache(cacheKey, sameCurrencyResult);
                return sameCurrencyResult;
            }

            ExchangeRateProviderResult resolvedResult;

            if (normalizedBase == EurCurrencyCode)
            {
                resolvedResult = await ResolveDirectRateAsync(normalizedBase, normalizedTarget, normalizedDate, cancellationToken)
                    .ConfigureAwait(false);
            }
            else if (normalizedTarget == EurCurrencyCode)
            {
                resolvedResult = await ResolveInverseRateAsync(normalizedBase, normalizedTarget, normalizedDate, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                resolvedResult = await ResolveCrossRateAsync(normalizedBase, normalizedTarget, normalizedDate, cancellationToken)
                    .ConfigureAwait(false);
            }

            if (!resolvedResult.Found)
                return resolvedResult;

            SetInCache(cacheKey, resolvedResult);
            return resolvedResult;
        }

        private async Task<ExchangeRateProviderResult> ResolveDirectRateAsync(
            string baseCurrency,
            string targetCurrency,
            DateTime? requestedDate,
            CancellationToken cancellationToken)
        {
            var observation = await _ecbHttpClient
                .GetObservationAsync(targetCurrency, baseCurrency, requestedDate, true, cancellationToken)
                .ConfigureAwait(false);

            if (!observation.Found || observation.Rate <= 0m)
                return ExchangeRateProviderResult.NotFound(baseCurrency, targetCurrency);

            return new ExchangeRateProviderResult
            {
                Found = true,
                BaseCurrency = baseCurrency,
                TargetCurrency = targetCurrency,
                Rate = observation.Rate,
                Date = observation.ObservationDate.Date,
                Source = SourceName
            };
        }

        private async Task<ExchangeRateProviderResult> ResolveInverseRateAsync(
            string baseCurrency,
            string targetCurrency,
            DateTime? requestedDate,
            CancellationToken cancellationToken)
        {
            var basePerEurObservation = await _ecbHttpClient
                .GetObservationAsync(baseCurrency, EurCurrencyCode, requestedDate, true, cancellationToken)
                .ConfigureAwait(false);

            if (!basePerEurObservation.Found || basePerEurObservation.Rate <= 0m)
                return ExchangeRateProviderResult.NotFound(baseCurrency, targetCurrency);

            var computedRate = SafeDivide(1m, basePerEurObservation.Rate);
            if (computedRate <= 0m)
                return ExchangeRateProviderResult.NotFound(baseCurrency, targetCurrency);

            return new ExchangeRateProviderResult
            {
                Found = true,
                BaseCurrency = baseCurrency,
                TargetCurrency = targetCurrency,
                Rate = computedRate,
                Date = basePerEurObservation.ObservationDate.Date,
                Source = SourceName
            };
        }

        private async Task<ExchangeRateProviderResult> ResolveCrossRateAsync(
            string baseCurrency,
            string targetCurrency,
            DateTime? requestedDate,
            CancellationToken cancellationToken)
        {
            var basePerEurObservation = await _ecbHttpClient
                .GetObservationAsync(baseCurrency, EurCurrencyCode, requestedDate, true, cancellationToken)
                .ConfigureAwait(false);
            if (!basePerEurObservation.Found || basePerEurObservation.Rate <= 0m)
                return ExchangeRateProviderResult.NotFound(baseCurrency, targetCurrency);

            var targetPerEurObservation = await _ecbHttpClient
                .GetObservationAsync(targetCurrency, EurCurrencyCode, requestedDate, true, cancellationToken)
                .ConfigureAwait(false);
            if (!targetPerEurObservation.Found || targetPerEurObservation.Rate <= 0m)
                return ExchangeRateProviderResult.NotFound(baseCurrency, targetCurrency);

            var eurPerBase = SafeDivide(1m, basePerEurObservation.Rate);
            if (eurPerBase <= 0m)
                return ExchangeRateProviderResult.NotFound(baseCurrency, targetCurrency);

            var computedRate = eurPerBase * targetPerEurObservation.Rate;
            if (computedRate <= 0m)
                return ExchangeRateProviderResult.NotFound(baseCurrency, targetCurrency);

            // La fecha efectiva mas conservadora es la menor entre ambas observaciones.
            var effectiveDate = basePerEurObservation.ObservationDate <= targetPerEurObservation.ObservationDate
                ? basePerEurObservation.ObservationDate.Date
                : targetPerEurObservation.ObservationDate.Date;

            return new ExchangeRateProviderResult
            {
                Found = true,
                BaseCurrency = baseCurrency,
                TargetCurrency = targetCurrency,
                Rate = computedRate,
                Date = effectiveDate,
                Source = SourceName
            };
        }

        private bool TryGetFromCache(string cacheKey, out ExchangeRateProviderResult result)
        {
            var cached = _cache.Get(cacheKey) as ExchangeRateProviderResult;
            if (cached == null || !cached.Found)
            {
                result = null;
                return false;
            }

            result = CloneResult(cached);
            _logger.Log($"[EXCHANGE-CACHE] HIT {cacheKey}");
            return true;
        }

        private void SetInCache(string cacheKey, ExchangeRateProviderResult result)
        {
            if (result == null || !result.Found)
                return;

            _cache.Set(
                cacheKey,
                CloneResult(result),
                new CacheItemPolicy
                {
                    AbsoluteExpiration = DateTimeOffset.UtcNow.Add(CacheTtl)
                });

            _logger.Log($"[EXCHANGE-CACHE] SET {cacheKey}");
        }

        private static string BuildCacheKey(string baseCurrency, string targetCurrency, string dateToken)
        {
            return $"exchange:{baseCurrency}:{targetCurrency}:{dateToken}";
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

        private static ExchangeRateProviderResult CloneResult(ExchangeRateProviderResult source)
        {
            return new ExchangeRateProviderResult
            {
                Found = source.Found,
                BaseCurrency = source.BaseCurrency,
                TargetCurrency = source.TargetCurrency,
                Rate = source.Rate,
                Date = source.Date,
                Source = source.Source
            };
        }
    }
}
