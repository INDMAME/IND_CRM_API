using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;
using AxaptaCOMConnector;
using Swashbuckle.Swagger.Annotations;
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

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.accountNum))
                    validationErrors.Add(new IndValidationError { Field = "accountNum", Message = "accountNum es obligatorio." });
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

                con.Append(company);
                con.Append(body.accountNum?.Trim() ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
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
                var items = ApplyPaging(data, body.page, body.pageSize);
                var total = data?.Length ?? 0;

                return Ok(new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = total,
                    Page = body.page,
                    PageSize = body.pageSize,
                    Items = items,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetContactoContainer API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
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

            // Validar header de compania.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

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

                con.Append(company);
                con.Append(body.accountNum?.Trim() ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMVisitsService",
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
                var items = ApplyPaging(data, body.page, body.pageSize);
                var total = data?.Length ?? 0;

                return Ok(new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = total,
                    Page = body.page,
                    PageSize = body.pageSize,
                    Items = items,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetAccounts API: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Applies in-memory paging over the array returned by AX.
        /// </summary>
        private static List<object> ApplyPaging(object[] data, int page, int pageSize)
        {
            if (data == null || data.Length == 0)
                return new List<object>();

            if (page <= 0 || pageSize <= 0)
                return data.ToList();

            var skip = (page - 1) * pageSize;
            if (skip < 0)
                skip = 0;

            if (skip >= data.Length)
                return new List<object>();

            return data.Skip(skip).Take(pageSize).ToList();
        }
    }
}



