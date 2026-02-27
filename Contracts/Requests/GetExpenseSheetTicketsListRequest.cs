namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Filtros y paginacion para listado de tickets.
    /// </summary>
    public class GetExpenseSheetTicketsListRequest
    {
        public int page { get; set; }
        public int pageSize { get; set; }
        public string searchKey { get; set; }
        public string filter { get; set; }
        public int? status { get; set; }
        public string createdDateFrom { get; set; }
        public string createdDateTo { get; set; }
        public string currencyCode { get; set; }
        public int? gastoType { get; set; }
    }
}
