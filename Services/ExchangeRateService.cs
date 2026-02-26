using IND_CRM_API.Services.Interfaces;
using System;
using System.Globalization;
using System.Runtime.Caching;
using System.Text.RegularExpressions;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Orquestador de tipos de cambio con estrategia ECB primario y ExchangeRate.host fallback.
    /// </summary>
    public class ExchangeRateService : IExchangeRateProvider
    {
        private const string PublicSourceName = "ECB";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
        private static readonly Regex IsoCurrencyRegex = new Regex("^[A-Z]{3}$", RegexOptions.Compiled);

        private readonly IRawExchangeRateProvider _ecbProvider;
        private readonly IRawExchangeRateProvider _fallbackProvider;
        private readonly IAxLogger _logger;
        private readonly ObjectCache _cache;

        public ExchangeRateService(
            IRawExchangeRateProvider ecbProvider,
            IRawExchangeRateProvider fallbackProvider,
            IAxLogger logger,
            ObjectCache cache = null)
        {
            _ecbProvider = ecbProvider ?? throw new ArgumentNullException(nameof(ecbProvider));
            _fallbackProvider = fallbackProvider ?? throw new ArgumentNullException(nameof(fallbackProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _cache = cache ?? MemoryCache.Default;
        }

        public ExchangeRateResult GetRate(string baseCurrency, string targetCurrency, DateTime date)
        {
            var normalizedBase = NormalizeCurrency(baseCurrency);
            var normalizedTarget = NormalizeCurrency(targetCurrency);
            var requestedDate = date == DateTime.MinValue ? DateTime.UtcNow.Date : date.Date;

            if (!IsIsoCurrency(normalizedBase) || !IsIsoCurrency(normalizedTarget))
            {
                return BuildFailure(ExchangeRateProviderErrorCodes.CurrencyNotFound, requestedDate, "VALIDATION", false);
            }

            if (normalizedBase == normalizedTarget)
            {
                return BuildSuccess(1m, requestedDate, _ecbProvider.ProviderName, false);
            }

            var cacheKey = BuildCacheKey(normalizedBase, normalizedTarget, requestedDate);
            if (TryGetFromCache(cacheKey, out var cachedResult))
                return cachedResult;

            var primaryResult = ExecuteProvider(_ecbProvider, normalizedBase, normalizedTarget, requestedDate);
            if (primaryResult.Success)
            {
                var normalizedResult = BuildSuccess(primaryResult.Rate, primaryResult.Date, _ecbProvider.ProviderName, false);
                SetInCache(cacheKey, normalizedResult);
                return normalizedResult;
            }

            var shouldFallback = string.Equals(primaryResult.ErrorCode, ExchangeRateProviderErrorCodes.CurrencyNotFound, StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(primaryResult.ErrorCode, ExchangeRateProviderErrorCodes.ProviderError, StringComparison.OrdinalIgnoreCase);
            if (shouldFallback)
            {
                _logger.Log($"[EXCHANGE-FALLBACK] from={_ecbProvider.ProviderName} to={_fallbackProvider.ProviderName} base={normalizedBase} target={normalizedTarget} date={requestedDate:yyyy-MM-dd}");

                var fallbackResult = ExecuteProvider(_fallbackProvider, normalizedBase, normalizedTarget, requestedDate);
                if (fallbackResult.Success)
                {
                    var normalizedResult = BuildSuccess(fallbackResult.Rate, fallbackResult.Date, _fallbackProvider.ProviderName, true);
                    SetInCache(cacheKey, normalizedResult);
                    return normalizedResult;
                }
            }

            return BuildFailure(ExchangeRateProviderErrorCodes.RateUnavailable, requestedDate, _ecbProvider.ProviderName, shouldFallback);
        }

        private ExchangeRateResult ExecuteProvider(
            IRawExchangeRateProvider provider,
            string baseCurrency,
            string targetCurrency,
            DateTime date)
        {
            try
            {
                var result = provider.GetRate(baseCurrency, targetCurrency, date);
                if (result != null)
                    return result;

                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date, provider.ProviderName, false);
            }
            catch (Exception ex)
            {
                _logger.Log($"[EXCHANGE-PROVIDER-ERROR] provider={provider.ProviderName} base={baseCurrency} target={targetCurrency} date={date:yyyy-MM-dd} error={ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date, provider.ProviderName, false);
            }
        }

        private bool TryGetFromCache(string cacheKey, out ExchangeRateResult result)
        {
            var cached = _cache.Get(cacheKey) as ExchangeRateResult;
            if (cached == null || !cached.Success)
            {
                result = null;
                return false;
            }

            var clone = CloneResult(cached);
            clone.ProviderUsed = "CACHE";
            result = clone;
            _logger.Log($"[EXCHANGE-CACHE] HIT {cacheKey}");
            return true;
        }

        private void SetInCache(string cacheKey, ExchangeRateResult result)
        {
            if (result == null || !result.Success)
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

        private static ExchangeRateResult BuildSuccess(decimal rate, DateTime date, string providerUsed, bool fallbackActivated)
        {
            return new ExchangeRateResult
            {
                Success = true,
                Rate = rate,
                Source = PublicSourceName,
                ErrorCode = null,
                Date = date.Date,
                ProviderUsed = providerUsed,
                FallbackActivated = fallbackActivated
            };
        }

        private static ExchangeRateResult BuildFailure(string errorCode, DateTime date, string providerUsed, bool fallbackActivated)
        {
            return new ExchangeRateResult
            {
                Success = false,
                Rate = 0m,
                Source = PublicSourceName,
                ErrorCode = errorCode,
                Date = date.Date,
                ProviderUsed = providerUsed,
                FallbackActivated = fallbackActivated
            };
        }

        private static ExchangeRateResult CloneResult(ExchangeRateResult source)
        {
            return new ExchangeRateResult
            {
                Success = source.Success,
                Rate = source.Rate,
                Source = source.Source,
                ErrorCode = source.ErrorCode,
                Date = source.Date,
                ProviderUsed = source.ProviderUsed,
                FallbackActivated = source.FallbackActivated
            };
        }

        private static string BuildCacheKey(string baseCurrency, string targetCurrency, DateTime date)
        {
            return $"{baseCurrency}|{targetCurrency}|{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";
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
    }
}
