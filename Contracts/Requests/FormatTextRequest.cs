using Newtonsoft.Json;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Contains untrusted source text to correct without changing its meaning.
    /// </summary>
    public sealed class FormatTextRequest
    {
        [JsonProperty("text")]
        public string Text { get; set; }

        [JsonProperty("languageId")]
        public string LanguageId { get; set; }
    }
}
