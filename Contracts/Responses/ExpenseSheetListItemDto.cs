namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Minimal data for expense sheet list rows.
    /// </summary>
    public class ExpenseSheetListItemDto
    {
        public string HojaGastosId { get; set; }
        public string Description { get; set; }
        // Expense sheet status enum value returned by AX.
        public int? ExpenseSheetStatus { get; set; }
        public string UserId { get; set; }
        public string Voucher { get; set; }
        public string ProjId { get; set; }
        public string CurrencyCode { get; set; }
        // Total amount in company currency returned by AX.
        public decimal? TotalAmountMST { get; set; }
        public decimal? ExchRate { get; set; }
        // Exchange rate mode enum value returned by AX.
        public int? ExchangeRateMode { get; set; }
        public string CreatedDate { get; set; }
    }
}
