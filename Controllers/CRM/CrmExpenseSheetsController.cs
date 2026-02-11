using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Web.Http;
using System.Web.Http.Description;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// CRM endpoints for expense sheets.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/crm/expensesheets")]
    public class CrmExpenseSheetsController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;

        /// <summary>
        /// Creates the controller with its dependencies.
        /// </summary>
        public CrmExpenseSheetsController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// Creates an expense sheet with lines in AX.
        /// </summary>
        [HttpPost, Route("")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.Created, "Hoja de gastos creada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult CreateExpenseSheet([FromBody] CreateExpenseSheetRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.description))
                    validationErrors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.currencyCode))
                    validationErrors.Add(new IndValidationError { Field = "currencyCode", Message = "currencyCode es obligatorio." });
                if (body.lines == null || body.lines.Count == 0)
                    validationErrors.Add(new IndValidationError { Field = "lines", Message = "lines es obligatorio." });
                else
                    ValidateLines(body.lines, validationErrors);
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] CreateExpenseSheet {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                if (!string.IsNullOrWhiteSpace(body.userId) &&
                    !string.Equals(body.userId.Trim(), axUserId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"[WARN] CreateExpenseSheet userId mismatch body={body.userId} header={axUserId} token={username}");
                }

                Logger.Log($"[API-IN] CreateExpenseSheet user={username} axUserId={axUserId} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var rootCon = ax.CreateContainer();

                rootCon.Append(company);

                var headerCon = ax.CreateContainer();
                headerCon.Append(axUserId);
                headerCon.Append(body.description?.Trim() ?? string.Empty);
                headerCon.Append(body.currencyCode?.Trim() ?? string.Empty);
                headerCon.Append(body.exchRate ?? 0m);
                headerCon.Append(body.projId?.Trim() ?? string.Empty);
                rootCon.Append(headerCon);

                var linesCon = ax.CreateContainer();
                foreach (var line in body.lines)
                {
                    var lineCon = ax.CreateContainer();
                    var normalizedDate = NormalizeYmdDate(line.transDate);

                    lineCon.Append(normalizedDate);
                    lineCon.Append(line.typeValue ?? 0);
                    lineCon.Append(line.description?.Trim() ?? string.Empty);
                    lineCon.Append(ToAxBool(line.internacional));
                    lineCon.Append(ToAxBool(line.ticket));
                    lineCon.Append(line.qty ?? 0m);
                    lineCon.Append(line.amount ?? 0m);
                    lineCon.Append(line.projId?.Trim() ?? string.Empty);
                    lineCon.Append(line.indAttachFiles ?? string.Empty);

                    linesCon.Append(lineCon);
                }
                rootCon.Append(linesCon);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "createExpenseSheet",
                    rootCon
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                if (!success)
                {
                    var errorResponse = BuildActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, errorResponse);
                }

                var hojaGastosId = extras.Count > 0 ? extras[0] : string.Empty;
                var lineRecIds = MapRecIdList(linesOut);

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = new { HojaGastosId = hojaGastosId, LineRecIds = lineRecIds },
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.Created);
                return Content(HttpStatusCode.Created, okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateExpenseSheet: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Gets an expense sheet by id with its lines.
        /// </summary>
        [HttpGet, Route("{hojaGastosId}")]
        [ResponseType(typeof(IndPagedResponse<ExpenseSheetDetailDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Hoja de gastos encontrada", typeof(IndPagedResponse<ExpenseSheetDetailDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Hoja de gastos no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetExpenseSheet(string hojaGastosId)
        {
            var traceId = Guid.NewGuid().ToString("N");

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(hojaGastosId))
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "hojaGastosId es obligatorio.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                    Errors = new List<IndValidationError> { new IndValidationError { Field = "hojaGastosId", Message = "Valor invalido." } },
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetExpenseSheet {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetExpenseSheet hojaGastosId={hojaGastosId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getExpenseSheet",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                if (!success)
                {
                    var errorResponse = BuildActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, errorResponse);
                }

                var detail = MapExpenseSheetDetail(extras, linesOut);
                if (detail == null)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                var okResponse = new IndPagedResponse<ExpenseSheetDetailDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Items = new List<ExpenseSheetDetailDto> { detail },
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetExpenseSheet: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Updates the header data of an expense sheet.
        /// </summary>
        [HttpPut, Route("{hojaGastosId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Hoja de gastos actualizada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Hoja de gastos no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpdateExpenseSheetHeader(string hojaGastosId, [FromBody] UpdateExpenseSheetHeaderRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(hojaGastosId))
                validationErrors.Add(new IndValidationError { Field = "hojaGastosId", Message = "hojaGastosId es obligatorio." });

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(body.description))
                    validationErrors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.currencyCode))
                    validationErrors.Add(new IndValidationError { Field = "currencyCode", Message = "currencyCode es obligatorio." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] UpdateExpenseSheetHeader {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] UpdateExpenseSheetHeader hojaGastosId={hojaGastosId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());
                con.Append(body.description?.Trim() ?? string.Empty);
                con.Append(body.currencyCode?.Trim() ?? string.Empty);
                con.Append(body.exchRate ?? 0m);
                con.Append(body.projId?.Trim() ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "updateExpenseSheetHeader",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out _, out _))
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                if (!success)
                {
                    var errorResponse = BuildActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, errorResponse);
                }

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = new { HojaGastosId = hojaGastosId },
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] UpdateExpenseSheetHeader: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Updates one expense sheet line.
        /// </summary>
        [HttpPut, Route("{hojaGastosId}/lines/{lineRecId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Linea actualizada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Linea o hoja no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpdateExpenseSheetLine(string hojaGastosId, long lineRecId, [FromBody] UpdateExpenseSheetLineRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(hojaGastosId))
                validationErrors.Add(new IndValidationError { Field = "hojaGastosId", Message = "hojaGastosId es obligatorio." });
            if (lineRecId <= 0)
                validationErrors.Add(new IndValidationError { Field = "lineRecId", Message = "lineRecId es obligatorio." });

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (!TryNormalizeYmdDate(body.transDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser yyyymmdd." });
                if (!body.typeValue.HasValue)
                    validationErrors.Add(new IndValidationError { Field = "typeValue", Message = "typeValue es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.description))
                    validationErrors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio." });
                if (!body.qty.HasValue || body.qty.Value <= 0)
                    validationErrors.Add(new IndValidationError { Field = "qty", Message = "qty debe ser mayor que cero." });
                if (!body.amount.HasValue)
                    validationErrors.Add(new IndValidationError { Field = "amount", Message = "amount es obligatorio." });
            }

            if (validationErrors.Any())
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] UpdateExpenseSheetLine {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] UpdateExpenseSheetLine hojaGastosId={hojaGastosId} lineRecId={lineRecId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());
                con.Append(lineRecId.ToString());

                var normalizedDate = NormalizeYmdDate(body.transDate);
                con.Append(normalizedDate);
                con.Append(body.typeValue ?? 0);
                con.Append(body.description?.Trim() ?? string.Empty);
                con.Append(ToAxBool(body.internacional));
                con.Append(ToAxBool(body.ticket));
                con.Append(body.qty ?? 0m);
                con.Append(body.amount ?? 0m);
                con.Append(body.projId?.Trim() ?? string.Empty);
                con.Append(body.indAttachFiles ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "updateExpenseSheetLine",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out _, out _))
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                if (!success)
                {
                    var errorResponse = BuildActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, errorResponse);
                }

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = new { HojaGastosId = hojaGastosId, LineRecId = lineRecId },
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] UpdateExpenseSheetLine: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Deletes one expense sheet line or the whole sheet when requested.
        /// </summary>
        /// <param name="hojaGastosId">Identificador de la hoja de gastos.</param>
        /// <param name="lineRecId">Identificador de la linea (puede ser 0 si deleteWholeSheet es true).</param>
        /// <param name="deleteWholeSheet">Cuando es true, elimina cabecera y lineas.</param>
        [HttpDelete, Route("{hojaGastosId}/lines/{lineRecId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Linea eliminada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Linea o hoja no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteExpenseSheetLine(string hojaGastosId, long lineRecId, [FromUri] bool deleteWholeSheet = false)
        {
            var traceId = Guid.NewGuid().ToString("N");

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            var validationErrors = new List<IndValidationError>();
            if (string.IsNullOrWhiteSpace(hojaGastosId))
                validationErrors.Add(new IndValidationError { Field = "hojaGastosId", Message = "hojaGastosId es obligatorio." });
            if (!deleteWholeSheet && lineRecId <= 0)
                validationErrors.Add(new IndValidationError { Field = "lineRecId", Message = "lineRecId es obligatorio cuando deleteWholeSheet es false." });

            if (validationErrors.Count > 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] DeleteExpenseSheetLine {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] DeleteExpenseSheetLine hojaGastosId={hojaGastosId} lineRecId={lineRecId} deleteWholeSheet={deleteWholeSheet} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());
                var lineRecIdValue = deleteWholeSheet ? 0 : lineRecId;
                con.Append(lineRecIdValue.ToString());
                con.Append(deleteWholeSheet ? 1 : 0);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "deleteExpenseSheetLine",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out _, out _))
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                if (!success)
                {
                    var errorResponse = BuildActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, errorResponse);
                }

                var okResponse = new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] DeleteExpenseSheetLine: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Lists expense sheets filtered by search text.
        /// </summary>
        /// <param name="body">Filtros y paginacion del listado.</param>
        [HttpPost, Route("list")]
        [ResponseType(typeof(IndPagedResponse<ExpenseSheetListItemDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de hojas de gastos", typeof(IndPagedResponse<ExpenseSheetListItemDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetExpenseSheetsList([FromBody] GetExpenseSheetsListRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            string createdDateFromYmd = string.Empty;
            string createdDateToYmd = string.Empty;

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (body.page <= 0)
                    validationErrors.Add(new IndValidationError { Field = "page", Message = "page debe ser mayor que cero." });
                if (body.pageSize <= 0)
                    validationErrors.Add(new IndValidationError { Field = "pageSize", Message = "pageSize debe ser mayor que cero." });

                if (!string.IsNullOrWhiteSpace(body.createdDateFrom) && !TryNormalizeYmdDate(body.createdDateFrom, out createdDateFromYmd))
                    validationErrors.Add(new IndValidationError { Field = "createdDateFrom", Message = "createdDateFrom debe ser yyyyMMdd o yyyy-MM-dd." });

                if (!string.IsNullOrWhiteSpace(body.createdDateTo) && !TryNormalizeYmdDate(body.createdDateTo, out createdDateToYmd))
                    validationErrors.Add(new IndValidationError { Field = "createdDateTo", Message = "createdDateTo debe ser yyyyMMdd o yyyy-MM-dd." });

                if (!string.IsNullOrWhiteSpace(createdDateFromYmd) && !string.IsNullOrWhiteSpace(createdDateToYmd))
                {
                    var fromOk = DateTime.TryParseExact(createdDateFromYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate);
                    var toOk = DateTime.TryParseExact(createdDateToYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate);
                    if (fromOk && toOk && fromDate > toDate)
                    {
                        validationErrors.Add(new IndValidationError
                        {
                            Field = "createdDateFrom",
                            Message = "createdDateFrom no puede ser mayor que createdDateTo."
                        });
                    }
                }
            }

            if (validationErrors.Count > 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetExpenseSheetsList {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var pageValue = body.page;
                var pageSizeValue = body.pageSize;
                var filterValue = body.filter?.Trim() ?? string.Empty;
                var billedModeValue = body.billedMode ?? 0;
                if (billedModeValue != 0 && billedModeValue != 1 && billedModeValue != 2)
                    billedModeValue = 0;

                var projIdValue = body.projId?.Trim() ?? string.Empty;
                var currencyCodeValue = body.currencyCode?.Trim() ?? string.Empty;

                Logger.Log(
                    $"[API-IN] GetExpenseSheetsList filter={filterValue} billedMode={billedModeValue} page={pageValue} pageSize={pageSizeValue} " +
                    $"createdDateFrom={createdDateFromYmd} createdDateTo={createdDateToYmd} projId={projIdValue} currencyCode={currencyCodeValue} " +
                    $"user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(filterValue);
                con.Append(billedModeValue);
                con.Append(createdDateFromYmd);
                con.Append(createdDateToYmd);
                con.Append(projIdValue);
                con.Append(currencyCodeValue);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getExpenseSheetsList",
                    con
                );

                var root = resultObj as IAxaptaContainer;
                var items = MapExpenseSheetList(root, out var message);
                var pagedItems = PagingHelper.Apply(items, pageValue, pageSizeValue);

                var okResponse = new IndPagedResponse<ExpenseSheetListItemDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Total = items.Count,
                    Page = pageValue,
                    PageSize = pageSizeValue,
                    Items = pagedItems,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetExpenseSheetsList: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        // Validates line inputs for create operations.
        private static void ValidateLines(List<CreateExpenseSheetLineRequest> lines, List<IndValidationError> errors)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var prefix = $"lines[{i}]";

                if (line == null)
                {
                    errors.Add(new IndValidationError { Field = prefix, Message = "linea es obligatoria." });
                    continue;
                }

                if (!TryNormalizeYmdDate(line.transDate, out _))
                    errors.Add(new IndValidationError { Field = prefix + ".transDate", Message = "transDate debe ser yyyymmdd." });
                if (!line.typeValue.HasValue)
                    errors.Add(new IndValidationError { Field = prefix + ".typeValue", Message = "typeValue es obligatorio." });
                if (string.IsNullOrWhiteSpace(line.description))
                    errors.Add(new IndValidationError { Field = prefix + ".description", Message = "description es obligatorio." });
                if (!line.qty.HasValue || line.qty.Value <= 0)
                    errors.Add(new IndValidationError { Field = prefix + ".qty", Message = "qty debe ser mayor que cero." });
                if (!line.amount.HasValue)
                    errors.Add(new IndValidationError { Field = prefix + ".amount", Message = "amount es obligatorio." });
            }
        }

        // Normalizes yyyymmdd and yyyy-MM-dd into yyyymmdd.
        private static string NormalizeYmdDate(string input)
        {
            return TryNormalizeYmdDate(input, out var normalized) ? normalized : string.Empty;
        }

        // Checks date format for yyyymmdd or yyyy-MM-dd.
        private static bool TryNormalizeYmdDate(string input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();
            if (DateTime.TryParseExact(trimmed, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                normalized = date.ToString("yyyyMMdd");
                return true;
            }

            if (DateTime.TryParseExact(trimmed, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            {
                normalized = date.ToString("yyyyMMdd");
                return true;
            }

            return false;
        }

        // Converts bool to AX int (1/0).
        private static int ToAxBool(bool? value)
        {
            return value.HasValue && value.Value ? 1 : 0;
        }

        // Reads a header and optional lines container from AX.
        private static bool TryReadHeader(IAxaptaContainer root, out bool success, out string message, out List<string> extras, out IAxaptaContainer linesCon)
        {
            success = false;
            message = string.Empty;
            extras = new List<string>();
            linesCon = null;

            if (root == null)
                return false;

            var rootLen = AxContainerReadHelper.SafeLength(root);
            IAxaptaContainer headerCon = rootLen >= 2 ? AxContainerReadHelper.SafePeekContainer(root, 1) : root;
            linesCon = rootLen >= 2 ? AxContainerReadHelper.SafePeekContainer(root, 2) : null;

            var rowCon = AxContainerReadHelper.SafePeekContainer(headerCon, 1) ?? headerCon;
            if (rowCon == null || AxContainerReadHelper.SafeLength(rowCon) < 2)
                return false;

            success = ToBool(AxContainerReadHelper.SafeString(rowCon, 1));
            message = AxContainerReadHelper.SafeString(rowCon, 2);

            var len = AxContainerReadHelper.SafeLength(rowCon);
            for (int i = 3; i <= len; i++)
                extras.Add(AxContainerReadHelper.SafeString(rowCon, i));

            return true;
        }

        // Builds a standard error response for action calls.
        private IndApiResponse<object> BuildActionError(string message, string traceId, out HttpStatusCode status)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("no encontrada") || lower.Contains("no encontrado"))
            {
                status = HttpStatusCode.NotFound;
                return new IndApiResponse<object>
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(message) ? "Registro no encontrado." : message,
                    ErrorCode = lower.Contains("linea") ? IndErrorCodes.CrmExpenseSheetLineNotFound : IndErrorCodes.CrmExpenseSheetNotFound,
                    Data = null,
                    TraceId = traceId
                };
            }

            if (lower.Contains("bloqueada"))
            {
                status = (HttpStatusCode)422;
                return new IndApiResponse<object>
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(message) ? "Hoja de gastos bloqueada." : message,
                    ErrorCode = IndErrorCodes.CrmExpenseSheetLocked,
                    Data = null,
                    TraceId = traceId
                };
            }

            status = (HttpStatusCode)422;
            return new IndApiResponse<object>
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message) ? "Error de validacion." : message,
                ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                Data = null,
                TraceId = traceId
            };
        }

        // Maps header extras and lines to a typed DTO.
        private static ExpenseSheetDetailDto MapExpenseSheetDetail(List<string> headerExtras, IAxaptaContainer linesCon)
        {
            if (headerExtras == null || headerExtras.Count < 7)
                return null;

            // Supports both the old AX header shape (7 fields) and the new one (8 fields with TotalAmountMST).
            var hasNewHeaderShape = headerExtras.Count >= 8;

            var detail = new ExpenseSheetDetailDto
            {
                HojaGastosId = headerExtras[0],
                UserId = headerExtras[1],
                Description = headerExtras[2],
                CurrencyCode = headerExtras[3],
                TotalAmountMST = hasNewHeaderShape ? ToDecimal(headerExtras[4]) : null,
                ExchRate = hasNewHeaderShape ? ToDecimal(headerExtras[5]) : ToDecimal(headerExtras[4]),
                ProjId = hasNewHeaderShape ? headerExtras[6] : headerExtras[5],
                Voucher = hasNewHeaderShape ? headerExtras[7] : headerExtras[6],
                Lines = new List<ExpenseSheetLineDto>()
            };

            var lineCount = AxContainerReadHelper.SafeLength(linesCon);
            for (int i = 1; i <= lineCount; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(linesCon, i);
                if (row == null || AxContainerReadHelper.SafeLength(row) < 10)
                    continue;

                var line = new ExpenseSheetLineDto
                {
                    RecId = AxContainerReadHelper.SafeString(row, 1),
                    TransDate = AxContainerReadHelper.SafeString(row, 2),
                    TypeValue = SafeInt(row, 3),
                    Description = AxContainerReadHelper.SafeString(row, 4),
                    Internacional = ToBool(AxContainerReadHelper.SafeString(row, 5)),
                    Ticket = ToBool(AxContainerReadHelper.SafeString(row, 6)),
                    Qty = SafeDecimal(row, 7),
                    Amount = SafeDecimal(row, 8),
                    ProjId = AxContainerReadHelper.SafeString(row, 9),
                    IndAttachFiles = AxContainerReadHelper.SafeString(row, 10)
                };

                detail.Lines.Add(line);
            }

            return detail;
        }

        // Maps list items for expense sheet list endpoint.
        private static List<ExpenseSheetListItemDto> MapExpenseSheetList(IAxaptaContainer root, out string message)
        {
            message = string.Empty;
            var items = new List<ExpenseSheetListItemDto>();

            if (root == null || AxContainerReadHelper.SafeLength(root) == 0)
                return items;

            if (AxContainerReadHelper.IsSinDatos(root, out message))
                return items;

            var len = AxContainerReadHelper.SafeLength(root);
            for (int i = 1; i <= len; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(root, i);
                var rowLen = AxContainerReadHelper.SafeLength(row);
                if (row == null || rowLen < 3)
                    continue;

                var amountAndDate = ResolveAmountAndDate(row, rowLen);

                items.Add(new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ProjId = AxContainerReadHelper.SafeString(row, 3),
                    CurrencyCode = rowLen >= 4 ? AxContainerReadHelper.SafeString(row, 4) : string.Empty,
                    TotalAmountMST = amountAndDate.TotalAmountMST,
                    CreatedDate = amountAndDate.CreatedDate
                });
            }

            return items;
        }

        // Resuelve de forma defensiva el monto total y la fecha cuando AX cambia el orden de columnas.
        private static (decimal? TotalAmountMST, string CreatedDate) ResolveAmountAndDate(IAxaptaContainer row, int rowLen)
        {
            var value5 = rowLen >= 5 ? AxContainerReadHelper.SafeString(row, 5) : string.Empty;
            var value6 = rowLen >= 6 ? AxContainerReadHelper.SafeString(row, 6) : string.Empty;

            if (rowLen >= 6)
            {
                var value5IsDate = IsLikelyDateValue(value5);
                var value6IsDate = IsLikelyDateValue(value6);
                var value5Amount = ToDecimal(value5);
                var value6Amount = ToDecimal(value6);

                if (!value5IsDate && value6IsDate)
                    return (value5Amount, value6);

                if (value5IsDate && !value6IsDate)
                    return (value6Amount, value5);

                if (value5Amount.HasValue && !value6Amount.HasValue)
                    return (value5Amount, value6);

                if (!value5Amount.HasValue && value6Amount.HasValue)
                    return (value6Amount, value5);

                return (value5Amount, value6);
            }

            if (rowLen == 5)
            {
                if (IsLikelyDateValue(value5))
                    return (null, value5);

                var amount = ToDecimal(value5);
                if (amount.HasValue)
                    return (amount, string.Empty);

                return (null, value5);
            }

            return (null, string.Empty);
        }

        // Extracts RecId list from AX container.
        private static List<long> MapRecIdList(IAxaptaContainer linesCon)
        {
            var list = new List<long>();
            var len = AxContainerReadHelper.SafeLength(linesCon);
            for (int i = 1; i <= len; i++)
            {
                var value = AxContainerReadHelper.SafeValue(linesCon, i);
                if (TryToLong(value, out var recId))
                    list.Add(recId);
            }

            return list;
        }

        // Converts container value to int.
        private static int? SafeInt(IAxaptaContainer container, int index)
        {
            var value = AxContainerReadHelper.SafeString(container, index);
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;
            return null;
        }

        // Converts container value to decimal.
        private static decimal? SafeDecimal(IAxaptaContainer container, int index)
        {
            return ToDecimal(AxContainerReadHelper.SafeString(container, index));
        }

        // Converts a string to decimal with invariant culture.
        private static decimal? ToDecimal(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = NormalizeDecimalValue(value);
            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            // Culturas de respaldo para ambientes AX con separadores regionales.
            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out parsed))
                return parsed;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("es-ES"), out parsed))
                return parsed;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("en-US"), out parsed))
                return parsed;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                return parsed;

            return null;
        }

        // Normaliza montos de AX para parsear separadores regionales de forma consistente.
        private static string NormalizeDecimalValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var raw = value.Trim()
                .Replace("\u00A0", string.Empty)
                .Replace(" ", string.Empty);

            var hasComma = raw.Contains(",");
            var hasDot = raw.Contains(".");

            if (hasComma && hasDot)
            {
                var lastComma = raw.LastIndexOf(',');
                var lastDot = raw.LastIndexOf('.');
                var decimalSeparator = lastComma > lastDot ? ',' : '.';
                var thousandSeparator = decimalSeparator == ',' ? "." : ",";

                var withoutThousands = raw.Replace(thousandSeparator, string.Empty);
                return decimalSeparator == ','
                    ? withoutThousands.Replace(',', '.')
                    : withoutThousands;
            }

            if (hasComma)
            {
                var commaCount = raw.Count(c => c == ',');
                var lastComma = raw.LastIndexOf(',');
                var digitsAfter = lastComma >= 0 ? raw.Length - lastComma - 1 : 0;

                // Caso tipico de AX: coma como separador decimal.
                if (digitsAfter > 0 && digitsAfter <= 2)
                {
                    var whole = raw.Substring(0, lastComma).Replace(",", string.Empty);
                    var fraction = raw.Substring(lastComma + 1);
                    return string.Concat(whole, ".", fraction);
                }

                // Si la coma solo agrupa miles, se elimina.
                if (commaCount >= 1)
                    return raw.Replace(",", string.Empty);
            }

            if (hasDot)
            {
                var dotCount = raw.Count(c => c == '.');
                if (dotCount > 1)
                {
                    var lastDot = raw.LastIndexOf('.');
                    var whole = raw.Substring(0, lastDot).Replace(".", string.Empty);
                    var fraction = raw.Substring(lastDot + 1);
                    return string.Concat(whole, ".", fraction);
                }
            }

            return raw;
        }

        // Detecta textos de fecha comunes para distinguirlos de importes numericos.
        private static bool IsLikelyDateValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            var trimmed = value.Trim();
            var acceptedFormats = new[]
            {
                "yyyyMMdd",
                "yyyy-MM-dd",
                "MM/dd/yyyy",
                "M/d/yyyy",
                "dd/MM/yyyy",
                "d/M/yyyy"
            };

            if (DateTime.TryParseExact(trimmed, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out _))
                return true;

            return DateTime.TryParse(trimmed, CultureInfo.CurrentCulture, DateTimeStyles.None, out _);
        }

        // Converts a string to bool.
        private static bool ToBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (bool.TryParse(value, out var parsed))
                return parsed;

            return value == "1";
        }

        // Converts an object to long.
        private static bool TryToLong(object value, out long result)
        {
            result = 0L;
            if (value == null)
                return false;

            if (value is long longValue)
            {
                result = longValue;
                return true;
            }

            if (value is int intValue)
            {
                result = intValue;
                return true;
            }

            if (long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
            {
                result = parsed;
                return true;
            }

            return false;
        }
    }
}
