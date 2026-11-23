using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Bridge between the api/crm/webstore/* routes and the X++ classes CRMWeb*: builds the
    /// positional container, calls AX through the per-request COM session and reads the result.
    /// </summary>
    public interface ICrmWebStoreService
    {
        /// <summary>CRMWebStoreCommon::ping for the company.</summary>
        WebStorePingDto Ping(string username, string company);

        /// <summary>CRMWebVisitStore::upsert.</summary>
        WebStoreResultDto UpsertVisit(string username, string company, WebStoreVisitUpsertRequest message);

        /// <summary>CRMWebVisitStore::remove.</summary>
        WebStoreResultDto DeleteVisit(string username, string company, WebStoreVisitDeleteRequest message);

        /// <summary>CRMWebContactStore::upsert.</summary>
        WebStoreResultDto UpsertContact(string username, string company, WebStoreContactUpsertRequest message);

        /// <summary>CRMWebProspectStore::upsert.</summary>
        WebStoreResultDto UpsertProspect(string username, string company, WebStoreProspectUpsertRequest message);
    }
}
