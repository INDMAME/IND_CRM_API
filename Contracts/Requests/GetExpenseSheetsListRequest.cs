namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request payload to list expense sheets with optional filters.
    /// </summary>
    public class GetExpenseSheetsListRequest
    {
        public string filter { get; set; }
        public int page { get; set; }
        public int pageSize { get; set; }
        public int? billedMode { get; set; }
        public string createdDateFrom { get; set; }
        public string createdDateTo { get; set; }
        public string projId { get; set; }
        public string currencyCode { get; set; }

        /// <summary>
        /// Estado de la hoja de gastos para filtrar (enum AX INDExpenseSheetStatus).
        /// Valores permitidos: 0 Draft, 1 InReview, 2 Approved, 3 Rejected, 4 Paid.
        /// </summary>
        public int? expenseSheetStatus { get; set; }
    }
}
