using System;
using System.Web.Http;
using IND_CRM_API.Services;
//TEST
namespace IND_CRM_API.Controllers
{
    public abstract class BaseCrmController : ApiController
    {
        protected static readonly AxaptaSessionManager SessionManager = new AxaptaSessionManager();

        protected string GetAuthenticatedUsername()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username)) 
                throw new Exception("User not authenticated or invalid token.");
            return username;
        }
    }
}
