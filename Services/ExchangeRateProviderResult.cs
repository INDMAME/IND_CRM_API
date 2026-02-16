using System;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Resultado interno normalizado para el endpoint de tipo de cambio.
    /// </summary>
    public sealed class ExchangeRateProviderResult
    {
        /// <summary>
        /// Indica si se encontro un tipo de cambio.
        /// </summary>
        public bool Found { get; set; }

        /// <summary>
        /// Moneda base.
        /// </summary>
        public string BaseCurrency { get; set; }

        /// <summary>
        /// Moneda destino.
        /// </summary>
        public string TargetCurrency { get; set; }

        /// <summary>
        /// Tipo de cambio base -> destino.
        /// </summary>
        public decimal Rate { get; set; }

        /// <summary>
        /// Fecha efectiva de la observacion.
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Fuente de datos.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Crea un resultado no encontrado.
        /// </summary>
        public static ExchangeRateProviderResult NotFound(string baseCurrency, string targetCurrency)
        {
            return new ExchangeRateProviderResult
            {
                Found = false,
                BaseCurrency = baseCurrency,
                TargetCurrency = targetCurrency,
                Rate = 0m,
                Date = DateTime.MinValue,
                Source = "ECB"
            };
        }
    }
}
