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
            var services = new Dictionary<Type, object>
            {
                { typeof(IAxaptaSessionManager), new AxaptaSessionManager() },
                { typeof(IJwtService), new JwtService() }
            };

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
                    var ctor = serviceType.GetConstructors()
                        .OrderByDescending(c => c.GetParameters().Length)
                        .FirstOrDefault();
                    if (ctor == null) return null;

                    var args = ctor.GetParameters()
                        .Select(p => _instances.TryGetValue(p.ParameterType, out var dep) ? dep : null)
                        .ToArray();

                    if (args.Any(a => a == null)) return null;

                    return Activator.CreateInstance(serviceType, args);
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
