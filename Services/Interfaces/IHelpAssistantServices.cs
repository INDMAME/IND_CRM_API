using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Loads and validates the immutable help knowledge snapshot.
    /// </summary>
    public interface IHelpKnowledgeStore
    {
        bool IsEnabled { get; }

        HelpKnowledgeSnapshot GetSnapshot();
    }

    /// <summary>
    /// Resolves exact or free-text questions without external vector storage.
    /// </summary>
    public interface IHelpTopicRetriever
    {
        HelpRetrievalResult Retrieve(HelpKnowledgeSnapshot snapshot, HelpRetrievalRequest request);
    }

    /// <summary>
    /// Generates one answer from the already selected knowledge chunks.
    /// </summary>
    public interface IHelpAnswerService
    {
        Task<HelpGeneratedAnswer> AnswerAsync(HelpAnswerRequest request, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Persists privacy-safe help metrics independently from operational logs.
    /// </summary>
    public interface IHelpAnalyticsStore
    {
        void RecordInteraction(HelpInteractionAnalyticsEvent analyticsEvent);

        void RecordFeedback(HelpFeedbackAnalyticsEvent analyticsEvent);
    }

    /// <summary>
    /// Creates and atomically consumes short-lived feedback capabilities.
    /// </summary>
    public interface IHelpFeedbackTokenService
    {
        bool IsConfigured { get; }

        string Create(string interactionId, string userKey);

        bool TryConsume(string token, string userKey, out HelpFeedbackTokenPayload payload);
    }
}
