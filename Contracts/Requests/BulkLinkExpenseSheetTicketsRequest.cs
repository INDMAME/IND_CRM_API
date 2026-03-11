using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request body for bulk ticket linking into an existing expense sheet.
    /// </summary>
    public class BulkLinkExpenseSheetTicketsRequest
    {
        public string expenseSheetId { get; set; }
        public List<string> ticketIds { get; set; }
    }
}
