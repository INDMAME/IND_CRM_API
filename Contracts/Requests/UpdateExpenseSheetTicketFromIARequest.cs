using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request para reemplazar el contenido del ticket con datos procesados por IA.
    /// </summary>
    public class UpdateExpenseSheetTicketFromIARequest
    {
        public string description { get; set; }
        public string currencyCode { get; set; }
        public int? gastoType { get; set; }
        public decimal? totalAmount { get; set; }

        /// <summary>
        /// Ticket date in mandatory DDMMYYYY format.
        /// </summary>
        public string transDate { get; set; }
        public string comentario { get; set; }
        public string urlFile { get; set; }
        public string fileName { get; set; }
        public string ocrJson { get; set; }
        public string normalizedJson { get; set; }

        /// <summary>
        /// Extension para generar fileName final en formato estandar cuando no se envie fileName.
        /// </summary>
        public string fileExtension { get; set; }

        /// <summary>
        /// Lineas del ticket que reemplazan por completo el detalle actual.
        /// </summary>
        public List<ExpenseSheetTicketLineRequest> lines { get; set; }
    }
}
