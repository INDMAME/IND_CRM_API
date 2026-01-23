namespace IND_CRM_API.Models.Responses
{
    /// <summary>
    /// Codigos estaticos para clasificar errores de negocio y tecnicos.
    /// </summary>
    public static class IndErrorCodes
    {
        /// <summary>Error de validacion general.</summary>
        public const string ValidationError = "VALIDATION_ERROR";

        /// <summary>Error interno del servidor.</summary>
        public const string InternalError = "INTERNAL_ERROR";

        /// <summary>Autenticacion requerida.</summary>
        public const string AuthRequired = "AUTH_REQUIRED";

        /// <summary>Token de autenticacion expirado.</summary>
        public const string AuthTokenExpired = "AUTH_TOKEN_EXPIRED";

        /// <summary>Credenciales invalidas.</summary>
        public const string AuthInvalidCredentials = "AUTH_INVALID_CREDENTIALS";

        /// <summary>Acceso denegado por permisos.</summary>
        public const string AuthForbidden = "AUTH_FORBIDDEN";

        /// <summary>Campos obligatorios faltantes en actividad CRM.</summary>
        public const string CrmActivityMissingFields = "CRM_ACTIVITY_MISSING_FIELDS";

        /// <summary>Actividad CRM no encontrada.</summary>
        public const string CrmActivityNotFound = "CRM_ACTIVITY_NOT_FOUND";

        /// <summary>Cuenta CRM no encontrada.</summary>
        public const string CrmAccountNotFound = "CRM_ACCOUNT_NOT_FOUND";

        /// <summary>Missing required fields for expense sheet operations.</summary>
        public const string CrmExpenseSheetMissingFields = "CRM_EXPENSESHEET_MISSING_FIELDS";

        /// <summary>Expense sheet not found.</summary>
        public const string CrmExpenseSheetNotFound = "CRM_EXPENSESHEET_NOT_FOUND";

        /// <summary>Expense sheet line not found.</summary>
        public const string CrmExpenseSheetLineNotFound = "CRM_EXPENSESHEET_LINE_NOT_FOUND";

        /// <summary>Expense sheet is locked by voucher.</summary>
        public const string CrmExpenseSheetLocked = "CRM_EXPENSESHEET_LOCKED";

        /// <summary>Error de sesion de Axapta.</summary>
        public const string AxSessionError = "AX_SESSION_ERROR";

        /// <summary>Error en llamada COM o contenedor de Axapta.</summary>
        public const string AxComError = "AX_COM_ERROR";
    }
}
