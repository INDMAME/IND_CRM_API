using System;
using System.Threading;
using System.Threading.Tasks;
using IND_CRM_API.Services;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Proveedor de tipos de cambio normalizados para la API.
    /// </summary>
    public interface IExchangeRateProvider
    {
        /// <summary>
        /// Obtiene el tipo de cambio base -> destino para la fecha solicitada.
        /// </summary>
        Task<ExchangeRateProviderResult> GetExchangeRateAsync(
            string baseCurrency,
            string targetCurrency,
            DateTime? requestedDate,
            CancellationToken cancellationToken);
    }
}
