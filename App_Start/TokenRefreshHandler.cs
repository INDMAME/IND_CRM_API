using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Helpers;
using Microsoft.IdentityModel.Tokens;
using System;
using System.Configuration;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
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
        private readonly TokenValidationParameters _tokenValidationParameters;

        public TokenRefreshHandler(IJwtService jwtService, IAxaptaSessionManager sessionManager, TimeSpan refreshThreshold)
        {
            _jwtService = jwtService ?? throw new ArgumentNullException(nameof(jwtService));
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _refreshThreshold = refreshThreshold;
            _tokenValidationParameters = BuildTokenValidationParameters();
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string refreshedToken = null;
            DateTime? refreshedExpiration = null;
            try
            {
                var auth = request.Headers.Authorization;
                var oldToken = (auth != null && string.Equals(auth.Scheme, "Bearer", StringComparison.OrdinalIgnoreCase))
                    ? auth.Parameter
                    : null;

                if (!string.IsNullOrWhiteSpace(oldToken))
                {
                    var handler = new JwtSecurityTokenHandler();
                    ClaimsPrincipal principal = null;
                    SecurityToken validatedToken = null;
                    try
                    {
                        principal = handler.ValidateToken(oldToken, _tokenValidationParameters, out validatedToken);
                    }
                    catch
                    {
                        principal = null;
                        validatedToken = null;
                    }

                    var jwt = validatedToken as JwtSecurityToken;
                    if (jwt != null && principal?.Identity?.IsAuthenticated == true)
                    {
                        var validTo = jwt.ValidTo; // UTC
                        var now = DateTime.UtcNow;

                        // Sólo renovar si el token sigue siendo válido pero está cerca de expirar
                        if (validTo > now && (validTo - now) <= _refreshThreshold)
                        {
                            var username = principal.Identity.Name;
                            if (string.IsNullOrWhiteSpace(username))
                            {
                                var nameClaim = principal.Claims.FirstOrDefault(c => c.Type == ClaimTypes.Name || c.Type == "name");
                                username = nameClaim?.Value;
                            }

                            if (!string.IsNullOrWhiteSpace(username))
                            {
                                int expirationMinutes = 60;
                                var expirationSetting = AppSettingsHelper.GetSetting(
                                    "JwtSettings:ExpirationMinutes",
                                    "INDCRM_JWT_EXPIRATION_MINUTES");
                                if (int.TryParse(expirationSetting, out var cfg))
                                    expirationMinutes = cfg;

                                var tokenInfo = _jwtService.GenerateToken(username, expirationMinutes);

                                var refreshed = false;
                                try
                                {
                                    refreshed = _sessionManager.RefreshSessionToken(username, tokenInfo, oldToken);
                                }
                                catch
                                {
                                    refreshed = false;
                                }

                                // Solo emitir un token renovado si el token original fue validado y la sesión se pudo actualizar.
                                if (refreshed)
                                {
                                    refreshedToken = tokenInfo.Token;
                                    refreshedExpiration = tokenInfo.Expiration;
                                }
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

            // Si generamos un token fresco, añadir a la respuesta solo en respuestas exitosas.
            if (!string.IsNullOrWhiteSpace(refreshedToken) && (int)response.StatusCode < 400)
            {
                if (!response.Headers.Contains("X-Refreshed-Token"))
                    response.Headers.Add("X-Refreshed-Token", refreshedToken);

                if (refreshedExpiration.HasValue)
                {
                    if (!response.Headers.Contains("X-Refreshed-Token-Expires"))
                        response.Headers.Add("X-Refreshed-Token-Expires", refreshedExpiration.Value.ToString("o"));
                }

                // Expose headers for CORS clients
                if (!response.Headers.Contains("Access-Control-Expose-Headers"))
                    response.Headers.Add("Access-Control-Expose-Headers", "X-Refreshed-Token,X-Refreshed-Token-Expires");
            }

            return response;
        }

        private static TokenValidationParameters BuildTokenValidationParameters()
        {
            var issuer = AppSettingsHelper.GetSetting("JwtSettings:Issuer", "INDCRM_JWT_ISSUER");
            var audience = AppSettingsHelper.GetSetting("JwtSettings:Audience", "INDCRM_JWT_AUDIENCE");
            var secret = AppSettingsHelper.GetSetting("JwtSettings:SecretKey", "JWT_SECRET_KEY");

            if (string.IsNullOrWhiteSpace(secret))
                throw new Exception("Falta JwtSettings:SecretKey en App.config o variable de entorno JWT_SECRET_KEY");

            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
            return new TokenValidationParameters
            {
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = issuer,
                ValidAudience = audience,
                IssuerSigningKey = key,
                ClockSkew = TimeSpan.FromMinutes(3)
            };
        }
    }
}
