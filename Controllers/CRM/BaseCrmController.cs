using System;
using System.Web.Http;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Services;

namespace IND_CRM_API.Controllers
{
    public abstract class BaseCrmController : ApiController
    {
        protected readonly IAxaptaSessionManager SessionManager;
        protected readonly IAxLogger Logger;

        protected BaseCrmController(IAxaptaSessionManager sessionManager, IAxLogger logger)
        {
            SessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
