using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Detalle de ticket con cabecera y lineas.
    /// </summary>
    public class ExpenseSheetTicketDetailDto
    {
        public string FileId { get; set; }
        public string Description { get; set; }
        public int? Status { get; set; }
        public int? GastoType { get; set; }
        public bool? ProcessedByAI { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? TotalAmount { get; set; }
        public string CreatedByUserId { get; set; }
        public string TransDate { get; set; }
        public string Comentario { get; set; }
        public string UrlFile { get; set; }
        public string FileName { get; set; }
        public string HojaGastosIdDisplay { get; set; }
        public List<ExpenseSheetTicketLineDto> Lines { get; set; }
    }

    /// <summary>
    /// Linea de detalle de ticket.
    /// </summary>
    public class ExpenseSheetTicketLineDto
    {
        public string RecId { get; set; }
        public string Description { get; set; }
        public decimal? Qty { get; set; }
        public decimal? Price { get; set; }
        public decimal? TotalAmount { get; set; }
        public string RefRecIdTable { get; set; }
        public string CreatedByUserId { get; set; }
    }

    /// <summary>
    /// Item de listado de tickets.
    /// </summary>
    public class ExpenseSheetTicketListItemDto
    {
        public string FileId { get; set; }
        public string Description { get; set; }
        public int? Status { get; set; }
        public int? GastoType { get; set; }
        public bool? ProcessedByAI { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? TotalAmount { get; set; }
        public string CreatedByUserId { get; set; }
        public string TransDate { get; set; }
        public string UrlFile { get; set; }
        public string FileName { get; set; }
        public string HojaGastosIdDisplay { get; set; }
    }
}
