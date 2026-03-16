using System.Threading;
using System.Threading.Tasks;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Orchestrates OCR plus OpenAI normalization for ticket processing.
    /// </summary>
    public interface ITicketAIProcessingService
    {
        Task<TicketAIProcessingResult> ProcessFromStoredBlobAsync(
            string blobUrl,
            string fileName,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken);

        Task<TicketAIProcessingResult> ProcessFromImageAsync(
            byte[] imageBytes,
            string fileName,
            string contentType,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Final AI processing result with both persisted JSON payloads.
    /// </summary>
    public sealed class TicketAIProcessingResult
    {
        public ExpenseSheetDraftResponse Draft { get; set; }
        public string OcrJson { get; set; }
        public string NormalizedJson { get; set; }
    }
}
