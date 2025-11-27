using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Web.Http;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/visits")]
    public class CrmVisitsController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;
         
        public CrmVisitsController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        // CREAR ASISTENTE (Container)
        [HttpPost, Route("createVisitaAsistente")]
        public IHttpActionResult CreateVisitaAsistente([FromBody] CreateVisitaAsistenteRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null || !ModelState.IsValid)
                    return BadRequest(ModelState);

                Logger.Log($"[API-IN] CreateVisitaAsistente llamado por {username}");
                Logger.Log($" -> refRecIdActividad: {body.refRecIdActividad}");
                Logger.Log($" -> asistenteTipo: {body.asistenteTipo}");
                Logger.Log($" -> asistenteId: {body.asistenteId}");
                Logger.Log($" -> contactoRecId: {body.contactoRecId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.refRecIdActividad?.Trim() ?? string.Empty);
                con.Append(body.asistenteTipo?.Trim() ?? string.Empty);
                con.Append(body.asistenteId?.Trim() ?? string.Empty);
                con.Append(body.contactoRecId?.Trim() ?? string.Empty);

                Logger.Log("[CONTAINER] Enviado a AX (CreateVisitaAsistente):");
                for (int i = 1; i <= con.Length(); i++)
                    Logger.Log($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "createVisitaAsistente",
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

                bool successFlag =
                    string.Equals(result, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);

                Logger.Log($"[API-OUT] Resultado CreateVisitaAsistente: {result} - {message}");

                return Ok(new { success = successFlag, message });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateVisitaAsistente API: {ex.Message}");
                return InternalServerError(new Exception($"Error CreateVisitaAsistente: {ex.Message}", ex));
            }
        }
    }
}

