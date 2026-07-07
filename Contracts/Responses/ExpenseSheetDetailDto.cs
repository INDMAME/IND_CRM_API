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
        // Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.
        public int? ExpenseSheetStatus { get; set; }
        public string CurrencyCode { get; set; }
        // Legacy alias for TotalAmountCurrency kept for existing clients.
        public decimal? TotalAmount { get; set; }
        // Total amount in the document currency returned by AX.
        public decimal? TotalAmountCurrency { get; set; }
        public decimal? ExchRate { get; set; }
        // Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.
        public int? ExchangeRateMode { get; set; }
        public string ProjId { get; set; }
        public string Voucher { get; set; }
        public string CreatedDate { get; set; }
        public string EstadoComentarios { get; set; }
        // Numeric AX enum value returned by AX; resolve labels through INDReimbursableExpense in /api/crm/enums/by-name.
        public int? ReimbursableExpense { get; set; }
        public List<ExpenseSheetLineDto> Lines { get; set; }
        // Display name resolved from CRMUsuarioTable for the sheet owner.
        public string UserName { get; set; }
        /// <summary>Functional AX owner user id returned at the end of the AX header contract.</summary>
        public string OwnerAxUserId { get; set; }

        /// <summary>Display name for the functional owner returned at the end of the AX header contract.</summary>
        public string OwnerName { get; set; }
        /// <summary>Total reimbursable amount in MST, appended at the end of the AX header contract.</summary>
        public decimal? TotalAmountMST { get; set; }
    }

    /// <summary>
    /// Line data returned for an expense sheet.
    /// </summary>
    public class ExpenseSheetLineDto
    {
        public string RecId { get; set; }
        public string TransDate { get; set; }
        /// <summary>Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.</summary>
        public int? TypeValue { get; set; }
        public string Description { get; set; }
        public bool? Internacional { get; set; }
        public string FileId { get; set; }
        // Unit price returned by AX.
        public decimal? Price { get; set; }
        public decimal? Qty { get; set; }
        public decimal? Amount { get; set; }
        public string ProjId { get; set; }
        /// <summary>Numeric AX enum value returned by AX; resolve labels through INDReimbursableExpenseLines in /api/crm/enums/by-name.</summary>
        public int? ReimbursableExpense { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? AmountMST { get; set; }
        public decimal? ExchRate { get; set; }
        /// <summary>Alias for Amount, exposed so card totals can use the same naming convention as headers.</summary>
        public decimal? TotalAmountCurrency { get; set; }
        /// <summary>Alias for AmountMST, exposed so card totals can use the same naming convention as headers.</summary>
        public decimal? TotalAmountMST { get; set; }
    }
}
