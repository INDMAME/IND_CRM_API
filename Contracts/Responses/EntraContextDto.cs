using System;
using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Resultado tipado del contexto Entra.
    /// </summary>
    public class EntraContextDto
    {
        /// <summary>Tenant id used to isolate the user snapshot.</summary>
        public string TenantId { get; set; }

        /// <summary>Real Entra OID of the signed-in business user.</summary>
        public string EntraOid { get; set; }

        /// <summary>Monotonic version of the current authorization snapshot.</summary>
        public long ContextVersion { get; set; }

        /// <summary>Stable revision of the current company permission set.</summary>
        public string PermissionsRevision { get; set; }

        /// <summary>UTC instant when the snapshot was generated.</summary>
        public DateTime ContextIssuedUtc { get; set; }

        /// <summary>UTC instant when the snapshot expires.</summary>
        public DateTime ContextExpiresUtc { get; set; }

        /// <summary>Signed context token used by the web app on downstream API calls.</summary>
        public string ContextToken { get; set; }

        /// <summary>Header de estado del contexto.</summary>
        public EntraContextHeaderDto Header { get; set; }

        /// <summary>Companias con modulos y permisos.</summary>
        public List<EntraCompanyDto> Companies { get; set; }
    }

    /// <summary>
    /// Encabezado de estado devuelto por AX.
    /// </summary>
    public class EntraContextHeaderDto
    {
        public bool Success { get; set; }
        public string Message { get; set; }
        public string AxUserId { get; set; }
        public bool UserActive { get; set; }
        public bool AppActive { get; set; }
        public string DefaultCompany { get; set; }
        public string DefaultCurrencyCode { get; set; }
    }

    /// <summary>
    /// Compania disponible para el usuario.
    /// </summary>
    public class EntraCompanyDto
    {
        public string CompanyId { get; set; }
        public bool IsDefault { get; set; }
        public string CompanyName { get; set; }
        public string CurrencyCode { get; set; }
        // Flag from CRM persona settings that allows user self-management features.
        public bool AllowSelfManagement { get; set; }
        // CRM user identifier resolved per company context.
        public string CrmUserId { get; set; }
        public List<EntraModuleDto> Modules { get; set; }
    }

    /// <summary>
    /// Modulo y permisos de acceso.
    /// </summary>
    public class EntraModuleDto
    {
        public string ModuleCode { get; set; }
        public string Description { get; set; }
        public bool IsActive { get; set; }
        public int AccessRightsInt { get; set; }
    }
}
