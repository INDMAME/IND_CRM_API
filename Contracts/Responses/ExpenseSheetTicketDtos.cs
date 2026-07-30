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
        /// <summary>Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.</summary>
        public int? Status { get; set; }
        /// <summary>Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.</summary>
        public int? GastoType { get; set; }
        public bool? ProcessedByAI { get; set; }
        public string CurrencyCode { get; set; }
        /// <summary>Legacy alias for TotalAmountCurrency kept for existing clients.</summary>
        public decimal? TotalAmount { get; set; }
        /// <summary>Total amount in the ticket currency returned by AX.</summary>
        public decimal? TotalAmountCurrency { get; set; }
        /// <summary>Legacy alias for TotalAmountMST kept for existing clients.</summary>
        public decimal? AmountMST { get; set; }
        /// <summary>Total reimbursable amount in MST returned by AX.</summary>
        public decimal? TotalAmountMST { get; set; }
        public decimal? ExchRate { get; set; }
        public string CreatedByUserId { get; set; }
        public string TransDate { get; set; }
        public string TicketDate { get; set; }
        public string TicketTime { get; set; }
        public string Comentario { get; set; }
        public string UrlFile { get; set; }
        public string FileName { get; set; }
        public string HojaGastosIdDisplay { get; set; }
        public string OcrJson { get; set; }
        public string NormalizedJson { get; set; }
        /// <summary>Functional AX owner user id returned at the end of the AX ticket detail contract.</summary>
        public string OwnerAxUserId { get; set; }

        /// <summary>Display name for the functional ticket owner.</summary>
        public string OwnerName { get; set; }
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
        public string RefRecIdTable { get; set; }
        public string CreatedByUserId { get; set; }
        /// <summary>Indica si la linea fue generada como ajuste de importe total.</summary>
        public bool? AdjustmentAmount { get; set; }
        /// <summary>AX reimbursement flag inherited from the linked expense-sheet line: Yes (0) includes AmountMST and No (1) excludes it.</summary>
        public int? ReimbursableExpense { get; set; }
        /// <summary>Reimbursable MST amount inherited from the linked expense-sheet line; zero when ReimbursableExpense is No and repeated as non-summable metadata.</summary>
        public decimal? ReimbursableAmount { get; set; }
    }

    /// <summary>
    /// Item de listado de tickets.
    /// </summary>
    public class ExpenseSheetTicketListItemDto
    {
        public string FileId { get; set; }
        public string Description { get; set; }
        /// <summary>Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.</summary>
        public int? Status { get; set; }
        /// <summary>Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.</summary>
        public int? GastoType { get; set; }
        public bool? ProcessedByAI { get; set; }
        public string CurrencyCode { get; set; }
        /// <summary>Legacy alias for TotalAmountCurrency kept for existing clients.</summary>
        public decimal? TotalAmount { get; set; }
        /// <summary>Total amount in the ticket currency returned by AX.</summary>
        public decimal? TotalAmountCurrency { get; set; }
        public string TransDate { get; set; }
        public string TicketDate { get; set; }
        public string TicketTime { get; set; }
        public string FileName { get; set; }
        /// <summary>Functional AX owner user id returned at the end of the AX ticket list row.</summary>
        public string OwnerAxUserId { get; set; }

        /// <summary>Display name for the functional ticket owner.</summary>
        public string OwnerName { get; set; }
        /// <summary>Total reimbursable amount in MST, appended at the end of the AX row.</summary>
        public decimal? TotalAmountMST { get; set; }
    }

    /// <summary>
    /// Item de listado de tickets para vinculacion.
    /// </summary>
    public class ExpenseSheetTicketLinkListItemDto
    {
        public string FileId { get; set; }
        public string Description { get; set; }
        public string CurrencyCode { get; set; }
        /// <summary>Legacy alias for TotalAmountCurrency kept for existing clients.</summary>
        public decimal? TotalAmount { get; set; }
        /// <summary>Total amount in the ticket currency returned by AX.</summary>
        public decimal? TotalAmountCurrency { get; set; }
        public string TransDate { get; set; }
        public string TicketDate { get; set; }
        public string TicketTime { get; set; }
        public string FileName { get; set; }
        public bool? ProcessedByAI { get; set; }
        /// <summary>Numeric AX enum value returned by AX; resolve labels through /api/crm/enums.</summary>
        public int? GastoType { get; set; }
        /// <summary>Functional AX owner user id returned at the end of the AX link-list row.</summary>
        public string OwnerAxUserId { get; set; }

        /// <summary>Display name for the functional ticket owner.</summary>
        public string OwnerName { get; set; }
        /// <summary>Total reimbursable amount in MST, appended at the end of the AX row.</summary>
        public decimal? TotalAmountMST { get; set; }
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
        public decimal? TotalAmountCurrency { get; set; }
        public decimal? TotalAmountMST { get; set; }
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

    /// <summary>
    /// Resultado de un ajuste de importe total sobre ticket.
    /// </summary>
    public class ExpenseSheetTicketTotalAdjustmentResultDto
    {
        /// <summary>Identificador funcional del ticket ajustado.</summary>
        public string FileId { get; set; }

        /// <summary>Importe total de cabecera antes del ajuste.</summary>
        public decimal? PreviousTotalAmount { get; set; }

        /// <summary>Nuevo importe total de cabecera guardado en AX.</summary>
        public decimal? NewTotalAmount { get; set; }

        /// <summary>Nuevo importe total de cabecera en la divisa del ticket.</summary>
        public decimal? TotalAmountCurrency { get; set; }

        /// <summary>Importe reembolsable MST de la cabecera tras el ajuste.</summary>
        public decimal? TotalAmountMST { get; set; }

        /// <summary>Diferencia calculada como nuevo total menos total anterior.</summary>
        public decimal? DifferenceAmount { get; set; }

        /// <summary>RecId of the differential line created or recalculated; empty when no adjustment exists.</summary>
        public string AdjustmentLineRecId { get; set; }

        /// <summary>Indicates whether AX created a new differential line.</summary>
        public bool? AdjustmentLineCreated { get; set; }

        /// <summary>Descripcion fija usada para la linea diferencial.</summary>
        public string AdjustmentDescription { get; set; }

        /// <summary>Flag de ajuste aplicado en INDTicketInfoLine.Adjustment y expuesto como AdjustmentAmount.</summary>
        public bool? AdjustmentAmount { get; set; }
    }
}
