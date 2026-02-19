using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request data to update an expense sheet header.
    /// </summary>
    public class UpdateExpenseSheetHeaderRequest
    {
        [Required] public string description { get; set; }
        [Required] public string currencyCode { get; set; }
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
        /// Comentario de estado de la hoja de gastos.
        /// </summary>
        public string estadoComentarios { get; set; }
    }
}
