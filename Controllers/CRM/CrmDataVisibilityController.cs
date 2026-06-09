using AxaptaCOMConnector;
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
    [Authorize]
    [RoutePrefix("api/crm/data-visibility")]
    public class CrmDataVisibilityController : BaseCrmController
    {
        private const string DefaultAppCode = "CRM";
        private const string DefaultVisitsModuleCode = "VISITAS_GESTION";
        private readonly IAxaptaSessionManager _sessionManager;

        public CrmDataVisibilityController(IAxaptaSessionManager sessionManager, IAxLogger logger)
            : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// Devuelve los usuarios visibles del modulo para el usuario AX del header.
        /// </summary>
        [HttpGet, Route("visible-users")]
        [ResponseType(typeof(IndPagedResponse<DataVisibilityVisibleUserDto>))]
        [SwaggerOperation(Tags = new[] { "Visibilidad de datos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Usuarios visibles", typeof(IndPagedResponse<DataVisibilityVisibleUserDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion o negocio", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetVisibleUsers(
            string moduleCode = DefaultVisitsModuleCode,
            string appCode = DefaultAppCode,
            string asOfDate = null,
            bool includeCrmUserId = true)
        {
            var traceId = GetOrCreateTraceId();
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.ValidationError);
            if (userError != null)
                return userError;

            appCode = string.IsNullOrWhiteSpace(appCode) ? DefaultAppCode : appCode.Trim();
            moduleCode = string.IsNullOrWhiteSpace(moduleCode) ? DefaultVisitsModuleCode : moduleCode.Trim();

            var axDate = string.Empty;
            if (!string.IsNullOrWhiteSpace(asOfDate))
            {
                if (!TryParseAsOfDate(asOfDate, out var parsedDate))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "asOfDate",
                        Message = "asOfDate debe ser yyyyMMdd o yyyy-MM-dd."
                    });
                }
                else
                {
                    axDate = parsedDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                }
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
                Logger.Log($"[API-IN] GetVisibleUsers user={username} axUserId={axUserId} company={company} app={appCode} module={moduleCode} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(appCode);
                con.Append(moduleCode);
                con.Append(axUserId);
                con.Append(axDate);
                con.Append(includeCrmUserId ? "1" : "0");

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMUtilityService",
                    "ctrlGetVisibleUsers",
                    con);

                var root = resultObj as IAxaptaContainer;
                if (root == null)
                {
                    return Content(HttpStatusCode.InternalServerError, BuildError("Error al procesar la respuesta de AX.", IndErrorCodes.AxComError, traceId));
                }

                var response = MapVisibleUsers(root, traceId);
                if (!response.Success)
                    return Content((HttpStatusCode)422, response);

                return Ok(response);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetVisibleUsers API: {ex}");
                return Content(HttpStatusCode.InternalServerError, BuildError(
                    "Error interno del servidor.",
                    ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    traceId));
            }
        }

        private static bool TryParseAsOfDate(string value, out DateTime date)
        {
            var formats = new[] { "yyyyMMdd", "yyyy-MM-dd" };
            return DateTime.TryParseExact(
                (value ?? string.Empty).Trim(),
                formats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
        }

        private static IndPagedResponse<DataVisibilityVisibleUserDto> MapVisibleUsers(IAxaptaContainer root, string traceId)
        {
            var headerWrap = AxContainerReadHelper.SafePeekContainer(root, 1);
            var header = AxContainerReadHelper.SafePeekContainer(headerWrap, 1) ?? headerWrap;
            var lines = AxContainerReadHelper.SafePeekContainer(root, 2);

            var successRaw = AxContainerReadHelper.SafeString(header, 1);
            var success = string.Equals(successRaw, "1", StringComparison.OrdinalIgnoreCase)
                       || string.Equals(successRaw, "true", StringComparison.OrdinalIgnoreCase);
            var message = AxContainerReadHelper.SafeString(header, 2);
            var items = new List<DataVisibilityVisibleUserDto>();

            var lineCount = AxContainerReadHelper.SafeLength(lines);
            for (var i = 1; i <= lineCount; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(lines, i);
                if (row == null)
                    continue;

                var axUserId = AxContainerReadHelper.SafeString(row, 2);
                if (string.IsNullOrWhiteSpace(axUserId))
                    continue;

                items.Add(new DataVisibilityVisibleUserDto
                {
                    Alias = AxContainerReadHelper.SafeString(row, 1),
                    AxUserId = axUserId,
                    CrmUserId = AxContainerReadHelper.SafeString(row, 3),
                    Name = AxContainerReadHelper.SafeString(row, 4),
                    Source = AxContainerReadHelper.SafeString(row, 5),
                    MutationPolicy = AxContainerReadHelper.SafeString(row, 6),
                    MutationPolicyInt = ToNullableInt(AxContainerReadHelper.SafeString(row, 7)),
                    MutationPolicyLabel = AxContainerReadHelper.SafeString(row, 8),
                    CanMutate = ToBool(AxContainerReadHelper.SafeString(row, 9))
                });
            }

            return new IndPagedResponse<DataVisibilityVisibleUserDto>
            {
                Success = success,
                Message = string.IsNullOrWhiteSpace(message) ? (success ? "OK" : "No se pudo resolver la visibilidad.") : message,
                Total = items.Count,
                Items = items,
                TraceId = traceId
            };
        }

        private static int? ToNullableInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
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
