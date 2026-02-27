using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Dependencies;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Registro manual de dependencias sin contenedor externo.
    /// </summary>
    public static class DependencyConfig
    {
        public static void Register(HttpConfiguration config)
        {
            var axLogger = new FileAxLogger();
            var jwtService = new JwtService();
            var sessionManager = new AxaptaSessionManager(axLogger);
            var speechService = new IND_OpenAiAudioTranscriptionService(axLogger);
            var moderationService = new IND_OpenAiModerationService(axLogger);
            var expenseDraftService = new IND_OpenAiExpenseTicketDraftService(axLogger);
            var expenseTicketBlobStorageService = new ExpenseTicketBlobStorageService(axLogger);
            var openAiRateLimitHandler = new IND_OpenAiRateLimitHandler(axLogger);
            var ecbExchangeRateProvider = new EcbExchangeRateProvider(axLogger);
            var frankfurterExchangeRateProvider = new FrankfurterExchangeRateProvider(axLogger);
            var openErApiExchangeRateProvider = new OpenErApiExchangeRateProvider(axLogger);
            var exchangeRateProvider = new ExchangeRateService(
                ecbExchangeRateProvider,
                frankfurterExchangeRateProvider,
                openErApiExchangeRateProvider,
                axLogger);

            // Compartir la misma instancia en todo el proceso.
            AxSession.Initialize(sessionManager);

            var services = new Dictionary<Type, object>
            {
                { typeof(IAxaptaSessionManager), sessionManager },
                { typeof(IJwtService), jwtService },
                { typeof(IAxLogger), axLogger },
                { typeof(IND_IAudioTranscriptionService), speechService },
                { typeof(IND_ITextModerationService), moderationService },
                { typeof(IND_IExpenseTicketDraftService), expenseDraftService },
                { typeof(IExpenseTicketBlobStorageService), expenseTicketBlobStorageService },
                { typeof(IExchangeRateProvider), exchangeRateProvider }
            };

            // Per-request Axapta session scope
            var scopeHandler = new IND_AxSessionScopeHandler(sessionManager, axLogger);
            config.MessageHandlers.Add(scopeHandler);

            // OpenAI endpoint protection: per-user throttling and concurrency cap.
            config.MessageHandlers.Add(openAiRateLimitHandler);

            // Register message handler to refresh tokens automatically
            var refreshThresholdMinutes = 5; // renovar cuando queden 5 minutos o menos
            if (int.TryParse(System.Configuration.ConfigurationManager.AppSettings["JwtSettings:RefreshThresholdMinutes"], out var cfg))
                refreshThresholdMinutes = cfg;

            var tokenHandler = new TokenRefreshHandler(jwtService, sessionManager, TimeSpan.FromMinutes(refreshThresholdMinutes));
            config.MessageHandlers.Add(tokenHandler);

            config.DependencyResolver = new SimpleResolver(services);
        }

        private class SimpleResolver : IDependencyResolver
        {
            private readonly IDictionary<Type, object> _instances;

            public SimpleResolver(IDictionary<Type, object> instances)
            {
                _instances = instances;
            }

            public IDependencyScope BeginScope() => this;

            public object GetService(Type serviceType)
            {
                if (_instances.TryGetValue(serviceType, out var instance))
                    return instance;

                if (typeof(ApiController).IsAssignableFrom(serviceType))
                {
                    var ctors = serviceType.GetConstructors()
                        .OrderByDescending(c => c.GetParameters().Length)
                        .ToList();

                    foreach (var ctor in ctors)
                    {
                        var parameters = ctor.GetParameters();
                        if (parameters.Length == 0)
                            return Activator.CreateInstance(serviceType);

                        var args = new object[parameters.Length];
                        var canResolve = true;

                        for (int i = 0; i < parameters.Length; i++)
                        {
                            if (_instances.TryGetValue(parameters[i].ParameterType, out var dep) && dep != null)
                            {
                                args[i] = dep;
                                continue;
                            }

                            canResolve = false;
                            break;
                        }

                        if (canResolve)
                            return Activator.CreateInstance(serviceType, args);
                    }

                    return null;
                }

                return null;
            }

            public IEnumerable<object> GetServices(Type serviceType)
            {
                if (_instances.TryGetValue(serviceType, out var instance))
                    return new[] { instance };
                return Array.Empty<object>();
            }

            public void Dispose()
            {
                // Singletons; nothing to dispose here.
            }
        }
    }
}
