namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Minimal data for expense sheet list rows.
    /// </summary>
    public class ExpenseSheetListItemDto
    {
        public string HojaGastosId { get; set; }
        public string Description { get; set; }
        // Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.
        public int? ExpenseSheetStatus { get; set; }
        public string UserId { get; set; }
        // Display name resolved from CRM user/person tables.
        public string UserName { get; set; }
        public string Voucher { get; set; }
        public string ProjId { get; set; }
        public string CurrencyCode { get; set; }
        // Legacy alias for TotalAmountCurrency kept for existing clients.
        public decimal? TotalAmount { get; set; }
        // Total amount in the document currency returned by AX.
        public decimal? TotalAmountCurrency { get; set; }
        public decimal? ExchRate { get; set; }
        // Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.
        public int? ExchangeRateMode { get; set; }
        public string CreatedDate { get; set; }
        public string EstadoComentarios { get; set; }
        // Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.
        public int? ReimbursableExpense { get; set; }
        /// <summary>Functional AX owner user id returned at the end of the AX row.</summary>
        public string OwnerAxUserId { get; set; }

        /// <summary>Display name for the functional owner returned at the end of the AX row.</summary>
        public string OwnerName { get; set; }

        /// <summary>Total reimbursable amount in MST, appended at the end of the AX row.</summary>
        public decimal? TotalAmountMST { get; set; }

        /// <summary>Additional AX-created date appended at the end of the AX row.</summary>
        public string AxCreatedDate { get; set; }
    }
}
