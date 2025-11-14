using IND_CRM_API.Services;
using System;
using System.Web.Http;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/visits")]
    public class CrmVisitsController : BaseCrmController
    {
        // Use global AxSession singleton
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;
         

        // ---------------------------------------------------------
        // CREAR ASISTENTE (Container)
        // ---------------------------------------------------------
        public class CreateVisitaAsistenteRequest
        {
            public string refRecIdActividad { get; set; }
            public string asistenteTipo { get; set; }
            public string asistenteId { get; set; }
            public string contactoRecId { get; set; }
        }

        [HttpPost, Route("createVisitaAsistente")]
        public IHttpActionResult CreateVisitaAsistente([FromBody] CreateVisitaAsistenteRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Body vacio o invalido.");

                AxaptaSessionManager.LogStatic($"[API-IN] CreateVisitaAsistente llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> refRecIdActividad: {body.refRecIdActividad}");
                AxaptaSessionManager.LogStatic($" -> asistenteTipo: {body.asistenteTipo}");
                AxaptaSessionManager.LogStatic($" -> asistenteId: {body.asistenteId}");
                AxaptaSessionManager.LogStatic($" -> contactoRecId: {body.contactoRecId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.refRecIdActividad?.Trim() ?? string.Empty);
                con.Append(body.asistenteTipo?.Trim() ?? string.Empty);
                con.Append(body.asistenteId?.Trim() ?? string.Empty);
                con.Append(body.contactoRecId?.Trim() ?? string.Empty);

                AxaptaSessionManager.LogStatic("[CONTAINER] Enviado a AX (CreateVisitaAsistente):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

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
                    return Ok(new { success = false, message = "Estructura invalida de AX." });

                string rawSuccess = row.Peek(1)?.ToString()?.Trim().ToLower() ?? "false";
                bool success =
                       rawSuccess == "1"
                    || rawSuccess == "true";

                string message = row.Peek(2)?.ToString() ?? string.Empty;

                return Ok(new { success, message });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] CreateVisitaAsistente API: {ex.Message}");
                return InternalServerError(new Exception($"Error CreateVisitaAsistente: {ex.Message}", ex));
            }
        }
    }
}
