using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using IND_CRM_API.Services;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Stores the latest authorization snapshot per real Entra user.
    /// </summary>
    public static class UserCompanyAccessCache
    {
        public sealed class Snapshot
        {
            public bool Exists { get; set; }
            public bool Expired { get; set; }
            public string SnapshotKey { get; set; }
            public string TenantId { get; set; }
            public string EntraOid { get; set; }
            public string AxUserId { get; set; }
            public string DefaultCompany { get; set; }
            public string AppCode { get; set; }
            public long ContextVersion { get; set; }
            public string PermissionsRevision { get; set; }
            public DateTime? IssuedUtc { get; set; }
            public DateTime? ExpiresUtc { get; set; }
            public string[] Companies { get; set; }
        }

        private sealed class CacheEntry
        {
            public string SnapshotKey { get; set; }
            public string TenantId { get; set; }
            public string EntraOid { get; set; }
            public string AxUserId { get; set; }
            public string DefaultCompany { get; set; }
            public string AppCode { get; set; }
            public long ContextVersion { get; set; }
            public string PermissionsRevision { get; set; }
            public HashSet<string> Companies { get; set; }
            public DateTime IssuedUtc { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan DefaultTtl = ResolveTtl();

        /// <summary>
        /// Builds a stable cache key from tenant and Entra OID.
        /// </summary>
        public static string BuildSnapshotKey(string tenantId, string entraOid)
        {
            var normalizedTenantId = NormalizeTokenPart(tenantId);
            var normalizedEntraOid = NormalizeTokenPart(entraOid);
            if (string.IsNullOrWhiteSpace(normalizedTenantId) || string.IsNullOrWhiteSpace(normalizedEntraOid))
                return null;

            return normalizedTenantId + ":" + normalizedEntraOid;
        }

        /// <summary>
        /// Stores a fresh snapshot for the real Entra user.
        /// </summary>
        public static Snapshot SetSnapshot(
            string tenantId,
            string entraOid,
            string axUserId,
            string defaultCompany,
            string appCode,
            IEnumerable<string> companyIds,
            long contextVersion)
        {
            var snapshotKey = BuildSnapshotKey(tenantId, entraOid);
            if (string.IsNullOrWhiteSpace(snapshotKey))
            {
                LogCacheEvent("set-invalid-key", null, null, null, "TenantId or EntraOid missing.");
                return CreateMissingSnapshot();
            }

            var companies = NormalizeCompanies(companyIds);
            if (companies.Count == 0)
            {
                _cache.TryRemove(snapshotKey, out _);
                LogCacheEvent("set-empty", snapshotKey, null, null, "No companies returned; cache entry removed.");
                return CreateMissingSnapshot(snapshotKey, tenantId, entraOid);
            }

            var issuedUtc = DateTime.UtcNow;
            var permissionsRevision = CreatePermissionsRevision(
                tenantId,
                entraOid,
                axUserId,
                defaultCompany,
                appCode,
                companies);
            var entry = new CacheEntry
            {
                SnapshotKey = snapshotKey,
                TenantId = NormalizeTokenPart(tenantId),
                EntraOid = NormalizeTokenPart(entraOid),
                AxUserId = NormalizeText(axUserId),
                DefaultCompany = NormalizeText(defaultCompany),
                AppCode = NormalizeText(appCode),
                ContextVersion = contextVersion > 0 ? contextVersion : issuedUtc.Ticks,
                PermissionsRevision = permissionsRevision,
                Companies = companies,
                IssuedUtc = issuedUtc,
                ExpiresUtc = issuedUtc.Add(DefaultTtl)
            };

            _cache[snapshotKey] = entry;

            LogCacheEvent(
                "set",
                snapshotKey,
                null,
                entry.ExpiresUtc,
                "Snapshot loaded. contextVersion=" + entry.ContextVersion + " permissionsRevision=" + entry.PermissionsRevision + " companies=" + string.Join("|", entry.Companies.OrderBy(company => company, StringComparer.OrdinalIgnoreCase)));

            return CreateSnapshot(entry);
        }

        /// <summary>
        /// Gets the latest snapshot for a real Entra user.
        /// </summary>
        public static Snapshot GetSnapshot(string tenantId, string entraOid)
        {
            return GetSnapshotByKey(BuildSnapshotKey(tenantId, entraOid));
        }

        /// <summary>
        /// Gets the latest snapshot by pre-built snapshot key.
        /// </summary>
        public static Snapshot GetSnapshotByKey(string snapshotKey)
        {
            if (string.IsNullOrWhiteSpace(snapshotKey))
                return CreateMissingSnapshot();

            if (!_cache.TryGetValue(snapshotKey, out var entry) || entry == null)
            {
                LogCacheEvent("miss", snapshotKey, null, null, "No snapshot entry found.");
                return CreateMissingSnapshot(snapshotKey, null, null);
            }

            return CreateSnapshot(entry);
        }

        /// <summary>
        /// Creates a new monotonic version for refreshed context snapshots.
        /// </summary>
        public static long CreateContextVersion()
        {
            return DateTime.UtcNow.Ticks;
        }

        /// <summary>
        /// Builds a stable revision from the real-user permission footprint.
        /// </summary>
        public static string CreatePermissionsRevision(
            string tenantId,
            string entraOid,
            string axUserId,
            string defaultCompany,
            string appCode,
            IEnumerable<string> companyIds)
        {
            var normalizedCompanies = NormalizeCompanies(companyIds)
                .OrderBy(company => company, StringComparer.OrdinalIgnoreCase)
                .Select(NormalizeTokenPart);

            var fingerprint = string.Join(
                "\n",
                new[]
                {
                    NormalizeTokenPart(tenantId),
                    NormalizeTokenPart(entraOid),
                    NormalizeTokenPart(axUserId),
                    NormalizeTokenPart(defaultCompany),
                    NormalizeTokenPart(appCode)
                }.Concat(normalizedCompanies));

            using (var sha = SHA256.Create())
            {
                var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(fingerprint));
                return string.Concat(bytes.Select(b => b.ToString("x2")));
            }
        }

        public static int GetConfiguredSnapshotMinutes()
        {
            return (int)Math.Max(1, DefaultTtl.TotalMinutes);
        }

        private static Snapshot CreateSnapshot(CacheEntry entry)
        {
            if (entry == null)
                return CreateMissingSnapshot();

            return new Snapshot
            {
                Exists = true,
                Expired = entry.ExpiresUtc <= DateTime.UtcNow,
                SnapshotKey = entry.SnapshotKey,
                TenantId = entry.TenantId,
                EntraOid = entry.EntraOid,
                AxUserId = entry.AxUserId,
                DefaultCompany = entry.DefaultCompany,
                AppCode = entry.AppCode,
                ContextVersion = entry.ContextVersion,
                PermissionsRevision = entry.PermissionsRevision ?? string.Empty,
                IssuedUtc = entry.IssuedUtc,
                ExpiresUtc = entry.ExpiresUtc,
                Companies = entry.Companies == null
                    ? Array.Empty<string>()
                    : entry.Companies
                        .OrderBy(company => company, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
            };
        }

        private static Snapshot CreateMissingSnapshot(string snapshotKey = null, string tenantId = null, string entraOid = null)
        {
            return new Snapshot
            {
                Exists = false,
                Expired = false,
                SnapshotKey = snapshotKey ?? string.Empty,
                TenantId = NormalizeTokenPart(tenantId),
                EntraOid = NormalizeTokenPart(entraOid),
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

        private static HashSet<string> NormalizeCompanies(IEnumerable<string> companyIds)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (companyIds == null)
                return result;

            foreach (var companyId in companyIds)
            {
                var normalized = NormalizeText(companyId);
                if (!string.IsNullOrWhiteSpace(normalized))
                    result.Add(normalized);
            }

            return result;
        }

        private static string NormalizeTokenPart(string value)
        {
            var normalized = NormalizeText(value);
            return string.IsNullOrWhiteSpace(normalized) ? string.Empty : normalized.ToLowerInvariant();
        }

        private static string NormalizeText(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        private static TimeSpan ResolveTtl()
        {
            try
            {
                var minutesSetting = AppSettingsHelper.GetSetting("CompanyAccessCache:Minutes", "COMPANY_ACCESS_CACHE_MINUTES");
                if (int.TryParse(minutesSetting, out var minutes) && minutes > 0)
                    return TimeSpan.FromMinutes(minutes);
            }
            catch
            {
                // Use the default value when configuration cannot be read.
            }

            return TimeSpan.FromMinutes(30);
        }

        private static void LogCacheEvent(
            string action,
            string snapshotKey,
            string companyId,
            DateTime? expiresUtc,
            string detail)
        {
            try
            {
                var message =
                    "[COMPANY-CACHE] action=" + (action ?? string.Empty) +
                    " snapshotKey=" + (snapshotKey ?? string.Empty) +
                    " company=" + (companyId ?? string.Empty) +
                    " expiresUtc=" + (expiresUtc.HasValue ? expiresUtc.Value.ToString("o") : string.Empty) +
                    " detail=" + (detail ?? string.Empty);

                AxaptaSessionManager.LogStatic(message);
            }
            catch
            {
                // Cache logging must never break the request flow.
            }
        }
    }
}
