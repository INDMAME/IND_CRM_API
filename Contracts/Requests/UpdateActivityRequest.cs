namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Datos para actualizar una actividad CRM existente.
    /// </summary>
    public class UpdateActivityRequest
    {
        public string accountNum { get; set; }
        public string visitType { get; set; }
        public string userId { get; set; }
        public string description { get; set; }
        public string transDate { get; set; }
        public int? contactMethod { get; set; }
        public string comentarios { get; set; }
        public string antecedentes { get; set; }
        public string conclusiones { get; set; }
    }
}
