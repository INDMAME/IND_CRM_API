namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Filtros y paginacion para listado de tickets.
    /// </summary>
    public class GetExpenseSheetTicketsListRequest
    {
        public int page { get; set; }
        public int pageSize { get; set; }
        public string filter { get; set; }
        public int? status { get; set; }
    }
}
