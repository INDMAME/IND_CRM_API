using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Web.Http;
using System.Web.Http.Description;
using IND_CRM_API.App_Start;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// Web store receiver of the CRM_INSERTEC web (api/crm/webstore/*). It is a data pass-through:
    /// structural checks, one call to the X++ classes CRMWeb* and the AX result as JSON. Access is
    /// only through CrmWebStoreAuthorizeAttribute (own token); no Entra context, no legacy bearer.
    /// HTTP: 200 applied or duplicate, 422 rejected, 401/403 access, 503 Axapta session failure.
    /// </summary>
    [CrmWebStoreAuthorize]
    [RoutePrefix("api/crm/webstore")]
    public class CrmWebStoreController : ApiController
    {
        private const string CompanyHeader = "X-IND-Company";
        private const string SwaggerTag = "Almacen web CRM_INSERTEC";

        private readonly ICrmWebStoreService _store;
        private readonly IAxLogger _logger;

        /// <summary>
        /// Constructor resolved by the API dependency resolver with services that are already
        /// registered, so DependencyConfig does not need any new entry.
        /// </summary>
        public CrmWebStoreController(IAxaptaSessionManager sessionManager, IAxLogger logger)
            : this(new CrmWebStoreService(sessionManager, logger), logger)
        {
        }

        /// <summary>Constructor with an explicit store service (tests).</summary>
        internal CrmWebStoreController(ICrmWebStoreService store, IAxLogger logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Proves token, company and AX session with one call to CRMWebStoreCommon::ping.
        /// </summary>
        [HttpGet, Route("ping")]
        [ResponseType(typeof(IndApiResponse<WebStorePingDto>))]
        [SwaggerOperation(Tags = new[] { SwaggerTag })]
        [SwaggerResponse(HttpStatusCode.OK, "Acceso y sesion AX correctos", typeof(IndApiResponse<WebStorePingDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Falta X-IND-Company", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult Ping()
        {
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
            var company = ReadCompany();
            if (company == null)
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Falta la cabecera X-IND-Company.",
                    ErrorCode = CrmWebStoreErrorCodes.MessageInvalid,
                    Errors = new List<IndValidationError> { new IndValidationError { Field = "company", Message = "Falta la cabecera X-IND-Company." } },
                    TraceId = traceId
                });
            }

            try
            {
                var username = CurrentUser();
                _logger.Log("[WEBSTORE-IN] operation=ping user=" + username + " company=" + company + " traceId=" + traceId);
                var data = _store.Ping(username, company);
                return Ok(new IndApiResponse<WebStorePingDto>
                {
                    Success = true,
                    Message = "Acceso correcto.",
                    Data = data,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "ping", traceId);
            }
        }

        /// <summary>
        /// Creates or updates a finished visit with its complete attendee list (CRMWebVisitStore::upsert).
        /// </summary>
        [HttpPost, Route("visits/upsert")]
        [ResponseType(typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerOperation(Tags = new[] { SwaggerTag })]
        [SwaggerResponse(HttpStatusCode.OK, "applied o duplicate", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "rejected", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpsertVisit([FromBody] WebStoreVisitUpsertRequest body)
        {
            return Store("visits/upsert", body, CrmWebStoreMessageValidator.ValidateVisitUpsert, _store.UpsertVisit);
        }

        /// <summary>
        /// Deletes a visit and its attendees (CRMWebVisitStore::remove); an already deleted visit is applied.
        /// </summary>
        [HttpPost, Route("visits/delete")]
        [ResponseType(typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerOperation(Tags = new[] { SwaggerTag })]
        [SwaggerResponse(HttpStatusCode.OK, "applied o duplicate", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "rejected", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteVisit([FromBody] WebStoreVisitDeleteRequest body)
        {
            return Store("visits/delete", body, CrmWebStoreMessageValidator.ValidateVisitDelete, _store.DeleteVisit);
        }

        /// <summary>
        /// Creates or updates a contact (CRMWebContactStore::upsert).
        /// </summary>
        [HttpPost, Route("contacts/upsert")]
        [ResponseType(typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerOperation(Tags = new[] { SwaggerTag })]
        [SwaggerResponse(HttpStatusCode.OK, "applied o duplicate", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "rejected", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpsertContact([FromBody] WebStoreContactUpsertRequest body)
        {
            return Store("contacts/upsert", body, CrmWebStoreMessageValidator.ValidateContactUpsert, _store.UpsertContact);
        }

        /// <summary>
        /// Creates or updates a customer prospect (CRMWebProspectStore::upsert).
        /// </summary>
        [HttpPost, Route("prospects/upsert")]
        [ResponseType(typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerOperation(Tags = new[] { SwaggerTag })]
        [SwaggerResponse(HttpStatusCode.OK, "applied o duplicate", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "rejected", typeof(IndApiResponse<WebStoreResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpsertProspect([FromBody] WebStoreProspectUpsertRequest body)
        {
            return Store("prospects/upsert", body, CrmWebStoreMessageValidator.ValidateProspectUpsert, _store.UpsertProspect);
        }

        // Shared flow of the message routes: binding errors, structural checks, AX call, HTTP mapping.
        private IHttpActionResult Store<T>(
            string operation,
            T body,
            Func<string, T, List<IndValidationError>> validate,
            Func<string, string, T, WebStoreResultDto> call)
            where T : WebStoreMessageRequest
        {
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
            var company = ReadCompany();
            var errors = BindingErrors();
            errors.AddRange(validate(company, body));
            if (errors.Count > 0)
            {
                _logger.Log("[WEBSTORE-IN] operation=" + operation + " structural-errors=" + errors.Count +
                            " fields=" + string.Join(",", errors.Select(e => e.Field).Distinct()) + " traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Warning);
                return Rejected(new WebStoreResultDto
                {
                    Result = "rejected",
                    ErrorCode = CrmWebStoreErrorCodes.MessageInvalid,
                    ErrorMessage = "El mensaje esta incompleto o tiene tipos no validos.",
                    Attendees = new List<WebStoreAttendeeResultDto>(),
                    MessageId = body?.messageId
                }, errors, traceId);
            }

            try
            {
                var username = CurrentUser();
                _logger.Log("[WEBSTORE-IN] operation=" + operation + " user=" + username + " company=" + company +
                            " messageId=" + body.messageId + " externalId=" + body.externalId + " version=" + body.version +
                            " axRecId=" + (body.axRecId ?? 0) + " traceId=" + traceId);
                var result = call(username, company, body);
                _logger.Log("[WEBSTORE-OUT] operation=" + operation + " result=" + result.Result + " recId=" + (result.RecId?.ToString() ?? "-") +
                            " code=" + (result.Code ?? "-") + " errorCode=" + (result.ErrorCode ?? "-") +
                            " messageId=" + body.messageId + " traceId=" + traceId);

                if (result.Result == "rejected")
                    return Rejected(result, null, traceId);

                return Ok(new IndApiResponse<WebStoreResultDto>
                {
                    Success = true,
                    Message = result.Result == "applied" ? "Mensaje aplicado en Axapta." : "Mensaje ya aplicado en Axapta.",
                    Data = result,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                return AxFailure(ex, operation, traceId);
            }
        }

        // 422 with the rejected result; the envelope code repeats the AX code when there is one.
        private IHttpActionResult Rejected(WebStoreResultDto result, List<IndValidationError> errors, string traceId)
        {
            return Content((HttpStatusCode)422, new IndApiResponse<WebStoreResultDto>
            {
                Success = false,
                Message = result.ErrorMessage ?? "Axapta rechazo el mensaje.",
                ErrorCode = result.ErrorCode ?? CrmWebStoreErrorCodes.Rejected,
                Errors = errors,
                Data = result,
                TraceId = traceId
            });
        }

        // Any failure of the AX session or of the result layout is 503: the web retries and the
        // receiver is idempotent by messageId.
        private IHttpActionResult AxFailure(Exception ex, string operation, string traceId)
        {
            var timeout = ex as IND_AxCallTimeoutException;
            string errorCode;
            if (timeout != null)
                errorCode = IndErrorCodes.AxTimeout;
            else if (ex is CrmWebStoreResultException)
                errorCode = CrmWebStoreErrorCodes.ResultInvalid;
            else if (ex is COMException)
                errorCode = IndErrorCodes.AxComError;
            else
                errorCode = IndErrorCodes.AxSessionError;

            _logger.Log("[WEBSTORE-ERROR] operation=" + operation + " errorCode=" + errorCode + " failureType=" + ex.GetType().Name +
                        " message=" + ex.Message + " traceId=" + traceId, AxaptaSessionManager.LogLevel.Error);

            return Content(HttpStatusCode.ServiceUnavailable, new IndApiResponse<object>
            {
                Success = false,
                Message = timeout != null ? timeout.UserMessage : "Axapta no esta disponible en este momento. El mensaje se puede reintentar.",
                ErrorCode = errorCode,
                TraceId = traceId
            });
        }

        // Company header used by the request scope and sent to AX at position 1.
        private string ReadCompany()
        {
            var company = IndRequestDiagnosticsHelper.GetHeaderValue(Request, CompanyHeader);
            return string.IsNullOrWhiteSpace(company) ? null : company.Trim();
        }

        // Model binding errors are type errors of the JSON (for example text in an integer field).
        private List<IndValidationError> BindingErrors()
        {
            var errors = new List<IndValidationError>();
            foreach (var entry in ModelState.Where(e => e.Value != null && e.Value.Errors.Count > 0))
            {
                var field = string.IsNullOrWhiteSpace(entry.Key) ? "body" : entry.Key;
                var dot = field.IndexOf('.');
                if (dot > 0 && field.StartsWith("body", StringComparison.OrdinalIgnoreCase))
                    field = field.Substring(dot + 1);

                errors.Add(new IndValidationError { Field = field, Message = "Tipo o formato no valido." });
            }

            return errors;
        }

        // User set by CrmWebStoreAuthorizeAttribute (the configured AX_WEBSTORE_USER).
        private string CurrentUser()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("Web store principal is missing.");

            return username;
        }
    }
}
