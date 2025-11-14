using System;
using System.Web.Http;
using IND_CRM_API.Services;
using AxaptaCOMConnector;

namespace IND_CRM_API.Controllers.System
{
    [Authorize]
    [RoutePrefix("api/system")]
    public class EnvironmentController : ApiController
    {
        // Use global AxSession singleton
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;

        private string GetAuthenticatedUsername()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no autenticado o token invalido.");
            return username;
        }

        // ---------------------------------------------------------
        // OBTENER ENTORNO (TEST / PROD) via container
        // X++: static container getEnvironmentName() returns [["TEST"]]
        // ---------------------------------------------------------
        [HttpGet, Route("getEnvironmentName")]
        public IHttpActionResult GetEnvironmentName()
        {
            try
            {
                var username = GetAuthenticatedUsername();
                AxaptaSessionManager.LogStatic($"[API-IN] GetEnvironmentName llamado por {username}");

                object resultObj = _sessionManager.CallMethodByUser(
                    username,
                    "INDCRMApiClass",
                    "getEnvironmentName"
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { environment = "Unknown" });

                var row = root.Peek(1) as IAxaptaContainer;
                if (row == null || row.Length() < 1)
                    return Ok(new { environment = "Unknown" });

                string env = row.Peek(1)?.ToString() ?? string.Empty;
                if (string.IsNullOrWhiteSpace(env))
                    env = "Unknown";

                return Ok(new { environment = env });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetEnvironmentName API: {ex.Message}");
                return InternalServerError(new Exception($"Error GetEnvironmentName: {ex.Message}", ex));
            }
        }

        // ---------------------------------------------------------
        // OBTENER NOMBRE DE COMPANIA via container
        // X++: static container getCompanyName()
        //      returns [[ dataAreaId, Name ]]
        // ---------------------------------------------------------
        [HttpGet, Route("getCompanyName")]
        public IHttpActionResult GetCompanyName()
        {
            try
            {
                var username = GetAuthenticatedUsername();
                AxaptaSessionManager.LogStatic($"[API-IN] GetCompanyName llamado por {username}");

                object resultObj = _sessionManager.CallMethodByUser(
                    username,
                    "INDCRMApiClass",
                    "getCompanyName"
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { companyId = "", companyName = "", company = "" });

                var row = root.Peek(1) as IAxaptaContainer;
                if (row == null || row.Length() < 2)
                    return Ok(new { companyId = "", companyName = "", company = "" });

                string companyId = row.Peek(1)?.ToString() ?? string.Empty;
                string companyName = row.Peek(2)?.ToString() ?? string.Empty;

                return Ok(new
                {
                    companyId,
                    companyName,
                    company = companyName
                });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetCompanyName API: {ex.Message}");
                return InternalServerError(new Exception($"Error GetCompanyName: {ex.Message}", ex));
            }
        }
    }
}
