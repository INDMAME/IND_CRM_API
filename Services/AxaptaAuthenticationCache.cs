using IND_CRM_API.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace IND_CRM_API.Services
{
    // Retains credentials only while issued JWTs can still authenticate; never stores COM objects or raw tokens.
    internal sealed class AxaptaAuthenticationCache
    {
        private const int MaxUsers = 10000;
        private const int MaxTokens = 50000;
        private readonly object _sync = new object();
        private readonly Dictionary<string, Credential> _credentials = new Dictionary<string, Credential>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TokenBinding> _tokens = new Dictionary<string, TokenBinding>(StringComparer.Ordinal);
        private readonly TimeSpan _initialLifetime = TimeSpan.FromMinutes(Math.Max(5,
            AppSettingsHelper.GetIntSetting("JwtSettings:ExpirationMinutes", 60, "INDCRM_JWT_EXPIRATION_MINUTES")) + 3d);
        private DateTime _nextCleanupUtc;

        // A login keeps its password until token issuance; later token refreshes extend this same retention deadline.
        public bool StorePassword(string user, string password)
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                Cleanup(now);
                if (!_credentials.TryGetValue(user, out var credential))
                {
                    if (_credentials.Count >= MaxUsers) return false;
                    credential = new Credential();
                    _credentials[user] = credential;
                }
                credential.Password = password;
                credential.ExpiresUtc = Later(credential.ExpiresUtc, now.Add(_initialLifetime));
                return true;
            }
        }

        // Expiry never extends merely because a request supplied a username.
        public bool TryGetPassword(string user, out string password)
        {
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                Cleanup(now);
                if (_credentials.TryGetValue(user, out var credential) && credential.ExpiresUtc > now)
                {
                    password = credential.Password;
                    return true;
                }
                password = null;
                return false;
            }
        }

        // Atomically replaces the optional old binding and preserves credentials for every live token of the user.
        public bool BindToken(string user, string token, DateTime expiresUtc, string oldToken)
        {
            if (string.IsNullOrWhiteSpace(user) || string.IsNullOrWhiteSpace(token)) return false;
            var key = HashToken(token);
            var oldKey = string.IsNullOrWhiteSpace(oldToken) ? null : HashToken(oldToken);
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                Cleanup(now);
                var expiresWithSkew = expiresUtc.AddMinutes(3);
                if (expiresWithSkew <= now) return false;
                TokenBinding oldBinding = null;
                if (oldKey != null && _tokens.TryGetValue(oldKey, out oldBinding) &&
                    !string.Equals(oldBinding.User, user, StringComparison.OrdinalIgnoreCase)) return false;
                if (!_tokens.ContainsKey(key) && oldBinding == null && _tokens.Count >= MaxTokens) return false;
                if (oldKey != null) _tokens.Remove(oldKey);
                _tokens[key] = new TokenBinding { User = user, ExpiresUtc = expiresWithSkew };
                if (_credentials.TryGetValue(user, out var credential))
                    credential.ExpiresUtc = Later(credential.ExpiresUtc, expiresWithSkew);
                return true;
            }
        }

        // Resolves only a live, previously bound token; bearer validation remains at the API boundary.
        public bool TryGetUser(string token, out string user)
        {
            user = null;
            if (string.IsNullOrWhiteSpace(token)) return false;
            var key = HashToken(token);
            lock (_sync)
            {
                var now = DateTime.UtcNow;
                Cleanup(now);
                if (!_tokens.TryGetValue(key, out var binding) || binding.ExpiresUtc <= now) return false;
                user = binding.User;
                return true;
            }
        }

        // Runs at most once per minute and never evicts live tokens or the credentials they still need.
        private void Cleanup(DateTime nowUtc)
        {
            if (nowUtc < _nextCleanupUtc) return;
            _nextCleanupUtc = nowUtc.AddMinutes(1);
            foreach (var key in _tokens.Where(item => item.Value.ExpiresUtc <= nowUtc).Select(item => item.Key).ToArray()) _tokens.Remove(key);
            foreach (var key in _credentials.Where(item => item.Value.ExpiresUtc <= nowUtc).Select(item => item.Key).ToArray()) _credentials.Remove(key);
        }

        // Stores a fixed-size digest instead of the reusable credential represented by the bearer token.
        private static string HashToken(string token)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(token)));
        }

        private static DateTime Later(DateTime first, DateTime second) { return first > second ? first : second; }
        private sealed class Credential { public string Password; public DateTime ExpiresUtc; }
        private sealed class TokenBinding { public string User; public DateTime ExpiresUtc; }
    }
}
