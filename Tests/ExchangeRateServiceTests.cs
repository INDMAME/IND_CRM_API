using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.Runtime.Caching;
using System.Threading.Tasks;

namespace IND_CRM_API.Tests
{
    [TestClass]
    public class ExchangeRateServiceTests
    {
        [TestMethod]
        public void EcbSuccess_DoesNotUseFallback()
        {
            using (var cache = new MemoryCache(Guid.NewGuid().ToString("N")))
            {
                var ecb = new FakeRawProvider("ECB", (b, t, d) => BuildSuccess("ECB", 1.21m, d));
                var host = new FakeRawProvider("EXR_HOST", (b, t, d) => BuildSuccess("EXR_HOST", 900m, d));
                var service = BuildService(ecb, host, cache);

                var result = service.GetRate("EUR", "USD", new DateTime(2026, 2, 26));

                Assert.IsNotNull(result);
                Assert.IsTrue(result.Success);
                Assert.AreEqual(1.21m, result.Rate);
                Assert.AreEqual("ECB", result.Source);
                Assert.AreEqual("ECB", result.ProviderUsed);
                Assert.IsFalse(result.FallbackActivated);
                Assert.AreEqual(1, ecb.CallCount);
                Assert.AreEqual(0, host.CallCount);
            }
        }

        [TestMethod]
        public void CurrencyNotFoundInEcb_UsesFallback()
        {
            using (var cache = new MemoryCache(Guid.NewGuid().ToString("N")))
            {
                var ecb = new FakeRawProvider("ECB", (b, t, d) => BuildFailure("ECB", ExchangeRateProviderErrorCodes.CurrencyNotFound, d));
                var host = new FakeRawProvider("EXR_HOST", (b, t, d) => BuildSuccess("EXR_HOST", 945.33m, d));
                var service = BuildService(ecb, host, cache);

                var result = service.GetRate("EUR", "CLP", new DateTime(2026, 2, 26));

                Assert.IsNotNull(result);
                Assert.IsTrue(result.Success);
                Assert.AreEqual(945.33m, result.Rate);
                Assert.AreEqual("ECB", result.Source);
                Assert.AreEqual("EXR_HOST", result.ProviderUsed);
                Assert.IsTrue(result.FallbackActivated);
                Assert.AreEqual(1, ecb.CallCount);
                Assert.AreEqual(1, host.CallCount);
            }
        }

        [TestMethod]
        public void EcbTimeout_UsesFallback()
        {
            using (var cache = new MemoryCache(Guid.NewGuid().ToString("N")))
            {
                var ecb = new FakeRawProvider("ECB", (b, t, d) => throw new TaskCanceledException("timeout"));
                var host = new FakeRawProvider("EXR_HOST", (b, t, d) => BuildSuccess("EXR_HOST", 943.11m, d));
                var service = BuildService(ecb, host, cache);

                var result = service.GetRate("EUR", "CLP", new DateTime(2026, 2, 26));

                Assert.IsNotNull(result);
                Assert.IsTrue(result.Success);
                Assert.AreEqual("EXR_HOST", result.ProviderUsed);
                Assert.IsTrue(result.FallbackActivated);
                Assert.AreEqual(1, ecb.CallCount);
                Assert.AreEqual(1, host.CallCount);
            }
        }

        [TestMethod]
        public void BothProvidersFail_ReturnsRateUnavailable()
        {
            using (var cache = new MemoryCache(Guid.NewGuid().ToString("N")))
            {
                var ecb = new FakeRawProvider("ECB", (b, t, d) => BuildFailure("ECB", ExchangeRateProviderErrorCodes.CurrencyNotFound, d));
                var host = new FakeRawProvider("EXR_HOST", (b, t, d) => BuildFailure("EXR_HOST", ExchangeRateProviderErrorCodes.ProviderError, d));
                var service = BuildService(ecb, host, cache);

                var result = service.GetRate("EUR", "CLP", new DateTime(2026, 2, 26));

                Assert.IsNotNull(result);
                Assert.IsFalse(result.Success);
                Assert.AreEqual(ExchangeRateProviderErrorCodes.RateUnavailable, result.ErrorCode);
                Assert.IsTrue(result.FallbackActivated);
                Assert.AreEqual(1, ecb.CallCount);
                Assert.AreEqual(1, host.CallCount);
            }
        }

        [TestMethod]
        public void CacheHit_DoesNotInvokeProvidersOnSecondCall()
        {
            using (var cache = new MemoryCache(Guid.NewGuid().ToString("N")))
            {
                var ecb = new FakeRawProvider("ECB", (b, t, d) => BuildSuccess("ECB", 1.19m, d));
                var host = new FakeRawProvider("EXR_HOST", (b, t, d) => BuildSuccess("EXR_HOST", 945m, d));
                var service = BuildService(ecb, host, cache);

                var first = service.GetRate("EUR", "USD", new DateTime(2026, 2, 26));
                var second = service.GetRate("EUR", "USD", new DateTime(2026, 2, 26));

                Assert.IsTrue(first.Success);
                Assert.IsTrue(second.Success);
                Assert.AreEqual(1.19m, second.Rate);
                Assert.AreEqual("CACHE", second.ProviderUsed);
                Assert.AreEqual(1, ecb.CallCount);
                Assert.AreEqual(0, host.CallCount);
            }
        }

        private static ExchangeRateService BuildService(FakeRawProvider ecb, FakeRawProvider host, ObjectCache cache)
        {
            return new ExchangeRateService(ecb, host, new FakeLogger(), cache);
        }

        private static ExchangeRateResult BuildSuccess(string providerName, decimal rate, DateTime date)
        {
            return new ExchangeRateResult
            {
                Success = true,
                Rate = rate,
                Source = providerName,
                ErrorCode = null,
                Date = date.Date,
                ProviderUsed = providerName,
                FallbackActivated = false
            };
        }

        private static ExchangeRateResult BuildFailure(string providerName, string errorCode, DateTime date)
        {
            return new ExchangeRateResult
            {
                Success = false,
                Rate = 0m,
                Source = providerName,
                ErrorCode = errorCode,
                Date = date.Date,
                ProviderUsed = providerName,
                FallbackActivated = false
            };
        }

        private sealed class FakeRawProvider : IRawExchangeRateProvider
        {
            private readonly Func<string, string, DateTime, ExchangeRateResult> _handler;

            public FakeRawProvider(string providerName, Func<string, string, DateTime, ExchangeRateResult> handler)
            {
                ProviderName = providerName;
                _handler = handler;
            }

            public int CallCount { get; private set; }

            public string ProviderName { get; }

            public ExchangeRateResult GetRate(string baseCurrency, string targetCurrency, DateTime date)
            {
                CallCount++;
                return _handler(baseCurrency, targetCurrency, date);
            }
        }

        private sealed class FakeLogger : IAxLogger
        {
            public void Log(string message, AxaptaSessionManager.LogLevel level = AxaptaSessionManager.LogLevel.Info)
            {
                // Logger fake intencionalmente vacio para pruebas unitarias.
            }
        }
    }
}
