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
        /// Estado de la hoja de gastos para filtrar (valor numerico AX de INDExpenseSheetStatus).
        /// Consultar opciones activas en /api/crm/enums.
        /// </summary>
        public int? expenseSheetStatus { get; set; }

        /// <summary>
        /// Reimbursement filter: Yes (0) includes, No (1) excludes, and Both (2) represents mixed values.
        /// </summary>
        public int? reimbursableExpense { get; set; }

        /// <summary>
        /// Cuando es true, lista las hojas propias y las de los subordinados directos del usuario del header.
        /// Cuando no se informa o es false, mantiene el listado del propio usuario.
        /// </summary>
        public bool? includeSubordinates { get; set; }
    }
}
