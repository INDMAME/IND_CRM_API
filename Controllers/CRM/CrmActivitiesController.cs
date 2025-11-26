using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Linq;
using System.Web.Http;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/activities")]
    public class CrmActivitiesController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;
 
        public CrmActivitiesController(IAxaptaSessionManager sessionManager) : base(sessionManager)
        {
            _sessionManager = sessionManager;
        }

        // CREATE ACTIVIDADES (Container)
        [HttpPost, Route("create")]
        public IHttpActionResult CreateActivity([FromBody] CreateActivityRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Datos vacios o invalidos.");

                AxaptaSessionManager.LogStatic($"[API-IN] CreateActivity llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> accountNum: {body.accountNum}");
                AxaptaSessionManager.LogStatic($" -> visitType: {body.visitType}");
                AxaptaSessionManager.LogStatic($" -> userId: {body.userId}");
                AxaptaSessionManager.LogStatic($" -> transDate: {body.transDate}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.accountNum?.Trim() ?? string.Empty);
                con.Append(body.visitType?.Trim() ?? string.Empty);
                con.Append(body.userId?.Trim() ?? string.Empty);
                con.Append(body.description?.Trim() ?? string.Empty);

                DateTime dt = DateTime.Parse(body.transDate);
                string axDate = dt.ToString("yyyyMMdd");
                con.Append(axDate);

                con.Append(body.comentarios ?? string.Empty);
                con.Append(body.antecedentes ?? string.Empty);
                con.Append(body.conclusiones ?? string.Empty);

                AxaptaSessionManager.LogStatic("Container enviado a AX (CreateActivity):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "createActivity",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { success = false, message = "Contenedor vacio." });

                var row = root.Peek(1) as AxaptaCOMConnector.IAxaptaContainer;

                if (row == null || row.Length() < 2)
                    return Ok(new { success = false, message = "Estructura inesperada en la respuesta." });

                string result = row.Peek(1)?.ToString() ?? string.Empty;
                string message = row.Peek(2)?.ToString() ?? string.Empty;

                AxaptaSessionManager.LogStatic($"[API-OUT] Resultado CreateActivity: {result} - {message}");

                return Ok(new { success = result == "1", message });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] CreateActivity API: {ex.Message}");
                return InternalServerError(new Exception($"Error CreateActivity: {ex.Message}", ex));
            }
        }
    }
}

