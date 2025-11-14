using IND_CRM_API.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/activities")]
    public class CrmActivitiesController : BaseCrmController
    {
        // Use global AxSession singleton
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;
 
        // ---------------------------------------------------------
        // CREATE ACTIVIDADES (Container)
        // ---------------------------------------------------------
        public class CreateActivityRequest
        {
            public string accountNum { get; set; }
            public string visitType { get; set; }
            public string userId { get; set; }
            public string description { get; set; }
            public string transDate { get; set; }
            public string comentarios { get; set; }
            public string antecedentes { get; set; }
            public string conclusiones { get; set; }
        }

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
                AxaptaSessionManager.LogStatic($"[ERROR] CreateActivity API: {ex.Message}");
                return InternalServerError(new Exception($"Error CreateActivity: {ex.Message}", ex));
            }
        }

        // ---------------------------------------------------------
        // GET ACTIVITIES (via container)
        // ---------------------------------------------------------
        public class GetActivitiesRequest
        {
            public string userId { get; set; }
            public string fromDate { get; set; }
            public string toDate { get; set; }
        }

        [HttpPost, Route("list")]
        public IHttpActionResult GetActivitiesContainer([FromBody] GetActivitiesRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Body vacio o invalido.");

                AxaptaSessionManager.LogStatic($"[API-IN] GetActivitiesContainer llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> userId: {body.userId}");
                AxaptaSessionManager.LogStatic($" -> fromDate: {body.fromDate}");
                AxaptaSessionManager.LogStatic($" -> toDate: {body.toDate}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.userId?.Trim() ?? string.Empty);

                string from = string.Empty;
                if (!string.IsNullOrWhiteSpace(body.fromDate))
                {
                    DateTime dt = DateTime.Parse(body.fromDate);
                    from = dt.ToString("yyyyMMdd");
                }
                con.Append(from);

                string to = string.Empty;
                if (!string.IsNullOrWhiteSpace(body.toDate))
                {
                    DateTime dt = DateTime.Parse(body.toDate);
                    to = dt.ToString("yyyyMMdd");
                }
                con.Append(to);

                AxaptaSessionManager.LogStatic("[CONTAINER] Enviado a AX (GetActivitiesContainer):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getActivityContainer",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { total = 0, items = new object[0] });

                var items = new List<object>();

                for (int i = 1; i <= root.Length(); i++)
                {
                    var act = root.Peek(i) as AxaptaCOMConnector.IAxaptaContainer;
                    if (act == null) continue;

                    string actividadId = act.Peek(1)?.ToString() ?? string.Empty;
                    string name = act.Peek(2)?.ToString() ?? string.Empty;
                    string transDate = act.Peek(3)?.ToString() ?? string.Empty;
                    string country = act.Peek(4)?.ToString() ?? string.Empty;
                    string actividadTypeS = act.Peek(5)?.ToString() ?? string.Empty;
                    string description = act.Peek(6)?.ToString() ?? string.Empty;

                    var asistentesList = new List<object>();
                    var asistCon = act.Peek(7) as AxaptaCOMConnector.IAxaptaContainer;

                    if (asistCon != null)
                    {
                        for (int j = 1; j <= asistCon.Length(); j++)
                        {
                            var asist = asistCon.Peek(j) as AxaptaCOMConnector.IAxaptaContainer;
                            if (asist == null) continue;

                            asistentesList.Add(new
                            {
                                AsistenteId = asist.Peek(1)?.ToString() ?? string.Empty,
                                AsistenteTipo = asist.Peek(2)?.ToString() ?? string.Empty,
                                AsistenteCargo = asist.Peek(3)?.ToString() ?? string.Empty
                            });
                        }
                    }

                    items.Add(new
                    {
                        ActividadId = actividadId,
                        Name = name,
                        TransDate = transDate,
                        Country = country,
                        ActividadType = actividadTypeS,
                        Description = description,
                        Asistentes = asistentesList
                    });
                }

                return Ok(new { total = items.Count, items });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetActivitiesContainer API: {ex.Message}");
                return InternalServerError(new Exception($"Error GetActivitiesContainer: {ex.Message}", ex));
            }
        }

        public class TestActivitiesRequest
        {
            public string userId { get; set; }
            public string fromDate { get; set; }
            public string toDate { get; set; }
            public string actividadType { get; set; }
        }

        [HttpPost, Route("test")]
        public IHttpActionResult TestActivitiesContainer([FromBody] TestActivitiesRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Body vacio o invalido.");

                AxaptaSessionManager.LogStatic($"[API-IN] TestActivitiesContainer llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> userId: {body.userId}");
                AxaptaSessionManager.LogStatic($" -> fromDate: {body.fromDate}");
                AxaptaSessionManager.LogStatic($" -> toDate: {body.toDate}");
                AxaptaSessionManager.LogStatic($" -> actividadType: {body.actividadType}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.userId?.Trim() ?? string.Empty);

                string f = string.Empty;
                if (!string.IsNullOrWhiteSpace(body.fromDate))
                    f = DateTime.Parse(body.fromDate).ToString("yyyyMMdd");
                con.Append(f);

                string t = string.Empty;
                if (!string.IsNullOrWhiteSpace(body.toDate))
                    t = DateTime.Parse(body.toDate).ToString("yyyyMMdd");
                con.Append(t);

                con.Append(body.actividadType?.Trim() ?? string.Empty);

                AxaptaSessionManager.LogStatic("[CONTAINER] Enviado a AX (TestActivitiesContainer):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getActivityContainer",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;

                if (root == null)
                    return Ok(new { error = "AX devolvio NULL", type = resultObj?.GetType().FullName });

                int len = root.Length();

                var preview = new List<object>();
                int max = Math.Min(5, len);

                for (int i = 1; i <= max; i++)
                {
                    var item = root.Peek(i);
                    string type = item?.GetType().FullName ?? "null";

                    preview.Add(new
                    {
                        index = i,
                        value = item?.ToString(),
                        type
                    });
                }

                return Ok(new
                {
                    dotnetType = resultObj.GetType().FullName,
                    isContainer = true,
                    length = len,
                    preview
                });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] TestActivitiesContainer API: {ex.Message}");
                return InternalServerError(new Exception($"Error TestActivitiesContainer: {ex.Message}", ex));
            }
        }
    }
}
