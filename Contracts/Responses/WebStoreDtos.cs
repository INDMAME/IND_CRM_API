using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Token issued by POST api/crm/webstore/auth/login; valid only on api/crm/webstore/*.
    /// </summary>
    public class WebStoreTokenDto
    {
        [JsonProperty("token")] public string Token { get; set; }

        /// <summary>Expiration instant in UTC.</summary>
        [JsonProperty("expires")] public DateTime Expires { get; set; }
    }

    /// <summary>
    /// Answer of GET api/crm/webstore/ping (CRMWebStoreCommon::ping).
    /// </summary>
    public class WebStorePingDto
    {
        [JsonProperty("company")] public string Company { get; set; }

        [JsonProperty("companyName")] public string CompanyName { get; set; }

        [JsonProperty("axUser")] public string AxUser { get; set; }
    }

    /// <summary>
    /// Result of a web store message, read from the AX result container (camelCase for the web client).
    /// </summary>
    public class WebStoreResultDto
    {
        /// <summary>applied | duplicate | rejected</summary>
        [JsonProperty("result")] public string Result { get; set; }

        /// <summary>RecId of the written record; null when AX returns 0.</summary>
        [JsonProperty("recId")] public int? RecId { get; set; }

        /// <summary>ActividadId or ClientePotencialId; null when empty.</summary>
        [JsonProperty("code")] public string Code { get; set; }

        /// <summary>AX modified date and time as ISO 8601 with the Europe/Madrid offset; null when unknown.</summary>
        [JsonProperty("modifiedAt")] public string ModifiedAt { get; set; }

        /// <summary>RecId of the CRMAccountTable row (prospects only); null when AX returns 0.</summary>
        [JsonProperty("accountRecId")] public int? AccountRecId { get; set; }

        [JsonProperty("attendees")] public List<WebStoreAttendeeResultDto> Attendees { get; set; }

        [JsonProperty("errorCode")] public string ErrorCode { get; set; }

        [JsonProperty("errorMessage")] public string ErrorMessage { get; set; }

        [JsonProperty("messageId")] public string MessageId { get; set; }
    }

    /// <summary>
    /// RecId assigned in AX to one attendee of the message.
    /// </summary>
    public class WebStoreAttendeeResultDto
    {
        [JsonProperty("externalId")] public string ExternalId { get; set; }

        [JsonProperty("recId")] public int? RecId { get; set; }
    }
}
