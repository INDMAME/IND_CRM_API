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

        /// <summary>Contexto de autorizacion requerido o no inicializado.</summary>
        public const string AuthContextRequired = "AUTH_CONTEXT_REQUIRED";

        /// <summary>Contexto de autorizacion caducado o desincronizado.</summary>
        public const string AuthContextStale = "AUTH_CONTEXT_STALE";

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

        /// <summary>Campos obligatorios faltantes en operaciones de ticket de gasto.</summary>
        public const string CrmExpenseSheetTicketMissingFields = "CRM_EXPENSESHEET_TICKET_MISSING_FIELDS";

        /// <summary>Duplicate expense ticket date and time for the same owner.</summary>
        public const string CrmExpenseSheetTicketDuplicate = "CRM_EXPENSESHEET_TICKET_DUPLICATE";

        /// <summary>Ticket de gasto no encontrado.</summary>
        public const string CrmExpenseSheetTicketNotFound = "CRM_EXPENSESHEET_TICKET_NOT_FOUND";

        /// <summary>Linea de ticket de gasto no encontrada.</summary>
        public const string CrmExpenseSheetTicketLineNotFound = "CRM_EXPENSESHEET_TICKET_LINE_NOT_FOUND";

        /// <summary>Ticket asignado a linea de gasto y no eliminable.</summary>
        public const string CrmExpenseSheetTicketAssigned = "CRM_EXPENSESHEET_TICKET_ASSIGNED";

        /// <summary>No hay archivo asociado al ticket de gasto.</summary>
        public const string CrmExpenseSheetTicketFileNotFound = "CRM_EXPENSESHEET_TICKET_FILE_NOT_FOUND";

        /// <summary>Configuracion de Azure Blob no valida o no disponible.</summary>
        public const string CrmExpenseSheetTicketFileStorageNotConfigured = "CRM_EXPENSESHEET_TICKET_FILE_STORAGE_NOT_CONFIGURED";

        /// <summary>Error al cargar archivo del ticket en Azure Blob.</summary>
        public const string CrmExpenseSheetTicketFileUploadFailed = "CRM_EXPENSESHEET_TICKET_FILE_UPLOAD_FAILED";

        /// <summary>Error al eliminar archivo del ticket en Azure Blob.</summary>
        public const string CrmExpenseSheetTicketFileDeleteFailed = "CRM_EXPENSESHEET_TICKET_FILE_DELETE_FAILED";

        /// <summary>Exchange rate not available for requested currencies/date.</summary>
        public const string ExchangeRateNotFound = "EXCHANGE_RATE_NOT_FOUND";

        /// <summary>No rate available after exhausting all providers.</summary>
        public const string RateUnavailable = "RATE_UNAVAILABLE";

        /// <summary>Error de sesion de Axapta.</summary>
        public const string AxSessionError = "AX_SESSION_ERROR";

        /// <summary>Error en llamada COM o contenedor de Axapta.</summary>
        public const string AxComError = "AX_COM_ERROR";

        /// <summary>Axapta no respondio dentro del tiempo configurado.</summary>
        public const string AxTimeout = "AX_TIMEOUT";

        /// <summary>OpenAI rate limit exceeded for the current user and endpoint.</summary>
        public const string AiRateLimitExceeded = "AI_RATE_LIMIT_EXCEEDED";

        /// <summary>OpenAI concurrency limit exceeded for the current user.</summary>
        public const string AiConcurrencyLimitExceeded = "AI_CONCURRENCY_LIMIT_EXCEEDED";

        /// <summary>Servicio de IA externo no disponible temporalmente.</summary>
        public const string AiServiceUnavailable = "AI_SERVICE_UNAVAILABLE";

        /// <summary>Dependencia externa no disponible temporalmente.</summary>
        public const string ExternalServiceUnavailable = "EXTERNAL_SERVICE_UNAVAILABLE";

        /// <summary>Dependencia externa agoto su tiempo de espera.</summary>
        public const string ExternalServiceTimeout = "EXTERNAL_SERVICE_TIMEOUT";
    }
}
