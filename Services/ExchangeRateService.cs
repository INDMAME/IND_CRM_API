using IND_CRM_API.Services.Interfaces;
using System;
using System.Globalization;
using System.Runtime.Caching;
using System.Text.RegularExpressions;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Orchestrates exchange rates using primary and multi-level fallback providers.
    /// </summary>
    public class ExchangeRateService : IExchangeRateProvider
    {
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
        private static readonly Regex IsoCurrencyRegex = new Regex("^[A-Z]{3}$", RegexOptions.Compiled);

        private readonly IRawExchangeRateProvider _primaryProvider;
        private readonly IRawExchangeRateProvider _secondaryProvider;
        private readonly IRawExchangeRateProvider _tertiaryProvider;
        private readonly IAxLogger _logger;
        private readonly ObjectCache _cache;

        public ExchangeRateService(
            IRawExchangeRateProvider primaryProvider,
            IRawExchangeRateProvider secondaryProvider,
            IRawExchangeRateProvider tertiaryProvider,
            IAxLogger logger,
            ObjectCache cache = null)
        {
            _primaryProvider = primaryProvider ?? throw new ArgumentNullException(nameof(primaryProvider));
            _secondaryProvider = secondaryProvider ?? throw new ArgumentNullException(nameof(secondaryProvider));
            _tertiaryProvider = tertiaryProvider ?? throw new ArgumentNullException(nameof(tertiaryProvider));
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
                return BuildFailure(ExchangeRateProviderErrorCodes.CurrencyNotFound, requestedDate, "VALIDATION", false, false);
            }

            if (normalizedBase == normalizedTarget)
            {
                return BuildSuccess(1m, requestedDate, _primaryProvider.ProviderName, false, false);
            }

            var cacheKey = BuildCacheKey(normalizedBase, normalizedTarget, requestedDate);
            if (TryGetFromCache(cacheKey, out var cachedResult))
                return cachedResult;

            var primaryResult = ExecuteProvider(_primaryProvider, normalizedBase, normalizedTarget, requestedDate);
            if (primaryResult.Success)
            {
                var normalizedResult = BuildSuccess(primaryResult.Rate, primaryResult.Date, _primaryProvider.ProviderName, false, false);
                SetInCache(cacheKey, normalizedResult);
                return normalizedResult;
            }

            var fallbackLevel2Activated = false;
            var fallbackLevel3Activated = false;

            if (CanFallback(primaryResult))
            {
                fallbackLevel2Activated = true;
                _logger.Log($"[EXCHANGE-FALLBACK-L2] from={_primaryProvider.ProviderName} to={_secondaryProvider.ProviderName} base={normalizedBase} target={normalizedTarget} date={requestedDate:yyyy-MM-dd}");

                var secondaryResult = ExecuteProvider(_secondaryProvider, normalizedBase, normalizedTarget, requestedDate);
                if (secondaryResult.Success)
                {
                    var normalizedResult = BuildSuccess(secondaryResult.Rate, secondaryResult.Date, _secondaryProvider.ProviderName, true, false);
                    SetInCache(cacheKey, normalizedResult);
                    return normalizedResult;
                }

                if (CanFallback(secondaryResult))
                {
                    fallbackLevel3Activated = true;
                    _logger.Log($"[EXCHANGE-FALLBACK-L3] from={_secondaryProvider.ProviderName} to={_tertiaryProvider.ProviderName} base={normalizedBase} target={normalizedTarget} date={requestedDate:yyyy-MM-dd}");

                    var tertiaryResult = ExecuteProvider(_tertiaryProvider, normalizedBase, normalizedTarget, requestedDate);
                    if (tertiaryResult.Success)
                    {
                        var normalizedResult = BuildSuccess(tertiaryResult.Rate, tertiaryResult.Date, _tertiaryProvider.ProviderName, true, true);
                        SetInCache(cacheKey, normalizedResult);
                        return normalizedResult;
                    }
                }
            }

            _logger.Log(
                $"[EXCHANGE-FAIL] base={normalizedBase} target={normalizedTarget} date={requestedDate:yyyy-MM-dd} l2={(fallbackLevel2Activated ? 1 : 0)} l3={(fallbackLevel3Activated ? 1 : 0)} error={ExchangeRateProviderErrorCodes.RateUnavailable}",
                AxaptaSessionManager.LogLevel.Warning);

            var finalProvider = fallbackLevel3Activated
                ? _tertiaryProvider.ProviderName
                : (fallbackLevel2Activated ? _secondaryProvider.ProviderName : _primaryProvider.ProviderName);

            return BuildFailure(
                ExchangeRateProviderErrorCodes.RateUnavailable,
                requestedDate,
                finalProvider,
                fallbackLevel2Activated || fallbackLevel3Activated,
                fallbackLevel3Activated);
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

                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date, provider.ProviderName, false, false);
            }
            catch (Exception ex)
            {
                _logger.Log($"[EXCHANGE-PROVIDER-ERROR] provider={provider.ProviderName} base={baseCurrency} target={targetCurrency} date={date:yyyy-MM-dd} error={ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return BuildFailure(ExchangeRateProviderErrorCodes.ProviderError, date, provider.ProviderName, false, false);
            }
        }

        private static bool CanFallback(ExchangeRateResult result)
        {
            if (result == null || result.Success)
                return false;

            return string.Equals(result.ErrorCode, ExchangeRateProviderErrorCodes.CurrencyNotFound, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(result.ErrorCode, ExchangeRateProviderErrorCodes.ProviderError, StringComparison.OrdinalIgnoreCase);
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

        private static ExchangeRateResult BuildSuccess(
            decimal rate,
            DateTime date,
            string providerSource,
            bool fallbackLevel2Activated,
            bool fallbackLevel3Activated)
        {
            return new ExchangeRateResult
            {
                Success = true,
                Rate = rate,
                Source = providerSource,
                ErrorCode = null,
                Date = date.Date,
                ProviderUsed = providerSource,
                FallbackActivated = fallbackLevel2Activated || fallbackLevel3Activated,
                FallbackLevel2Activated = fallbackLevel2Activated,
                FallbackLevel3Activated = fallbackLevel3Activated
            };
        }

        private static ExchangeRateResult BuildFailure(
            string errorCode,
            DateTime date,
            string providerUsed,
            bool fallbackLevel2Activated,
            bool fallbackLevel3Activated)
        {
            return new ExchangeRateResult
            {
                Success = false,
                Rate = 0m,
                Source = providerUsed,
                ErrorCode = errorCode,
                Date = date.Date,
                ProviderUsed = providerUsed,
                FallbackActivated = fallbackLevel2Activated || fallbackLevel3Activated,
                FallbackLevel2Activated = fallbackLevel2Activated,
                FallbackLevel3Activated = fallbackLevel3Activated
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
                FallbackActivated = source.FallbackActivated,
                FallbackLevel2Activated = source.FallbackLevel2Activated,
                FallbackLevel3Activated = source.FallbackLevel3Activated
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
