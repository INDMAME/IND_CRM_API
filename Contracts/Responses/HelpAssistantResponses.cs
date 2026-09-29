using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Localized help catalog returned to the Home assistant.
    /// </summary>
    public sealed class HelpCatalogDto
    {
        public string KnowledgeVersion { get; set; }

        public string DefaultLocale { get; set; }

        public string ResponseLocale { get; set; }

        public List<HelpCatalogModuleDto> Modules { get; set; }
    }

    public sealed class HelpCatalogModuleDto
    {
        public string Id { get; set; }

        public string Title { get; set; }

        public string Description { get; set; }

        public int Order { get; set; }

        public List<HelpCatalogTopicDto> Topics { get; set; }
    }

    public sealed class HelpCatalogTopicDto
    {
        public string Id { get; set; }

        public string ModuleId { get; set; }

        public string Title { get; set; }

        public string Summary { get; set; }

        public string RouteKey { get; set; }

        public bool HasQuickAnswers { get; set; }
    }

    /// <summary>
    /// Canonical topic content that can be rendered without an AI call.
    /// </summary>
    public sealed class HelpTopicDto
    {
        public string Id { get; set; }

        public string ModuleId { get; set; }

        public string Title { get; set; }

        public string Summary { get; set; }

        public string RouteKey { get; set; }

        public List<string> PrerequisiteTopicIds { get; set; }

        public List<string> RelatedTopicIds { get; set; }

        public List<HelpTopicChunkDto> Chunks { get; set; }

        public List<HelpQuickAnswerDto> QuickAnswers { get; set; }

        public string KnowledgeVersion { get; set; }

        public string ResponseLocale { get; set; }
    }

    public sealed class HelpTopicChunkDto
    {
        public string Id { get; set; }

        public string Heading { get; set; }

        public string Body { get; set; }

        public List<string> ImageRefs { get; set; }
    }

    public sealed class HelpQuickAnswerDto
    {
        public string Id { get; set; }

        public string Question { get; set; }

        public string Answer { get; set; }

        public List<string> SourceChunkIds { get; set; }
    }

    /// <summary>
    /// Result of retrieval and, when needed, grounded answer generation.
    /// </summary>
    public sealed class AskHelpResponse
    {
        public string InteractionId { get; set; }

        public string Resolution { get; set; }

        public string Answer { get; set; }

        public List<HelpTopicCandidateDto> Candidates { get; set; }

        public List<HelpAnswerSourceDto> Sources { get; set; }

        public List<HelpAnswerActionDto> Actions { get; set; }

        public string KnowledgeVersion { get; set; }

        public string ResponseLocale { get; set; }

        public string FeedbackToken { get; set; }

        public bool QuickAnswerUsed { get; set; }

        public string Model { get; set; }
    }

    public sealed class HelpTopicCandidateDto
    {
        public string TopicId { get; set; }

        public string Title { get; set; }

        public string Summary { get; set; }

        public decimal Score { get; set; }
    }

    public sealed class HelpAnswerSourceDto
    {
        public string TopicId { get; set; }

        public string TopicTitle { get; set; }

        public string ChunkId { get; set; }

        public string Heading { get; set; }
    }

    public sealed class HelpAnswerActionDto
    {
        public string Type { get; set; }

        public string RouteKey { get; set; }

        public string Label { get; set; }
    }

    public sealed class HelpFeedbackResponse
    {
        public bool Accepted { get; set; }
    }
}
