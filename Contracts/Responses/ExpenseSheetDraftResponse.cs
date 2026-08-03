using System.Collections.Generic;
using IND_CRM_API.Contracts.Requests;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Draft result for a ticket extraction request.
    /// </summary>
    public class ExpenseSheetDraftResponse : CreateExpenseSheetRequest
    {
        /// <summary>Numeric AX enum value inferred for CRMGastoType; resolve labels through /api/crm/enums.</summary>
        public int? gastoType { get; set; }
        public string transDate { get; set; }
        public string ticketDate { get; set; }
        public string ticketTime { get; set; }
        /// <summary>Gross receipt total selected by OCR and reconciled against the draft lines.</summary>
        public decimal? totalAmount { get; set; }
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
        /// <summary>Numeric AX enum value persisted for CRMGastoType; resolve labels through /api/crm/enums.</summary>
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
