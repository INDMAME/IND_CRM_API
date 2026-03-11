namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Filtros y paginacion para el listado de tickets disponibles para vinculacion.
    /// </summary>
    public class GetExpenseSheetTicketLinkListRequest
    {
        public int page { get; set; }
        public int pageSize { get; set; }
        public string searchKey { get; set; }
        public string filter { get; set; }

        /// <summary>
        /// Optional from date filter in DDMMYYYY format.
        /// </summary>
        public string createdDateFrom { get; set; }

        /// <summary>
        /// Optional to date filter in DDMMYYYY format.
        /// </summary>
        public string createdDateTo { get; set; }
        public string currencyCode { get; set; }
        public int? gastoType { get; set; }
        public bool? processedByAI { get; set; }
    }
}
