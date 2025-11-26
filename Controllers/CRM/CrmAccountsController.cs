using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using AxaptaCOMConnector;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/accounts")]
    public class CrmAccountsController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;

        public CrmAccountsController(IAxaptaSessionManager sessionManager) : base(sessionManager)
        {
            _sessionManager = sessionManager;
        }

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

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.accountNum ?? "");
                con.Append(body.page);
                con.Append(body.pageSize);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getContactos",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null)
                    return Ok(new { success = false, message = "Respuesta nula de AX." });

                var data = IND_CRM_API.Helpers.AxContainerHelper.ToArray(root);

                return Ok(new { success = true, data });
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
        [HttpPost, Route("list")]
        public IHttpActionResult GetAccounts([FromBody] GetAccountsRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null)
                    return BadRequest("Body vacio o invalido.");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.accountNum ?? "");
                con.Append(body.page);
                con.Append(body.pageSize);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getAccounts",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null)
                    return Ok(new { success = false, message = "Respuesta nula de AX." });

                var data = IND_CRM_API.Helpers.AxContainerHelper.ToArray(root);

                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] GetAccounts API: {ex.Message}");
                return InternalServerError(new Exception($"Error GetAccounts: {ex.Message}", ex));
            }
        }
    }
}
