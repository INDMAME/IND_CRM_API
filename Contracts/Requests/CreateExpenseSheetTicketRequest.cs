using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request para crear tickets de gastos en AX.
    /// </summary>
    public class CreateExpenseSheetTicketRequest
    {
        /// <summary>
        /// Modo de creacion: 0=Cabecera+Lineas, 1=Solo cabecera, 2=Agregar lineas a ticket existente.
        /// </summary>
        [Range(0, 2)]
        public int? mode { get; set; }

        /// <summary>
        /// FileId existente (requerido cuando mode=2).
        /// </summary>
        public string existingFileId { get; set; }

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
        public string ocrJson { get; set; }
        public string normalizedJson { get; set; }

        /// <summary>
        /// Extension para generar INDFilename final (ej: jpg, jpeg, png, webp).
        /// </summary>
        public string fileExtension { get; set; }

        public List<ExpenseSheetTicketLineRequest> lines { get; set; }
    }

    /// <summary>
    /// Payload de linea para tickets (alta/actualizacion granular).
    /// </summary>
    public class ExpenseSheetTicketLineRequest
    {
        [Required] public string description { get; set; }
        [Required] public decimal? qty { get; set; }
        [Required] public decimal? price { get; set; }
        public decimal? totalAmount { get; set; }
    }
}
