namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request para actualizar cabecera/metadatos de ticket por FileId.
    /// </summary>
    public class UpdateExpenseSheetTicketRequest
    {
        public string description { get; set; }
        public string currencyCode { get; set; }
        /// <summary>
        /// Numeric AX enum value for CRMGastoType. Resolve active options through /api/crm/enums.
        /// </summary>
        public int? gastoType { get; set; }
        public decimal? totalAmount { get; set; }
        /// <summary>
        /// Numeric AX enum value for the ticket status. Resolve active options through /api/crm/enums.
        /// </summary>
        public int? status { get; set; }

        /// <summary>
        /// Ticket date in mandatory DDMMYYYY format.
        /// </summary>
        public string transDate { get; set; }
        /// <summary>
        /// Receipt date stored in INDTicketInfoTable. Accepts DDMMYYYY or DD.MM.YYYY.
        /// </summary>
        public string ticketDate { get; set; }
        /// <summary>
        /// Receipt time stored in INDTicketInfoTable. Accepts HH:mm or HH:mm:ss.
        /// </summary>
        public string ticketTime { get; set; }
        public string comentario { get; set; }
        public string urlFile { get; set; }
        public string fileName { get; set; }
        public bool? processedByAI { get; set; }
        public string ocrJson { get; set; }
        public string normalizedJson { get; set; }

        /// <summary>
        /// Extension para generar fileName final en formato estandar.
        /// </summary>
        public string fileExtension { get; set; }
    }
}
