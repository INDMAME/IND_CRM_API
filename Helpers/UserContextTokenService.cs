using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Creates and validates signed context tokens for real Entra users.
    /// </summary>
    public static class UserContextTokenService
    {
        private const string TokenUseClaim = "ind_token_use";
        private const string TokenUseValue = "context_snapshot";
        private const string TenantIdClaim = "ind_tenant_id";
        private const string EntraOidClaim = "ind_entra_oid";
        private const string AxUserIdClaim = "ind_ax_user_id";
        private const string AppCodeClaim = "ind_app_code";
        private const string ContextVersionClaim = "ind_context_version";
        private const string PermissionsRevisionClaim = "ind_permissions_revision";
        private const string DefaultCompanyClaim = "ind_default_company";
        private const string SnapshotKeyClaim = "ind_snapshot_key";
        private const string CompaniesClaim = "ind_companies";

        public sealed class ValidationResult
        {
            public bool IsValid { get; set; }
            public bool IsMissing { get; set; }
            public bool IsExpired { get; set; }
            public bool IsStale { get; set; }
            public string Reason { get; set; }
            public UserCompanyAccessCache.Snapshot Snapshot { get; set; }
        }

        /// <summary>
        /// Signs the current authorization snapshot into a compact JWT.
        /// </summary>
        public static string CreateToken(UserCompanyAccessCache.Snapshot snapshot)
        {
            if (snapshot == null || !snapshot.Exists)
                throw new InvalidOperationException("Context snapshot is required to create a context token.");

            var creds = BuildSigningCredentials();
            var expiresUtc = snapshot.ExpiresUtc ?? DateTime.UtcNow.AddMinutes(UserCompanyAccessCache.GetConfiguredSnapshotMinutes());
            var issuedUtc = snapshot.IssuedUtc ?? DateTime.UtcNow;
            var issuer = GetContextIssuer();
            var audience = GetContextAudience();

            var claims = new List<Claim>
            {
                new Claim(TokenUseClaim, TokenUseValue),
                new Claim(TenantIdClaim, snapshot.TenantId ?? string.Empty),
                new Claim(EntraOidClaim, snapshot.EntraOid ?? string.Empty),
                new Claim(AxUserIdClaim, snapshot.AxUserId ?? string.Empty),
                new Claim(AppCodeClaim, snapshot.AppCode ?? string.Empty),
                new Claim(ContextVersionClaim, snapshot.ContextVersion.ToString()),
                new Claim(PermissionsRevisionClaim, snapshot.PermissionsRevision ?? string.Empty),
                new Claim(DefaultCompanyClaim, snapshot.DefaultCompany ?? string.Empty),
                new Claim(SnapshotKeyClaim, snapshot.SnapshotKey ?? string.Empty),
                new Claim(CompaniesClaim, string.Join("|", snapshot.Companies ?? Array.Empty<string>()))
            };

            var token = new JwtSecurityToken(
                issuer: issuer,
                audience: audience,
                claims: claims,
                notBefore: issuedUtc,
                expires: expiresUtc,
                signingCredentials: creds);

            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        /// <summary>
        /// Validates a context token against the expected real-user request metadata.
        /// </summary>
        public static ValidationResult Validate(
            string token,
            string expectedTenantId,
            string expectedEntraOid,
            long expectedContextVersion,
            string expectedPermissionsRevision,
            string requestedCompany,
            UserCompanyAccessCache.Snapshot latestSnapshot)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return new ValidationResult
                {
                    IsMissing = true,
                    Reason = "context-token-missing",
                    Snapshot = CreateSnapshotFromLatest(latestSnapshot)
                };
            }

            try
            {
                var handler = new JwtSecurityTokenHandler();
                var principal = handler.ValidateToken(token, BuildTokenValidationParameters(), out _);
                var snapshot = CreateSnapshotFromPrincipal(principal);
                if (snapshot == null || !snapshot.Exists)
                {
                    return new ValidationResult
                    {
                        IsStale = true,
                        Reason = "context-token-invalid-payload",
                        Snapshot = CreateSnapshotFromLatest(latestSnapshot)
                    };
                }

                if (!string.Equals(GetClaimValue(principal, TokenUseClaim), TokenUseValue, StringComparison.Ordinal))
                {
                    return new ValidationResult
                    {
                        IsStale = true,
                        Reason = "context-token-invalid-use",
                        Snapshot = snapshot
                    };
                }

                if (!string.Equals(Normalize(expectedTenantId), Normalize(snapshot.TenantId), StringComparison.Ordinal))
                {
                    return new ValidationResult
                    {
                        IsStale = true,
                        Reason = "context-token-tenant-mismatch",
                        Snapshot = snapshot
                    };
                }

                if (!string.Equals(Normalize(expectedEntraOid), Normalize(snapshot.EntraOid), StringComparison.Ordinal))
                {
                    return new ValidationResult
                    {
                        IsStale = true,
                        Reason = "context-token-entraoid-mismatch",
                        Snapshot = snapshot
                    };
                }

                if (expectedContextVersion <= 0 || snapshot.ContextVersion != expectedContextVersion)
                {
                    return new ValidationResult
                    {
                        IsStale = true,
                        Reason = "context-token-version-mismatch",
                        Snapshot = snapshot
                    };
                }

                var normalizedExpectedRevision = NormalizeRevision(expectedPermissionsRevision);
                if (string.IsNullOrWhiteSpace(normalizedExpectedRevision) ||
                    !string.Equals(normalizedExpectedRevision, NormalizeRevision(snapshot.PermissionsRevision), StringComparison.Ordinal))
                {
                    return new ValidationResult
                    {
                        IsStale = true,
                        Reason = "context-token-permissions-revision-mismatch",
                        Snapshot = snapshot
                    };
                }

                if (snapshot.Expired)
                {
                    return new ValidationResult
                    {
                        IsExpired = true,
                        Reason = "context-token-expired",
                        Snapshot = snapshot
                    };
                }

                if (latestSnapshot != null &&
                    latestSnapshot.Exists &&
                    !string.IsNullOrWhiteSpace(latestSnapshot.PermissionsRevision) &&
                    !string.Equals(
                        NormalizeRevision(latestSnapshot.PermissionsRevision),
                        NormalizeRevision(snapshot.PermissionsRevision),
                        StringComparison.Ordinal))
                {
                    return new ValidationResult
                    {
                        IsStale = true,
                        Reason = "context-token-revision-outdated",
                        Snapshot = latestSnapshot
                    };
                }

                var normalizedCompany = (requestedCompany ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(normalizedCompany) || snapshot.Companies == null || !snapshot.Companies.Contains(normalizedCompany, StringComparer.OrdinalIgnoreCase))
                {
                    return new ValidationResult
                    {
                        IsValid = false,
                        Reason = "context-company-not-allowed",
                        Snapshot = snapshot
                    };
                }

                return new ValidationResult
                {
                    IsValid = true,
                    Reason = "context-token-valid",
                    Snapshot = snapshot
                };
            }
            catch (SecurityTokenExpiredException)
            {
                return new ValidationResult
                {
                    IsExpired = true,
                    Reason = "context-token-expired",
                    Snapshot = CreateSnapshotFromLatest(latestSnapshot)
                };
            }
            catch (Exception)
            {
                return new ValidationResult
                {
                    IsStale = true,
                    Reason = "context-token-invalid",
                    Snapshot = CreateSnapshotFromLatest(latestSnapshot)
                };
            }
        }

        private static UserCompanyAccessCache.Snapshot CreateSnapshotFromPrincipal(ClaimsPrincipal principal)
        {
            if (principal == null)
                return null;

            var contextVersion = TryParseLong(GetClaimValue(principal, ContextVersionClaim));
            var expiresUtc = TryReadUtcClaim(principal, JwtRegisteredClaimNames.Exp);
            var issuedUtc = TryReadUtcClaim(principal, JwtRegisteredClaimNames.Nbf) ?? TryReadUtcClaim(principal, JwtRegisteredClaimNames.Iat);
            var companies = SplitCompanies(GetClaimValue(principal, CompaniesClaim));

            return new UserCompanyAccessCache.Snapshot
            {
                Exists = true,
                Expired = expiresUtc.HasValue && expiresUtc.Value <= DateTime.UtcNow,
                SnapshotKey = GetClaimValue(principal, SnapshotKeyClaim) ?? string.Empty,
                TenantId = Normalize(GetClaimValue(principal, TenantIdClaim)),
                EntraOid = Normalize(GetClaimValue(principal, EntraOidClaim)),
                AxUserId = (GetClaimValue(principal, AxUserIdClaim) ?? string.Empty).Trim(),
                DefaultCompany = (GetClaimValue(principal, DefaultCompanyClaim) ?? string.Empty).Trim(),
                AppCode = (GetClaimValue(principal, AppCodeClaim) ?? string.Empty).Trim(),
                ContextVersion = contextVersion,
                PermissionsRevision = NormalizeRevision(GetClaimValue(principal, PermissionsRevisionClaim)),
                IssuedUtc = issuedUtc,
                ExpiresUtc = expiresUtc,
                Companies = companies
            };
        }

        private static UserCompanyAccessCache.Snapshot CreateSnapshotFromLatest(UserCompanyAccessCache.Snapshot latestSnapshot)
        {
            return latestSnapshot ?? new UserCompanyAccessCache.Snapshot
            {
                Exists = false,
                Expired = false,
                SnapshotKey = string.Empty,
                TenantId = string.Empty,
                EntraOid = string.Empty,
                AxUserId = string.Empty,
                DefaultCompany = string.Empty,
                AppCode = string.Empty,
                ContextVersion = 0,
                PermissionsRevision = string.Empty,
                IssuedUtc = null,
                ExpiresUtc = null,
                Companies = Array.Empty<string>()
            };
        }

        private static string GetContextIssuer()
        {
            var issuer = AppSettingsHelper.GetSetting("ContextTokenSettings:Issuer", "INDCRM_CONTEXT_TOKEN_ISSUER");
            if (!string.IsNullOrWhiteSpace(issuer))
                return issuer.Trim();

            var jwtIssuer = AppSettingsHelper.GetSetting("JwtSettings:Issuer", "INDCRM_JWT_ISSUER") ?? string.Empty;
            return string.IsNullOrWhiteSpace(jwtIssuer) ? "IND_CRM_CONTEXT" : jwtIssuer.Trim() + ":context";
        }

        private static string GetContextAudience()
        {
            var audience = AppSettingsHelper.GetSetting("ContextTokenSettings:Audience", "INDCRM_CONTEXT_TOKEN_AUDIENCE");
            if (!string.IsNullOrWhiteSpace(audience))
                return audience.Trim();

            var jwtAudience = AppSettingsHelper.GetSetting("JwtSettings:Audience", "INDCRM_JWT_AUDIENCE") ?? string.Empty;
            return string.IsNullOrWhiteSpace(jwtAudience) ? "IND_CRM_WEB_CONTEXT" : jwtAudience.Trim() + ":context";
        }

        private static SigningCredentials BuildSigningCredentials()
        {
            var key = new SymmetricSecurityKey(GetContextSecretBytes());
            return new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        }

        private static TokenValidationParameters BuildTokenValidationParameters()
        {
            return new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = GetContextIssuer(),
                ValidAudience = GetContextAudience(),
                IssuerSigningKey = new SymmetricSecurityKey(GetContextSecretBytes()),
                ClockSkew = TimeSpan.FromMinutes(1)
            };
        }

        private static byte[] GetContextSecretBytes()
        {
            var explicitSecret = AppSettingsHelper.GetSetting("ContextTokenSettings:SecretKey", "INDCRM_CONTEXT_TOKEN_SECRET_KEY");
            if (!string.IsNullOrWhiteSpace(explicitSecret))
                return Encoding.UTF8.GetBytes(explicitSecret.Trim());

            var jwtSecret = AppSettingsHelper.GetSetting("JwtSettings:SecretKey", "JWT_SECRET_KEY");
            if (string.IsNullOrWhiteSpace(jwtSecret))
                throw new InvalidOperationException("Missing JWT secret to derive context token signing key.");

            using (var sha = SHA256.Create())
            {
                return sha.ComputeHash(Encoding.UTF8.GetBytes("ind-crm-context|" + jwtSecret.Trim()));
            }
        }

        private static string GetClaimValue(ClaimsPrincipal principal, string claimType)
        {
            return principal?.FindFirst(claimType)?.Value;
        }

        private static long TryParseLong(string raw)
        {
            return long.TryParse(raw, out var parsed) ? parsed : 0L;
        }

        private static DateTime? TryReadUtcClaim(ClaimsPrincipal principal, string claimType)
        {
            var raw = GetClaimValue(principal, claimType);
            if (!long.TryParse(raw, out var seconds))
                return null;

            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
            }
            catch
            {
                return null;
            }
        }

        private static string[] SplitCompanies(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return Array.Empty<string>();

            return raw
                .Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(entry => (entry ?? string.Empty).Trim())
                .Where(entry => !string.IsNullOrWhiteSpace(entry))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static string Normalize(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }

        private static string NormalizeRevision(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim().ToLowerInvariant();
        }
    }
}
