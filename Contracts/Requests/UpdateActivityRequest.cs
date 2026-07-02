namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Datos para actualizar una actividad CRM existente.
    /// </summary>
    public class UpdateActivityRequest
    {
        public string accountNum { get; set; }
        /// <summary>
        /// Numeric AX enum value for the visit type. Resolve active options through /api/crm/enums.
        /// </summary>
        public int? visitType { get; set; }
        public string userId { get; set; }
        public string description { get; set; }
        public string transDate { get; set; }
        /// <summary>
        /// Numeric AX enum value for INDContactMethod. Resolve active options through /api/crm/enums.
        /// </summary>
        public int? contactMethod { get; set; }
        public string comentarios { get; set; }
        public string antecedentes { get; set; }
        public string conclusiones { get; set; }
    }
}
