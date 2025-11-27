using IND_CRM_API.Services.Interfaces;
using System;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// DelegatingHandler que comprueba en cada petición autenticada si el token
    /// está próximo a expirar y, en ese caso, genera un token renovado y lo
    /// incluye en la respuesta en la cabecera "X-Refreshed-Token".
    /// </summary>
    public class TokenRefreshHandler : DelegatingHandler
    {
        private readonly IJwtService _jwtService;
        private readonly IAxaptaSessionManager _sessionManager;
        private readonly TimeSpan _refreshThreshold;

        public TokenRefreshHandler(IJwtService jwtService, IAxaptaSessionManager sessionManager, TimeSpan refreshThreshold)
        {
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _refreshThreshold = refreshThreshold;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string oldToken = null;
            try
            {
                var auth = request.Headers.Authorization;
                if (auth != null && string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                    oldToken = auth.Parameter;

                if (!string.IsNullOrEmpty(oldToken))
                {
                    var handler = new JwtSecurityTokenHandler();
                    JwtSecurityToken jwt = null;
                    try
                    {
                        jwt = handler.ReadJwtToken(oldToken);
                    }
                    catch
                    {
                        jwt = null;
                    }

                    if (jwt != null)
                    {
                        var validTo = jwt.ValidTo; // UTC
                        var now = DateTime.UtcNow;

                        // Sólo renovar si el token sigue siendo válido pero está cerca de expirar
                        if (validTo > now && (validTo - now) <= _refreshThreshold)
                        {
                            // Obtener nombre de usuario del claim
                            var nameClaim = jwt.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "name");
                            var username = nameClaim?.Value;

                            if (!string.IsNullOrWhiteSpace(username))
                            {
                                int expirationMinutes = 60;
                                var expirationSetting = ConfigurationManager.AppSettings["JwtSettings:ExpirationMinutes"];
                                if (int.TryParse(expirationSetting, out var cfg))
                                    expirationMinutes = cfg;

                                var tokenInfo = _jwtService.GenerateToken(username, expirationMinutes);

                                try
                                {
                                    _sessionManager.RefreshSessionToken(username, tokenInfo, oldToken);
                                }
                                catch
                                {
                                    // Ignorar fallos de refresco de sesión; no queremos bloquear la petición
                                }

                                // Añadir el nuevo token a la cabecera de respuesta más adelante
                                // Guardamos en la propiedad del request para usarla después
                                request.Properties["RefreshedToken"] = tokenInfo.Token;
                                request.Properties["RefreshedTokenExpiration"] = tokenInfo.Expiration;
                            }
                        }
                    }
                }
            }
            catch
            {
                // No interrumpir la cadena de petición por errores al inspeccionar token
            }

            var response = await base.SendAsync(request, cancellationToken);

            // Si generamos un token fresco, añadir a la respuesta
            if (request.Properties.TryGetValue("RefreshedToken", out var refreshedObj) && refreshedObj is string refreshedToken)
            {
                if (!response.Headers.Contains("X-Refreshed-Token"))
                    response.Headers.Add("X-Refreshed-Token", refreshedToken);

                if (request.Properties.TryGetValue("RefreshedTokenExpiration", out var expObj) && expObj is DateTime exp)
                {
                    if (!response.Headers.Contains("X-Refreshed-Token-Expires"))
                        response.Headers.Add("X-Refreshed-Token-Expires", exp.ToString("o"));
                }

                // Expose headers for CORS clients
                if (!response.Headers.Contains("Access-Control-Expose-Headers"))
                    response.Headers.Add("Access-Control-Expose-Headers", "X-Refreshed-Token,X-Refreshed-Token-Expires");
            }

            return response;
        }
    }
}
