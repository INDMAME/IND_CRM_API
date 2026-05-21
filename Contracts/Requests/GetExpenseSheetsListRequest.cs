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

        /// <summary>
        /// Optional from date filter in DDMMYYYY format.
        /// </summary>
        public string createdDateFrom { get; set; }

        /// <summary>
        /// Optional to date filter in DDMMYYYY format.
        /// </summary>
        public string createdDateTo { get; set; }
        public string projId { get; set; }
        public string currencyCode { get; set; }

        /// <summary>
        /// Estado de la hoja de gastos para filtrar (enum AX INDExpenseSheetStatus).
        /// Valores permitidos: 0 Draft, 1 InReview, 2 Approved, 3 Rejected, 4 Paid.
        /// </summary>
        public int? expenseSheetStatus { get; set; }

        /// <summary>
        /// Reimbursable expense filter. Valores permitidos: 0 No, 1 Yes, 2 Both.
        /// </summary>
        public int? reimbursableExpense { get; set; }

        /// <summary>
        /// Cuando es true, lista las hojas de los subordinados directos del usuario del header.
        /// Cuando no se informa o es false, mantiene el listado del propio usuario.
        /// </summary>
        public bool? includeSubordinates { get; set; }
    }
}
