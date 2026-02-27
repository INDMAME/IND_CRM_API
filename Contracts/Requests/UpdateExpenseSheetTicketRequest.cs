namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request para actualizar cabecera/metadatos de ticket por FileId.
    /// </summary>
    public class UpdateExpenseSheetTicketRequest
    {
        public string description { get; set; }
        public string currencyCode { get; set; }
        public int? gastoType { get; set; }
        public decimal? totalAmount { get; set; }
        public int? status { get; set; }
        public string transDate { get; set; }
        public string comentario { get; set; }
        public string urlFile { get; set; }
        public string fileName { get; set; }
        public bool? processedByAI { get; set; }

        /// <summary>
        /// Extension para generar fileName final en formato estandar.
        /// </summary>
        public string fileExtension { get; set; }
    }
}
