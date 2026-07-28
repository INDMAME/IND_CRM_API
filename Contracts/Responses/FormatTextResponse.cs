using Newtonsoft.Json;
using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Contains the complete formatted text and any fragments that need review.
    /// </summary>
    public sealed class FormatTextResponse
    {
        [JsonProperty("formattedText")]
        public string FormattedText { get; set; }

        [JsonProperty("hasChanges")]
        public bool HasChanges { get; set; }

        [JsonProperty("warnings")]
        public List<FormatTextWarning> Warnings { get; set; } = new List<FormatTextWarning>();
    }

    /// <summary>
    /// Identifies ambiguous source content that the service preserved.
    /// </summary>
    public sealed class FormatTextWarning
    {
        [JsonProperty("fragment")]
        public string Fragment { get; set; }

        [JsonProperty("reason")]
        public string Reason { get; set; }
    }
}
