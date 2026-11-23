namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Error codes owned by the api/crm/webstore/* routes. Kept apart from IndErrorCodes so the
    /// shared catalog used by the legacy CRM does not change.
    /// </summary>
    public static class CrmWebStoreErrorCodes
    {
        /// <summary>AX_WEBSTORE_USER is not defined: the web store routes are disabled.</summary>
        public const string Disabled = "WEBSTORE_DISABLED";

        /// <summary>The user or token subject is not the configured web store user.</summary>
        public const string Forbidden = "WEBSTORE_FORBIDDEN";

        /// <summary>Envelope or structure of the message is incomplete or has wrong types.</summary>
        public const string MessageInvalid = "STORE_MESSAGE_INVALID";

        /// <summary>Envelope code of a rejected result when AX does not return its own error code.</summary>
        public const string Rejected = "STORE_REJECTED";

        /// <summary>The AX result container does not follow the documented layout.</summary>
        public const string ResultInvalid = "STORE_RESULT_INVALID";
    }
}
