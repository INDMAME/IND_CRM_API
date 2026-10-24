using System.Collections.Generic;
using Newtonsoft.Json;

namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Acknowledgement of a sync message. Serialized in camelCase because the CRM_INSERTEC
    /// worker reads these fields literally (docs/ENVIO_AX_VISITAS.md, SyncResult).
    /// </summary>
    public class SyncResultDto
    {
        /// <summary>applied | duplicate | divergent | rejected</summary>
        [JsonProperty("result")] public string Result { get; set; }

        /// <summary>AX RecId of the record the web must link (visit, account or contact).</summary>
        [JsonProperty("recId")] public string RecId { get; set; }

        [JsonProperty("axCompany")] public string AxCompany { get; set; }

        /// <summary>AX code assigned (ActividadId or ClientePotencialId); null when unchanged.</summary>
        [JsonProperty("code")] public string Code { get; set; }

        /// <summary>Modified date and time of the AX record after the operation, ISO 8601 UTC.</summary>
        [JsonProperty("modifiedAt")] public string ModifiedAt { get; set; }

        /// <summary>Receiver fingerprint of the applied record (diagnostic; the product keeps its own hash).</summary>
        [JsonProperty("hash")] public string Hash { get; set; }

        [JsonProperty("errorCode")] public string ErrorCode { get; set; }

        [JsonProperty("errorMessage")] public string ErrorMessage { get; set; }

        [JsonProperty("detail")] public string Detail { get; set; }

        /// <summary>Current AX version when the result is divergent.</summary>
        [JsonProperty("current")] public SyncVersionDto Current { get; set; }

        [JsonProperty("attendees")] public List<SyncAttendeeResultDto> Attendees { get; set; }

        [JsonProperty("messageId")] public string MessageId { get; set; }
    }

    /// <summary>Base version of an AX visit.</summary>
    public class SyncVersionDto
    {
        [JsonProperty("modifiedAt")] public string ModifiedAt { get; set; }

        [JsonProperty("hash")] public string Hash { get; set; }
    }

    /// <summary>AX RecId assigned to one attendee of the visit.</summary>
    public class SyncAttendeeResultDto
    {
        [JsonProperty("externalId")] public string ExternalId { get; set; }

        [JsonProperty("recId")] public string RecId { get; set; }

        [JsonProperty("hash")] public string Hash { get; set; }
    }

    /// <summary>Result of the sync ping: proves the bearer, the role and the AX company.</summary>
    public class SyncPingDto
    {
        [JsonProperty("user")] public string User { get; set; }

        [JsonProperty("company")] public string Company { get; set; }

        [JsonProperty("companyName")] public string CompanyName { get; set; }

        [JsonProperty("axUser")] public string AxUser { get; set; }
    }

    /// <summary>Visit as AX holds it, used by the web to accept the AX version of a divergent visit.</summary>
    public class SyncVisitSnapshotDto
    {
        [JsonProperty("recId")] public string RecId { get; set; }

        [JsonProperty("code")] public string Code { get; set; }

        [JsonProperty("modifiedAt")] public string ModifiedAt { get; set; }

        [JsonProperty("hash")] public string Hash { get; set; }

        /// <summary>Visit date as yyyy-MM-dd.</summary>
        [JsonProperty("transDate")] public string TransDate { get; set; }

        [JsonProperty("visitType")] public int VisitType { get; set; }

        [JsonProperty("contactMethod")] public int ContactMethod { get; set; }

        [JsonProperty("rating")] public int Rating { get; set; }

        [JsonProperty("description")] public string Description { get; set; }

        [JsonProperty("comments")] public string Comments { get; set; }

        [JsonProperty("background")] public string Background { get; set; }

        [JsonProperty("conclusions")] public string Conclusions { get; set; }

        [JsonProperty("accountNum")] public string AccountNum { get; set; }

        [JsonProperty("ownerAxUserId")] public string OwnerAxUserId { get; set; }
    }
}
