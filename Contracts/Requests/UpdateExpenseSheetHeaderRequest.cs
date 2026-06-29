using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request data to update an expense sheet header.
    /// </summary>
    public class UpdateExpenseSheetHeaderRequest
    {
        [Required] public string description { get; set; }
        /// <summary>
        /// Legacy field accepted for compatibility. AX keeps the header reimbursement currency as local currency.
        /// </summary>
        public string currencyCode { get; set; }
        public decimal? exchRate { get; set; }
        public string projId { get; set; }

        /// <summary>
        /// Numeric AX enum value for INDExpenseSheetStatus. Resolve active options through /api/crm/enums.
        /// </summary>
        public int? expenseSheetStatus { get; set; }

        /// <summary>
        /// Numeric AX enum value for exchange rate mode. Resolve active options through /api/crm/enums.
        /// </summary>
        public int? exchangeRateMode { get; set; }

        /// <summary>
        /// Comentario de estado de la hoja de gastos.
        /// </summary>
        public string estadoComentarios { get; set; }

        /// <summary>
        /// Numeric AX enum value for INDReimbursableExpense. Resolve active options through /api/crm/enums/by-name?axEnumNames=INDReimbursableExpense.
        /// </summary>
        public int? reimbursableExpense { get; set; }
    }
}
