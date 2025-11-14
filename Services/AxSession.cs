using IND_CRM_API.Services;

namespace IND_CRM_API.Services
{
    // Global Axapta session manager (singleton)
    public static class AxSession
    {
        public static readonly AxaptaSessionManager Manager = new AxaptaSessionManager();
    }
}
