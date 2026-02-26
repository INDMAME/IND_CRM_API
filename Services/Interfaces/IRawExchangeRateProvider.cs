using System;
using IND_CRM_API.Services;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Contrato interno para proveedores tecnicos de tipos de cambio.
    /// </summary>
    public interface IRawExchangeRateProvider
    {
        /// <summary>
        /// Nombre tecnico del proveedor.
        /// </summary>
        string ProviderName { get; }

        /// <summary>
        /// Obtiene tipo de cambio base -> destino para la fecha solicitada.
        /// </summary>
        ExchangeRateResult GetRate(string baseCurrency, string targetCurrency, DateTime date);
    }
}
