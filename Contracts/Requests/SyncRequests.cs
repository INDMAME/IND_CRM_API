using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Actor of the web operation that produced an outbox message (diagnostics only; never an AX actor).
    /// </summary>
    public class SyncActorDto
    {
        public string realPersonAlias { get; set; }
        public string effectivePersonAlias { get; set; }
        public string supportSessionId { get; set; }
        public string traceId { get; set; }
    }

    /// <summary>
    /// Common envelope of the outbox messages sent by the CRM_INSERTEC worker (docs/ENVIO_AX_VISITAS.md).
    /// </summary>
    public abstract class SyncMessageRequest
    {
        /// <summary>Outbox row id; the AX receiver deduplicates by it.</summary>
        public string messageId { get; set; }

        /// <summary>Web aggregate id (UUID), stable across versions.</summary>
        public string externalId { get; set; }

        /// <summary>Aggregate version carried by the message; lower versions are rejected.</summary>
        public int? version { get; set; }

        /// <summary>Product company code; must match X-IND-Company.</summary>
        public string company { get; set; }

        public SyncActorDto actor { get; set; }
    }

    /// <summary>Base version of the AX record known by the web (null for a first send).</summary>
    public class SyncBaseVersionDto
    {
        public string modifiedAt { get; set; }
        public string hash { get; set; }
    }

    /// <summary>Account reference as AX identifies it (Origen + AccountNum).</summary>
    public class SyncAccountRefDto
    {
        public string origin { get; set; }
        public string accountNum { get; set; }
    }

    /// <summary>Visit header fields sent by the web.</summary>
    public class SyncVisitFieldsDto
    {
        /// <summary>Visit date as yyyy-MM-dd.</summary>
        public string transDate { get; set; }
        public int? visitType { get; set; }
        public int? contactMethod { get; set; }
        public int? rating { get; set; }
        public string description { get; set; }
        public string comments { get; set; }
        public string background { get; set; }
        public string conclusions { get; set; }
        public string ownerAxUserId { get; set; }
        public string ownerCrmUserId { get; set; }
    }

    /// <summary>Attendee of a visit; axRecId identifies an existing AX row, contactRecId an AX contact.</summary>
    public class SyncVisitAttendeeDto
    {
        public string externalId { get; set; }
        public string contactRecId { get; set; }
        public string name { get; set; }
        public string position { get; set; }
        public string type { get; set; }
        public string axRecId { get; set; }
    }

    /// <summary>visit.upsert message: header plus the complete attendee list.</summary>
    public class SyncVisitUpsertRequest : SyncMessageRequest
    {
        public string webCode { get; set; }
        public SyncBaseVersionDto baseVersion { get; set; }
        public string axRecId { get; set; }
        public SyncAccountRefDto account { get; set; }
        public SyncVisitFieldsDto visit { get; set; }
        public List<SyncVisitAttendeeDto> attendees { get; set; }
    }

    /// <summary>visit.delete message.</summary>
    public class SyncVisitDeleteRequest : SyncMessageRequest
    {
        public string webCode { get; set; }
        public string axRecId { get; set; }
        public SyncBaseVersionDto baseVersion { get; set; }
    }

    /// <summary>Prospect (CRMClientePotencialTable) fields sent by the web.</summary>
    public class SyncProspectFieldsDto
    {
        public string tradeName { get; set; }
        public string legalName { get; set; }
        public string city { get; set; }
        public string countryId { get; set; }
        public string statisticsGroup { get; set; }
        public int? contactOrigin { get; set; }
        public string vatNum { get; set; }
        public string responsibleCrmUserId { get; set; }
    }

    /// <summary>account.upsert message (web prospects only).</summary>
    public class SyncProspectUpsertRequest : SyncMessageRequest
    {
        /// <summary>RecId of CRMAccountTable when the prospect already exists in AX.</summary>
        public string axRecId { get; set; }
        public string localCode { get; set; }
        public SyncProspectFieldsDto prospect { get; set; }
    }

    /// <summary>Contact (CRMContactoTable) fields sent by the web.</summary>
    public class SyncContactFieldsDto
    {
        public string name { get; set; }
        public string position { get; set; }
        public string email { get; set; }
        public string phone { get; set; }
    }

    /// <summary>contact.upsert message.</summary>
    public class SyncContactUpsertRequest : SyncMessageRequest
    {
        public string axRecId { get; set; }
        public SyncAccountRefDto account { get; set; }
        public SyncContactFieldsDto contact { get; set; }
    }
}
