using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Helpers;
using AxaptaCOMConnector;
using System;
using System.Linq;
using System.Web.Http;
using System.Runtime.InteropServices;
using Newtonsoft.Json;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/activities")]
    public class CrmActivitiesController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;
 
        public CrmActivitiesController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        public class GetActivitiesRequest
        {
            public string userId { get; set; }
            public string fromDate { get; set; }
            public string toDate { get; set; }
        }

        // CREATE ACTIVIDADES (Container)
        [HttpPost, Route("create")]
        public IHttpActionResult CreateActivity([FromBody] CreateActivityRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null || !ModelState.IsValid)
                    return BadRequest(ModelState);

                Logger.Log($"[API-IN] CreateActivity llamado por {username}");
                Logger.Log($" -> accountNum: {body.accountNum}");
                Logger.Log($" -> visitType: {body.visitType}");
                Logger.Log($" -> userId: {body.userId}");
                Logger.Log($" -> transDate: {body.transDate}");

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

                Logger.Log("Container enviado a AX (CreateActivity):");
                for (int i = 1; i <= con.Length(); i++)
                    Logger.Log($" - Item {i}: {con.Peek(i)}");

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

                bool successFlag =
                    string.Equals(result, "1", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(result, "true", StringComparison.OrdinalIgnoreCase);

                Logger.Log($"[API-OUT] Resultado CreateActivity: {result} - {message}");

                return Ok(new { success = successFlag, message });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateActivity API: {ex}");
                return InternalServerError(new Exception($"Error CreateActivity: {ex.GetType().FullName} {ex.Message}", ex));
            }
        }

        // LIST ACTIVITIES (container)
        [HttpPost, Route("list")]
        public IHttpActionResult ListActivities([FromBody] GetActivitiesRequest body)
        {
            object resultObj = null;
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null || !ModelState.IsValid)
                    return BadRequest(ModelState);

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.userId ?? string.Empty);
                con.Append(DateTime.Parse(body.fromDate).ToString("yyyyMMdd"));
                con.Append(DateTime.Parse(body.toDate).ToString("yyyyMMdd"));

                resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getActivityContainer",
                    con
                );

                var root = resultObj as AxaptaCOMConnector.IAxaptaContainer;
                if (root == null)
                    return Ok(new { success = false, message = "Contenedor nulo." });

                try
                {
                    var data = Helpers.AxContainerHelper.ToArray(root);
                    return Ok(new { success = true, data });
                }
                catch (COMException comEx)
                {
                    // Attempt to safely serialize the resultObj for diagnostics
                    string serialized = SafeSerializeResultObject(resultObj);
                    Logger.Log($"[ERROR] ListActivities COMException: HResult={comEx.ErrorCode} Message={comEx.Message} {comEx}");
                    Logger.Log($"[ERROR] Serialized resultObj: {serialized}");
                    return InternalServerError(new Exception($"Error ListActivities: COMException HResult={comEx.ErrorCode} Message={comEx.Message}", comEx));
                }
            }
            catch (Exception ex)
            {
                // If there is a COMException at higher level, try to log serialized resultObj as well
                try
                {
                    if (resultObj != null)
                    {
                        string serialized = SafeSerializeResultObject(resultObj);
                        Logger.Log($"[ERROR] ListActivities - resultObj serialized on exception: {serialized}");
                    }
                }
                catch { /* ignore */ }

                Logger.Log($"[ERROR] ListActivities API: {ex}");
                int h = ex is COMException cex ? cex.ErrorCode : 0;
                return InternalServerError(new Exception($"Error ListActivities: {ex.GetType().FullName} {ex.Message} HResult={h}", ex));
            }
        }

        private string SafeSerializeResultObject(object obj)
        {
            try
            {
                if (obj == null) return "<null>";

                // If it's a container, convert to array first (this returns error placeholders instead of throwing)
                if (obj is AxaptaCOMConnector.IAxaptaContainer con)
                {
                    try
                    {
                        var arr = AxContainerHelper.ToArray(con);
                        return JsonConvert.SerializeObject(arr);
                    }
                    catch (Exception ex)
                    {
                        return $"<Failed to ToArray container: {ex.Message}>";
                    }
                }

                // Fallback to JSON serialize
                try
                {
                    return JsonConvert.SerializeObject(obj);
                }
                catch (Exception ex)
                {
                    return $"<Failed to JsonSerialize: {ex.Message} ObjectToString: {obj.ToString()}>";
                }
            }
            catch (Exception ex)
            {
                return $"<SafeSerialize failed: {ex.Message}>";
            }
        }

        // TEST endpoint (debug container)
        [HttpPost, Route("test")]
        public IHttpActionResult TestActivities([FromBody] GetActivitiesRequest body)
        {
            return ListActivities(body);
        }
    }
}
