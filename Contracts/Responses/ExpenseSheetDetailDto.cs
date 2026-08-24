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
        /// <summary>Legacy accounting total kept under the published currency field name.</summary>
        public decimal? TotalAmountCurrency { get; set; }
        public decimal? ExchRate { get; set; }
        // Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.
        public int? ExchangeRateMode { get; set; }
        public string ProjId { get; set; }
        public string Voucher { get; set; }
        public string CreatedDate { get; set; }
        public string EstadoComentarios { get; set; }
        // AX reimbursement state: Yes includes lines, No excludes them, and Both represents mixed line values.
        public int? ReimbursableExpense { get; set; }
        public List<ExpenseSheetLineDto> Lines { get; set; }
        // Display name resolved from CRMUsuarioTable for the sheet owner.
        public string UserName { get; set; }
        /// <summary>Functional AX owner user id returned at the end of the AX header contract.</summary>
        public string OwnerAxUserId { get; set; }

        /// <summary>Display name for the functional owner returned at the end of the AX header contract.</summary>
        public string OwnerName { get; set; }
        /// <summary>Legacy accounting total in company currency/MST, kept for existing clients.</summary>
        public decimal? TotalAmountMST { get; set; }

        /// <summary>Additional AX-created date appended at the end of the AX header contract.</summary>
        public string AxCreatedDate { get; set; }

        /// <summary>Gross expense total in company currency/MST, independent of reimbursement and legacy Visa.</summary>
        public decimal? TotalGrossAmountMST { get; set; }

        /// <summary>Total payable to the employee in company currency/MST; only lines marked ReimbursableExpense=Yes are included.</summary>
        public decimal? TotalReimbursableAmount { get; set; }

        /// <summary>Valid project default for a newly created line; null when an older AX contract does not provide it.</summary>
        public string DefaultLineProjId { get; set; }
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
        /// <summary>Line total in the original line currency.</summary>
        public decimal? Amount { get; set; }
        public string ProjId { get; set; }
        /// <summary>AX reimbursement flag: Yes (0) includes AmountMST and No (1) excludes the line from reimbursement.</summary>
        public int? ReimbursableExpense { get; set; }
        public string CurrencyCode { get; set; }
        /// <summary>Line total in company currency/MST.</summary>
        public decimal? AmountMST { get; set; }
        /// <summary>Reimbursable line amount in company currency/MST; zero when ReimbursableExpense is No. Visa does not affect this amount.</summary>
        public decimal? ReimbursableAmount { get; set; }
        public decimal? ExchRate { get; set; }
        /// <summary>Alias for Amount, exposed so card totals can use the same naming convention as headers.</summary>
        public decimal? TotalAmountCurrency { get; set; }
        /// <summary>Alias for AmountMST, exposed so card totals can use the same naming convention as headers.</summary>
        public decimal? TotalAmountMST { get; set; }
    }
}
