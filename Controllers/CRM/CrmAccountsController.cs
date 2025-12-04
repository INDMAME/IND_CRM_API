using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;
using AxaptaCOMConnector;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Helpers;
using IND_CRM_API.Contracts.Responses;
using System.Net;

namespace IND_CRM_API.Controllers.CRM
{
    [Authorize]
    [RoutePrefix("api/crm/accounts")]
    public class CrmAccountsController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;
        public CrmAccountsController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
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
        [ResponseType(typeof(INDPagedResponse<object>))]
        public IHttpActionResult GetContactoContainer([FromBody] GetContactosRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null || !ModelState.IsValid)
                    return BadRequest(ModelState);

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.accountNum ?? "");
                con.Append(body.page);
                con.Append(body.pageSize);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getContactoContainer",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null)
                    return Ok(new INDPagedResponse<object> { Success = false, Message = "Null AX response.", Total = 0, Items = new List<object>() });

                var data = IND_CRM_API.Helpers.AxContainerHelper.ToArray(root);

                return Ok(new INDPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = data?.Length ?? 0,
                    Items = data?.ToList() ?? new List<object>()
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetContactoContainer API: {ex.Message}");
                var response = new INDPagedResponse<object>
                {
                    Success = false,
                    Message = $"Error GetContactoContainer: {ex.Message}",
                    Total = 0,
                    Items = new List<object>()
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        // -----------------------------------------
        // LIST ACCOUNTS
        // -----------------------------------------
        [HttpPost, Route("listAccounts")]
        [ResponseType(typeof(INDPagedResponse<object>))]
        public IHttpActionResult GetAccounts([FromBody] GetAccountsRequest body)
        {
            try
            {
                var username = GetAuthenticatedUsername();

                if (body == null || !ModelState.IsValid)
                    return BadRequest(ModelState);

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();

                con.Append(body.accountNum ?? "");
                con.Append(body.page);
                con.Append(body.pageSize);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "getAccountContainer",
                    con
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null)
                    return Ok(new INDPagedResponse<object> { Success = false, Message = "Null AX response.", Total = 0, Items = new List<object>() });

                var data = IND_CRM_API.Helpers.AxContainerHelper.ToArray(root);

                return Ok(new INDPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = data?.Length ?? 0,
                    Items = data?.ToList() ?? new List<object>()
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetAccounts API: {ex.Message}");
                var response = new INDPagedResponse<object>
                {
                    Success = false,
                    Message = $"Error GetAccounts: {ex.Message}",
                    Total = 0,
                    Items = new List<object>()
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }
    }
}

