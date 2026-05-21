using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request data to update one expense sheet line.
    /// </summary>
    public class UpdateExpenseSheetLineRequest
    {
        /// <summary>
        /// Transaction date in mandatory DDMMYYYY format.
        /// </summary>
        [Required] public string transDate { get; set; }
        [Required] public int? typeValue { get; set; }
        [Required] public string description { get; set; }
        public bool? internacional { get; set; }
        /// <summary>
        /// FileId del ticket asociado a la linea (INDFileId).
        /// </summary>
        public string fileId { get; set; }
        [Required] public decimal? qty { get; set; }
        /// <summary>
        /// Unit price. Amount is calculated internally in AX as qty * price.
        /// </summary>
        [Required] public decimal? price { get; set; }
        public string projId { get; set; }
        /// <summary>
        /// Reimbursable expense enum value from AX. 0 = No, 1 = Yes, 2 = Both.
        /// </summary>
        public int? reimbursableExpense { get; set; }
        /// <summary>
        /// Optional line currency code.
        /// </summary>
        public string currencyCode { get; set; }
        /// <summary>
        /// Optional line amount in company currency.
        /// </summary>
        public decimal? amountMST { get; set; }
        /// <summary>
        /// Optional line exchange rate.
        /// </summary>
        public decimal? exchRate { get; set; }
    }
}
