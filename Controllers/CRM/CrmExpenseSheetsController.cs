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
        /// <summary>
        /// Supported modes for DELETE /expensesheets/{hojaGastosId}/lines/{lineRecId}.
        /// </summary>
        public enum ExpenseSheetDeleteMode
        {
            LineOnly = 0,
            // AX does not expose a separate header-only delete; this mode is mapped to WholeSheet.
            HeaderOnly = 1,
            WholeSheet = 2
        }

        private const int ModeCreateHeaderAndLines = 0;
        private const int ModeCreateHeaderOnly = 1;
        private const int ModeAddLinesToExisting = 2;
        private const int ExpenseSheetStatusDraft = 0;
        private const int ExpenseSheetStatusPaid = 4;
        private const int ReimbursableExpenseNo = 0;
        private const int ReimbursableExpenseBoth = 2;
        private const int MaxPageSize = 50;

        private readonly IAxaptaSessionManager _sessionManager;

        /// <summary>
        /// Creates the controller with its dependencies.
        /// </summary>
        public CrmExpenseSheetsController(IAxaptaSessionManager sessionManager, IAxLogger logger)
            : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// Creates expense sheet content in AX using mode-based behavior.
        /// </summary>
        /// <remarks>
        /// Conditional request body rules:
        /// - mode 0 (default): description, currencyCode and lines are required.
        /// - mode 1: description and currencyCode are required, lines must be null or empty.
        /// - mode 2: existingHojaGastosId is required and lines must include at least one line.
        /// Optional header enums: expenseSheetStatus, exchangeRateMode and reimbursableExpense.
        /// </remarks>
        [HttpPost, Route("")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.Created, "Hoja de gastos creada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult CreateExpenseSheet([FromBody] CreateExpenseSheetRequest body)
        {
            var traceId = GetOrCreateTraceId();
            var validationErrors = new List<IndValidationError>();
            var modeValue = ResolveCreateExpenseMode(body);

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (!ModelState.IsValid)
                AddModelStateErrors(validationErrors);

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                ValidateCreateExpenseSheetBody(body, modeValue, validationErrors);
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
                var existingHojaGastosId = (body.existingHojaGastosId ?? string.Empty).Trim();
                if (!string.IsNullOrWhiteSpace(body.userId) &&
                    !string.Equals(body.userId.Trim(), axUserId, StringComparison.OrdinalIgnoreCase))
                {
                    Logger.Log($"[WARN] CreateExpenseSheet userId mismatch body={body.userId} header={axUserId} token={username}");
                }

                Logger.Log(
                    $"[API-IN] CreateExpenseSheet user={username} axUserId={axUserId} company={company} mode={modeValue} " +
                    $"existingHojaGastosId={existingHojaGastosId} expenseSheetStatus={ToLogValue(body.expenseSheetStatus)} " +
                    $"exchangeRateMode={ToLogValue(body.exchangeRateMode)} reimbursableExpense={ToLogValue(body.reimbursableExpense)} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var rootCon = ax.CreateContainer();

                rootCon.Append(company);

                var headerCon = ax.CreateContainer();
                headerCon.Append(axUserId);
                headerCon.Append(body.description?.Trim() ?? string.Empty);
                headerCon.Append(body.currencyCode?.Trim() ?? string.Empty);
                headerCon.Append(body.exchRate ?? 0m);
                headerCon.Append(body.projId?.Trim() ?? string.Empty);
                AppendCreateHeaderOptionalFields(headerCon, body.expenseSheetStatus, body.exchangeRateMode, body.reimbursableExpense);
                rootCon.Append(headerCon);

                var linesCon = ax.CreateContainer();
                if (body.lines != null)
                {
                    foreach (var line in body.lines)
                    {
                        var lineCon = ax.CreateContainer();
                        var normalizedDate = NormalizeApiDateToAxYmd(line.transDate);

                        lineCon.Append(normalizedDate);
                        lineCon.Append(line.typeValue ?? 0);
                        lineCon.Append(line.description?.Trim() ?? string.Empty);
                        lineCon.Append(ToAxBool(line.internacional));
                        lineCon.Append(line.fileId?.Trim() ?? string.Empty);
                        lineCon.Append(line.qty ?? 0m);
                        lineCon.Append(line.price ?? 0m);
                        lineCon.Append(line.projId?.Trim() ?? string.Empty);
                        AppendLineOptionalFields(
                            lineCon,
                            line.reimbursableExpense,
                            line.currencyCode,
                            line.amountMST,
                            line.exchRate);

                        linesCon.Append(lineCon);
                    }
                }
                rootCon.Append(linesCon);

                var optionsCon = ax.CreateContainer();
                optionsCon.Append(modeValue);
                optionsCon.Append(existingHojaGastosId);
                rootCon.Append(optionsCon);

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
        /// Lists available currencies for expense sheet capture.
        /// </summary>
        [HttpGet, Route("currencies")]
        [ResponseType(typeof(IndPagedResponse<ExpenseSheetCurrencyDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de monedas", typeof(IndPagedResponse<ExpenseSheetCurrencyDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetExpenseSheetCurrencies()
        {
            var traceId = GetOrCreateTraceId();

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetExpenseSheetCurrencies {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetExpenseSheetCurrencies user={username} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getCurrencyList",
                    con
                );

                var root = resultObj as IAxaptaContainer;
                if (root == null)
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

                var items = MapCurrencyList(root, out var message);

                var okResponse = new IndPagedResponse<ExpenseSheetCurrencyDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Total = items.Count,
                    Items = items,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetExpenseSheetCurrencies: {ex}");
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
        /// Lista subordinados del usuario actual para la captura de hojas de gastos.
        /// </summary>
        [HttpGet, Route("subordinates")]
        [ResponseType(typeof(IndPagedResponse<ExpenseSheetSubordinateDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de subordinados", typeof(IndPagedResponse<ExpenseSheetSubordinateDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetExpenseSheetSubordinates()
        {
            var traceId = GetOrCreateTraceId();

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetExpenseSheetSubordinates {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetExpenseSheetSubordinates user={username} axUserId={axUserId} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getSubordinatesByUser",
                    con
                );

                var root = resultObj as IAxaptaContainer;
                if (root == null)
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

                var items = MapSubordinateList(root, out var message);

                var okResponse = new IndPagedResponse<ExpenseSheetSubordinateDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Total = items.Count,
                    Items = items,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetExpenseSheetSubordinates: {ex}");
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
        /// Gets fuel price per kilometer for the current user and date.
        /// </summary>
        /// <param name="transDate">Fecha de consulta en formato DDMMYYYY o DD.MM.YYYY. Si no se envia, usa hoy.</param>
        [HttpGet, Route("fuel-price-km")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetFuelPriceKmDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Precio por kilometro", typeof(IndApiResponse<ExpenseSheetFuelPriceKmDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetFuelPriceKm([FromUri] string transDate = null)
        {
            var traceId = Guid.NewGuid().ToString("N");

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            string transDateYmd;
            if (string.IsNullOrWhiteSpace(transDate))
            {
                transDateYmd = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            }
            else if (!TryNormalizeApiDateToAxYmd(transDate, out transDateYmd))
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                    Errors = new List<IndValidationError>
                    {
                        new IndValidationError
                        {
                            Field = "transDate",
                            Message = "transDate debe ser DDMMYYYY o DD.MM.YYYY."
                        }
                    },
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            Action<HttpStatusCode> logOut = statusCode =>
                Logger.Log($"[API-OUT] GetFuelPriceKm {(int)statusCode} traceId={traceId}");

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetFuelPriceKm transDate={transDateYmd} user={username} axUserId={axUserId} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(transDateYmd);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getFuelPriceKm",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out _))
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    logOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                if (!success)
                {
                    var errorResponse = BuildActionError(message, traceId, out var status);
                    logOut(status);
                    return Content(status, errorResponse);
                }

                var data = new ExpenseSheetFuelPriceKmDto
                {
                    PriceKm = extras.Count >= 1 ? ToDecimal(extras[0]) : null,
                    Source = extras.Count >= 2 ? extras[1] : string.Empty,
                    TransDate = FormatApiDate(transDateYmd)
                };

                var okResponse = new IndApiResponse<ExpenseSheetFuelPriceKmDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                };
                logOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetFuelPriceKm: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    Data = null,
                    TraceId = traceId
                };
                logOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        /// <summary>
        /// Gets an expense sheet by id with its lines.
        /// </summary>
        /// <remarks>
        /// Header fields include expenseSheetStatus, estadoComentarios, exchangeRateMode, createdDate and reimbursableExpense.
        /// Line fields include reimbursableExpense, currencyCode, amountMST and exchRate.
        /// </remarks>
        // Prevent collision with ticket resource prefix (/api/crm/expensesheets/tickets).
        [HttpGet, Route("{hojaGastosId:regex(^(?![Tt][Ii][Cc][Kk][Ee][Tt][Ss]$).+)}")]
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
                Logger.Log($"[API-IN] GetExpenseSheet hojaGastosId={hojaGastosId} user={username} axUserId={axUserId} company={company} traceId={traceId}");

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
                    Logger.Log(
                        $"[EXPENSE-AUTHZ-DETAIL] gate=CrmExpenseSheetsController.GetExpenseSheet result=deny reason=ax-response-unreadable " +
                        $"company={ToLogValue(company)} axUserId={ToLogValue(axUserId)} hojaGastosId={ToLogValue(hojaGastosId)} traceId={traceId}",
                        AxaptaSessionManager.LogLevel.Warning);

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
                    Logger.Log(
                        $"[EXPENSE-AUTHZ-DETAIL] gate=CrmExpenseSheetsController.GetExpenseSheet result=deny reason=ax-validation-failed " +
                        $"company={ToLogValue(company)} axUserId={ToLogValue(axUserId)} hojaGastosId={ToLogValue(hojaGastosId)} " +
                        $"axMessage={ToLogValue(message)} mappedStatus={(int)status} mappedErrorCode={ToLogValue(errorResponse.ErrorCode)} " +
                        $"axExtras={FormatAxExtrasForLog(extras)} traceId={traceId}",
                        AxaptaSessionManager.LogLevel.Warning);
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
                Logger.Log(
                    $"[EXPENSE-AUTHZ-DETAIL] gate=CrmExpenseSheetsController.GetExpenseSheet result=allow reason=ax-detail-found " +
                    $"company={ToLogValue(company)} axUserId={ToLogValue(axUserId)} hojaGastosId={ToLogValue(hojaGastosId)} " +
                    $"sheetUserId={ToLogValue(detail.UserId)} lineCount={(detail.Lines == null ? 0 : detail.Lines.Count)} traceId={traceId}");
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
        /// <remarks>
        /// Optional header fields: expenseSheetStatus, exchangeRateMode, estadoComentarios and reimbursableExpense.
        /// If estadoComentarios is provided, expenseSheetStatus and exchangeRateMode are required.
        /// </remarks>
        // Prevent collision with ticket resource prefix (/api/crm/expensesheets/tickets).
        [HttpPut, Route("{hojaGastosId:regex(^(?![Tt][Ii][Cc][Kk][Ee][Tt][Ss]$).+)}")]
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

            if (!ModelState.IsValid)
                AddModelStateErrors(validationErrors);

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
                if (body.expenseSheetStatus.HasValue && body.expenseSheetStatus.Value < 0)
                    validationErrors.Add(new IndValidationError { Field = "expenseSheetStatus", Message = "expenseSheetStatus debe ser mayor o igual que 0." });
                if (body.exchangeRateMode.HasValue && body.exchangeRateMode.Value < 0)
                    validationErrors.Add(new IndValidationError { Field = "exchangeRateMode", Message = "exchangeRateMode debe ser mayor o igual que 0." });
                if (body.reimbursableExpense.HasValue && !IsValidReimbursableExpense(body.reimbursableExpense.Value))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "reimbursableExpense",
                        Message = "reimbursableExpense invalido. Valores permitidos: 0 No, 1 Yes, 2 Both."
                    });
                }
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
                Logger.Log(
                    $"[API-IN] UpdateExpenseSheetHeader hojaGastosId={hojaGastosId} user={username} axUserId={axUserId} " +
                    $"expenseSheetStatus={ToLogValue(body.expenseSheetStatus)} exchangeRateMode={ToLogValue(body.exchangeRateMode)} " +
                    $"reimbursableExpense={ToLogValue(body.reimbursableExpense)} estadoComentariosLength={(body.estadoComentarios ?? string.Empty).Length} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());
                con.Append(body.description?.Trim() ?? string.Empty);
                con.Append(body.currencyCode?.Trim() ?? string.Empty);
                con.Append(body.exchRate ?? 0m);
                con.Append(body.projId?.Trim() ?? string.Empty);
                AppendUpdateHeaderOptionalFields(con, body.expenseSheetStatus, body.exchangeRateMode, body.estadoComentarios, body.reimbursableExpense);

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
        /// Propagates current header currency and exchange rate to all existing lines.
        /// </summary>
        /// <remarks>
        /// The web client must ask for user confirmation before calling this endpoint.
        /// AX blocks the operation when the sheet already has multi-currency lines unless force=true.
        /// </remarks>
        [HttpPost, Route("{hojaGastosId:regex(^(?![Tt][Ii][Cc][Kk][Ee][Tt][Ss]$).+)}/currency-defaults/propagate")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetPropagationResultDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Propagacion de divisa aplicada", typeof(IndApiResponse<ExpenseSheetPropagationResultDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Hoja de gastos no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult PropagateExpenseSheetCurrencyDefaults(
            string hojaGastosId,
            [FromUri] bool recalculateAmountMST = true,
            [FromUri] bool force = false)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(hojaGastosId))
                validationErrors.Add(new IndValidationError { Field = "hojaGastosId", Message = "hojaGastosId es obligatorio." });

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

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] PropagateExpenseSheetCurrencyDefaults {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log(
                    $"[API-IN] PropagateExpenseSheetCurrencyDefaults hojaGastosId={hojaGastosId} " +
                    $"recalculateAmountMST={recalculateAmountMST} force={force} user={username} axUserId={axUserId} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());
                con.Append(recalculateAmountMST ? 1 : 0);
                con.Append(force ? 1 : 0);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "propagateExpenseSheetCurrencyDefaults",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out _))
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

                var data = MapExpenseSheetPropagationResult(hojaGastosId, "currencyDefaults", extras, recalculateAmountMST);
                var okResponse = new IndApiResponse<ExpenseSheetPropagationResultDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] PropagateExpenseSheetCurrencyDefaults: {ex}");
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
        /// Propagates current header project to all existing lines.
        /// </summary>
        /// <remarks>
        /// The web client must ask for user confirmation before calling this endpoint.
        /// AX blocks the operation when the header project is the configured "various" marker.
        /// </remarks>
        [HttpPost, Route("{hojaGastosId:regex(^(?![Tt][Ii][Cc][Kk][Ee][Tt][Ss]$).+)}/project-default/propagate")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetPropagationResultDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Propagacion de proyecto aplicada", typeof(IndApiResponse<ExpenseSheetPropagationResultDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Hoja de gastos no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult PropagateExpenseSheetProjectDefault(string hojaGastosId)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(hojaGastosId))
                validationErrors.Add(new IndValidationError { Field = "hojaGastosId", Message = "hojaGastosId es obligatorio." });

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

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] PropagateExpenseSheetProjectDefault {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log(
                    $"[API-IN] PropagateExpenseSheetProjectDefault hojaGastosId={hojaGastosId} " +
                    $"user={username} axUserId={axUserId} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "propagateExpenseSheetProjectDefault",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out _))
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

                var data = MapExpenseSheetPropagationResult(hojaGastosId, "projectDefault", extras, false);
                var okResponse = new IndApiResponse<ExpenseSheetPropagationResultDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] PropagateExpenseSheetProjectDefault: {ex}");
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
        /// Propagates current header reimbursable expense value to all existing lines.
        /// </summary>
        /// <remarks>
        /// The web client must ask for user confirmation before calling this endpoint.
        /// </remarks>
        [HttpPost, Route("{hojaGastosId:regex(^(?![Tt][Ii][Cc][Kk][Ee][Tt][Ss]$).+)}/reimbursable-expense/propagate")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetPropagationResultDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Propagacion de gasto reembolsable aplicada", typeof(IndApiResponse<ExpenseSheetPropagationResultDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Hoja de gastos no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult PropagateExpenseSheetReimbursableExpense(string hojaGastosId)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(hojaGastosId))
                validationErrors.Add(new IndValidationError { Field = "hojaGastosId", Message = "hojaGastosId es obligatorio." });

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

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] PropagateExpenseSheetReimbursableExpense {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log(
                    $"[API-IN] PropagateExpenseSheetReimbursableExpense hojaGastosId={hojaGastosId} " +
                    $"user={username} axUserId={axUserId} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(hojaGastosId.Trim());

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "propagateExpenseSheetReimbursableExpense",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out _))
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

                var data = MapExpenseSheetPropagationResult(hojaGastosId, "reimbursableExpense", extras, false);
                var okResponse = new IndApiResponse<ExpenseSheetPropagationResultDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] PropagateExpenseSheetReimbursableExpense: {ex}");
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
            AddLineRecIdValidation(validationErrors, lineRecId);

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (!TryNormalizeApiDateToAxYmd(body.transDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser DDMMYYYY o DD.MM.YYYY." });
                if (!body.typeValue.HasValue)
                    validationErrors.Add(new IndValidationError { Field = "typeValue", Message = "typeValue es obligatorio." });
                if (string.IsNullOrWhiteSpace(body.description))
                    validationErrors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio." });
                if (!body.qty.HasValue || body.qty.Value <= 0)
                    validationErrors.Add(new IndValidationError { Field = "qty", Message = "qty debe ser mayor que cero." });
                if (!body.price.HasValue)
                    validationErrors.Add(new IndValidationError { Field = "price", Message = "price es obligatorio." });
                if (body.reimbursableExpense.HasValue && !IsValidReimbursableExpense(body.reimbursableExpense.Value))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "reimbursableExpense",
                        Message = "reimbursableExpense invalido. Valores permitidos: 0 No, 1 Yes, 2 Both."
                    });
                }
                if (body.exchRate.HasValue && body.exchRate.Value <= 0m)
                    validationErrors.Add(new IndValidationError { Field = "exchRate", Message = "exchRate debe ser mayor que cero cuando se informa." });
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
                // Axapta COM container is sensitive to Int64 values; send RecId as numeric text.
                con.Append(lineRecId.ToString(CultureInfo.InvariantCulture));

                var normalizedDate = NormalizeApiDateToAxYmd(body.transDate);
                con.Append(normalizedDate);
                con.Append(body.typeValue ?? 0);
                con.Append(body.description?.Trim() ?? string.Empty);
                con.Append(ToAxBool(body.internacional));
                con.Append(body.fileId?.Trim() ?? string.Empty);
                con.Append(body.qty ?? 0m);
                con.Append(body.price ?? 0m);
                con.Append(body.projId?.Trim() ?? string.Empty);
                AppendLineOptionalFields(
                    con,
                    body.reimbursableExpense,
                    body.currencyCode,
                    body.amountMST,
                    body.exchRate);

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
        /// Deletes expense sheet data using line, header, or whole-sheet mode.
        /// </summary>
        /// <remarks>
        /// Preferred mode selector: deleteMode (0 LineOnly, 1 HeaderOnly, 2 WholeSheet).
        /// In AX, HeaderOnly (1) is handled as WholeSheet (deleteWholeSheet=1).
        /// Legacy selector: deleteWholeSheet (bool). When deleteMode is provided, deleteWholeSheet is ignored.
        /// </remarks>
        /// <param name="hojaGastosId">Identificador de la hoja de gastos.</param>
        /// <param name="lineRecId">Identificador de la linea (puede ser 0 cuando deleteMode no es LineOnly).</param>
        /// <param name="deleteWholeSheet">Parametro legacy. Cuando es true, elimina cabecera y lineas.</param>
        /// <param name="deleteMode">Modo recomendado: LineOnly, HeaderOnly o WholeSheet.</param>
        [HttpDelete, Route("{hojaGastosId}/lines/{lineRecId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Eliminacion aplicada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Linea o hoja no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteExpenseSheetLine(
            string hojaGastosId,
            long lineRecId,
            [FromUri] bool deleteWholeSheet = false,
            [FromUri] ExpenseSheetDeleteMode? deleteMode = null)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var effectiveDeleteMode = deleteMode ?? (deleteWholeSheet ? ExpenseSheetDeleteMode.WholeSheet : ExpenseSheetDeleteMode.LineOnly);

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
            if (deleteMode.HasValue && !IsValidDeleteMode(deleteMode.Value))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "deleteMode",
                    Message = "deleteMode invalido. Valores permitidos: 0 LineOnly, 1 HeaderOnly, 2 WholeSheet."
                });
            }
            if (effectiveDeleteMode == ExpenseSheetDeleteMode.LineOnly)
                AddLineRecIdValidation(validationErrors, lineRecId);

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
                Logger.Log(
                    $"[API-IN] DeleteExpenseSheetLine hojaGastosId={hojaGastosId} lineRecId={lineRecId} deleteWholeSheet={deleteWholeSheet} " +
                    $"deleteMode={effectiveDeleteMode} user={username} axUserId={axUserId} traceId={traceId}");

                var hojaGastosIdTrimmed = hojaGastosId.Trim();
                var ax = _sessionManager.GetAxInstanceForUser(username);
                var deleteWholeSheetFlag = effectiveDeleteMode == ExpenseSheetDeleteMode.LineOnly ? 0 : 1;
                var lineCon = ax.CreateContainer();
                lineCon.Append(company);
                lineCon.Append(axUserId);
                lineCon.Append(hojaGastosIdTrimmed);
                // AX expects lineRecId as numeric text. For whole-sheet modes the value can be 0.
                var lineRecIdValue = deleteWholeSheetFlag == 1
                    ? "0"
                    : lineRecId.ToString(CultureInfo.InvariantCulture);
                lineCon.Append(lineRecIdValue);
                lineCon.Append(deleteWholeSheetFlag);

                if (effectiveDeleteMode == ExpenseSheetDeleteMode.HeaderOnly)
                {
                    Logger.Log(
                        $"[WARN] DeleteExpenseSheetLine deleteMode=HeaderOnly mapped to WholeSheet for AX contract traceId={traceId}");
                }

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "deleteExpenseSheetLine",
                    lineCon
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out _, out _))
                {
                    Logger.Log(
                        $"[WARN] DeleteExpenseSheetLine invalid AX response type={resultObj?.GetType().FullName ?? "null"} " +
                        $"deleteMode={effectiveDeleteMode} traceId={traceId}");

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
        /// <remarks>
        /// Filtro opcional por estado: expenseSheetStatus.
        /// Valores permitidos (INDExpenseSheetStatus): 0 Draft, 1 InReview, 2 Approved, 3 Rejected, 4 Paid.
        /// Filtro opcional por reembolso: reimbursableExpense (0 No, 1 Yes, 2 Both).
        /// Filtro opcional includeSubordinates: cuando es true lista hojas de subordinados directos del usuario del header.
        /// Response items include estadoComentarios, userName and reimbursableExpense.
        /// </remarks>
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
                if (body.pageSize > MaxPageSize)
                    validationErrors.Add(new IndValidationError { Field = "pageSize", Message = $"pageSize no puede ser mayor que {MaxPageSize}." });

                if (!string.IsNullOrWhiteSpace(body.createdDateFrom) && !TryNormalizeApiDateToAxYmd(body.createdDateFrom, out createdDateFromYmd))
                    validationErrors.Add(new IndValidationError { Field = "createdDateFrom", Message = "createdDateFrom debe ser DDMMYYYY o DD.MM.YYYY." });

                if (!string.IsNullOrWhiteSpace(body.createdDateTo) && !TryNormalizeApiDateToAxYmd(body.createdDateTo, out createdDateToYmd))
                    validationErrors.Add(new IndValidationError { Field = "createdDateTo", Message = "createdDateTo debe ser DDMMYYYY o DD.MM.YYYY." });

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
                var expenseSheetStatusValue = NormalizeExpenseSheetStatusOrNull(body.expenseSheetStatus);
                var reimbursableExpenseValue = NormalizeReimbursableExpenseOrNull(body.reimbursableExpense);
                var includeSubordinatesValue = body.includeSubordinates ?? false;

                Logger.Log(
                    $"[API-IN] GetExpenseSheetsList filter={filterValue} billedMode={billedModeValue} page={pageValue} pageSize={pageSizeValue} " +
                    $"createdDateFrom={createdDateFromYmd} createdDateTo={createdDateToYmd} projId={projIdValue} currencyCode={currencyCodeValue} " +
                    $"expenseSheetStatus={ToLogValue(expenseSheetStatusValue)} reimbursableExpense={ToLogValue(reimbursableExpenseValue)} includeSubordinates={includeSubordinatesValue} " +
                    $"user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(filterValue);
                con.Append(billedModeValue);
                AppendExpenseSheetListFilters(
                    con,
                    createdDateFromYmd,
                    createdDateToYmd,
                    projIdValue,
                    currencyCodeValue,
                    expenseSheetStatusValue,
                    reimbursableExpenseValue,
                    includeSubordinatesValue);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getExpenseSheetsList",
                    con
                );

                var root = resultObj as IAxaptaContainer;
                var items = MapExpenseSheetList(root, pageValue, pageSizeValue, out var message, out var total);

                var okResponse = new IndPagedResponse<ExpenseSheetListItemDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Total = total,
                    Page = pageValue,
                    PageSize = pageSizeValue,
                    Items = items,
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

        // Resolves creation mode with backward-compatible default.
        private static int ResolveCreateExpenseMode(CreateExpenseSheetRequest body)
        {
            if (body == null || !body.mode.HasValue)
                return ModeCreateHeaderAndLines;

            return body.mode.Value;
        }

        // Validates create expense sheet body based on selected mode.
        private static void ValidateCreateExpenseSheetBody(CreateExpenseSheetRequest body, int mode, List<IndValidationError> errors)
        {
            if (mode != ModeCreateHeaderAndLines && mode != ModeCreateHeaderOnly && mode != ModeAddLinesToExisting)
            {
                errors.Add(new IndValidationError
                {
                    Field = "mode",
                    Message = "mode invalido. Valores permitidos: 0, 1, 2."
                });
                return;
            }

            if (body.expenseSheetStatus.HasValue && body.expenseSheetStatus.Value < 0)
                errors.Add(new IndValidationError { Field = "expenseSheetStatus", Message = "expenseSheetStatus debe ser mayor o igual que 0." });
            if (body.exchangeRateMode.HasValue && body.exchangeRateMode.Value < 0)
                errors.Add(new IndValidationError { Field = "exchangeRateMode", Message = "exchangeRateMode debe ser mayor o igual que 0." });
            if (body.exchangeRateMode.HasValue && !body.expenseSheetStatus.HasValue)
                errors.Add(new IndValidationError { Field = "expenseSheetStatus", Message = "expenseSheetStatus es obligatorio cuando se envia exchangeRateMode." });
            if (body.reimbursableExpense.HasValue && !IsValidReimbursableExpense(body.reimbursableExpense.Value))
            {
                errors.Add(new IndValidationError
                {
                    Field = "reimbursableExpense",
                    Message = "reimbursableExpense invalido. Valores permitidos: 0 No, 1 Yes, 2 Both."
                });
            }

            if (mode == ModeCreateHeaderAndLines || mode == ModeCreateHeaderOnly)
            {
                if (string.IsNullOrWhiteSpace(body.description))
                    errors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio cuando mode es 0 o 1." });

                if (string.IsNullOrWhiteSpace(body.currencyCode))
                    errors.Add(new IndValidationError { Field = "currencyCode", Message = "currencyCode es obligatorio cuando mode es 0 o 1." });
            }

            var hasLines = body.lines != null && body.lines.Count > 0;

            if (mode == ModeCreateHeaderAndLines)
            {
                if (!hasLines)
                {
                    errors.Add(new IndValidationError { Field = "lines", Message = "lines es obligatorio cuando mode es 0." });
                    return;
                }

                ValidateLines(body.lines, errors);
                return;
            }

            if (mode == ModeCreateHeaderOnly)
            {
                if (hasLines)
                    errors.Add(new IndValidationError { Field = "lines", Message = "lines debe ser null o vacio cuando mode es 1." });
                return;
            }

            if (string.IsNullOrWhiteSpace(body.existingHojaGastosId))
                errors.Add(new IndValidationError { Field = "existingHojaGastosId", Message = "existingHojaGastosId es obligatorio cuando mode es 2." });

            if (!hasLines)
            {
                errors.Add(new IndValidationError { Field = "lines", Message = "lines debe incluir al menos una linea cuando mode es 2." });
                return;
            }

            ValidateLines(body.lines, errors);
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

                if (!TryNormalizeApiDateToAxYmd(line.transDate, out _))
                    errors.Add(new IndValidationError { Field = prefix + ".transDate", Message = "transDate debe ser DDMMYYYY o DD.MM.YYYY." });
                if (!line.typeValue.HasValue)
                    errors.Add(new IndValidationError { Field = prefix + ".typeValue", Message = "typeValue es obligatorio." });
                if (string.IsNullOrWhiteSpace(line.description))
                    errors.Add(new IndValidationError { Field = prefix + ".description", Message = "description es obligatorio." });
                if (!line.qty.HasValue || line.qty.Value <= 0)
                    errors.Add(new IndValidationError { Field = prefix + ".qty", Message = "qty debe ser mayor que cero." });
                if (!line.price.HasValue || line.price.Value <= 0)
                    errors.Add(new IndValidationError { Field = prefix + ".price", Message = "price debe ser mayor que cero." });
                if (line.reimbursableExpense.HasValue && !IsValidReimbursableExpense(line.reimbursableExpense.Value))
                {
                    errors.Add(new IndValidationError
                    {
                        Field = prefix + ".reimbursableExpense",
                        Message = "reimbursableExpense invalido. Valores permitidos: 0 No, 1 Yes, 2 Both."
                    });
                }
                if (line.exchRate.HasValue && line.exchRate.Value <= 0m)
                    errors.Add(new IndValidationError { Field = prefix + ".exchRate", Message = "exchRate debe ser mayor que cero cuando se informa." });
            }
        }

        // Normalizes API date (DDMMYYYY) into AX date format (yyyyMMdd).
        private static string NormalizeApiDateToAxYmd(string input)
        {
            return TryNormalizeApiDateToAxYmd(input, out var normalized) ? normalized : string.Empty;
        }

        // Validates accepted API date formats (DDMMYYYY / DD.MM.YYYY) and converts to AX format.
        private static bool TryNormalizeApiDateToAxYmd(string input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return TryParseTicketOrSheetDateExact(
                input.Trim(),
                new[]
                {
                    "ddMMyyyy",
                    "dd.MM.yyyy",
                    "d.M.yyyy"
                },
                out normalized);
        }

        // Parses known date shapes to AX format for internal compatibility paths.
        private static bool TryNormalizeAnyDateToAxYmd(string input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();
            if (trimmed.All(char.IsDigit))
            {
                if (trimmed.Length != 8)
                    return false;

                return TryParseTicketOrSheetDateExact(
                    trimmed,
                    new[]
                    {
                        "yyyyMMdd",
                        "ddMMyyyy"
                    },
                    out normalized);
            }

            return TryParseTicketOrSheetDateExact(
                trimmed,
                new[]
                {
                    "dd.MM.yyyy",
                    "d.M.yyyy",
                    "yyyy-MM-dd",
                    "dd/MM/yyyy"
                },
                out normalized);
        }

        // Rejects technically valid dates that are outside the supported business range.
        private static bool TryParseTicketOrSheetDateExact(string input, string[] acceptedFormats, out string normalized)
        {
            normalized = string.Empty;
            if (!DateTime.TryParseExact(input, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return false;

            if (!IsReasonableTicketOrSheetDate(date))
                return false;

            normalized = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return true;
        }

        // Filters out OCR or compatibility dates that would push impossible years into AX.
        private static bool IsReasonableTicketOrSheetDate(DateTime date)
        {
            var minDate = new DateTime(1900, 1, 1);
            var maxDate = DateTime.Today.AddYears(1);
            return date >= minDate && date <= maxDate;
        }

        // Formats any supported incoming AX/API date into DD.MM.YYYY for responses.
        private static string FormatApiDate(string input)
        {
            if (!TryNormalizeAnyDateToAxYmd(input, out var normalizedYmd))
                normalizedYmd = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            if (!DateTime.TryParseExact(normalizedYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        // Validates allowed values for AX INDExpenseSheetStatus.
        private static bool IsValidExpenseSheetStatus(int expenseSheetStatus)
        {
            return expenseSheetStatus >= ExpenseSheetStatusDraft &&
                   expenseSheetStatus <= ExpenseSheetStatusPaid;
        }

        // Validates allowed values for AX INDReimbursableExpense.
        private static bool IsValidReimbursableExpense(int reimbursableExpense)
        {
            return reimbursableExpense >= ReimbursableExpenseNo &&
                   reimbursableExpense <= ReimbursableExpenseBoth;
        }

        // Standard enum normalization: invalid values are treated as null.
        private static int? NormalizeExpenseSheetStatusOrNull(int? expenseSheetStatus)
        {
            if (!expenseSheetStatus.HasValue || !IsValidExpenseSheetStatus(expenseSheetStatus.Value))
                return null;

            return expenseSheetStatus.Value;
        }

        // Standard enum normalization: invalid values are treated as null.
        private static int? NormalizeReimbursableExpenseOrNull(int? reimbursableExpense)
        {
            if (!reimbursableExpense.HasValue || !IsValidReimbursableExpense(reimbursableExpense.Value))
                return null;

            return reimbursableExpense.Value;
        }

        // Appends expense sheet list filters to AX container using stable positions.
        private static void AppendExpenseSheetListFilters(
            IAxaptaContainer container,
            string createdDateFromYmd,
            string createdDateToYmd,
            string projId,
            string currencyCode,
            int? expenseSheetStatus,
            int? reimbursableExpense,
            bool includeSubordinates)
        {
            if (container == null)
                return;

            const string noOptionalValueToken = "null";

            container.Append(createdDateFromYmd ?? string.Empty);
            container.Append(createdDateToYmd ?? string.Empty);
            container.Append(projId ?? string.Empty);
            container.Append(currencyCode ?? string.Empty);

            if (expenseSheetStatus.HasValue)
                container.Append(expenseSheetStatus.Value);
            else
                container.Append(noOptionalValueToken);

            if (reimbursableExpense.HasValue)
                container.Append(reimbursableExpense.Value);
            else
                container.Append(noOptionalValueToken);

            container.Append(includeSubordinates ? 1 : 0);
        }

        // Validates supported delete modes for expense sheet DELETE endpoint.
        private static bool IsValidDeleteMode(ExpenseSheetDeleteMode mode)
        {
            return mode == ExpenseSheetDeleteMode.LineOnly ||
                   mode == ExpenseSheetDeleteMode.HeaderOnly ||
                   mode == ExpenseSheetDeleteMode.WholeSheet;
        }

        // Appends optional create-header fields to AX container using stable positions.
        private static void AppendCreateHeaderOptionalFields(
            IAxaptaContainer container,
            int? expenseSheetStatus,
            int? exchangeRateMode,
            int? reimbursableExpense)
        {
            if (container == null)
                return;

            if (!expenseSheetStatus.HasValue && !exchangeRateMode.HasValue && !reimbursableExpense.HasValue)
                return;

            const string noOptionalValueToken = "null";

            if (expenseSheetStatus.HasValue)
                container.Append(expenseSheetStatus.Value);
            else
                container.Append(noOptionalValueToken);

            if (exchangeRateMode.HasValue || reimbursableExpense.HasValue)
            {
                if (exchangeRateMode.HasValue)
                    container.Append(exchangeRateMode.Value);
                else
                    container.Append(noOptionalValueToken);

                if (reimbursableExpense.HasValue)
                    container.Append(reimbursableExpense.Value);
            }
        }

        // Appends optional update-header fields without shifting the legacy estadoComentarios slot.
        private static void AppendUpdateHeaderOptionalFields(
            IAxaptaContainer container,
            int? expenseSheetStatus,
            int? exchangeRateMode,
            string estadoComentarios,
            int? reimbursableExpense)
        {
            if (container == null)
                return;

            var hasEstadoComentarios = estadoComentarios != null;
            if (!expenseSheetStatus.HasValue && !exchangeRateMode.HasValue && !hasEstadoComentarios && !reimbursableExpense.HasValue)
                return;

            const string noOptionalValueToken = "null";

            if (expenseSheetStatus.HasValue)
                container.Append(expenseSheetStatus.Value);
            else
                container.Append(noOptionalValueToken);

            if (exchangeRateMode.HasValue || hasEstadoComentarios || reimbursableExpense.HasValue)
            {
                if (exchangeRateMode.HasValue)
                    container.Append(exchangeRateMode.Value);
                else
                    container.Append(noOptionalValueToken);

                if (hasEstadoComentarios)
                    container.Append(estadoComentarios.Trim());
                else if (reimbursableExpense.HasValue)
                    container.Append(noOptionalValueToken);

                if (reimbursableExpense.HasValue)
                    container.Append(reimbursableExpense.Value);
            }
        }

        // Appends optional line fields to AX container using stable positions after legacy columns.
        private static void AppendLineOptionalFields(
            IAxaptaContainer container,
            int? reimbursableExpense,
            string currencyCode,
            decimal? amountMST,
            decimal? exchRate)
        {
            if (container == null)
                return;

            var hasCurrencyCode = !string.IsNullOrWhiteSpace(currencyCode);
            if (!reimbursableExpense.HasValue && !hasCurrencyCode && !amountMST.HasValue && !exchRate.HasValue)
                return;

            const string noOptionalValueToken = "null";

            if (reimbursableExpense.HasValue)
                container.Append(reimbursableExpense.Value);
            else
                container.Append(noOptionalValueToken);

            container.Append(hasCurrencyCode ? currencyCode.Trim() : string.Empty);

            if (amountMST.HasValue)
                container.Append(amountMST.Value);
            else
                container.Append(noOptionalValueToken);

            if (exchRate.HasValue)
                container.Append(exchRate.Value);
            else
                container.Append(noOptionalValueToken);
        }

        // Adds model binding/deserialization errors to standard validation list.
        private void AddModelStateErrors(List<IndValidationError> validationErrors)
        {
            if (validationErrors == null || ModelState == null || ModelState.IsValid)
                return;

            foreach (var entry in ModelState)
            {
                var field = string.IsNullOrWhiteSpace(entry.Key) ? "body" : entry.Key;
                var state = entry.Value;
                if (state == null || state.Errors == null || state.Errors.Count == 0)
                    continue;

                foreach (var modelError in state.Errors)
                {
                    var message = modelError?.ErrorMessage;
                    if (string.IsNullOrWhiteSpace(message))
                        message = modelError?.Exception?.Message;
                    if (string.IsNullOrWhiteSpace(message))
                        message = "Valor invalido.";

                    validationErrors.Add(new IndValidationError
                    {
                        Field = field,
                        Message = message
                    });
                }
            }
        }

        // Formats nullable ints for logs.
        private static string ToLogValue(int? value)
        {
            return value.HasValue
                ? value.Value.ToString(CultureInfo.InvariantCulture)
                : "null";
        }

        // Formats optional text for compact diagnostic logs.
        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "-"
                : value.Trim().Replace("\r", " ").Replace("\n", " ");
        }

        // Formats AX header extras without leaking them back through the API response.
        private static string FormatAxExtrasForLog(IEnumerable<string> extras)
        {
            if (extras == null)
                return "-";

            var values = extras
                .Take(20)
                .Select((value, index) => $"{index + 1}:{ToLogValue(value)}")
                .ToList();

            return values.Count == 0 ? "-" : string.Join("|", values);
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
            if (lower.Contains("no encontrada") || lower.Contains("no encontrado") || lower.Contains("no existe"))
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

        // Maps AX propagation extras to the API response contract.
        private static ExpenseSheetPropagationResultDto MapExpenseSheetPropagationResult(
            string requestedHojaGastosId,
            string propagationType,
            List<string> extras,
            bool recalculateAmountMST)
        {
            var hojaGastosId = requestedHojaGastosId?.Trim() ?? string.Empty;
            var updatedLines = 0;

            if (extras != null)
            {
                if (extras.Count >= 1 && !string.IsNullOrWhiteSpace(extras[0]))
                    hojaGastosId = extras[0];

                if (extras.Count >= 2)
                    updatedLines = ToInt(extras[1]) ?? 0;

                if (extras.Count >= 3)
                    recalculateAmountMST = ToBool(extras[2]);
            }

            return new ExpenseSheetPropagationResultDto
            {
                HojaGastosId = hojaGastosId,
                PropagationType = propagationType,
                UpdatedLines = updatedLines,
                RecalculateAmountMST = recalculateAmountMST
            };
        }

        // Maps AX currency rows to typed currency DTOs.
        private static List<ExpenseSheetCurrencyDto> MapCurrencyList(IAxaptaContainer root, out string message)
        {
            message = string.Empty;
            var items = new List<ExpenseSheetCurrencyDto>();

            if (root == null || AxContainerReadHelper.SafeLength(root) == 0)
                return items;

            if (AxContainerReadHelper.IsSinDatos(root, out message))
                return items;

            var len = AxContainerReadHelper.SafeLength(root);
            for (int i = 1; i <= len; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(root, i);
                if (row == null || AxContainerReadHelper.SafeLength(row) < 2)
                    continue;

                items.Add(new ExpenseSheetCurrencyDto
                {
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 1),
                    CurrencyCodeISO = AxContainerReadHelper.SafeString(row, 2)
                });
            }

            return items;
        }

        // Maps AX subordinate rows to typed subordinate DTOs.
        private static List<ExpenseSheetSubordinateDto> MapSubordinateList(IAxaptaContainer root, out string message)
        {
            message = string.Empty;
            var items = new List<ExpenseSheetSubordinateDto>();

            if (root == null || AxContainerReadHelper.SafeLength(root) == 0)
                return items;

            if (AxContainerReadHelper.IsSinDatos(root, out message))
                return items;

            var len = AxContainerReadHelper.SafeLength(root);
            for (int i = 1; i <= len; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(root, i);
                var rowLen = AxContainerReadHelper.SafeLength(row);
                if (row == null || rowLen < 2)
                    continue;

                var userId = AxContainerReadHelper.SafeString(row, 1);
                var secondValue = AxContainerReadHelper.SafeString(row, 2);
                var thirdValue = rowLen >= 3 ? AxContainerReadHelper.SafeString(row, 3) : string.Empty;

                // AX vNext: [crmUserId, axUserId, name]
                // Legacy:   [crmUserId, name]
                var axUserId = rowLen >= 3 ? secondValue : userId;
                var name = rowLen >= 3 ? thirdValue : secondValue;

                // Defensive fallback when AX row is partially populated.
                if (string.IsNullOrWhiteSpace(name))
                    name = secondValue;

                if (string.IsNullOrWhiteSpace(userId) &&
                    string.IsNullOrWhiteSpace(axUserId) &&
                    string.IsNullOrWhiteSpace(name))
                    continue;

                items.Add(new ExpenseSheetSubordinateDto
                {
                    UserId = userId,
                    AxUserId = axUserId,
                    Name = name
                });
            }

            return items;
        }

        // Maps header extras and lines to a typed DTO.
        private static ExpenseSheetDetailDto MapExpenseSheetDetail(List<string> headerExtras, IAxaptaContainer linesCon)
        {
            if (headerExtras == null || headerExtras.Count < 7)
                return null;

            // AX detail header mapping:
            // Current (13): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]EstadoComentarios [5]UserId [6]CurrencyCode [7]TotalAmountMST [8]ExchRate [9]ExchangeRateMode [10]ProjId [11]Voucher [12]CreatedDate [13]ReimbursableExpense
            // Current (12): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]EstadoComentarios [5]UserId [6]CurrencyCode [7]TotalAmountMST [8]ExchRate [9]ExchangeRateMode [10]ProjId [11]Voucher [12]CreatedDate
            // Current (11): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]EstadoComentarios [5]UserId [6]CurrencyCode [7]TotalAmountMST [8]ExchRate [9]ExchangeRateMode [10]ProjId [11]Voucher
            // Previous (11): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]UserId [5]CurrencyCode [6]TotalAmountMST [7]ExchRate [8]ExchangeRateMode [9]ProjId [10]Voucher [11]CreatedDate
            // Previous (10): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]UserId [5]CurrencyCode [6]TotalAmountMST [7]ExchRate [8]ExchangeRateMode [9]ProjId [10]Voucher
            // Previous (8): [1]HojaGastosId [2]Description [3]UserId [4]CurrencyCode [5]TotalAmountMST [6]ExchRate [7]ProjId [8]Voucher
            // Legacy (7): [1]HojaGastosId [2]UserId [3]Description [4]CurrencyCode [5]ExchRate [6]ProjId [7]Voucher
            var detail = new ExpenseSheetDetailDto
            {
                HojaGastosId = headerExtras[0],
                Lines = new List<ExpenseSheetLineDto>()
            };

            if (headerExtras.Count >= 12)
            {
                detail.Description = headerExtras[1];
                detail.ExpenseSheetStatus = ToInt(headerExtras[2]);
                detail.EstadoComentarios = headerExtras[3];
                detail.UserId = headerExtras[4];
                detail.CurrencyCode = headerExtras[5];
                detail.TotalAmount = ToDecimal(headerExtras[6]);
                detail.ExchRate = ToDecimal(headerExtras[7]);
                detail.ExchangeRateMode = ToInt(headerExtras[8]);
                detail.ProjId = headerExtras[9];
                detail.Voucher = NormalizeVoucher(headerExtras[10]);
                detail.CreatedDate = FormatApiDate(headerExtras[11]);
                detail.ReimbursableExpense = headerExtras.Count >= 13 ? ToInt(headerExtras[12]) : null;
            }
            else if (headerExtras.Count == 11)
            {
                if (IsLikelyDateValue(headerExtras[10]))
                {
                    detail.Description = headerExtras[1];
                    detail.ExpenseSheetStatus = ToInt(headerExtras[2]);
                    detail.EstadoComentarios = null;
                    detail.UserId = headerExtras[3];
                    detail.CurrencyCode = headerExtras[4];
                    detail.TotalAmount = ToDecimal(headerExtras[5]);
                    detail.ExchRate = ToDecimal(headerExtras[6]);
                    detail.ExchangeRateMode = ToInt(headerExtras[7]);
                    detail.ProjId = headerExtras[8];
                    detail.Voucher = NormalizeVoucher(headerExtras[9]);
                    detail.CreatedDate = FormatApiDate(headerExtras[10]);
                }
                else
                {
                    detail.Description = headerExtras[1];
                    detail.ExpenseSheetStatus = ToInt(headerExtras[2]);
                    detail.EstadoComentarios = headerExtras[3];
                    detail.UserId = headerExtras[4];
                    detail.CurrencyCode = headerExtras[5];
                    detail.TotalAmount = ToDecimal(headerExtras[6]);
                    detail.ExchRate = ToDecimal(headerExtras[7]);
                    detail.ExchangeRateMode = ToInt(headerExtras[8]);
                    detail.ProjId = headerExtras[9];
                    detail.Voucher = NormalizeVoucher(headerExtras[10]);
                    detail.CreatedDate = null;
                }
            }
            else if (headerExtras.Count == 10)
            {
                detail.Description = headerExtras[1];
                detail.ExpenseSheetStatus = ToInt(headerExtras[2]);
                detail.EstadoComentarios = null;
                detail.UserId = headerExtras[3];
                detail.CurrencyCode = headerExtras[4];
                detail.TotalAmount = ToDecimal(headerExtras[5]);
                detail.ExchRate = ToDecimal(headerExtras[6]);
                detail.ExchangeRateMode = ToInt(headerExtras[7]);
                detail.ProjId = headerExtras[8];
                detail.Voucher = NormalizeVoucher(headerExtras[9]);
                detail.CreatedDate = null;
            }
            else if (headerExtras.Count >= 8)
            {
                detail.Description = headerExtras[1];
                detail.ExpenseSheetStatus = null;
                detail.EstadoComentarios = null;
                detail.UserId = headerExtras[2];
                detail.CurrencyCode = headerExtras[3];
                detail.TotalAmount = ToDecimal(headerExtras[4]);
                detail.ExchRate = ToDecimal(headerExtras[5]);
                detail.ExchangeRateMode = null;
                detail.ProjId = headerExtras[6];
                detail.Voucher = NormalizeVoucher(headerExtras[7]);
                detail.CreatedDate = null;
            }
            else
            {
                detail.Description = headerExtras[2];
                detail.ExpenseSheetStatus = null;
                detail.EstadoComentarios = null;
                detail.UserId = headerExtras[1];
                detail.CurrencyCode = headerExtras[3];
                detail.TotalAmount = null;
                detail.ExchRate = ToDecimal(headerExtras[4]);
                detail.ExchangeRateMode = null;
                detail.ProjId = headerExtras[5];
                detail.Voucher = NormalizeVoucher(headerExtras[6]);
                detail.CreatedDate = null;
            }

            var lineCount = AxContainerReadHelper.SafeLength(linesCon);
            for (int i = 1; i <= lineCount; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(linesCon, i);
                var rowLen = AxContainerReadHelper.SafeLength(row);
                if (row == null || rowLen < 9)
                    continue;

                // Current shape (14): [1]RecId [2]TransDate [3]Type [4]Description [5]Internacional [6]FileId [7]Price [8]Qty [9]Amount [10]ProjId [11]ReimbursableExpense [12]CurrencyCode [13]AmountMST [14]ExchRate
                // Current shape (10): [1]RecId [2]TransDate [3]Type [4]Description [5]Internacional [6]FileId [7]Price [8]Qty [9]Amount [10]ProjId
                // Previous shape (9): [1]RecId [2]TransDate [3]Type [4]Description [5]Internacional [6]FileId [7]Qty [8]Amount [9]ProjId
                var hasPriceColumn = rowLen >= 10;
                var hasReimbursableColumns = rowLen >= 14;
                var line = new ExpenseSheetLineDto
                {
                    RecId = AxContainerReadHelper.SafeString(row, 1),
                    TransDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 2)),
                    TypeValue = SafeInt(row, 3),
                    Description = AxContainerReadHelper.SafeString(row, 4),
                    Internacional = ToBool(AxContainerReadHelper.SafeString(row, 5)),
                    FileId = AxContainerReadHelper.SafeString(row, 6),
                    Price = hasPriceColumn ? SafeDecimal(row, 7) : null,
                    Qty = hasPriceColumn ? SafeDecimal(row, 8) : SafeDecimal(row, 7),
                    Amount = hasPriceColumn ? SafeDecimal(row, 9) : SafeDecimal(row, 8),
                    ProjId = hasPriceColumn ? AxContainerReadHelper.SafeString(row, 10) : AxContainerReadHelper.SafeString(row, 9),
                    ReimbursableExpense = hasReimbursableColumns ? SafeInt(row, 11) : null,
                    CurrencyCode = hasReimbursableColumns ? AxContainerReadHelper.SafeString(row, 12) : null,
                    AmountMST = hasReimbursableColumns ? SafeDecimal(row, 13) : null,
                    ExchRate = hasReimbursableColumns ? SafeDecimal(row, 14) : null
                };

                detail.Lines.Add(line);
            }

            return detail;
        }

        // Maps list items for expense sheet list endpoint.
        private static List<ExpenseSheetListItemDto> MapExpenseSheetList(IAxaptaContainer root, int page, int pageSize, out string message, out int total)
        {
            message = string.Empty;
            total = 0;
            var items = new List<ExpenseSheetListItemDto>();

            if (root == null || AxContainerReadHelper.SafeLength(root) == 0)
                return items;

            if (AxContainerReadHelper.IsSinDatos(root, out message))
                return items;

            total = AxContainerReadHelper.SafeLength(root);
            if (total <= 0)
                return items;

            var skipLong = ((long)page - 1L) * pageSize;
            if (skipLong < 0L)
                skipLong = 0L;

            if (skipLong >= total)
                return items;

            var start = (int)skipLong + 1;
            var end = Math.Min(total, start + pageSize - 1);
            for (int i = start; i <= end; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(root, i);
                var rowLen = AxContainerReadHelper.SafeLength(row);
                if (row == null || rowLen < 3)
                    continue;

                // AX list row mapping:
                // Current (14): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]EstadoComentarios [5]UserId [6]UserName [7]CurrencyCode [8]TotalAmountMST [9]ExchRate [10]ExchangeRateMode [11]ProjId [12]Voucher [13]CreatedDate [14]ReimbursableExpense
                // Current (13): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]EstadoComentarios [5]UserId [6]UserName [7]CurrencyCode [8]TotalAmountMST [9]ExchRate [10]ExchangeRateMode [11]ProjId [12]Voucher [13]CreatedDate
                // Current (12): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]EstadoComentarios [5]UserId [6]CurrencyCode [7]TotalAmountMST [8]ExchRate [9]ExchangeRateMode [10]ProjId [11]Voucher [12]CreatedDate
                // Current (11): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]EstadoComentarios [5]UserId [6]CurrencyCode [7]TotalAmountMST [8]ExchRate [9]ExchangeRateMode [10]ProjId [11]Voucher
                // Previous (11): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]UserId [5]CurrencyCode [6]TotalAmountMST [7]ExchRate [8]ExchangeRateMode [9]ProjId [10]Voucher [11]CreatedDate
                // Previous (10): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]UserId [5]CurrencyCode [6]TotalAmountMST [7]ExchRate [8]ExchangeRateMode [9]ProjId [10]Voucher
                // Current (9): [1]HojaGastosId [2]Description [3]ExpenseSheetStatus [4]CurrencyCode [5]TotalAmountMST [6]ExchRate [7]ExchangeRateMode [8]ProjId [9]Voucher
                // Previous (7): [1]HojaGastosId [2]Description [3]Voucher [4]ProjId [5]CurrencyCode [6]TotalAmountMST [7]CreatedDate
                // Legacy (5/6): [1]HojaGastosId [2]Description [3]ProjId [4]CurrencyCode [5]Amount|Date [6]Date|Amount
                if (rowLen >= 14)
                {
                    items.Add(new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                        EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                        UserId = AxContainerReadHelper.SafeString(row, 5),
                        UserName = AxContainerReadHelper.SafeString(row, 6),
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 12)),
                        ProjId = AxContainerReadHelper.SafeString(row, 11),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 7),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                        ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 9)),
                        ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 10)),
                        CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 13)),
                        ReimbursableExpense = ToInt(AxContainerReadHelper.SafeString(row, 14))
                    });
                    continue;
                }

                if (rowLen >= 13)
                {
                    items.Add(new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                        EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                        UserId = AxContainerReadHelper.SafeString(row, 5),
                        UserName = AxContainerReadHelper.SafeString(row, 6),
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 12)),
                        ProjId = AxContainerReadHelper.SafeString(row, 11),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 7),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                        ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 9)),
                        ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 10)),
                        CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 13))
                    });
                    continue;
                }

                if (rowLen == 12)
                {
                    items.Add(new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                        EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                        UserId = AxContainerReadHelper.SafeString(row, 5),
                        UserName = null,
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 11)),
                        ProjId = AxContainerReadHelper.SafeString(row, 10),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 6),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                        ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                        ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 9)),
                        CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 12))
                    });
                    continue;
                }

                if (rowLen == 11)
                {
                    var column11 = AxContainerReadHelper.SafeString(row, 11);
                    if (IsLikelyDateValue(column11))
                    {
                        items.Add(new ExpenseSheetListItemDto
                        {
                            HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                            Description = AxContainerReadHelper.SafeString(row, 2),
                            ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                            EstadoComentarios = null,
                            UserId = AxContainerReadHelper.SafeString(row, 4),
                            UserName = null,
                            Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 10)),
                            ProjId = AxContainerReadHelper.SafeString(row, 9),
                            CurrencyCode = AxContainerReadHelper.SafeString(row, 5),
                            TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                            ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                            ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 8)),
                            CreatedDate = FormatApiDate(column11)
                        });
                        continue;
                    }

                    items.Add(new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                        EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                        UserId = AxContainerReadHelper.SafeString(row, 5),
                        UserName = null,
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 11)),
                        ProjId = AxContainerReadHelper.SafeString(row, 10),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 6),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                        ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                        ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 9)),
                        CreatedDate = null
                    });
                    continue;
                }

                if (rowLen == 10)
                {
                    items.Add(new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                        EstadoComentarios = null,
                        UserId = AxContainerReadHelper.SafeString(row, 4),
                        UserName = null,
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 10)),
                        ProjId = AxContainerReadHelper.SafeString(row, 9),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 5),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                        ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                        ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 8)),
                        CreatedDate = null
                    });
                    continue;
                }

                if (rowLen == 9)
                {
                    items.Add(new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                        EstadoComentarios = null,
                        UserId = null,
                        UserName = null,
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 9)),
                        ProjId = AxContainerReadHelper.SafeString(row, 8),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 4),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 5)),
                        ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                        ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 7)),
                        CreatedDate = null
                    });
                    continue;
                }

                if (rowLen >= 7)
                {
                    items.Add(new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = null,
                        EstadoComentarios = null,
                        UserId = null,
                        UserName = null,
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 3)),
                        ProjId = AxContainerReadHelper.SafeString(row, 4),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 5),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                        ExchRate = null,
                        ExchangeRateMode = null,
                        CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 7))
                    });
                    continue;
                }

                var amountAndDate = ResolveAmountAndDate(row, rowLen);

                items.Add(new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = null,
                    EstadoComentarios = null,
                    UserId = null,
                    UserName = null,
                    Voucher = string.Empty,
                    ProjId = AxContainerReadHelper.SafeString(row, 3),
                    CurrencyCode = rowLen >= 4 ? AxContainerReadHelper.SafeString(row, 4) : string.Empty,
                    TotalAmount = amountAndDate.TotalAmount,
                    ExchRate = null,
                    ExchangeRateMode = null,
                    CreatedDate = FormatApiDate(amountAndDate.CreatedDate)
                });
            }

            return items;
        }

        // Resuelve de forma defensiva el monto total y la fecha cuando AX cambia el orden de columnas.
        private static (decimal? TotalAmount, string CreatedDate) ResolveAmountAndDate(IAxaptaContainer row, int rowLen)
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

        // Converts a string to int with invariant culture.
        private static int? ToInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
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
                "dd.MM.yyyy",
                "d.M.yyyy",
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

        // AX can return voucher as "0" when it is effectively empty.
        private static string NormalizeVoucher(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var trimmed = value.Trim();
            return trimmed == "0" ? string.Empty : trimmed;
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
