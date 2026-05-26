using System.Collections.Generic;
using Newtonsoft.Json;

namespace IND_CRM_API.Contracts.Notifications
{
    /// <summary>
    /// Generic mail command sent to the internal API mail transport.
    /// </summary>
    public class InternalMailRequest
    {
        public InternalMailRequest()
        {
            To = new List<InternalMailAddress>();
            Cc = new List<InternalMailAddress>();
            Bcc = new List<InternalMailAddress>();
            ReplyTo = new List<InternalMailAddress>();
        }

        [JsonProperty("company")]
        public string Company { get; set; }

        [JsonProperty("from")]
        public InternalMailAddress From { get; set; }

        [JsonProperty("to")]
        public List<InternalMailAddress> To { get; set; }

        [JsonProperty("cc")]
        public List<InternalMailAddress> Cc { get; set; }

        [JsonProperty("bcc")]
        public List<InternalMailAddress> Bcc { get; set; }

        [JsonProperty("replyTo")]
        public List<InternalMailAddress> ReplyTo { get; set; }

        [JsonProperty("subject")]
        public string Subject { get; set; }

        [JsonProperty("htmlBody")]
        public string HtmlBody { get; set; }

        [JsonProperty("textBody")]
        public string TextBody { get; set; }

        [JsonProperty("saveToSentItems")]
        public bool SaveToSentItems { get; set; }

        [JsonProperty("importance")]
        public string Importance { get; set; }

        [JsonProperty("sourceSystem")]
        public string SourceSystem { get; set; }

        [JsonProperty("sourceProcess")]
        public string SourceProcess { get; set; }

        [JsonProperty("eventType")]
        public string EventType { get; set; }

        [JsonProperty("aggregateType")]
        public string AggregateType { get; set; }

        [JsonProperty("aggregateId")]
        public string AggregateId { get; set; }

        [JsonProperty("idempotencyKey")]
        public string IdempotencyKey { get; set; }

        [JsonProperty("correlationId")]
        public string CorrelationId { get; set; }
    }

    /// <summary>
    /// Mailbox address used by the generic internal mail contract.
    /// </summary>
    public class InternalMailAddress
    {
        [JsonProperty("email")]
        public string Email { get; set; }

        [JsonProperty("displayName")]
        public string DisplayName { get; set; }
    }

    /// <summary>
    /// Result returned by the internal mail transport client.
    /// </summary>
    public class InternalMailResponse
    {
        public bool AcceptedByProvider { get; set; }
        public string Provider { get; set; }
        public int? ProviderStatusCode { get; set; }
        public int RecipientCount { get; set; }
        public string CorrelationId { get; set; }
        public string IdempotencyKey { get; set; }
        public string ErrorCode { get; set; }
        public string Message { get; set; }
        public string RawResponse { get; set; }
    }
}
