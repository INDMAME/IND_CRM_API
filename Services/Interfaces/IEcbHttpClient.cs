using System;
using System.Threading;
using System.Threading.Tasks;
using IND_CRM_API.Services;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Cliente HTTP aislado para consultar observaciones de tipos de cambio en ECB.
    /// </summary>
    public interface IEcbHttpClient
    {
        /// <summary>
        /// Consulta una observacion de tipo de cambio para un par target/base.
        /// </summary>
        Task<EcbObservationResult> GetObservationAsync(
            string targetCurrency,
            string baseCurrency,
            DateTime? requestedDate,
            bool fallbackToPreviousBusinessDay,
            CancellationToken cancellationToken);
    }
}
