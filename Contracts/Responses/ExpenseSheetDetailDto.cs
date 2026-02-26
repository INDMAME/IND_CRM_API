using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Detailed expense sheet payload with header and lines.
    /// </summary>
    public class ExpenseSheetDetailDto
    {
        public string HojaGastosId { get; set; }
        public string UserId { get; set; }
        public string Description { get; set; }
        // Expense sheet status enum value returned by AX.
        public int? ExpenseSheetStatus { get; set; }
        public string CurrencyCode { get; set; }
        // Total amount in company currency returned by AX.
        public decimal? TotalAmount { get; set; }
        public decimal? ExchRate { get; set; }
        // Exchange rate mode enum value returned by AX.
        public int? ExchangeRateMode { get; set; }
        public string ProjId { get; set; }
        public string Voucher { get; set; }
        public string CreatedDate { get; set; }
        public string EstadoComentarios { get; set; }
        public List<ExpenseSheetLineDto> Lines { get; set; }
    }

    /// <summary>
    /// Line data returned for an expense sheet.
    /// </summary>
    public class ExpenseSheetLineDto
    {
        public string RecId { get; set; }
        public string TransDate { get; set; }
        public int? TypeValue { get; set; }
        public string Description { get; set; }
        public bool? Internacional { get; set; }
        public string FileId { get; set; }
        // Unit price returned by AX.
        public decimal? Price { get; set; }
        public decimal? Qty { get; set; }
        public decimal? Amount { get; set; }
        public string ProjId { get; set; }
    }
}
