using System;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Resultado interno normalizado para proveedores de tipo de cambio.
    /// </summary>
    public sealed class ExchangeRateResult
    {
        /// <summary>
        /// Indica si el proveedor devolvio tipo de cambio valido.
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// Tipo de cambio base -> destino.
        /// </summary>
        public decimal Rate { get; set; }

        /// <summary>
        /// Fuente normalizada expuesta por la API.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Codigo de error interno cuando Success es false.
        /// </summary>
        public string ErrorCode { get; set; }

        /// <summary>
        /// Fecha efectiva del tipo de cambio.
        /// </summary>
        public DateTime Date { get; set; }

        /// <summary>
        /// Proveedor tecnico que resolvio la tasa.
        /// </summary>
        public string ProviderUsed { get; set; }

        /// <summary>
        /// Indica si se utilizo proveedor de fallback.
        /// </summary>
        public bool FallbackActivated { get; set; }
    }
}
