namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Loads expense sheet list data for AI analysis.
    /// </summary>
    public interface IExpenseSheetAiDatasetProvider
    {
        /// <summary>
        /// Stable source key for observability and reuse.
        /// </summary>
        string SourceKey { get; }

        /// <summary>
        /// Loads all expense sheet rows that match the sanitized filters.
        /// </summary>
        AiDatasetLoadResult Load(string username, string company, string axUserId, ExpenseSheetAiQueryOptions options);
    }

    /// <summary>
    /// Sanitized expense sheet filters used by the provider.
    /// </summary>
    public sealed class ExpenseSheetAiQueryOptions
    {
        public string Filter { get; set; }

        public int BilledMode { get; set; }

        public string CreatedDateFromYmd { get; set; }

        public string CreatedDateToYmd { get; set; }

        public string ProjId { get; set; }

        public string CurrencyCode { get; set; }

        public int? ExpenseSheetStatus { get; set; }

        public bool IncludeSubordinates { get; set; }
    }
}
