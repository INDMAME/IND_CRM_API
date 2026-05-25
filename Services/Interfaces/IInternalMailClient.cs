using IND_CRM_API.Contracts.Notifications;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Client for the generic mail endpoint exposed by IND_INTERNAL_API.
    /// </summary>
    public interface IInternalMailClient
    {
        bool IsConfigured { get; }
        InternalMailResponse Send(InternalMailRequest request);
    }
}
