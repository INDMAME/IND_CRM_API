using AxaptaCOMConnector;
using IND_CRM_APIs.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;

namespace IND_CRM_APIs.Controllers
{
    [Authorize]
    [RoutePrefix("api/axapta")]
    public class AxaptaController : ApiController
    {
        private static readonly AxaptaSessionManager _sessionManager = new AxaptaSessionManager();

        private string GetAuthenticatedUsername()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                throw new Exception("Usuario no autenticado o token inválido.");
            return username;
        }

        
        // ---------------------------------------------------------
        // OBTENER ENTORNO (TEST / PROD)
        // ---------------------------------------------------------
        [HttpGet, Route("GetEnvironmentName")]
        public IHttpActionResult GetEnvironmentName()
        {
            try
            {
                var username = GetAuthenticatedUsername();
                var resultObj = _sessionManager.CallMethodByUser(username, "INDWebServiceTest", "getEnvironmentName");
                string result = resultObj?.ToString() ?? string.Empty;

                return Ok(new { environment = string.IsNullOrWhiteSpace(result) ? "Unknown" : result });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }

        // ---------------------------------------------------------
        // OBTENER NOMBRE DE COMPAÑÍA
        // ---------------------------------------------------------
        [HttpGet, Route("GetCompanyName")]
        public IHttpActionResult GetCompanyName()
        {
            try
            {
                var username = GetAuthenticatedUsername();
                var resultObj = _sessionManager.CallMethodByUser(username, "INDWebServiceTest", "getCompanyName");
                string result = resultObj?.ToString() ?? string.Empty;

                return Ok(new { company = string.IsNullOrWhiteSpace(result) ? "N/A" : result });
            }
            catch (Exception ex)
            {
                return InternalServerError(ex);
            }
        }


        // ---------------------------------------------------------
        // CREAR ASISTENTE (Container)
        // ---------------------------------------------------------
        public class CreateVisitaAsistenteRequest
        {
            public string refRecIdActividad { get; set; }   // long
            public string asistenteTipo { get; set; }       // int enum AX
            public string asistenteId { get; set; }         // string
            public string contactoRecId { get; set; }       // long
        }


        [HttpPost, Route("CreateVisitaAsistente")]
        public IHttpActionResult CreateVisitaAsistente([FromBody] CreateVisitaAsistenteRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                AxaptaSessionManager.LogStatic($"[API-IN] CreateVisitaAsistente llamado por {username}");
                AxaptaSessionManager.LogStatic($" → refRecIdActividad: {body.refRecIdActividad}");
                AxaptaSessionManager.LogStatic($" → asistenteTipo: {body.asistenteTipo}");
                AxaptaSessionManager.LogStatic($" → asistenteId: {body.asistenteId}");
                AxaptaSessionManager.LogStatic($" → contactoRecId: {body.contactoRecId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                // 🚨 IMPORTANTE: TODO COMO STRING
                con.Append(body.refRecIdActividad.ToString());
                con.Append(body.asistenteTipo.ToString());
                con.Append(body.asistenteId?.Trim() ?? "");
                con.Append(body.contactoRecId.ToString());

                AxaptaSessionManager.LogStatic("Container enviado (CreateVisitaAsistente):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" • Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "createVisitaAsistente",
                    con
                );

                string result = resultObj?.ToString() ?? "";

                AxaptaSessionManager.LogStatic($"[API-OUT] Resultado CreateVisitaAsistente: {result}");

                return Ok(new { message = result });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] CreateVisitaAsistente API: {ex.Message}");
                return InternalServerError(new Exception($"Error CreateVisitaAsistente: {ex.Message}", ex));
            }
        }



        // ---------------------------------------------------------
        // GET ACTIVIDADES (Container)
        // ---------------------------------------------------------
        public class CreateActivityRequest
        {
            public string accountNum { get; set; }
            public string actividadType { get; set; }
            public string visitType { get; set; }
            public string userId { get; set; }
            public string description { get; set; }
            public string origen { get; set; }
            public string transDate { get; set; }
            public string comentarios { get; set; }
            public string antecedentes { get; set; }
            public string conclusiones { get; set; }
        }


        [HttpPost, Route("CreateActivity")]
        public IHttpActionResult CreateActivity([FromBody] CreateActivityRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();
                if (body == null)
                    return BadRequest("Datos vacíos o inválidos.");

                // ============================
                // LOG DE ENTRADA AL ENDPOINT
                // ============================
                AxaptaSessionManager.LogStatic($"[API-IN] CreateActivity llamado por {username}");
                AxaptaSessionManager.LogStatic($" → accountNum: {body.accountNum}");
                AxaptaSessionManager.LogStatic($" → actividadType: {body.actividadType}");
                AxaptaSessionManager.LogStatic($" → visitType: {body.visitType}");
                AxaptaSessionManager.LogStatic($" → userId: {body.userId}");
                AxaptaSessionManager.LogStatic($" → description: {body.description}");
                AxaptaSessionManager.LogStatic($" → origen: {body.origen}");
                AxaptaSessionManager.LogStatic($" → transDate (raw): {body.transDate}");
                AxaptaSessionManager.LogStatic($" → comentarios: {body.comentarios}");
                AxaptaSessionManager.LogStatic($" → antecedentes: {body.antecedentes}");
                AxaptaSessionManager.LogStatic($" → conclusiones: {body.conclusiones}");

                var ax = _sessionManager.GetAxInstanceForUser(username);

                var con = ax.CreateContainer();

                con.Append(body.accountNum?.Trim() ?? "");
                con.Append(int.Parse(body.actividadType.Trim()));
                con.Append(int.Parse(body.visitType.Trim()));
                con.Append(body.userId?.Trim() ?? "");
                con.Append(body.description?.Trim() ?? "");
                con.Append(int.Parse(body.origen.Trim()));

                // FECHA
                var dt = DateTime.Parse(body.transDate);
                var axDate = dt.ToString("yyyyMMdd");
                con.Append(axDate);

                con.Append(body.comentarios ?? "");
                con.Append(body.antecedentes ?? "");
                con.Append(body.conclusiones ?? "");

                // ============================
                // LOG DEL CONTENEDOR FINAL
                // ============================
                AxaptaSessionManager.LogStatic("Container enviado a Axapta (CreateActivity):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" • Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "createActivity",
                    con
                );

                string result = resultObj?.ToString() ?? "";

                AxaptaSessionManager.LogStatic($"[API-OUT] Resultado CreateActivity: {result}");

                return Ok(new { message = result });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] CreateActivity API: {ex.Message}");
                return InternalServerError(new Exception($"Error CreateActivity: {ex.Message}", ex));
            }
        }





        // ---------------------------------------------------------
        // TEST CONTAINER
        // ---------------------------------------------------------
        [HttpGet, Route("TestContainerType")]
        public IHttpActionResult TestContainerType(string userId)
        {
            var username = GetAuthenticatedUsername();

            object resultObj = _sessionManager.CallMethodByUser(
                username,
                "INDCRMApiClass",
                "getActivityContainer",
                new object[] { userId ?? "", "", "", 0 });

            var con = resultObj as IAxaptaContainer;

            return Ok(new
            {
                dotnetType = resultObj?.GetType().FullName,
                isContainer = con != null,
                length = con?.Length() ?? 0,
                firstItemType =
                    con != null && con.Length() > 0 ?
                    (con.Peek(1)?.GetType().FullName ?? "null")
                    : "N/A",
            });
        }
    }
}
