using System;
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
        ExchangeRateResult GetRate(
            string baseCurrency,
            string targetCurrency,
            DateTime date);
    }
}
