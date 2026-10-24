using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Servicio responsable de generar tokens JWT (JSON Web Token)
    /// utilizados para autenticar las solicitudes entre el cliente y la API.
    /// 
    /// Los parametros de configuracion se leen desde App.config o variables de entorno:
    /// <list type="bullet">
    /// <item><description><b>JwtSettings:SecretKey</b> → Clave secreta de firma (256 bits o superior).</description></item>
    /// <item><description><b>JwtSettings:Issuer</b> → Emisor del token (identificador del servicio).</description></item>
    /// <item><description><b>JwtSettings:Audience</b> → Destinatario del token (identificador del cliente).</description></item>
    /// <item><description><b>JwtSettings:ExpirationMinutes</b> → Tiempo de validez en minutos.</description></item>
    /// </list>
    /// </summary>
    public class JwtService : IJwtService
    {
        /// <summary>
        /// Estructura contenedora de información sobre el token JWT generado.
        /// Incluye el token en formato string y la fecha de expiración.
        /// </summary>
        public class JwtTokenInfo
        {
            /// Token JWT firmado digitalmente.
            public string Token { get; set; }

            /// Fecha exacta (UTC) en que el token expira.
            public DateTime Expiration { get; set; }
        }



        /// <summary>
        /// Genera un nuevo token JWT firmado usando el algoritmo HMAC SHA256.
        /// 
        /// Este token contiene los claims estándar:
        /// <list type="bullet">
        /// <item><description><b>name</b>: nombre de usuario autenticado</description></item>
        /// <item><description><b>iss</b>: emisor (Issuer)</description></item>
        /// <item><description><b>aud</b>: destinatario (Audience)</description></item>
        /// </list>
        /// </summary>
        /// <param name="username">Nombre del usuario autenticado.</param>
        /// <param name="overrideMinutes">
        /// Tiempo opcional de expiración en minutos.
        /// Si no se especifica, se usa el valor configurado en App.config.
        /// </param>
        /// <returns>Objeto <see cref="JwtTokenInfo"/> con el token y su fecha de expiración.</returns>
        /// <exception cref="Exception">
        /// Se lanza si falta alguna clave de configuración o si la generación falla.
        /// </exception>
        public JwtTokenInfo GenerateToken(string username, int? overrideMinutes = null)
        {
            // API roles follow the configured identity so login, refresh and auto-renewal stay consistent.
            return GenerateToken(username, overrideMinutes, SyncWorkerIdentity.RolesFor(username));
        }

        /// <summary>
        /// Genera un token JWT con claims de rol de la API (ind:role) ademas de los claims basicos.
        /// </summary>
        /// <param name="username">Nombre del usuario autenticado.</param>
        /// <param name="overrideMinutes">Expiracion opcional en minutos.</param>
        /// <param name="roles">Roles de la API que se incluyen como claims ind:role (puede ser vacio).</param>
        public JwtTokenInfo GenerateToken(string username, int? overrideMinutes, IEnumerable<string> roles)
        {
            // Leer configuracion JWT desde App.config o variables de entorno
            var secretKey = AppSettingsHelper.GetSetting("JwtSettings:SecretKey", "JWT_SECRET_KEY");
            var issuer = AppSettingsHelper.GetSetting("JwtSettings:Issuer", "INDCRM_JWT_ISSUER");
            var audience = AppSettingsHelper.GetSetting("JwtSettings:Audience", "INDCRM_JWT_AUDIENCE");

            if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
                throw new Exception("Faltan valores JWT en App.config o variables de entorno.");

            int expirationMinutes = overrideMinutes ?? GetConfiguredExpirationMinutes();

            // Clave simétrica de firma
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            // Fecha de expiración absoluta (UTC)
            var expiration = DateTime.UtcNow.AddMinutes(expirationMinutes);

            // Crear token con claims básicos
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.Name, username),
                new Claim("iss", issuer),
                new Claim("aud", audience)
            };

            // One ind:role claim per API role (today only the sync worker has one).
            foreach (var role in (roles ?? Enumerable.Empty<string>()).Where(r => !string.IsNullOrWhiteSpace(r)))
                claims.Add(new Claim(SyncWorkerIdentity.RoleClaimType, role.Trim()));

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                expires: expiration,
                signingCredentials: creds
            );

            // Serializar token a formato string
            var tokenHandler = new JwtSecurityTokenHandler();
            string tokenString = tokenHandler.WriteToken(token);

            Debug.WriteLine($"[JWT] Token generado para {username}, expira en {expirationMinutes} minutos ({expiration}).");

            return new JwtTokenInfo
            {
                Token = tokenString,
                Expiration = expiration
            };
        }

        /// <summary>
        /// Obtiene el tiempo de expiración configurado en el archivo App.config.
        /// Si el valor no existe o no es válido, usa un valor predeterminado de 60 minutos.
        /// </summary>
        /// <returns>Duración del token en minutos.</returns>
        private int GetConfiguredExpirationMinutes()
        {
            return AppSettingsHelper.GetIntSetting("JwtSettings:ExpirationMinutes", 60, "INDCRM_JWT_EXPIRATION_MINUTES");
        }
    }
}
