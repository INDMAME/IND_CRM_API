using System.Collections.Generic;
using IND_CRM_API.Contracts.Requests;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Draft result for a ticket extraction request.
    /// </summary>
    public class ExpenseSheetDraftResponse : CreateExpenseSheetRequest
    {
        public int? gastoType { get; set; }
        public string transDate { get; set; }
        public string ticketDate { get; set; }
        public string ticketTime { get; set; }
        public decimal? Confidence { get; set; }
        public List<string> Warnings { get; set; }
        public string RawCurrency { get; set; }
        public string Merchant { get; set; }
        public ExpenseSheetDraftTicketCreationResult TicketCreation { get; set; }
    }

    /// <summary>
    /// Resultado de persistencia opcional de ticket durante la extraccion IA.
    /// </summary>
    public class ExpenseSheetDraftTicketCreationResult
    {
        public bool Persisted { get; set; }
        public bool? ProcessedByAI { get; set; }
        public int? GastoType { get; set; }
        public string FileId { get; set; }
        public string TicketRecId { get; set; }
        public List<long> LineRecIds { get; set; }
        public string UrlFile { get; set; }
        public string FileName { get; set; }
        public bool FileNameFinalized { get; set; }
        public string Message { get; set; }
    }
}
