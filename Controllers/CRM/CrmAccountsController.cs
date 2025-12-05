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
using IND_CRM_API.Models.Responses;
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
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Cuentas CRM" })]
        public IHttpActionResult GetContactoContainer([FromBody] GetContactosRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (body.page <= 0) validationErrors.Add(new IndValidationError { Field = "page", Message = "page debe ser mayor que cero." });
                if (body.pageSize <= 0) validationErrors.Add(new IndValidationError { Field = "pageSize", Message = "pageSize debe ser mayor que cero." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();

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
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Respuesta nula de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                var data = IND_CRM_API.Helpers.AxContainerHelper.ToArray(root);

                return Ok(new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = data?.Length ?? 0,
                    Page = body.page,
                    PageSize = body.pageSize,
                    Items = data?.ToList() ?? new List<object>(),
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetContactoContainer API: {ex.Message}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = $"Error GetContactoContainer: {ex.Message}",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        // -----------------------------------------
        // LIST ACCOUNTS
        // -----------------------------------------
        [HttpPost, Route("listAccounts")]
        [ResponseType(typeof(IndPagedResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Cuentas CRM" })]
        public IHttpActionResult GetAccounts([FromBody] GetAccountsRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (body.page <= 0) validationErrors.Add(new IndValidationError { Field = "page", Message = "page debe ser mayor que cero." });
                if (body.pageSize <= 0) validationErrors.Add(new IndValidationError { Field = "pageSize", Message = "pageSize debe ser mayor que cero." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                var username = GetAuthenticatedUsername();

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
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Respuesta nula de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                var data = IND_CRM_API.Helpers.AxContainerHelper.ToArray(root);

                return Ok(new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = data?.Length ?? 0,
                    Page = body.page,
                    PageSize = body.pageSize,
                    Items = data?.ToList() ?? new List<object>(),
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetAccounts API: {ex.Message}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = $"Error GetAccounts: {ex.Message}",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }
    }
}



