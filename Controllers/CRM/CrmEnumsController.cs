using IND_CRM_API.Controllers;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Web.Http;
using System.Web.Http.Description;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// CRM enum catalog endpoints backed by Axapta configuration.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/crm/enums")]
    public class CrmEnumsController : BaseCrmController
    {
        private const string DefaultAppCode = "CRM";
        private const int MaxRequestedEnums = 100;
        private readonly ICrmEnumCatalogService _enumCatalogService;

        public CrmEnumsController(IAxaptaSessionManager sessionManager, IAxLogger logger)
            : this(sessionManager, new CrmEnumCatalogService(sessionManager, logger), logger)
        {
        }

        public CrmEnumsController(
            IAxaptaSessionManager sessionManager,
            ICrmEnumCatalogService enumCatalogService,
            IAxLogger logger)
            : base(sessionManager, logger)
        {
            _enumCatalogService = enumCatalogService ?? throw new ArgumentNullException(nameof(enumCatalogService));
        }

        /// <summary>
        /// Devuelve opciones activas de enums AX por nombre tecnico.
        /// </summary>
        /// <param name="appCode">Aplicativo consumidor. Por defecto CRM.</param>
        /// <param name="axEnumNames">Lista separada por comas de AxEnumName. Si se omite, devuelve todos los enums configurados.</param>
        [HttpGet, Route("by-name")]
        [ResponseType(typeof(IndPagedResponse<CrmEnumCatalogDto>))]
        [SwaggerOperation(Tags = new[] { "Enums" })]
        [SwaggerResponse(HttpStatusCode.OK, "Catalogo de enums por nombre", typeof(IndPagedResponse<CrmEnumCatalogDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion o enum no encontrado", typeof(IndPagedResponse<CrmEnumCatalogDto>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetByName([FromUri] string appCode = DefaultAppCode, [FromUri] string axEnumNames = null)
        {
            return GetEnumCatalog("getEnumValuesByName", appCode, axEnumNames, "axEnumNames", "GetEnumCatalogByName");
        }

        /// <summary>
        /// Devuelve opciones activas de enums AX por id tecnico.
        /// </summary>
        /// <param name="appCode">Aplicativo consumidor. Por defecto CRM.</param>
        /// <param name="axEnumIds">Lista separada por comas de AxEnumId. Si se omite, devuelve todos los enums configurados.</param>
        [HttpGet, Route("by-id")]
        [ResponseType(typeof(IndPagedResponse<CrmEnumCatalogDto>))]
        [SwaggerOperation(Tags = new[] { "Enums" })]
        [SwaggerResponse(HttpStatusCode.OK, "Catalogo de enums por id", typeof(IndPagedResponse<CrmEnumCatalogDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion o enum no encontrado", typeof(IndPagedResponse<CrmEnumCatalogDto>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetById([FromUri] string appCode = DefaultAppCode, [FromUri] string axEnumIds = null)
        {
            return GetEnumCatalog("getEnumValuesById", appCode, axEnumIds, "axEnumIds", "GetEnumCatalogById");
        }

        /// <summary>
        /// Calls the AX enum catalog method and maps the generic container response.
        /// </summary>
        private IHttpActionResult GetEnumCatalog(string axMethodName, string appCode, string requestedEnums, string requestField, string operationName)
        {
            var traceId = GetOrCreateTraceId();
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            appCode = string.IsNullOrWhiteSpace(appCode) ? DefaultAppCode : appCode.Trim();
            requestedEnums = requestedEnums?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(appCode))
                validationErrors.Add(new IndValidationError { Field = "appCode", Message = "appCode es obligatorio." });

            var requestedCount = CountCsvValues(requestedEnums);
            if (requestedCount > MaxRequestedEnums)
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = requestField,
                    Message = $"No se pueden solicitar mas de {MaxRequestedEnums} enums por llamada."
                });
            }

            if (validationErrors.Count > 0)
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] {operationName} company={company} appCode={appCode} {requestField}={requestedEnums} user={username} traceId={traceId}");

                var response = string.Equals(axMethodName, "getEnumValuesById", StringComparison.Ordinal)
                    ? _enumCatalogService.GetById(username, company, appCode, requestedEnums, traceId)
                    : _enumCatalogService.GetByName(username, company, appCode, requestedEnums, traceId);
                if (!response.Success)
                {
                    Logger.Log($"[API-OUT] {operationName} status=422 total={response.Total ?? 0} traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                    return Content((HttpStatusCode)422, response);
                }

                Logger.Log($"[API-OUT] {operationName} status=200 total={response.Total ?? 0} traceId={traceId}");
                return Ok(response);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] {operationName}: {ex}", AxaptaSessionManager.LogLevel.Error);
                return Content(HttpStatusCode.InternalServerError, BuildError(
                    "Error interno del servidor.",
                    ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    traceId));
            }
        }

        private static int CountCsvValues(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return 0;

            var parts = value.Split(',');
            var count = 0;
            for (var i = 0; i < parts.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(parts[i]))
                    count++;
            }

            return count;
        }

        private static IndApiResponse<object> BuildError(string message, string errorCode, string traceId)
        {
            return new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Errors = null,
                Data = null,
                TraceId = traceId
            };
        }
    }
}
