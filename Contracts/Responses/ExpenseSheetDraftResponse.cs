using System.Collections.Generic;
using IND_CRM_API.Contracts.Requests;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Draft result for a ticket extraction request.
    /// </summary>
    public class ExpenseSheetDraftResponse : CreateExpenseSheetRequest
    {
        public decimal? Confidence { get; set; }
        public List<string> Warnings { get; set; }
        public string RawCurrency { get; set; }
        public string Merchant { get; set; }
    }
}
