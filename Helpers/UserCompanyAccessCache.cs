using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using IND_CRM_API.Services;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Simple cache of allowed companies per authenticated user.
    /// Populated by the Entra context endpoint.
    /// </summary>
    public static class UserCompanyAccessCache
    {
        public sealed class Evaluation
        {
            public bool Allowed { get; set; }
            public bool CacheMissing { get; set; }
            public bool CacheExpired { get; set; }
            public bool UsedGraceWindow { get; set; }
            public Snapshot Snapshot { get; set; }
        }

        public sealed class Snapshot
        {
            public bool Exists { get; set; }
            public bool Expired { get; set; }
            public DateTime? ExpiresUtc { get; set; }
            public DateTime? GraceUntilUtc { get; set; }
            public string[] Companies { get; set; }
        }

        private class CacheEntry
        {
            public HashSet<string> Companies { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan DefaultTtl = ResolveTtl();
        private static readonly TimeSpan GraceTtl = TimeSpan.FromMinutes(120);

        public static void SetAllowedCompanies(string username, IEnumerable<string> companyIds)
        {
            if (string.IsNullOrWhiteSpace(username))
                return;

            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (companyIds != null)
            {
                foreach (var company in companyIds)
                {
                    var trimmed = (company ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                        set.Add(trimmed);
                }
            }

            if (set.Count == 0)
            {
                _cache.TryRemove(username, out _);
                LogCacheEvent("set-empty", username, null, null, "No companies returned; cache entry removed.");
                return;
            }

            var entry = new CacheEntry
            {
                Companies = set,
                ExpiresUtc = DateTime.UtcNow.Add(DefaultTtl)
            };

            _cache[username] = entry;
            LogCacheEvent(
                "set",
                username,
                null,
                entry.ExpiresUtc,
                "Cache loaded from Entra context. companies=" + string.Join("|", set.OrderBy(company => company, StringComparer.OrdinalIgnoreCase)));
        }

        public static bool IsCompanyAllowed(string username, string companyId, out bool cacheMissing)
        {
            var evaluation = EvaluateCompanyAccess(username, companyId);
            cacheMissing = evaluation.CacheMissing;
            return evaluation.Allowed;
        }

        public static Evaluation EvaluateCompanyAccess(string username, string companyId)
        {
            var evaluation = new Evaluation
            {
                Allowed = false,
                CacheMissing = true,
                CacheExpired = false,
                UsedGraceWindow = false,
                Snapshot = null
            };

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(companyId))
            {
                LogCacheEvent("invalid-input", username, companyId, null, "Username or company is empty.");
                return evaluation;
            }

            if (!_cache.TryGetValue(username, out var entry) || entry == null)
            {
                LogCacheEvent("miss", username, companyId, null, "No cache entry found.");
                return evaluation;
            }

            var nowUtc = DateTime.UtcNow;
            var normalizedCompanyId = companyId.Trim();
            var snapshot = CreateSnapshot(entry);
            evaluation.Snapshot = snapshot;
            evaluation.CacheMissing = false;
            evaluation.CacheExpired = snapshot.Expired;

            if (entry.Companies == null || !entry.Companies.Contains(normalizedCompanyId))
            {
                LogCacheEvent(
                    "deny-company-not-found",
                    username,
                    normalizedCompanyId,
                    snapshot.ExpiresUtc,
                    "Company not present in cached company list.");
                return evaluation;
            }

            if (!snapshot.Expired)
            {
                RefreshEntry(username, entry);
                evaluation.Allowed = true;
                evaluation.Snapshot = CreateSnapshot(_cache[username]);
                LogCacheEvent(
                    "allow-refresh",
                    username,
                    normalizedCompanyId,
                    evaluation.Snapshot.ExpiresUtc,
                    "Allowed by active cache entry. Sliding expiration refreshed.");
                return evaluation;
            }

            if (GraceTtl > TimeSpan.Zero && snapshot.GraceUntilUtc.HasValue && snapshot.GraceUntilUtc.Value > nowUtc)
            {
                RefreshEntry(username, entry);
                evaluation.Allowed = true;
                evaluation.UsedGraceWindow = true;
                evaluation.Snapshot = CreateSnapshot(_cache[username]);
                LogCacheEvent(
                    "allow-grace-window",
                    username,
                    normalizedCompanyId,
                    evaluation.Snapshot.ExpiresUtc,
                    "Allowed by fixed grace window of " + (int)GraceTtl.TotalMinutes + " minutes; cache refreshed.");
                return evaluation;
            }

            LogCacheEvent(
                "deny-expired",
                username,
                normalizedCompanyId,
                snapshot.ExpiresUtc,
                "Cache expired and fixed grace window exhausted.");
            return evaluation;
        }

        public static Snapshot GetSnapshot(string username)
        {
            if (string.IsNullOrWhiteSpace(username))
            {
                return new Snapshot
                {
                    Exists = false,
                    Expired = false,
                    ExpiresUtc = null,
                    GraceUntilUtc = null,
                    Companies = Array.Empty<string>()
                };
            }

            if (!_cache.TryGetValue(username, out var entry) || entry == null)
            {
                return new Snapshot
                {
                    Exists = false,
                    Expired = false,
                    ExpiresUtc = null,
                    GraceUntilUtc = null,
                    Companies = Array.Empty<string>()
                };
            }

            return CreateSnapshot(entry);
        }

        private static Snapshot CreateSnapshot(CacheEntry entry)
        {
            if (entry == null)
            {
                return new Snapshot
                {
                    Exists = false,
                    Expired = false,
                    ExpiresUtc = null,
                    GraceUntilUtc = null,
                    Companies = Array.Empty<string>()
                };
            }

            return new Snapshot
            {
                Exists = true,
                Expired = entry.ExpiresUtc <= DateTime.UtcNow,
                ExpiresUtc = entry.ExpiresUtc,
                GraceUntilUtc = GraceTtl > TimeSpan.Zero ? entry.ExpiresUtc.Add(GraceTtl) : (DateTime?)null,
                Companies = entry.Companies == null
                    ? Array.Empty<string>()
                    : entry.Companies
                        .OrderBy(company => company, StringComparer.OrdinalIgnoreCase)
                        .ToArray()
            };
        }

        private static void RefreshEntry(string username, CacheEntry entry)
        {
            if (string.IsNullOrWhiteSpace(username) || entry?.Companies == null)
                return;

            var refreshedEntry = new CacheEntry
            {
                Companies = new HashSet<string>(entry.Companies, StringComparer.OrdinalIgnoreCase),
                ExpiresUtc = DateTime.UtcNow.Add(DefaultTtl)
            };

            _cache[username] = refreshedEntry;
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
                // On error, use the default value.
            }

            return TimeSpan.FromMinutes(30);
        }

        private static void LogCacheEvent(
            string action,
            string username,
            string companyId,
            DateTime? expiresUtc,
            string message)
        {
            AxaptaSessionManager.LogStatic(
                $"[COMPANY-CACHE] action={action} user={ToLogValue(username)} company={ToLogValue(companyId)} " +
                $"ttlMinutes={(int)DefaultTtl.TotalMinutes} fixedGraceMinutes={(int)GraceTtl.TotalMinutes} " +
                $"expiresUtc={(expiresUtc.HasValue ? expiresUtc.Value.ToString("o") : "-")} message={ToLogValue(message)}");
        }

        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        }
    }
}
