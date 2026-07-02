using System.Collections.Generic;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Models.Responses;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Reads the AX enum catalog configured per company and application.
    /// </summary>
    public interface ICrmEnumCatalogService
    {
        /// <summary>
        /// Reads configured enum catalog groups by AX enum name.
        /// </summary>
        IndPagedResponse<CrmEnumCatalogDto> GetByName(
            string username,
            string company,
            string appCode,
            string axEnumNames,
            string traceId);

        /// <summary>
        /// Reads configured enum catalog groups by AX enum id.
        /// </summary>
        IndPagedResponse<CrmEnumCatalogDto> GetById(
            string username,
            string company,
            string appCode,
            string axEnumIds,
            string traceId);

        /// <summary>
        /// Returns active numeric options for a single AX enum name.
        /// </summary>
        IReadOnlyList<CrmEnumOptionDto> GetActiveOptionsByName(
            string username,
            string company,
            string appCode,
            string axEnumName,
            string traceId);
    }
}
