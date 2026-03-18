using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Shared AI answer service for structured record datasets.
    /// </summary>
    public interface IAiDatasetAnswerService
    {
        /// <summary>
        /// Generates an answer using the provided question and dataset records.
        /// </summary>
        Task<AiDatasetAnswerResult> AnswerAsync(AiDatasetAnswerRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Record payload sent to the AI answer service.
    /// </summary>
    public sealed class AiDatasetRecord
    {
        public string RecordId { get; set; }

        public string JsonPayload { get; set; }
    }

    /// <summary>
    /// Dataset load result shared by module-specific providers.
    /// </summary>
    public sealed class AiDatasetLoadResult
    {
        public string SourceKey { get; set; }

        public int TotalRecords { get; set; }

        public List<AiDatasetRecord> Records { get; set; }

        public List<string> Warnings { get; set; }
    }

    /// <summary>
    /// AI answer request shared by module-specific endpoints.
    /// </summary>
    public sealed class AiDatasetAnswerRequest
    {
        public string SourceKey { get; set; }

        public string Question { get; set; }

        public string AnswerInstructions { get; set; }

        public List<AiDatasetRecord> Records { get; set; }
    }

    /// <summary>
    /// AI answer result returned to the endpoint layer.
    /// </summary>
    public sealed class AiDatasetAnswerResult
    {
        public string Answer { get; set; }

        public string Model { get; set; }

        public string RetrievalMode { get; set; }

        public bool Truncated { get; set; }

        public int RecordsSentToModel { get; set; }

        public List<string> Warnings { get; set; }
    }
}
