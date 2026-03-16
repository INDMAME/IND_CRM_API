using System;
using System.Threading;
using System.Threading.Tasks;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Wraps the OpenAI ticket normalizer so the AI pipeline exposes a focused service.
    /// </summary>
    public sealed class OpenAITicketNormalizationService : IOpenAITicketNormalizationService
    {
        private readonly IND_OpenAiExpenseTicketDraftService _openAiDraftService;

        public OpenAITicketNormalizationService(IND_OpenAiExpenseTicketDraftService openAiDraftService)
        {
            _openAiDraftService = openAiDraftService ?? throw new ArgumentNullException(nameof(openAiDraftService));
        }

        public Task<OpenAITicketNormalizationResult> NormalizeReceiptAsync(
            AzureReceiptAnalysisResult receiptAnalysis,
            string fileName,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken)
        {
            return _openAiDraftService.NormalizeReceiptAsync(receiptAnalysis, fileName, profile, cancellationToken);
        }
    }
}
