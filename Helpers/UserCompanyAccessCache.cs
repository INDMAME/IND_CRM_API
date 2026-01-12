using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Simple cache of allowed companies per authenticated user.
    /// Populated by the Entra context endpoint.
    /// </summary>
    public static class UserCompanyAccessCache
    {
        private class CacheEntry
        {
            public HashSet<string> Companies { get; set; }
            public DateTime ExpiresUtc { get; set; }
        }

        private static readonly ConcurrentDictionary<string, CacheEntry> _cache =
            new ConcurrentDictionary<string, CacheEntry>(StringComparer.OrdinalIgnoreCase);

        private static readonly TimeSpan DefaultTtl = ResolveTtl();

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
                return;
            }

            var entry = new CacheEntry
            {
                Companies = set,
                ExpiresUtc = DateTime.UtcNow.Add(DefaultTtl)
            };

            _cache[username] = entry;
        }

        public static bool IsCompanyAllowed(string username, string companyId, out bool cacheMissing)
        {
            cacheMissing = true;

            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(companyId))
                return false;

            if (!_cache.TryGetValue(username, out var entry) || entry == null)
                return false;

            if (entry.ExpiresUtc <= DateTime.UtcNow)
            {
                _cache.TryRemove(username, out _);
                return false;
            }

            cacheMissing = false;
            return entry.Companies.Contains(companyId.Trim());
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
    }
}
