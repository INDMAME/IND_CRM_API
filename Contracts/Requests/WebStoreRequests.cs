using System.Collections.Generic;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Credentials of the technical AX user allowed on api/crm/webstore/* (AX_WEBSTORE_USER).
    /// </summary>
    public class WebStoreLoginRequest
    {
        /// <summary>AX user id of the technical user.</summary>
        public string username { get; set; }

        /// <summary>AX password; never logged or echoed.</summary>
        public string password { get; set; }
    }

    /// <summary>
    /// Common envelope of the web store messages: container positions 1 to 4 plus the AX RecId at position 5.
    /// </summary>
    public abstract class WebStoreMessageRequest
    {
        /// <summary>Optional AX company; when present it must match X-IND-Company, which is the value sent to AX.</summary>
        public string company { get; set; }

        /// <summary>Message id (max 36); AX deduplicates by it.</summary>
        public string messageId { get; set; }

        /// <summary>Web record id (max 36), stable across versions.</summary>
        public string externalId { get; set; }

        /// <summary>Record version, integer greater than or equal to 1.</summary>
        public int? version { get; set; }

        /// <summary>AX RecId of the record (32-bit integer); null travels as 0.</summary>
        public long? axRecId { get; set; }
    }

    /// <summary>
    /// Message of POST api/crm/webstore/visits/upsert (CRMWebVisitStore::upsert).
    /// </summary>
    public class WebStoreVisitUpsertRequest : WebStoreMessageRequest
    {
        /// <summary>Final values of the CRMActividadTable fields.</summary>
        public WebStoreActivityDto activity { get; set; }

        /// <summary>Complete attendee list; AX deletes the attendees of the visit that are not in it.</summary>
        public List<WebStoreAttendeeDto> attendees { get; set; }
    }

    /// <summary>
    /// Message of POST api/crm/webstore/visits/delete (CRMWebVisitStore::remove).
    /// </summary>
    public class WebStoreVisitDeleteRequest : WebStoreMessageRequest
    {
    }

    /// <summary>
    /// Message of POST api/crm/webstore/contacts/upsert (CRMWebContactStore::upsert).
    /// </summary>
    public class WebStoreContactUpsertRequest : WebStoreMessageRequest
    {
        /// <summary>Final values of the CRMContactoTable fields.</summary>
        public WebStoreContactDto contact { get; set; }
    }

    /// <summary>
    /// Message of POST api/crm/webstore/prospects/upsert (CRMWebProspectStore::upsert).
    /// </summary>
    public class WebStoreProspectUpsertRequest : WebStoreMessageRequest
    {
        /// <summary>Final values of the CRMClientePotencialTable fields.</summary>
        public WebStoreProspectDto prospect { get; set; }
    }

    /// <summary>
    /// Visit fields as decided by the web; the API passes them through without business rules.
    /// </summary>
    public class WebStoreActivityDto
    {
        public string accountNum { get; set; }
        public int? origin { get; set; }
        public string custAccount { get; set; }
        public int? originCustAccount { get; set; }
        public long? refRecId { get; set; }
        public string name { get; set; }
        public string country { get; set; }
        public string userId { get; set; }
        public string createdByUserId { get; set; }
        public int? division { get; set; }

        /// <summary>Visit date, YYYY-MM-DD.</summary>
        public string transDate { get; set; }

        /// <summary>Real date, YYYY-MM-DD.</summary>
        public string realDate { get; set; }

        public int? activityType { get; set; }
        public int? status { get; set; }
        public int? priority { get; set; }
        public int? visitType { get; set; }
        public int? contactMethod { get; set; }
        public int? rating { get; set; }
        public string description { get; set; }
        public string comments { get; set; }
        public string background { get; set; }
        public string conclusions { get; set; }
    }

    /// <summary>
    /// One attendee of the visit (CRMCustVendVisitaAsistenteTable), seven container values.
    /// </summary>
    public class WebStoreAttendeeDto
    {
        public string externalId { get; set; }
        public long? axRecId { get; set; }
        public int? type { get; set; }
        public string name { get; set; }
        public string position { get; set; }
        public long? contactRecId { get; set; }
        public string createdByUserId { get; set; }
    }

    /// <summary>
    /// Contact fields as decided by the web (CRMContactoTable).
    /// </summary>
    public class WebStoreContactDto
    {
        public string accountNum { get; set; }
        public int? origin { get; set; }
        public string name { get; set; }
        public string position { get; set; }
        public string email { get; set; }
        public string phone { get; set; }
        public string company { get; set; }
        public string companyPhone { get; set; }
    }

    /// <summary>
    /// Customer prospect fields as decided by the web (CRMClientePotencialTable).
    /// </summary>
    public class WebStoreProspectDto
    {
        public string name { get; set; }
        public string legalName { get; set; }
        public string city { get; set; }
        public string countryId { get; set; }
        public string statisticsGroup { get; set; }
        public int? contactOrigin { get; set; }
        public string vatNum { get; set; }
        public string userId { get; set; }
    }
}
