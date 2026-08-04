using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request body for a question grounded in the CRM help knowledge bundle.
    /// </summary>
    public sealed class AskHelpRequest
    {
        public string question { get; set; }

        public string responseLocale { get; set; }

        public string selectedTopicId { get; set; }

        public List<HelpConversationMessageRequest> history { get; set; }

        public string clientInteractionId { get; set; }
    }

    /// <summary>
    /// One bounded conversation turn supplied by the Home help chat.
    /// </summary>
    public sealed class HelpConversationMessageRequest
    {
        public string role { get; set; }

        public string content { get; set; }
    }

    /// <summary>
    /// Request body for recording explicit user feedback on a help answer.
    /// </summary>
    public sealed class HelpFeedbackRequest
    {
        public string feedbackToken { get; set; }

        public bool? helpful { get; set; }

        public string reason { get; set; }

        public string comment { get; set; }
    }
}
