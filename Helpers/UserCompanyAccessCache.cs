using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
            public bool IsRevoked { get; set; }
            public bool RequiresRevalidation { get; set; }
            public long RevokedThroughVersion { get; set; }
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
            public bool IsRevoked { get; set; }
            public bool RequiresRevalidation { get; set; }
            public long RevokedThroughVersion { get; set; }
        }

        private const int MaxSnapshots = 50000;
        private static readonly object CacheSync = new object();
        private static readonly Dictionary<string, CacheEntry> _cache =
            new Dictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);
        private static readonly SortedSet<CacheEntry> ExpirationOrder = new SortedSet<CacheEntry>(
            Comparer<CacheEntry>.Create((left, right) => left.ExpiresUtc != right.ExpiresUtc
                ? left.ExpiresUtc.CompareTo(right.ExpiresUtc)
                : StringComparer.OrdinalIgnoreCase.Compare(left.SnapshotKey, right.SnapshotKey)));
        private static long _lastContextVersion;
        private static long _missingSnapshotRevokedBeforeTicks;

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
            long contextVersion,
            bool requiresRevalidation = false)
        {
            var snapshotKey = BuildSnapshotKey(tenantId, entraOid);
            if (string.IsNullOrWhiteSpace(snapshotKey))
            {
                LogCacheEvent("set-invalid-key", null, null, null, "TenantId or EntraOid missing.");
                return CreateMissingSnapshot();
            }

            var companies = NormalizeCompanies(companyIds);
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
                ExpiresUtc = issuedUtc.Add(DefaultTtl),
                IsRevoked = companies.Count == 0 && !requiresRevalidation,
                RequiresRevalidation = requiresRevalidation
            };

            lock (CacheSync)
            {
                RemoveExpiredEntries(issuedUtc);
                if (_cache.TryGetValue(snapshotKey, out var previous))
                {
                    if (entry.ContextVersion < previous.ContextVersion)
                        throw new RefreshConflictException();
                    entry.RevokedThroughVersion = previous.RevokedThroughVersion;
                    if (previous.ExpiresUtc > entry.ExpiresUtc) entry.ExpiresUtc = previous.ExpiresUtc;
                    ExpirationOrder.Remove(previous);
                }
                else if (_cache.Count >= MaxSnapshots)
                {
                    // Never evict a live revision. At saturation also reject old tokens missing after a restart.
                    if (entry.IsRevoked || entry.RequiresRevalidation)
                        Interlocked.Exchange(ref _missingSnapshotRevokedBeforeTicks, issuedUtc.Ticks);
                    throw new CapacityException();
                }
                if (entry.IsRevoked)
                    entry.RevokedThroughVersion = Math.Max(entry.RevokedThroughVersion, entry.ContextVersion);
                ExpirationOrder.Add(entry);
                _cache[snapshotKey] = entry;
            }

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

            lock (CacheSync)
            {
                RemoveExpiredEntries(DateTime.UtcNow);
                return _cache.TryGetValue(snapshotKey, out var entry)
                    ? CreateSnapshot(entry)
                    : CreateMissingSnapshot(snapshotKey, null, null);
            }
        }

        // Records a confirmed authorization denial without issuing a token or retaining credentials.
        public static Snapshot Revoke(string tenantId, string entraOid, string appCode, long contextVersion = 0)
        {
            return SetSnapshot(tenantId, entraOid, null, null, appCode, Array.Empty<string>(),
                contextVersion > 0 ? contextVersion : CreateContextVersion());
        }

        // Suspends use of an uncertain observation until AX returns a complete successful context again.
        public static Snapshot RequireRevalidation(string tenantId, string entraOid, string appCode, long contextVersion)
        {
            var key = BuildSnapshotKey(tenantId, entraOid);
            if (string.IsNullOrWhiteSpace(key)) return CreateMissingSnapshot();
            lock (CacheSync)
            {
                _cache.TryGetValue(key, out var previous);
                return SetSnapshot(tenantId, entraOid, previous?.AxUserId, previous?.DefaultCompany, appCode,
                    previous?.Companies ?? new HashSet<string>(), contextVersion, true);
            }
        }

        // Covers cache saturation when an older process issued a token that has no local snapshot.
        internal static bool IsMissingSnapshotRevoked(DateTime? issuedUtc)
        {
            var revokedBefore = Interlocked.Read(ref _missingSnapshotRevokedBeforeTicks);
            return revokedBefore > 0 && (!issuedUtc.HasValue || issuedUtc.Value.Ticks <= revokedBefore);
        }

        // Live revisions and denials share the same fixed token lifetime and are never evicted early.
        private static void RemoveExpiredEntries(DateTime nowUtc)
        {
            while (ExpirationOrder.Count > 0)
            {
                var entry = ExpirationOrder.Min;
                if (entry.ExpiresUtc > nowUtc) break;
                _cache.Remove(entry.SnapshotKey);
                ExpirationOrder.Remove(entry);
            }
        }

        // Stops new context issuance when preserving live revocation state exhausts the bounded store.
        public sealed class CapacityException : InvalidOperationException
        {
            public CapacityException() : base("Authorization context capacity is temporarily exhausted.") { }
        }

        // Prevents mixing an older AX response with another request's signed snapshot and company catalog.
        public sealed class RefreshConflictException : InvalidOperationException
        {
            public RefreshConflictException() : base("Authorization context changed during refresh.") { }
        }

        /// <summary>
        /// Creates a new monotonic version for refreshed context snapshots.
        /// </summary>
        public static long CreateContextVersion()
        {
            long previous;
            long next;
            do
            {
                previous = Interlocked.Read(ref _lastContextVersion);
                next = Math.Max(DateTime.UtcNow.Ticks, previous + 1);
            } while (Interlocked.CompareExchange(ref _lastContextVersion, next, previous) != previous);
            return next;
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
                IsRevoked = entry.IsRevoked,
                RequiresRevalidation = entry.RequiresRevalidation,
                RevokedThroughVersion = entry.RevokedThroughVersion,
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
