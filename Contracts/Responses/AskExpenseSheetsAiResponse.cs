using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// AI answer payload for expense sheet questions.
    /// </summary>
    public class AskExpenseSheetsAiResponse
    {
        /// <summary>
        /// Final natural-language answer returned to the caller.
        /// </summary>
        public string Answer { get; set; }

        /// <summary>
        /// OpenAI model used to generate the answer.
        /// </summary>
        public string Model { get; set; }

        /// <summary>
        /// Stable source key used by the dataset provider.
        /// </summary>
        public string SourceKey { get; set; }

        /// <summary>
        /// Sanitized filters applied before loading records.
        /// </summary>
        public ExpenseSheetsAiAppliedFiltersDto FiltersApplied { get; set; }

        /// <summary>
        /// Total records loaded from the source after filtering.
        /// </summary>
        public int TotalSourceRecords { get; set; }

        /// <summary>
        /// Records actually processed by the AI flow.
        /// </summary>
        public int RecordsSentToModel { get; set; }

        /// <summary>
        /// Retrieval mode used by the AI flow: direct or chunked.
        /// </summary>
        public string RetrievalMode { get; set; }

        /// <summary>
        /// Indicates whether the AI flow had to trim data for safety limits.
        /// </summary>
        public bool Truncated { get; set; }

        /// <summary>
        /// Non-fatal warnings collected during retrieval or answering.
        /// </summary>
        public List<string> Warnings { get; set; }
    }

    /// <summary>
    /// Filters echoed back to confirm what data was analyzed.
    /// </summary>
    public class ExpenseSheetsAiAppliedFiltersDto
    {
        public string Filter { get; set; }

        public int BilledMode { get; set; }

        public string CreatedDateFrom { get; set; }

        public string CreatedDateTo { get; set; }

        public string ProjId { get; set; }

        public string CurrencyCode { get; set; }

        public int? ExpenseSheetStatus { get; set; }

        public bool IncludeSubordinates { get; set; }
    }
}
