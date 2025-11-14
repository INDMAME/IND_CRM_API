using System;
using System.Configuration;
using System.Diagnostics;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IND_CRM_APIs.Services
{
    /// <summary>
    /// Servicio responsable de generar tokens JWT (JSON Web Token)
    /// utilizados para autenticar las solicitudes entre el cliente y la API.
    /// 
    /// Los parámetros de configuración se leen desde App.config:
    /// <list type="bullet">
    /// <item><description><b>JwtSettings:SecretKey</b> → Clave secreta de firma (256 bits o superior).</description></item>
    /// <item><description><b>JwtSettings:Issuer</b> → Emisor del token (identificador del servicio).</description></item>
    /// <item><description><b>JwtSettings:Audience</b> → Destinatario del token (identificador del cliente).</description></item>
    /// <item><description><b>JwtSettings:ExpirationMinutes</b> → Tiempo de validez en minutos.</description></item>
    /// </list>
    /// </summary>
    public class JwtService
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
            // Leer configuración JWT desde App.config
            var secretKey = ConfigurationManager.AppSettings["JwtSettings:SecretKey"];
            var issuer = ConfigurationManager.AppSettings["JwtSettings:Issuer"];
            var audience = ConfigurationManager.AppSettings["JwtSettings:Audience"];

            if (string.IsNullOrEmpty(secretKey) || string.IsNullOrEmpty(issuer) || string.IsNullOrEmpty(audience))
                throw new Exception("Faltan valores JWT en el archivo de configuración.");

            int expirationMinutes = overrideMinutes ?? GetConfiguredExpirationMinutes();

            // Clave simétrica de firma
            var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
            var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
            // Fecha de expiración absoluta (UTC)
            var expiration = DateTime.UtcNow.AddMinutes(expirationMinutes);

            // Crear token con claims básicos
            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: new[]
                {
                    new Claim(ClaimTypes.Name, username),
                    new Claim("iss", issuer),
                    new Claim("aud", audience)
                },
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
        /// Si el valor no existe o no es válido, usa un valor predeterminado de 15 minutos.
        /// </summary>
        /// <returns>Duración del token en minutos.</returns>
        private int GetConfiguredExpirationMinutes()
        {
            string minutes = ConfigurationManager.AppSettings["JwtSettings:ExpirationMinutes"];
            return int.TryParse(minutes, out int result) ? result : 15;
        }
    }
}
