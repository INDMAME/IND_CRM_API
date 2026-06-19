using AxaptaCOMConnector;
using IND_CRM_API.Controllers;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Collections.Generic;
using System.Globalization;
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
        private readonly IAxaptaSessionManager _sessionManager;

        public CrmEnumsController(IAxaptaSessionManager sessionManager, IAxLogger logger)
            : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
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

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(appCode);
                con.Append(requestedEnums);

                var resultObj = ax.CallStaticClassMethod("INDCRMUtilityService", axMethodName, con);
                var root = resultObj as IAxaptaContainer;
                if (root == null)
                {
                    Logger.Log($"[API-OUT] {operationName} status=500 reason=null-root traceId={traceId}", AxaptaSessionManager.LogLevel.Error);
                    return Content(HttpStatusCode.InternalServerError, BuildError("Error al procesar la respuesta de AX.", IndErrorCodes.AxComError, traceId));
                }

                var response = MapEnumCatalog(root, company, appCode, traceId);
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

        private static IndPagedResponse<CrmEnumCatalogDto> MapEnumCatalog(IAxaptaContainer root, string fallbackCompany, string fallbackAppCode, string traceId)
        {
            var headerWrap = AxContainerReadHelper.SafePeekContainer(root, 1);
            var header = AxContainerReadHelper.SafePeekContainer(headerWrap, 1) ?? headerWrap;
            var groups = AxContainerReadHelper.SafePeekContainer(root, 2);

            var success = ToBool(AxContainerReadHelper.SafeString(header, 1));
            var message = AxContainerReadHelper.SafeString(header, 2);
            var company = AxContainerReadHelper.SafeString(header, 3);
            var appCode = AxContainerReadHelper.SafeString(header, 4);
            var items = new List<CrmEnumCatalogDto>();

            if (string.IsNullOrWhiteSpace(company))
                company = fallbackCompany;
            if (string.IsNullOrWhiteSpace(appCode))
                appCode = fallbackAppCode;

            var groupCount = AxContainerReadHelper.SafeLength(groups);
            for (var i = 1; i <= groupCount; i++)
            {
                var group = AxContainerReadHelper.SafePeekContainer(groups, i);
                if (group == null)
                    continue;

                var options = AxContainerReadHelper.SafePeekContainer(group, 4);
                var dto = new CrmEnumCatalogDto
                {
                    Company = company,
                    AppCode = appCode,
                    AxEnumName = AxContainerReadHelper.SafeString(group, 1),
                    AxEnumId = ToNullableInt(AxContainerReadHelper.SafeString(group, 2)),
                    Found = ToBool(AxContainerReadHelper.SafeString(group, 3)),
                    Options = MapOptions(options)
                };

                items.Add(dto);
            }

            return new IndPagedResponse<CrmEnumCatalogDto>
            {
                Success = success,
                Message = string.IsNullOrWhiteSpace(message) ? (success ? "OK" : "No se pudo resolver el catalogo de enums.") : message,
                Total = items.Count,
                Items = items,
                TraceId = traceId
            };
        }

        private static List<CrmEnumOptionDto> MapOptions(IAxaptaContainer options)
        {
            var result = new List<CrmEnumOptionDto>();
            var optionCount = AxContainerReadHelper.SafeLength(options);

            for (var i = 1; i <= optionCount; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(options, i);
                if (row == null)
                    continue;

                result.Add(new CrmEnumOptionDto
                {
                    Value = ToNullableInt(AxContainerReadHelper.SafeString(row, 1)),
                    EnumIndex = ToNullableInt(AxContainerReadHelper.SafeString(row, 2)),
                    Label = AxContainerReadHelper.SafeString(row, 3),
                    Description = AxContainerReadHelper.SafeString(row, 4),
                    Active = ToBool(AxContainerReadHelper.SafeString(row, 5)),
                    SortOrder = ToNullableInt(AxContainerReadHelper.SafeString(row, 6)),
                    AxEnumsTableRefRecId = ToNullableLong(AxContainerReadHelper.SafeString(row, 7))
                });
            }

            return result;
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

        private static int? ToNullableInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;
        }

        private static long? ToNullableLong(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return long.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (long?)null;
        }

        private static bool ToBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var normalized = value.Trim();
            return normalized == "1" ||
                   normalized.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   normalized.Equals("yes", StringComparison.OrdinalIgnoreCase);
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
