using System;
using System.Web.Http;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Controllers
{
    public abstract class BaseCrmController : ApiController
    {
        protected readonly IAxaptaSessionManager SessionManager;

        protected BaseCrmController(IAxaptaSessionManager sessionManager)
        {
            SessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
        }

        protected string GetAuthenticatedUsername()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("User not authenticated or invalid token.");
            return username;
        }
    }
}
