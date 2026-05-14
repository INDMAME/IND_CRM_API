using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Detalle de ticket con cabecera y lineas.
    /// </summary>
    public class ExpenseSheetTicketDetailDto
    {
        public string FileId { get; set; }
        public string Description { get; set; }
        public int? Status { get; set; }
        public int? GastoType { get; set; }
        public bool? ProcessedByAI { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? TotalAmount { get; set; }
        public string CreatedByUserId { get; set; }
        public string TransDate { get; set; }
        public string Comentario { get; set; }
        public string UrlFile { get; set; }
        public string FileName { get; set; }
        public string HojaGastosIdDisplay { get; set; }
        public string OcrJson { get; set; }
        public string NormalizedJson { get; set; }
        public List<ExpenseSheetTicketLineDto> Lines { get; set; }
    }

    /// <summary>
    /// Linea de detalle de ticket.
    /// </summary>
    public class ExpenseSheetTicketLineDto
    {
        public string RecId { get; set; }
        public string Description { get; set; }
        public decimal? Qty { get; set; }
        public decimal? Price { get; set; }
        public decimal? TotalAmount { get; set; }
        public decimal? TaxPercent { get; set; }
        public string RefRecIdTable { get; set; }
        public string CreatedByUserId { get; set; }
    }

    /// <summary>
    /// Item de listado de tickets.
    /// </summary>
    public class ExpenseSheetTicketListItemDto
    {
        public string FileId { get; set; }
        public string Description { get; set; }
        public int? Status { get; set; }
        public int? GastoType { get; set; }
        public bool? ProcessedByAI { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? TotalAmount { get; set; }
        public string TransDate { get; set; }
        public string FileName { get; set; }
    }

    /// <summary>
    /// Item de listado de tickets para vinculacion.
    /// </summary>
    public class ExpenseSheetTicketLinkListItemDto
    {
        public string FileId { get; set; }
        public string Description { get; set; }
        public string CurrencyCode { get; set; }
        public decimal? TotalAmount { get; set; }
        public string TransDate { get; set; }
        public string FileName { get; set; }
        public bool? ProcessedByAI { get; set; }
        public int? GastoType { get; set; }
    }

    /// <summary>
    /// Resultado resumido de una vinculacion bulk de tickets.
    /// </summary>
    public class ExpenseSheetTicketBulkLinkResultDto
    {
        public string expenseSheetId { get; set; }
        public int requestedCount { get; set; }
        public int linkedCount { get; set; }
        public int skippedCount { get; set; }
        public int failedCount { get; set; }
        public List<string> linkedTicketIds { get; set; }
        public List<ExpenseSheetTicketBulkLinkIssueDto> skipped { get; set; }
        public List<ExpenseSheetTicketBulkLinkIssueDto> failed { get; set; }
    }

    /// <summary>
    /// Ticket omitido o fallido durante una vinculacion bulk.
    /// </summary>
    public class ExpenseSheetTicketBulkLinkIssueDto
    {
        public string ticketId { get; set; }
        public string reason { get; set; }
    }

    /// <summary>
    /// Resultado del flujo compuesto de alta rapida de ticket.
    /// </summary>
    public class ExpenseSheetTicketQuickCreateResultDto
    {
        public string FileId { get; set; }
        public string UrlFile { get; set; }
        public string FileName { get; set; }
        public bool? ProcessedByAI { get; set; }
        public bool LinkedToSheet { get; set; }
        public string HojaGastosId { get; set; }
        public string CompletedStage { get; set; }
        public string FailedStage { get; set; }
        public bool? RollbackAttempted { get; set; }
        public bool? RollbackSucceeded { get; set; }
        public string RollbackMessage { get; set; }
        public ExpenseSheetTicketQuickCreateStepTraceIdsDto StepTraceIds { get; set; }
    }

    /// <summary>
    /// Trace ids por etapa del flujo compuesto quick-create.
    /// </summary>
    public class ExpenseSheetTicketQuickCreateStepTraceIdsDto
    {
        public string TicketCreate { get; set; }
        public string FileUpload { get; set; }
        public string DraftExtract { get; set; }
        public string TicketFinalize { get; set; }
        public string SheetLink { get; set; }
    }
}
