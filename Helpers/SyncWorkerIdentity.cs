using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Security.Principal;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Resolves the technical identity of the CRM_INSERTEC outbox worker.
    /// The role claim is issued by the login only to the configured AX user
    /// (App.config SyncWorker:User or machine variable INDCRM_SYNC_USER).
    /// </summary>
    public static class SyncWorkerIdentity
    {
        /// <summary>Claim type that carries API roles issued by this service.</summary>
        public const string RoleClaimType = "ind:role";

        /// <summary>Role value required by the /api/crm/sync/* endpoints.</summary>
        public const string SyncWorkerRole = "sync-worker";

        /// <summary>
        /// Returns the configured sync worker user or null when the feature is not configured.
        /// </summary>
        public static string GetConfiguredUser()
        {
            var user = AppSettingsHelper.GetSetting("SyncWorker:User", "INDCRM_SYNC_USER");
            return string.IsNullOrWhiteSpace(user) ? null : user.Trim();
        }

        /// <summary>
        /// True when the username is the configured sync worker (case-insensitive).
        /// </summary>
        public static bool IsSyncWorkerUser(string username)
        {
            var configured = GetConfiguredUser();
            if (configured == null || string.IsNullOrWhiteSpace(username))
                return false;

            return string.Equals(configured, username.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Roles embedded in a JWT for the user: only the sync worker receives one.
        /// </summary>
        public static IReadOnlyList<string> RolesFor(string username)
        {
            return IsSyncWorkerUser(username) ? new[] { SyncWorkerRole } : Array.Empty<string>();
        }

        /// <summary>
        /// True when the principal carries the sync-worker role and still names the configured user.
        /// </summary>
        public static bool IsSyncWorkerPrincipal(IPrincipal principal)
        {
            var claims = principal as ClaimsPrincipal;
            if (claims?.Identity == null || !claims.Identity.IsAuthenticated)
                return false;

            var hasRole = claims.Claims.Any(claim =>
                string.Equals(claim.Type, RoleClaimType, StringComparison.Ordinal) &&
                string.Equals(claim.Value, SyncWorkerRole, StringComparison.Ordinal));

            return hasRole && IsSyncWorkerUser(claims.Identity.Name);
        }
    }
}
