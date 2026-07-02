using AxaptaCOMConnector;

namespace IND_CRM_API.Services.Interfaces
{
    public interface IAxaptaSessionManager
    {
        bool CreateOrGetSession(string username, string password, JwtService.JwtTokenInfo tokenInfo);
        bool RefreshSessionToken(string username, JwtService.JwtTokenInfo newToken, string oldToken);
        AxaptaComSession GetAxInstanceForUser(string username);
        object CallMethodByUser(string username, string className, string methodName, object args = null);
        IAxaptaContainer CallContainerMethodByUser(string username, string className, string methodName, object[] args = null);
    }
}

