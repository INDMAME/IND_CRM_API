using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request body for expense sheet creation with mode-based behavior.
    /// </summary>
    public class CreateExpenseSheetRequest
    {
        public string userId { get; set; }

        /// <summary>
        /// Operation mode. 0 = create header and lines (default), 1 = create header only, 2 = add lines to an existing header.
        /// </summary>
        [Range(0, 2)]
        public int? mode { get; set; }

        /// <summary>
        /// Existing header id. Required only when mode = 2.
        /// </summary>
        public string existingHojaGastosId { get; set; }

        /// <summary>
        /// Header description. Required when mode = 0 or mode = 1.
        /// </summary>
        public string description { get; set; }

        /// <summary>
        /// Header currency code. Required when mode = 0 or mode = 1.
        /// </summary>
        public string currencyCode { get; set; }

        public decimal? exchRate { get; set; }
        public string projId { get; set; }

        /// <summary>
        /// Expense sheet status enum value from AX.
        /// </summary>
        public int? expenseSheetStatus { get; set; }

        /// <summary>
        /// Exchange rate mode enum value from AX.
        /// </summary>
        public int? exchangeRateMode { get; set; }

        /// <summary>
        /// Lines payload. Required with at least one line in mode = 0 and mode = 2. Must be null or empty in mode = 1.
        /// </summary>
        public List<CreateExpenseSheetLineRequest> lines { get; set; }
    }

    /// <summary>
    /// Line payload for expense sheet creation.
    /// </summary>
    public class CreateExpenseSheetLineRequest
    {
        [Required] public string transDate { get; set; }
        [Required] public int? typeValue { get; set; }
        [Required] public string description { get; set; }
        public bool? internacional { get; set; }
        public bool? ticket { get; set; }
        [Required] public decimal? qty { get; set; }
        /// <summary>
        /// Unit price. Amount is calculated internally in AX as qty * price.
        /// </summary>
        [Required] public decimal? price { get; set; }
        public string projId { get; set; }
        public string indAttachFiles { get; set; }
    }
}
