namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// DTO de salida para exponer un tipo de cambio oficial.
    /// </summary>
    public class ExchangeRateDto
    {
        /// <summary>
        /// Moneda base ISO 4217.
        /// </summary>
        public string BaseCurrency { get; set; }

        /// <summary>
        /// Moneda destino ISO 4217.
        /// </summary>
        public string TargetCurrency { get; set; }

        /// <summary>
        /// Tipo de cambio calculado para base -> destino.
        /// </summary>
        public decimal Rate { get; set; }

        /// <summary>
        /// Fecha efectiva de la observacion en formato yyyy-MM-dd.
        /// </summary>
        public string Date { get; set; }

        /// <summary>
        /// Fuente del tipo de cambio.
        /// </summary>
        public string Source { get; set; }
    }
}
