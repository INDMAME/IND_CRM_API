using System;
using System.Collections.Generic;
using System.Linq;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Deserialized, validated CRM help knowledge bundle.
    /// </summary>
    public sealed class HelpKnowledgeBundle
    {
        public string schemaVersion { get; set; }

        public string knowledgeVersion { get; set; }

        public string knowledgeHash { get; set; }

        public string defaultLocale { get; set; }

        public List<string> supportedResponseLocales { get; set; }

        public HelpKnowledgeSource source { get; set; }

        public List<HelpKnowledgeModule> modules { get; set; }

        public List<HelpKnowledgeTopic> topics { get; set; }

        public List<HelpKnowledgeAsset> assets { get; set; }
    }

    public sealed class HelpKnowledgeSource
    {
        public string path { get; set; }

        public string sha256 { get; set; }
    }

    public sealed class HelpKnowledgeModule
    {
        public string id { get; set; }

        public string title { get; set; }

        public string description { get; set; }

        public int order { get; set; }

        public List<string> topicIds { get; set; }

        public Dictionary<string, HelpKnowledgeModuleLocalization> localizations { get; set; }
    }

    /// <summary>
    /// Localized display metadata for one help module.
    /// </summary>
    public sealed class HelpKnowledgeModuleLocalization
    {
        public string title { get; set; }

        public string description { get; set; }
    }

    public sealed class HelpKnowledgeTopic
    {
        public string id { get; set; }

        public string moduleId { get; set; }

        public string title { get; set; }

        public string summary { get; set; }

        public Dictionary<string, List<string>> aliases { get; set; }

        public Dictionary<string, List<string>> sampleQuestions { get; set; }

        public List<string> keywords { get; set; }

        public List<string> audiences { get; set; }

        public List<string> prerequisiteTopicIds { get; set; }

        public List<string> relatedTopicIds { get; set; }

        public string routeKey { get; set; }

        public string status { get; set; }

        public string contentVersion { get; set; }

        public string contentHash { get; set; }

        public List<HelpKnowledgeQuickAnswer> quickAnswers { get; set; }

        public List<HelpKnowledgeChunk> chunks { get; set; }

        public Dictionary<string, HelpKnowledgeTopicLocalization> localizations { get; set; }
    }

    /// <summary>
    /// Localized display content for one help topic without changing retrieval fields.
    /// </summary>
    public sealed class HelpKnowledgeTopicLocalization
    {
        public string title { get; set; }

        public string summary { get; set; }

        public List<HelpKnowledgeQuickAnswer> quickAnswers { get; set; }

        public List<HelpKnowledgeChunk> chunks { get; set; }
    }

    public sealed class HelpKnowledgeQuickAnswer
    {
        public string id { get; set; }

        public string question { get; set; }

        public string answer { get; set; }

        public List<string> sourceChunkIds { get; set; }
    }

    public sealed class HelpKnowledgeChunk
    {
        public string id { get; set; }

        public string heading { get; set; }

        public string body { get; set; }

        public List<string> imageRefs { get; set; }

        public int estimatedTokens { get; set; }
    }

    public sealed class HelpKnowledgeAsset
    {
        public string id { get; set; }

        public string path { get; set; }

        public string mimeType { get; set; }

        public string sha256 { get; set; }

        public string altText { get; set; }

        public string sourcePart { get; set; }
    }

    /// <summary>
    /// Immutable process snapshot used for retrieval and response validation.
    /// </summary>
    public sealed class HelpKnowledgeSnapshot
    {
        public HelpKnowledgeBundle Bundle { get; set; }

        public IDictionary<string, HelpKnowledgeTopic> TopicsById { get; set; }

        public IDictionary<string, HelpKnowledgeModule> ModulesById { get; set; }

        public string BundleHash { get; set; }

        public DateTime LoadedAtUtc { get; set; }

        public string ResolveLocale(string requestedLocale)
        {
            var supported = Bundle.supportedResponseLocales ?? new List<string>();
            var requested = string.IsNullOrWhiteSpace(requestedLocale) ? null : requestedLocale.Trim();
            var exact = supported.FirstOrDefault(value => string.Equals(value, requested, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(exact))
                return exact;

            return string.IsNullOrWhiteSpace(Bundle.defaultLocale) ? "es-ES" : Bundle.defaultLocale;
        }
    }

    public sealed class HelpRetrievalRequest
    {
        public string Question { get; set; }

        public string SelectedTopicId { get; set; }

        public string SelectedModuleId { get; set; }

        public string ResponseLocale { get; set; }
    }

    public sealed class HelpRetrievalResult
    {
        public string Resolution { get; set; }

        public List<HelpRetrievedTopic> Topics { get; set; }

        public List<HelpTopicCandidateDto> Candidates { get; set; }

        public List<HelpRetrievedTopic> Ranking { get; set; }

        public HelpKnowledgeQuickAnswer QuickAnswer { get; set; }

        public HelpKnowledgeTopic QuickAnswerTopic { get; set; }

        public decimal Confidence { get; set; }

        public string Mode { get; set; }
    }

    public sealed class HelpRetrievedTopic
    {
        public HelpKnowledgeTopic Topic { get; set; }

        public decimal Score { get; set; }
    }

    public sealed class HelpAnswerRequest
    {
        public string Question { get; set; }

        public string ResponseLocale { get; set; }

        public string AnswerInstructions { get; set; }

        public IList<HelpConversationMessageRequest> History { get; set; }

        public HelpKnowledgeSnapshot Snapshot { get; set; }

        public HelpRetrievalResult Retrieval { get; set; }
    }

    public sealed class HelpGeneratedAnswer
    {
        public string Answer { get; set; }

        public string Model { get; set; }

        public List<string> CitationChunkIds { get; set; }

        public List<string> ActionRouteKeys { get; set; }

        public int InputTokens { get; set; }

        public int OutputTokens { get; set; }

        public int CachedInputTokens { get; set; }

        public int DocumentTokens { get; set; }
    }

    public sealed class HelpInteractionAnalyticsEvent
    {
        public string InteractionId { get; set; }

        public string UserKey { get; set; }

        public string KnowledgeVersion { get; set; }

        public string Resolution { get; set; }

        public string ResponseLocale { get; set; }

        public string RetrievalMode { get; set; }

        public decimal Confidence { get; set; }

        public List<string> TopicIds { get; set; }

        public List<string> CandidateTopicIds { get; set; }

        public bool QuickAnswerUsed { get; set; }

        public int InputTokens { get; set; }

        public int OutputTokens { get; set; }

        public int CachedInputTokens { get; set; }

        public long LatencyMilliseconds { get; set; }

        public string RedactedQuestion { get; set; }

        public bool IsProblematic { get; set; }
    }

    public sealed class HelpFeedbackAnalyticsEvent
    {
        public string InteractionId { get; set; }

        public string UserKey { get; set; }

        public bool Helpful { get; set; }

        public string Reason { get; set; }

        public string RedactedComment { get; set; }
    }

    public sealed class HelpFeedbackTokenPayload
    {
        public string InteractionId { get; set; }

        public string UserFingerprint { get; set; }

        public DateTime ExpiresAtUtc { get; set; }
    }

    public sealed class HelpFeatureUnavailableException : Exception
    {
        public HelpFeatureUnavailableException(string errorCode, string message, Exception innerException = null)
            : base(message, innerException)
        {
            ErrorCode = errorCode;
        }

        public string ErrorCode { get; }
    }

    public static class HelpErrorCodes
    {
        public const string FeatureDisabled = "HELP_FEATURE_DISABLED";
        public const string KnowledgeUnavailable = "HELP_KNOWLEDGE_UNAVAILABLE";
        public const string TopicNotFound = "HELP_TOPIC_NOT_FOUND";
        public const string InvalidRequest = "HELP_INVALID_REQUEST";
        public const string FeedbackUnavailable = "HELP_FEEDBACK_UNAVAILABLE";
        public const string FeedbackTokenInvalid = "HELP_FEEDBACK_TOKEN_INVALID";
    }
}
