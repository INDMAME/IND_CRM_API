using System;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using IND_CRM_API.Helpers;
using Microsoft.IdentityModel.Tokens;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Issues and validates the bearer token of the api/crm/webstore/* routes. It is independent of
    /// JwtService: own issuer, own audience, a channel claim and a signing key derived from the API
    /// secret, so a legacy CRM token is never accepted here and this token is never accepted by the
    /// global JWT middleware. Only the AX user named by AX_WEBSTORE_USER (machine variable first, then
    /// the App.config key CrmWebStore:User) gets one. Tokens are tied to the running API process,
    /// because the AX credentials validated at login live only in its memory.
    /// </summary>
    public sealed class CrmWebStoreTokenService
    {
        /// <summary>Machine environment variable with the only AX user allowed on the web store routes.</summary>
        public const string UserVariableName = "AX_WEBSTORE_USER";

        /// <summary>App.config key read when the machine variable is not defined.</summary>
        public const string UserSettingKey = "CrmWebStore:User";

        /// <summary>Claim with the id of the API process that issued the token.</summary>
        public const string InstanceClaimType = "ind:wsi";

        // One id per API process: a restart drops the cached AX credentials, so older tokens must log in again.
        private static readonly string ProcessInstanceId = Guid.NewGuid().ToString("N");

        /// <summary>Issuer of the web store tokens.</summary>
        public const string Issuer = "IND_CRM_API/webstore";

        /// <summary>Audience of the web store tokens (never the legacy CRM audience).</summary>
        public const string Audience = "IND_CRM_WEBSTORE";

        /// <summary>Claim that marks a token as issued for the web store channel.</summary>
        public const string ChannelClaimType = "ind:channel";

        /// <summary>Value of the channel claim.</summary>
        public const string ChannelValue = "webstore";

        private const string SigningKeyLabel = "IND_CRM_API.webstore.signing-key.v1";
        private const int MaxTokenLength = 4096;
        private const int MinLifetimeMinutes = 5;
        private const int MaxLifetimeMinutes = 1440;
        private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(1);

        private readonly Func<string> _secretProvider;
        private readonly Func<string> _userProvider;
        private readonly int _lifetimeMinutes;

        /// <summary>
        /// Production constructor: API secret and token lifetime from the existing JWT settings,
        /// user from AX_WEBSTORE_USER (machine variable before App.config, read on every call).
        /// </summary>
        public CrmWebStoreTokenService()
            : this(
                () => AppSettingsHelper.GetSetting("JwtSettings:SecretKey", "JWT_SECRET_KEY"),
                () => AppSettingsHelper.GetSetting(UserSettingKey, UserVariableName),
                AppSettingsHelper.GetIntSetting("JwtSettings:ExpirationMinutes", 60, "INDCRM_JWT_EXPIRATION_MINUTES"))
        {
        }

        /// <summary>
        /// Explicit constructor used by tests and by the production constructor.
        /// </summary>
        public CrmWebStoreTokenService(Func<string> secretProvider, Func<string> userProvider, int lifetimeMinutes)
        {
            _secretProvider = secretProvider ?? throw new ArgumentNullException(nameof(secretProvider));
            _userProvider = userProvider ?? throw new ArgumentNullException(nameof(userProvider));
            _lifetimeMinutes = Math.Min(MaxLifetimeMinutes, Math.Max(MinLifetimeMinutes, lifetimeMinutes));
        }

        /// <summary>Returns the configured web store user, or null when the routes are disabled.</summary>
        public string ConfiguredUser
        {
            get
            {
                var value = _userProvider();
                return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            }
        }

        /// <summary>True when AX_WEBSTORE_USER is defined.</summary>
        public bool IsEnabled => ConfiguredUser != null;

        /// <summary>Checks (case-insensitive, like AX user ids, in constant time) whether the user is the configured one.</summary>
        public bool IsConfiguredUser(string username)
        {
            var configured = ConfiguredUser;
            return configured != null &&
                   !string.IsNullOrWhiteSpace(username) &&
                   SameUser(configured, username.Trim());
        }

        /// <summary>
        /// Case-insensitive comparison whose duration does not depend on where the texts differ.
        /// </summary>
        public static bool SameUser(string left, string right)
        {
            if (left == null || right == null)
                return false;

            var a = Encoding.UTF8.GetBytes(left.ToUpperInvariant());
            var b = Encoding.UTF8.GetBytes(right.ToUpperInvariant());
            var difference = a.Length ^ b.Length;
            for (var i = 0; i < Math.Max(a.Length, b.Length); i++)
                difference |= (i < a.Length ? a[i] : 0) ^ (i < b.Length ? b[i] : 0);

            return difference == 0;
        }

        /// <summary>
        /// Issues a token for the configured user. The caller must have validated the AX credentials first.
        /// </summary>
        public CrmWebStoreIssuedToken Issue(string username, DateTime? issuedAtUtc = null)
        {
            if (!IsConfiguredUser(username))
                throw new InvalidOperationException("Only the configured web store user can receive a web store token.");

            var issuedAt = issuedAtUtc ?? DateTime.UtcNow;
            var expires = issuedAt.AddMinutes(_lifetimeMinutes);
            var subject = ConfiguredUser;
            var claims = new[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim(ChannelClaimType, ChannelValue),
                new Claim(InstanceClaimType, ProcessInstanceId)
            };

            var token = new JwtSecurityToken(
                issuer: Issuer,
                audience: Audience,
                claims: claims,
                notBefore: issuedAt,
                expires: expires,
                signingCredentials: new SigningCredentials(BuildSigningKey(), SecurityAlgorithms.HmacSha256));

            return new CrmWebStoreIssuedToken(new JwtSecurityTokenHandler().WriteToken(token), expires, subject);
        }

        /// <summary>
        /// Validates signature, issuer, audience, lifetime, channel and subject of a bearer token.
        /// </summary>
        public CrmWebStoreTokenCheck Validate(string token)
        {
            var configured = ConfiguredUser;
            if (configured == null)
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Disabled);

            if (string.IsNullOrWhiteSpace(token))
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Missing);

            if (token.Length > MaxTokenLength)
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Invalid);

            JwtSecurityToken jwt;
            try
            {
                new JwtSecurityTokenHandler().ValidateToken(token.Trim(), BuildValidationParameters(), out var validated);
                jwt = validated as JwtSecurityToken;
            }
            catch (SecurityTokenExpiredException)
            {
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Expired);
            }
            catch (Exception)
            {
                // Bad signature, issuer, audience, algorithm or format: never a valid web store token.
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Invalid);
            }

            if (jwt == null || !string.Equals(jwt.Header.Alg, SecurityAlgorithms.HmacSha256, StringComparison.Ordinal))
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Invalid);

            var channel = jwt.Claims.FirstOrDefault(c => c.Type == ChannelClaimType)?.Value;
            if (!string.Equals(channel, ChannelValue, StringComparison.Ordinal))
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Invalid);

            // Issued before the last API restart: the AX credentials are gone, a new login is needed.
            var instance = jwt.Claims.FirstOrDefault(c => c.Type == InstanceClaimType)?.Value;
            if (!string.Equals(instance, ProcessInstanceId, StringComparison.Ordinal))
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Expired);

            if (!SameUser(jwt.Subject?.Trim(), configured))
                return CrmWebStoreTokenCheck.Of(CrmWebStoreTokenStatus.Forbidden);

            return new CrmWebStoreTokenCheck(CrmWebStoreTokenStatus.Valid, configured);
        }

        // Builds the validation rules: everything is checked and the clock skew stays small.
        private TokenValidationParameters BuildValidationParameters()
        {
            return new TokenValidationParameters
            {
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Issuer,
                ValidAudience = Audience,
                IssuerSigningKey = BuildSigningKey(),
                ClockSkew = ClockSkew
            };
        }

        // Derives a dedicated key from the API secret so the legacy and web store tokens never share a key.
        // Internal so the identity test can forge tokens with a wrong issuer or audience.
        internal SymmetricSecurityKey BuildSigningKey()
        {
            var secret = _secretProvider();
            if (string.IsNullOrWhiteSpace(secret))
                throw new InvalidOperationException("JWT secret is not configured.");

            using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret)))
                return new SymmetricSecurityKey(hmac.ComputeHash(Encoding.UTF8.GetBytes(SigningKeyLabel)));
        }
    }

    /// <summary>
    /// Outcome of a web store token validation.
    /// </summary>
    public enum CrmWebStoreTokenStatus
    {
        Valid,
        Missing,
        Invalid,
        Expired,
        Forbidden,
        Disabled
    }

    /// <summary>
    /// Validation result: status and, when valid, the configured AX user.
    /// </summary>
    public sealed class CrmWebStoreTokenCheck
    {
        /// <summary>Creates a result with a status and the user (only for valid tokens).</summary>
        public CrmWebStoreTokenCheck(CrmWebStoreTokenStatus status, string username)
        {
            Status = status;
            Username = status == CrmWebStoreTokenStatus.Valid ? username : null;
        }

        public CrmWebStoreTokenStatus Status { get; }

        public string Username { get; }

        /// <summary>Shortcut for results without a user.</summary>
        public static CrmWebStoreTokenCheck Of(CrmWebStoreTokenStatus status)
        {
            return new CrmWebStoreTokenCheck(status, null);
        }
    }

    /// <summary>
    /// Token issued for the web store user with its UTC expiration.
    /// </summary>
    public sealed class CrmWebStoreIssuedToken
    {
        /// <summary>Wraps the serialized token, its expiration and its subject.</summary>
        public CrmWebStoreIssuedToken(string token, DateTime expiresUtc, string username)
        {
            Token = token;
            ExpiresUtc = expiresUtc;
            Username = username;
        }

        public string Token { get; }

        public DateTime ExpiresUtc { get; }

        public string Username { get; }
    }
}
