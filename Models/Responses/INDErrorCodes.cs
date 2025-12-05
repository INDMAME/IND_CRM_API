namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Static codes used to classify business and technical errors.
    /// </summary>
    public static class INDErrorCodes
    {
        /// <summary>Validation error for missing or invalid fields.</summary>
        public const string ValidationError = "VALIDATION_ERROR";

        /// <summary>Validation error specific to CRM activity required fields.</summary>
        public const string CrmActivityMissingFields = "CRM_ACTIVITY_MISSING_FIELDS";

        /// <summary>Axapta session related error.</summary>
        public const string AxSessionError = "AX_SESSION_ERROR";

        /// <summary>Axapta COM call or container error.</summary>
        public const string AxComError = "AX_COM_ERROR";

        /// <summary>Generic internal server error.</summary>
        public const string InternalError = "INTERNAL_ERROR";
    }
}
