using System.Threading;
using System.Threading.Tasks;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Maps structured OCR JSON to the existing CRM ticket draft contract using OpenAI.
    /// </summary>
    public interface IOpenAITicketNormalizationService
    {
        Task<OpenAITicketNormalizationResult> NormalizeReceiptAsync(
            AzureReceiptAnalysisResult receiptAnalysis,
            string fileName,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken);
    }

    /// <summary>
    /// Holds the OpenAI normalization output plus the raw JSON used for persistence.
    /// </summary>
    public sealed class OpenAITicketNormalizationResult
    {
        public ExpenseSheetDraftResponse Draft { get; set; }
        public string NormalizedJson { get; set; }
        public int Attempts { get; set; }
    }
}
