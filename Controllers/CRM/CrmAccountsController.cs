using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using AxaptaCOMConnector;
using IND_CRM_API.Services;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/accounts")]
    public class CrmAccountsController : BaseCrmController
    {
        private readonly AxaptaSessionManager _sessionManager = AxSession.Manager;


        // -----------------------------------------
        // REQUEST DTOs
        // -----------------------------------------
        public class GetContactosRequest
        {
            public string accountNum { get; set; }
            public int page { get; set; }
            public int pageSize { get; set; }
        }

        public class GetAccountsRequest
        {
            public string accountNum { get; set; }
            public int page { get; set; }
            public int pageSize { get; set; }
        }

        // -----------------------------------------
        // LIST CONTACTS
        // -----------------------------------------
        [HttpPost, Route("listContacts")]
        public IHttpActionResult GetContactoContainer([FromBody] GetContactosRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Body vacio o invalido.");

                if (body.page <= 0) body.page = 1;
                if (body.pageSize <= 0) body.pageSize = 50;

                AxaptaSessionManager.LogStatic($"[API-IN] GetContactoContainer llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> accountNum: {body.accountNum}");
                AxaptaSessionManager.LogStatic($" -> page: {body.page}");
                AxaptaSessionManager.LogStatic($" -> pageSize: {body.pageSize}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                // 1) accountNum
                con.Append(body.accountNum?.Trim() ?? string.Empty);

                AxaptaSessionManager.LogStatic("[CONTAINER] Enviado a AX (GetContactoContainer):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getContactoContainer",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { total = 0, items = new object[0] });

                var fullList = new List<object>();

                for (int i = 1; i <= root.Length(); i++)
                {
                    var row = root.Peek(i) as IAxaptaContainer;
                    if (row == null) continue;

                    fullList.Add(new
                    {
                        Name = row.Peek(1)?.ToString() ?? string.Empty,
                        Cargo = row.Peek(2)?.ToString() ?? string.Empty,
                        Empresa = row.Peek(3)?.ToString() ?? string.Empty,
                        RecId = row.Peek(4)?.ToString() ?? string.Empty,
                        Origen = row.Peek(5)?.ToString() ?? string.Empty
                    });
                }

                int total = fullList.Count;

                var items = fullList
                    .Skip((body.page - 1) * body.pageSize)
                    .Take(body.pageSize)
                    .ToList();

                return Ok(new { total, items });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetContactoContainer API: {ex.Message}");
                return InternalServerError(new Exception($"Error GetContactoContainer: {ex.Message}", ex));
            }
        }

        // -----------------------------------------
        // LIST ACCOUNTS
        // -----------------------------------------
        [HttpPost, Route("listAccounts")]
        public IHttpActionResult GetAccountContainer([FromBody] GetAccountsRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Body vacio o invalido.");

                if (body.page <= 0) body.page = 1;
                if (body.pageSize <= 0) body.pageSize = 50;

                AxaptaSessionManager.LogStatic($"[API-IN] GetAccountContainer llamado por {username}");
                AxaptaSessionManager.LogStatic($" -> accountNum: {body.accountNum}");
                AxaptaSessionManager.LogStatic($" -> page: {body.page}");
                AxaptaSessionManager.LogStatic($" -> pageSize: {body.pageSize}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                // 1) accountNum
                con.Append(body.accountNum?.Trim() ?? string.Empty);

                AxaptaSessionManager.LogStatic("[CONTAINER] Enviado a AX (GetAccountContainer):");
                for (int i = 1; i <= con.Length(); i++)
                    AxaptaSessionManager.LogStatic($" - Item {i}: {con.Peek(i)}");

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getAccountContainer",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null || root.Length() == 0)
                    return Ok(new { total = 0, items = new object[0] });

                var fullList = new List<object>();

                for (int i = 1; i <= root.Length(); i++)
                {
                    var row = root.Peek(i) as IAxaptaContainer;
                    if (row == null) continue;

                    fullList.Add(new
                    {
                        AccountNum = row.Peek(1)?.ToString().Trim() ?? string.Empty,
                        NombreComercial = row.Peek(2)?.ToString() ?? string.Empty,
                        RazonSocial = row.Peek(3)?.ToString() ?? string.Empty,
                        Origen = row.Peek(4)?.ToString() ?? string.Empty
                    });
                }

                int total = fullList.Count;

                var items = fullList
                    .Skip((body.page - 1) * body.pageSize)
                    .Take(body.pageSize)
                    .ToList();

                return Ok(new { total, items });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetAccountContainer API: {ex.Message}");
                return InternalServerError(new Exception($"Error GetAccountContainer: {ex.Message}", ex));
            }
        }
    }
}
