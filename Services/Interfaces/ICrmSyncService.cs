using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Bridge between the sync endpoints and the X++ receiver INDCrmSyncService.
    /// Every method runs one COM call with the authenticated technical user.
    /// </summary>
    public interface ICrmSyncService
    {
        SyncPingDto Ping(string username, string company);

        SyncResultDto UpsertVisit(string username, string company, SyncVisitUpsertRequest message);

        SyncResultDto DeleteVisit(string username, string company, SyncVisitDeleteRequest message);

        SyncResultDto UpsertProspect(string username, string company, SyncProspectUpsertRequest message);

        SyncResultDto UpsertContact(string username, string company, SyncContactUpsertRequest message);

        /// <summary>Returns the current version of an AX visit, or null when it does not exist.</summary>
        SyncVersionDto GetVisitVersion(string username, string company, int recId);

        /// <summary>Returns the AX visit snapshot, or null when it does not exist.</summary>
        SyncVisitSnapshotDto GetVisit(string username, string company, int recId);
    }
}
