using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request body for bulk ticket linking into an existing expense sheet.
    /// </summary>
    public class BulkLinkExpenseSheetTicketsRequest
    {
        public string expenseSheetId { get; set; }
        public string selectionMode { get; set; }
        public List<string> ticketIds { get; set; }
        public BulkLinkExpenseSheetTicketFiltersRequest filters { get; set; }
        public List<string> excludedIds { get; set; }
    }

    /// <summary>
    /// Filter set used by bulk ticket linking when the server resolves candidates.
    /// </summary>
    public class BulkLinkExpenseSheetTicketFiltersRequest
    {
        public string searchKey { get; set; }
        public string filter { get; set; }
        public string createdDateFrom { get; set; }
        public string createdDateTo { get; set; }
        public string currencyCode { get; set; }
        public int? gastoType { get; set; }
        public bool? processedByAI { get; set; }
    }
}
