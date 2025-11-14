using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using AxaptaCOMConnector;
using IND_CRM_API.Services;

/*
    Prompt:
        Quiero un endpoint nuevo de "X Cosa" que haga X y llame al metodo Y de AX
*/

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// Template de controlador CRM.
    /// Usar esta clase como base para crear nuevos endpoints de negocio.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/crm/template")]
    public class CrmTemplateController : BaseCrmController
    {
        // Nombre de la clase X++ que expone los metodos via contenedor.
        // TODO: cambiar al nombre real de la clase de AX cuando se clone este template.
        private const string AxClassName = "INDCRMApiClass";

        private static readonly AxaptaSessionManager _sessionManager = new AxaptaSessionManager();

        // =========================================================
        // 1) REQUEST DTOs
        // =========================================================

        /// <summary>
        /// Request de ejemplo para un endpoint de lista.
        /// Adaptar nombres y tipos segun el caso de uso real.
        /// </summary>
        public class TemplateListRequest
        {
            // TODO: campos de filtro y paginacion segun negocio
            public string filterText { get; set; }
            public int page { get; set; }
            public int pageSize { get; set; }
        }

        /// <summary>
        /// Request de ejemplo para un endpoint de creacion / alta.
        /// Adaptar nombres y tipos segun el caso de uso real.
        /// </summary>
        public class TemplateCreateRequest
        {
            // TODO: campos necesarios para la creacion en AX
            public string field1 { get; set; }
            public string field2 { get; set; }
            public string fieldDate { get; set; }
        }

        // =========================================================
        // 2) ENDPOINT LIST (LISTA / CONSULTA)
        // =========================================================

        /// <summary>
        /// Endpoint de ejemplo para obtener una lista de registros via contenedor.
        /// Devuelve siempre un objeto { total, items }.
        /// </summary>
        /// <param name="body">Criterios de filtro y paginacion.</param>
        [HttpPost]
        [Route("list")]
        public IHttpActionResult TemplateList([FromBody] TemplateListRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Body vacio o invalido.");

                // Normalizar paginacion
                if (body.page <= 0) body.page = 1;
                if (body.pageSize <= 0) body.pageSize = 50;

                // Log de entrada
                AxaptaSessionManager.LogStatic($"[API-IN] TemplateList llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> filterText: {body.filterText}");
                AxaptaSessionManager.LogStatic($" -> page: {body.page}");
                AxaptaSessionManager.LogStatic($" -> pageSize: {body.pageSize}");

                // Obtener instancia de Axapta asociada al usuario
                var ax = _sessionManager.GetAxInstanceForUser(username);

                // Crear contenedor de entrada
                var con = ax.CreateContainer();

                // -------------------------------------------------
                // ARMAR CONTENEDOR DE ENTRADA PARA AX
                // -------------------------------------------------
                // TODO: adaptar el orden y contenido segun el metodo X++
                // 1) filtro de texto
                con.Append(body.filterText?.Trim() ?? string.Empty);

                AxaptaSessionManager.LogStatic("[CONTAINER] Enviado a AX (TemplateList):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

                // -------------------------------------------------
                // LLAMADA A AX
                // -------------------------------------------------
                // TODO: cambiar "templateGetList" por el metodo real en la clase X++
                object resultObj = ax.CallStaticClassMethod(
                    AxClassName,
                    "templateGetList",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { total = 0, items = new object[0] });

                // -------------------------------------------------
                // PARSEO DEL CONTENEDOR DEVUELTO POR AX
                // -------------------------------------------------
                var fullList = new List<object>();

                for (int i = 1; i <= root.Length(); i++)
                {
                    var row = root.Peek(i) as IAxaptaContainer;
                    if (row == null) continue;

                    // TODO: adaptar indices y nombres de campos
                    fullList.Add(new
                    {
                        FieldA = row.Peek(1)?.ToString() ?? string.Empty,
                        FieldB = row.Peek(2)?.ToString() ?? string.Empty,
                        FieldC = row.Peek(3)?.ToString() ?? string.Empty
                    });
                }

                int total = fullList.Count;

                // Paginacion en memoria
                var items = fullList
                    .Skip((body.page - 1) * body.pageSize)
                    .Take(body.pageSize)
                    .ToList();

                return Ok(new { total, items });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] TemplateList API: {ex.Message}");
                return InternalServerError(new Exception($"Error TemplateList: {ex.Message}", ex));
            }
        }

        // =========================================================
        // 3) ENDPOINT CREATE (ALTA / ACCION)
        // =========================================================

        /// <summary>
        /// Endpoint de ejemplo para crear un registro en AX via contenedor.
        /// Devuelve siempre { success, message }.
        /// </summary>
        /// <param name="body">Datos necesarios para la creacion.</param>
        [HttpPost]
        [Route("create")]
        public IHttpActionResult TemplateCreate([FromBody] TemplateCreateRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Datos vacios o invalidos.");

                // Log de entrada
                AxaptaSessionManager.LogStatic($"[API-IN] TemplateCreate llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> field1: {body.field1}");
                AxaptaSessionManager.LogStatic($" -> field2: {body.field2}");
                AxaptaSessionManager.LogStatic($" -> fieldDate: {body.fieldDate}");

                // Instancia de Axapta
                var ax = _sessionManager.GetAxInstanceForUser(username);

                // Contenedor de entrada
                var con = ax.CreateContainer();

                // -------------------------------------------------
                // ARMAR CONTENEDOR DE ENTRADA PARA AX
                // -------------------------------------------------
                // TODO: adaptar orden y conversiones segun X++
                con.Append(body.field1?.Trim() ?? string.Empty);
                con.Append(body.field2?.Trim() ?? string.Empty);

                // Ejemplo de conversion de fecha string -> yyyyMMdd
                string axDate = string.Empty;
                if (!string.IsNullOrWhiteSpace(body.fieldDate))
                {
                    DateTime dt = DateTime.Parse(body.fieldDate);
                    axDate = dt.ToString("yyyyMMdd");
                }
                con.Append(axDate);

                AxaptaSessionManager.LogStatic("[CONTAINER] Enviado a AX (TemplateCreate):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

                // -------------------------------------------------
                // LLAMADA A AX
                // -------------------------------------------------
                // TODO: cambiar "templateCreate" por el metodo real en la clase X++
                object resultObj = ax.CallStaticClassMethod(
                    AxClassName,
                    "templateCreate",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { success = false, message = "Contenedor vacio." });

                // Se espera siempre: [[ success, message ]]
                var row = root.Peek(1) as IAxaptaContainer;

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
                AxaptaSessionManager.LogStatic($"[ERROR] TemplateCreate API: {ex.Message}");
                return InternalServerError(new Exception($"Error TemplateCreate: {ex.Message}", ex));
            }
        }
    }
}
