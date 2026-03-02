using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// CRM endpoints for expense-sheet tickets.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/crm/expensesheets/tickets")]
    public class CrmExpenseSheetTicketsController : BaseCrmController
    {
        private const int ModeCreateHeaderAndLines = 0;
        private const int ModeCreateHeaderOnly = 1;
        private const int ModeAddLinesToExisting = 2;
        private const int TicketStatusPending = 0;
        private const int TicketStatusAssigned = 1;
        private const int MaxPageSize = 50;
        private static readonly HashSet<int> AllowedGastoTypes = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 14 };

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IExpenseTicketBlobStorageService _ticketBlobStorage;

        /// <summary>
        /// Compatibility constructor when DI does not provide blob service explicitly.
        /// </summary>
        public CrmExpenseSheetTicketsController(
            IAxaptaSessionManager sessionManager,
            IAxLogger logger) : this(sessionManager, null, logger)
        {
        }

        /// <summary>
        /// Creates the controller with its dependencies.
        /// </summary>
        public CrmExpenseSheetTicketsController(
            IAxaptaSessionManager sessionManager,
            IExpenseTicketBlobStorageService ticketBlobStorage,
            IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
            _ticketBlobStorage = ticketBlobStorage ?? new ExpenseTicketBlobStorageService(logger);
        }
        /// <summary>
        /// Crea ticket de gasto (cabecera/lineas) en AX.
        /// </summary>
        /// <remarks>
        /// Modo:
        /// - 0: cabecera + lineas.
        /// - 1: solo cabecera.
        /// - 2: agregar lineas a un FileId existente.
        /// </remarks>
        [HttpPost, Route("")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.Created, "Ticket creado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public async Task<IHttpActionResult> CreateExpenseSheetTicket()
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            var modeValue = ModeCreateHeaderAndLines;
            string company;
            string axUserId;
            CreateExpenseSheetTicketRequest body = null;
            var bodyParseFailed = false;
            var rawBodyLength = 0;

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] CreateExpenseSheetTicket {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var rawBody = string.Empty;
                if (Request?.Content != null)
                {
                    rawBody = await Request.Content.ReadAsStringAsync();
                    rawBodyLength = rawBody?.Length ?? 0;
                }

                // Deserialize manually to avoid pre-action binder failures hiding diagnostics in this endpoint.
                if (!string.IsNullOrWhiteSpace(rawBody))
                {
                    try
                    {
                        body = JsonConvert.DeserializeObject<CreateExpenseSheetTicketRequest>(rawBody);
                    }
                    catch (JsonException jsonEx)
                    {
                        bodyParseFailed = true;
                        validationErrors.Add(new IndValidationError
                        {
                            Field = "body",
                            Message = "El JSON del body no es valido."
                        });
                        Logger.Log($"[WARN] CreateExpenseSheetTicket invalid-json msg={jsonEx.Message} traceId={traceId}");
                    }
                }

                modeValue = ResolveCreateTicketMode(body);
                Logger.Log(
                    $"[API-IN] CreateExpenseSheetTicket(entry) mode={ToLogValue(body?.mode)} bodyNull={(body == null)} bodyLength={rawBodyLength} traceId={traceId}");

                company = RequireCompanyOrReturn422(out var companyError, traceId);
                if (companyError != null)
                {
                    LogOut((HttpStatusCode)422);
                    return companyError;
                }

                axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
                if (userError != null)
                {
                    LogOut((HttpStatusCode)422);
                    return userError;
                }

                if (!ModelState.IsValid)
                    AddModelStateErrors(validationErrors);

                if (body == null && !bodyParseFailed)
                {
                    validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
                }
                else if (body != null)
                {
                    ValidateCreateTicketBody(body, modeValue, validationErrors);
                }

                if (validationErrors.Any())
                {
                    LogOut((HttpStatusCode)422);
                    return Content((HttpStatusCode)422, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error de validacion.",
                        ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                        Errors = validationErrors,
                        Data = null,
                        TraceId = traceId
                    });
                }

                var username = GetAuthenticatedUsername();
                var extension = NormalizeFileExtension(body.fileExtension, "jpg");
                var provisionalFileName = BuildProvisionalTicketFileName(axUserId, extension);
                var normalizedTransDate = modeValue == ModeAddLinesToExisting
                    ? string.Empty
                    : NormalizeYmdDate(body.transDate);

                Logger.Log(
                    $"[API-IN] CreateExpenseSheetTicket mode={modeValue} existingFileId={body.existingFileId} lines={body.lines?.Count ?? 0} " +
                    $"user={username} axUserId={axUserId} company={company} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var rootCon = ax.CreateContainer();
                rootCon.Append(company);

                var headerCon = ax.CreateContainer();
                if (modeValue == ModeAddLinesToExisting)
                {
                    headerCon.Append(axUserId);
                }
                else
                {
                    headerCon.Append(axUserId);
                    headerCon.Append(body.description?.Trim() ?? string.Empty);
                    headerCon.Append((body.currencyCode ?? string.Empty).Trim().ToUpperInvariant());
                    headerCon.Append(body.totalAmount ?? 0m);
                    headerCon.Append(normalizedTransDate);
                    headerCon.Append(body.comentario?.Trim() ?? string.Empty);
                    headerCon.Append(body.urlFile?.Trim() ?? string.Empty);
                    headerCon.Append(provisionalFileName);
                    if (body.gastoType.HasValue)
                        headerCon.Append(body.gastoType.Value);
                }
                rootCon.Append(headerCon);

                var linesCon = ax.CreateContainer();
                if (body.lines != null)
                {
                    foreach (var line in body.lines)
                    {
                        var lineCon = ax.CreateContainer();
                        lineCon.Append(line.description?.Trim() ?? string.Empty);
                        lineCon.Append(line.qty ?? 0m);
                        lineCon.Append(line.price ?? 0m);
                        if (line.totalAmount.HasValue)
                            lineCon.Append(line.totalAmount.Value);
                        linesCon.Append(lineCon);
                    }
                }
                rootCon.Append(linesCon);

                var optionsCon = ax.CreateContainer();
                optionsCon.Append(modeValue);
                optionsCon.Append(body.existingFileId?.Trim() ?? string.Empty);
                rootCon.Append(optionsCon);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "createExpenseSheetTicket",
                    rootCon
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                var fileId = extras.Count > 0 ? extras[0] : string.Empty;
                var ticketRecId = extras.Count > 1 ? extras[1] : string.Empty;
                var lineRecIds = MapRecIdList(linesOut);

                var finalFileName = modeValue == ModeAddLinesToExisting ? string.Empty : provisionalFileName;
                var fileNameFinalized = modeValue == ModeAddLinesToExisting;
                if (modeValue != ModeAddLinesToExisting && !string.IsNullOrWhiteSpace(fileId))
                {
                    finalFileName = BuildTicketFileName(axUserId, fileId, extension);
                    fileNameFinalized = TryFinalizeTicketFileName(
                        ax,
                        company,
                        axUserId,
                        fileId,
                        body,
                        modeValue,
                        finalFileName,
                        out var finalizeMessage);

                    if (!fileNameFinalized)
                    {
                        message = string.IsNullOrWhiteSpace(finalizeMessage)
                            ? message
                            : $"{message} {finalizeMessage}".Trim();

                        Logger.Log($"[WARN] CreateExpenseSheetTicket no pudo finalizar nombre de archivo fileId={fileId} traceId={traceId}");
                    }
                }

                var response = new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = new
                    {
                        FileId = fileId,
                        TicketRecId = ticketRecId,
                        LineRecIds = lineRecIds,
                        FileName = finalFileName,
                        FileNameFinalized = fileNameFinalized
                    },
                    TraceId = traceId
                };

                LogOut(HttpStatusCode.Created);
                return Content(HttpStatusCode.Created, response);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateExpenseSheetTicket: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Obtiene detalle de ticket por FileId.
        /// </summary>
        [HttpGet, Route("{fileId}")]
        [ResponseType(typeof(IndPagedResponse<ExpenseSheetTicketDetailDto>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Ticket encontrado", typeof(IndPagedResponse<ExpenseSheetTicketDetailDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Ticket no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetExpenseSheetTicket(string fileId)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetExpenseSheetTicket {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetExpenseSheetTicket fileId={fileId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(fileId.Trim());

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getExpenseSheetTicket",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                var detail = MapExpenseSheetTicketDetail(extras, linesOut);
                if (detail == null)
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                LogOut(HttpStatusCode.OK);
                return Ok(new IndPagedResponse<ExpenseSheetTicketDetailDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Items = new List<ExpenseSheetTicketDetailDto> { detail },
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetExpenseSheetTicket: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Lista tickets de gasto del usuario con filtros y paginacion.
        /// </summary>
        [HttpPost, Route("list")]
        [ResponseType(typeof(IndPagedResponse<ExpenseSheetTicketListItemDto>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de tickets", typeof(IndPagedResponse<ExpenseSheetTicketListItemDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetExpenseSheetTicketsList([FromBody] GetExpenseSheetTicketsListRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            string createdDateFromYmd = string.Empty;
            string createdDateToYmd = string.Empty;

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
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

                if (!string.IsNullOrWhiteSpace(body.createdDateFrom) && !TryNormalizeYmdDate(body.createdDateFrom, out createdDateFromYmd))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "createdDateFrom",
                        Message = "createdDateFrom debe ser yyyyMMdd o yyyy-MM-dd."
                    });
                }

                if (!string.IsNullOrWhiteSpace(body.createdDateTo) && !TryNormalizeYmdDate(body.createdDateTo, out createdDateToYmd))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "createdDateTo",
                        Message = "createdDateTo debe ser yyyyMMdd o yyyy-MM-dd."
                    });
                }

                if (body.status.HasValue && !IsValidTicketStatus(body.status.Value))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "status",
                        Message = "status invalido. Valores permitidos: 0 Pending, 1 Assigned."
                    });
                }

                if (body.gastoType.HasValue && !IsValidGastoType(body.gastoType.Value))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "gastoType",
                        Message = "gastoType invalido. Valores permitidos: 0, 1, 2, 3, 4, 5, 6, 7, 8, 14."
                    });
                }

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

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetExpenseSheetTicketsList {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var pageValue = body.page;
                var pageSizeValue = body.pageSize;
                var searchKeyValue = (body.searchKey ?? body.filter ?? string.Empty).Trim();
                var statusValue = body.status;
                var currencyCodeValue = (body.currencyCode ?? string.Empty).Trim().ToUpperInvariant();
                var gastoTypeValue = body.gastoType;
                var processedByAIValue = body.processedByAI;
                Logger.Log(
                    $"[API-IN] GetExpenseSheetTicketsList searchKey={searchKeyValue} status={ToLogValue(statusValue)} page={pageValue} pageSize={pageSizeValue} " +
                    $"createdDateFrom={createdDateFromYmd} createdDateTo={createdDateToYmd} currencyCode={currencyCodeValue} gastoType={ToLogValue(gastoTypeValue)} " +
                    $"processedByAI={(processedByAIValue.HasValue ? (processedByAIValue.Value ? "1" : "0") : "null")} " +
                    $"user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                // Use explicit token so AX does not interpret empty filters as 0.
                const string NoFilterToken = "null";
                con.Append(company);
                con.Append(axUserId);
                con.Append(searchKeyValue);
                if (statusValue.HasValue)
                    con.Append(statusValue.Value);
                else
                    con.Append(NoFilterToken);
                con.Append(createdDateFromYmd);
                con.Append(createdDateToYmd);
                con.Append(currencyCodeValue);
                if (gastoTypeValue.HasValue)
                    con.Append(gastoTypeValue.Value);
                else
                    con.Append(string.Empty);
                if (processedByAIValue.HasValue)
                    con.Append(processedByAIValue.Value ? 1 : 0);
                else
                    con.Append(NoFilterToken);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getExpenseSheetTicketsList",
                    con
                );

                var items = MapExpenseSheetTicketList(resultObj as IAxaptaContainer, pageValue, pageSizeValue, out var message, out var total);

                LogOut(HttpStatusCode.OK);
                return Ok(new IndPagedResponse<ExpenseSheetTicketListItemDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Total = total,
                    Page = pageValue,
                    PageSize = pageSizeValue,
                    Items = items,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetExpenseSheetTicketsList: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Actualiza cabecera y metadatos DocuRef de un ticket.
        /// </summary>
        [HttpPut, Route("{fileId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Ticket actualizado", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Ticket no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpdateExpenseSheetTicket(string fileId, [FromBody] UpdateExpenseSheetTicketRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (body.status.HasValue && !IsValidTicketStatus(body.status.Value))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "status",
                        Message = "status invalido. Valores permitidos: 0 Pending, 1 Assigned."
                    });
                }

                if (body.gastoType.HasValue && !IsValidGastoType(body.gastoType.Value))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "gastoType",
                        Message = "gastoType invalido. Valores permitidos: 0, 1, 2, 3, 4, 5, 6, 7, 8, 14."
                    });
                }

                if (!string.IsNullOrWhiteSpace(body.transDate) && !TryNormalizeYmdDate(body.transDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser yyyyMMdd o yyyy-MM-dd." });

                if (string.IsNullOrWhiteSpace(body.description) &&
                    string.IsNullOrWhiteSpace(body.currencyCode) &&
                    !body.gastoType.HasValue &&
                    !body.totalAmount.HasValue &&
                    !body.status.HasValue &&
                    !body.processedByAI.HasValue &&
                    string.IsNullOrWhiteSpace(body.transDate) &&
                    body.comentario == null &&
                    body.urlFile == null &&
                    string.IsNullOrWhiteSpace(body.fileName) &&
                    string.IsNullOrWhiteSpace(body.fileExtension))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "body",
                        Message = "Debe enviar al menos un campo para actualizar."
                    });
                }
            }

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] UpdateExpenseSheetTicket {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] UpdateExpenseSheetTicket fileId={fileId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var getCon = ax.CreateContainer();
                getCon.Append(company);
                getCon.Append(axUserId);
                getCon.Append(fileId.Trim());

                var getResultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getExpenseSheetTicket",
                    getCon
                );

                if (!TryReadHeader(getResultObj as IAxaptaContainer, out var getSuccess, out var getMessage, out var getExtras, out var getLinesOut))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!getSuccess)
                {
                    var getError = BuildTicketActionError(getMessage, traceId, out var getStatus);
                    LogOut(getStatus);
                    return Content(getStatus, getError);
                }

                var existing = MapExpenseSheetTicketDetail(getExtras, getLinesOut);
                if (existing == null)
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                var mergedDescription = (body.description ?? existing.Description ?? string.Empty).Trim();
                var mergedCurrencyCode = (body.currencyCode ?? existing.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
                var mergedGastoType = body.gastoType ?? existing.GastoType ?? 0;
                var mergedTotalAmount = body.totalAmount ?? existing.TotalAmount ?? 0m;
                var mergedStatus = body.status ?? existing.Status ?? TicketStatusPending;
                var mergedProcessedByAI = body.processedByAI ?? existing.ProcessedByAI ?? false;
                var mergedTransDateRaw = body.transDate ?? existing.TransDate;
                var mergedTransDate = TryNormalizeYmdDate(mergedTransDateRaw, out var normalizedTransDate)
                    ? normalizedTransDate
                    : DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var mergedComentario = (body.comentario ?? existing.Comentario ?? string.Empty).Trim();
                var mergedUrlFile = (body.urlFile ?? existing.UrlFile ?? string.Empty).Trim();
                var mergedFileName = (body.fileName ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(mergedFileName))
                {
                    if (!string.IsNullOrWhiteSpace(body.fileExtension))
                    {
                        var extension = NormalizeFileExtension(body.fileExtension, "jpg");
                        mergedFileName = BuildTicketFileName(axUserId, fileId.Trim(), extension);
                    }
                    else
                    {
                        mergedFileName = (existing.FileName ?? string.Empty).Trim();
                    }
                }

                var updateCon = ax.CreateContainer();
                updateCon.Append(company);
                updateCon.Append(axUserId);
                updateCon.Append(fileId.Trim());
                updateCon.Append(mergedDescription);
                updateCon.Append(mergedCurrencyCode);
                updateCon.Append(mergedTotalAmount);
                updateCon.Append(mergedStatus);
                updateCon.Append(mergedTransDate);
                updateCon.Append(mergedComentario);
                updateCon.Append(mergedUrlFile);
                updateCon.Append(mergedFileName);
                updateCon.Append(mergedProcessedByAI ? 1 : 0);
                updateCon.Append(mergedGastoType);

                var updateResultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "updateExpenseSheetTicket",
                    updateCon
                );

                if (!TryReadHeader(updateResultObj as IAxaptaContainer, out var success, out var message, out var updateExtras, out _))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                var responseFileId = updateExtras.Count > 0 ? updateExtras[0] : fileId.Trim();
                var responseProcessedByAI = updateExtras.Count > 1
                    ? (ToNullableBool(updateExtras[1]) ?? mergedProcessedByAI)
                    : mergedProcessedByAI;

                LogOut(HttpStatusCode.OK);
                return Ok(new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = new
                    {
                        FileId = responseFileId,
                        FileName = mergedFileName,
                        ProcessedByAI = responseProcessedByAI,
                        GastoType = mergedGastoType
                    },
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] UpdateExpenseSheetTicket: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Reemplaza cabecera y lineas del ticket con los datos analizados por IA.
        /// </summary>
        /// <remarks>
        /// Aplica reemplazo total del detalle de lineas (delete + insert) mediante metodo atomico de AX.
        /// </remarks>
        [HttpPost, Route("{fileId}/ia")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Ticket actualizado desde IA", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Ticket no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public async Task<IHttpActionResult> UpdateExpenseSheetTicketFromIA(string fileId)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            UpdateExpenseSheetTicketFromIARequest body = null;
            var compatibilityMode = false;
            var rawBodyLength = 0;

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            var rawBody = string.Empty;
            if (Request?.Content != null)
            {
                rawBody = await Request.Content.ReadAsStringAsync();
                rawBodyLength = rawBody?.Length ?? 0;
            }

            if (!TryBuildUpdateTicketFromIARequest(rawBody, out body, out var parseError, out compatibilityMode))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "body",
                    Message = parseError
                });
            }

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });

            if (body == null)
            {
                if (!validationErrors.Any(e => string.Equals(e.Field, "body", StringComparison.OrdinalIgnoreCase)))
                    validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(body.transDate) && !TryNormalizeYmdDate(body.transDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser yyyyMMdd o yyyy-MM-dd." });

                if (body.totalAmount.HasValue && body.totalAmount.Value <= 0m)
                    validationErrors.Add(new IndValidationError { Field = "totalAmount", Message = "totalAmount debe ser mayor que cero cuando se envia." });

                if (body.gastoType.HasValue && !IsValidGastoType(body.gastoType.Value))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "gastoType",
                        Message = "gastoType invalido. Valores permitidos: 0, 1, 2, 3, 4, 5, 6, 7, 8, 14."
                    });
                }

                if (body.lines == null || body.lines.Count == 0)
                {
                    validationErrors.Add(new IndValidationError { Field = "lines", Message = "lines debe incluir al menos una linea." });
                }
                else
                {
                    ValidateTicketLines(body.lines, validationErrors);
                }
            }

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] UpdateExpenseSheetTicketFromIA {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log(
                    $"[API-IN] UpdateExpenseSheetTicketFromIA fileId={fileId} user={username} axUserId={axUserId} " +
                    $"compatMode={compatibilityMode} bodyLength={rawBodyLength} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                if (!TryGetTicketDetailFromAx(ax, company, axUserId, fileId.Trim(), traceId, out var existing, out var getError, out var getStatus))
                {
                    LogOut(getStatus);
                    return Content(getStatus, getError);
                }

                var mergedDescription = (body.description ?? existing.Description ?? string.Empty).Trim();
                var mergedCurrencyCode = (body.currencyCode ?? existing.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
                var mergedGastoType = body.gastoType ?? existing.GastoType ?? 0;
                var linesTotalAmount = CalculateTicketLinesTotal(body.lines);
                var mergedTotalAmount = body.totalAmount.HasValue && body.totalAmount.Value > 0m
                    ? body.totalAmount.Value
                    : (linesTotalAmount > 0m ? linesTotalAmount : (existing.TotalAmount ?? 0m));
                var mergedTransDateRaw = string.IsNullOrWhiteSpace(body.transDate) ? existing.TransDate : body.transDate;
                var mergedTransDate = TryNormalizeYmdDate(mergedTransDateRaw, out var normalizedTransDate)
                    ? normalizedTransDate
                    : DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                var mergedComentario = (body.comentario ?? existing.Comentario ?? string.Empty).Trim();
                var mergedUrlFile = (body.urlFile ?? existing.UrlFile ?? string.Empty).Trim();
                var mergedFileName = (body.fileName ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(mergedFileName))
                {
                    if (!string.IsNullOrWhiteSpace(body.fileExtension))
                    {
                        var extension = NormalizeFileExtension(body.fileExtension, "jpg");
                        mergedFileName = BuildTicketFileName(axUserId, fileId.Trim(), extension);
                    }
                    else
                    {
                        mergedFileName = (existing.FileName ?? string.Empty).Trim();
                    }
                }

                if (string.IsNullOrWhiteSpace(mergedDescription))
                    validationErrors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio para aplicar IA." });

                if (string.IsNullOrWhiteSpace(mergedCurrencyCode))
                    validationErrors.Add(new IndValidationError { Field = "currencyCode", Message = "currencyCode es obligatorio para aplicar IA." });

                if (string.IsNullOrWhiteSpace(mergedUrlFile))
                    validationErrors.Add(new IndValidationError { Field = "urlFile", Message = "urlFile es obligatorio para aplicar IA." });

                if (string.IsNullOrWhiteSpace(mergedFileName))
                    validationErrors.Add(new IndValidationError { Field = "fileName", Message = "fileName o fileExtension es obligatorio para aplicar IA." });

                if (validationErrors.Any())
                {
                    LogOut((HttpStatusCode)422);
                    return Content((HttpStatusCode)422, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error de validacion.",
                        ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                        Errors = validationErrors,
                        Data = null,
                        TraceId = traceId
                    });
                }

                var rootCon = ax.CreateContainer();
                rootCon.Append(company);

                var headerCon = ax.CreateContainer();
                headerCon.Append(axUserId);
                headerCon.Append(fileId.Trim());
                headerCon.Append(mergedDescription);
                headerCon.Append(mergedCurrencyCode);
                headerCon.Append(mergedTotalAmount);
                headerCon.Append(mergedTransDate);
                headerCon.Append(mergedComentario);
                headerCon.Append(mergedUrlFile);
                headerCon.Append(mergedFileName);
                headerCon.Append(mergedGastoType);
                rootCon.Append(headerCon);

                var linesCon = ax.CreateContainer();
                foreach (var line in body.lines)
                {
                    var qty = line.qty ?? 0m;
                    var price = line.price ?? 0m;
                    var lineTotal = line.totalAmount.HasValue && line.totalAmount.Value > 0m
                        ? line.totalAmount.Value
                        : qty * price;

                    var lineCon = ax.CreateContainer();
                    lineCon.Append(line.description?.Trim() ?? string.Empty);
                    lineCon.Append(qty);
                    lineCon.Append(price);
                    lineCon.Append(lineTotal);
                    linesCon.Append(lineCon);
                }
                rootCon.Append(linesCon);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "updateExpenseSheetTicketFromIA",
                    rootCon
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                var responseData = new
                {
                    FileId = extras.Count > 0 ? extras[0] : fileId.Trim(),
                    TicketRecId = extras.Count > 1 ? extras[1] : string.Empty,
                    TotalAmount = extras.Count > 2 ? ToDecimal(extras[2]) : mergedTotalAmount,
                    ProcessedByAI = extras.Count > 3 ? (ToNullableBool(extras[3]) ?? true) : true,
                    GastoType = mergedGastoType,
                    FileName = mergedFileName,
                    LineRecIds = MapRecIdList(linesOut)
                };

                LogOut(HttpStatusCode.OK);
                return Ok(new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = responseData,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] UpdateExpenseSheetTicketFromIA: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Sube o reemplaza la imagen del ticket en Azure Blob y sincroniza DocuRef en AX.
        /// </summary>
        /// <remarks>
        /// Espera Content-Type multipart/form-data con un archivo (campo libre, primer archivo del payload).
        /// Usa formato de nombre: yyyyMMddHHmmss_axUserId_fileId.ext
        /// </remarks>
        [HttpPost, Route("{fileId}/file")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.Created, "Archivo del ticket cargado", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Ticket no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult UploadExpenseSheetTicketFile(string fileId, [FromUri] string extension = null)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });

            if (Request?.Content == null || !Request.Content.IsMimeMultipartContent())
                validationErrors.Add(new IndValidationError { Field = "file", Message = "Se requiere multipart/form-data con el archivo del ticket." });

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] UploadExpenseSheetTicketFile {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var cleanFileId = fileId.Trim();
                Logger.Log(
                    $"[API-IN] UploadExpenseSheetTicketFile fileId={cleanFileId} user={username} axUserId={axUserId} traceId={traceId}");

                var provider = new MultipartMemoryStreamProvider();
                Request.Content.ReadAsMultipartAsync(provider).GetAwaiter().GetResult();

                var fileContent = provider.Contents.FirstOrDefault(p =>
                    p != null &&
                    p.Headers != null &&
                    p.Headers.ContentDisposition != null &&
                    !string.IsNullOrWhiteSpace(p.Headers.ContentDisposition.FileName));

                if (fileContent == null)
                {
                    LogOut((HttpStatusCode)422);
                    return Content((HttpStatusCode)422, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "No se recibio archivo en multipart/form-data.",
                        ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                        Errors = new List<IndValidationError>
                        {
                            new IndValidationError { Field = "file", Message = "Debe enviar un archivo." }
                        },
                        Data = null,
                        TraceId = traceId
                    });
                }

                var sourceFileName = UnquoteFileName(fileContent.Headers.ContentDisposition.FileName);
                var extensionCandidate = !string.IsNullOrWhiteSpace(extension)
                    ? extension
                    : Path.GetExtension(sourceFileName);
                var normalizedExtension = NormalizeFileExtension(extensionCandidate, "jpg");
                var finalFileName = BuildTicketFileName(axUserId, cleanFileId, normalizedExtension);
                var contentType = fileContent.Headers?.ContentType?.MediaType;

                var ax = _sessionManager.GetAxInstanceForUser(username);
                if (!TryGetTicketDetailFromAx(ax, company, axUserId, cleanFileId, traceId, out var existingTicket, out var getError, out var getStatus))
                {
                    LogOut(getStatus);
                    return Content(getStatus, getError);
                }

                TicketBlobUploadResult uploadResult;
                using (var stream = fileContent.ReadAsStreamAsync().GetAwaiter().GetResult())
                {
                    uploadResult = _ticketBlobStorage.UploadTicketFile(
                        company,
                        axUserId,
                        cleanFileId,
                        finalFileName,
                        stream,
                        contentType);
                }

                if (!TryUpdateTicketFromExisting(
                        ax,
                        company,
                        axUserId,
                        cleanFileId,
                        existingTicket,
                        uploadResult.BlobUrl,
                        finalFileName,
                        traceId,
                        out var updateMessage,
                        out var updateError,
                        out var updateStatus))
                {
                    try
                    {
                        _ticketBlobStorage.DeleteTicketFileByUrl(uploadResult.BlobUrl);
                    }
                    catch (Exception rollbackEx)
                    {
                        Logger.Log($"[WARN] UploadExpenseSheetTicketFile rollback blob delete failed: {rollbackEx.Message} traceId={traceId}");
                    }

                    LogOut(updateStatus);
                    return Content(updateStatus, updateError);
                }

                LogOut(HttpStatusCode.Created);
                return Content(HttpStatusCode.Created, new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(updateMessage) ? "Archivo cargado correctamente." : updateMessage,
                    ErrorCode = null,
                    Errors = null,
                    Data = new
                    {
                        FileId = cleanFileId,
                        FileName = finalFileName,
                        UrlFile = uploadResult.BlobUrl,
                        BlobName = uploadResult.BlobName,
                        ContentType = contentType
                    },
                    TraceId = traceId
                });
            }
            catch (InvalidOperationException ex)
            {
                Logger.Log($"[ERROR] UploadExpenseSheetTicketFile storage configuration: {ex.Message}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "No se pudo acceder a la configuracion de Azure Blob Storage.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketFileStorageNotConfigured,
                    Data = null,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] UploadExpenseSheetTicketFile: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno al cargar archivo del ticket.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.CrmExpenseSheetTicketFileUploadFailed,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Elimina la imagen asociada al ticket en Azure Blob y limpia DocuRef en AX.
        /// </summary>
        [HttpDelete, Route("{fileId}/file")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Archivo del ticket eliminado", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Ticket o archivo no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteExpenseSheetTicketFile(string fileId)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] DeleteExpenseSheetTicketFile {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var cleanFileId = fileId.Trim();
                Logger.Log(
                    $"[API-IN] DeleteExpenseSheetTicketFile fileId={cleanFileId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                if (!TryGetTicketDetailFromAx(ax, company, axUserId, cleanFileId, traceId, out var existingTicket, out var getError, out var getStatus))
                {
                    LogOut(getStatus);
                    return Content(getStatus, getError);
                }

                if (string.IsNullOrWhiteSpace(existingTicket.UrlFile) && string.IsNullOrWhiteSpace(existingTicket.FileName))
                {
                    LogOut(HttpStatusCode.NotFound);
                    return Content(HttpStatusCode.NotFound, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "El ticket no tiene archivo asociado.",
                        ErrorCode = IndErrorCodes.CrmExpenseSheetTicketFileNotFound,
                        Data = null,
                        TraceId = traceId
                    });
                }

                var blobDeleted = false;
                if (!string.IsNullOrWhiteSpace(existingTicket.UrlFile))
                {
                    blobDeleted = _ticketBlobStorage.DeleteTicketFileByUrl(existingTicket.UrlFile);
                }

                if (!TryUpdateTicketFromExisting(
                        ax,
                        company,
                        axUserId,
                        cleanFileId,
                        existingTicket,
                        string.Empty,
                        string.Empty,
                        traceId,
                        out var updateMessage,
                        out var updateError,
                        out var updateStatus))
                {
                    LogOut(updateStatus);
                    return Content(updateStatus, updateError);
                }

                LogOut(HttpStatusCode.OK);
                return Ok(new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(updateMessage) ? "Archivo eliminado correctamente." : updateMessage,
                    ErrorCode = null,
                    Errors = null,
                    Data = new
                    {
                        FileId = cleanFileId,
                        BlobDeleted = blobDeleted
                    },
                    TraceId = traceId
                });
            }
            catch (InvalidOperationException ex)
            {
                Logger.Log($"[ERROR] DeleteExpenseSheetTicketFile storage configuration: {ex.Message}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "No se pudo acceder a la configuracion de Azure Blob Storage.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketFileStorageNotConfigured,
                    Data = null,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] DeleteExpenseSheetTicketFile: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno al eliminar archivo del ticket.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.CrmExpenseSheetTicketFileDeleteFailed,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Elimina ticket completo o una linea granular de ticket.
        /// </summary>
        [HttpDelete, Route("{fileId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Eliminacion aplicada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Ticket no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteExpenseSheetTicket(string fileId, [FromUri] long? lineRecId = null)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });

            if (lineRecId.HasValue && lineRecId.Value <= 0)
                validationErrors.Add(new IndValidationError { Field = "lineRecId", Message = "lineRecId debe ser mayor que cero." });

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] DeleteExpenseSheetTicket {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log(
                    $"[API-IN] DeleteExpenseSheetTicket fileId={fileId} lineRecId={lineRecId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(fileId.Trim());
                if (lineRecId.HasValue)
                    con.Append(lineRecId.Value.ToString(CultureInfo.InvariantCulture));

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "deleteExpenseSheetTicket",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out _, out _))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                LogOut(HttpStatusCode.OK);
                return Ok(new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = new { FileId = fileId.Trim(), LineRecId = lineRecId },
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] DeleteExpenseSheetTicket: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Crea una linea granular de ticket.
        /// </summary>
        [HttpPost, Route("{fileId}/lines")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.Created, "Linea de ticket creada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult CreateExpenseSheetTicketLine(string fileId, [FromBody] ExpenseSheetTicketLineRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });

            ValidateTicketLineBody(body, "body", validationErrors);

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] CreateExpenseSheetTicketLine {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] CreateExpenseSheetTicketLine fileId={fileId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(fileId.Trim());
                con.Append(body.description?.Trim() ?? string.Empty);
                con.Append(body.qty ?? 0m);
                con.Append(body.price ?? 0m);
                if (body.totalAmount.HasValue)
                    con.Append(body.totalAmount.Value);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "createExpenseSheetTicketLine",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out _))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                var data = new
                {
                    FileId = extras.Count > 0 ? extras[0] : fileId.Trim(),
                    LineRecId = extras.Count > 1 ? extras[1] : string.Empty,
                    TotalAmount = extras.Count > 2 ? ToDecimal(extras[2]) : null
                };

                LogOut(HttpStatusCode.Created);
                return Content(HttpStatusCode.Created, new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] CreateExpenseSheetTicketLine: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Actualiza una linea granular de ticket.
        /// </summary>
        [HttpPut, Route("{fileId}/lines/{lineRecId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Linea de ticket actualizada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Linea o ticket no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpdateExpenseSheetTicketLine(string fileId, long lineRecId, [FromBody] ExpenseSheetTicketLineRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });
            if (lineRecId <= 0)
                validationErrors.Add(new IndValidationError { Field = "lineRecId", Message = "lineRecId es obligatorio." });

            ValidateTicketLineBody(body, "body", validationErrors);

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] UpdateExpenseSheetTicketLine {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log(
                    $"[API-IN] UpdateExpenseSheetTicketLine fileId={fileId} lineRecId={lineRecId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(fileId.Trim());
                con.Append(lineRecId.ToString(CultureInfo.InvariantCulture));
                con.Append(body.description?.Trim() ?? string.Empty);
                con.Append(body.qty ?? 0m);
                con.Append(body.price ?? 0m);
                if (body.totalAmount.HasValue)
                    con.Append(body.totalAmount.Value);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "updateExpenseSheetTicketLine",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out _))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                var data = new
                {
                    FileId = extras.Count > 0 ? extras[0] : fileId.Trim(),
                    LineRecId = extras.Count > 1 ? extras[1] : lineRecId.ToString(CultureInfo.InvariantCulture),
                    TotalAmount = extras.Count > 2 ? ToDecimal(extras[2]) : null
                };

                LogOut(HttpStatusCode.OK);
                return Ok(new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] UpdateExpenseSheetTicketLine: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        /// <summary>
        /// Elimina una linea granular de ticket.
        /// </summary>
        [HttpDelete, Route("{fileId}/lines/{lineRecId}")]
        [ResponseType(typeof(IndApiResponse<object>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Linea de ticket eliminada", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Linea o ticket no encontrado", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteExpenseSheetTicketLine(string fileId, long lineRecId)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            if (string.IsNullOrWhiteSpace(fileId))
                validationErrors.Add(new IndValidationError { Field = "fileId", Message = "fileId es obligatorio." });
            if (lineRecId <= 0)
                validationErrors.Add(new IndValidationError { Field = "lineRecId", Message = "lineRecId es obligatorio." });

            if (validationErrors.Any())
            {
                return Content((HttpStatusCode)422, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] DeleteExpenseSheetTicketLine {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log(
                    $"[API-IN] DeleteExpenseSheetTicketLine fileId={fileId} lineRecId={lineRecId} user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(fileId.Trim());
                con.Append(lineRecId.ToString(CultureInfo.InvariantCulture));

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "deleteExpenseSheetTicketLine",
                    con
                );

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out _))
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    });
                }

                if (!success)
                {
                    var error = BuildTicketActionError(message, traceId, out var status);
                    LogOut(status);
                    return Content(status, error);
                }

                var data = new
                {
                    FileId = extras.Count > 0 ? extras[0] : fileId.Trim(),
                    LineRecId = extras.Count > 1 ? extras[1] : lineRecId.ToString(CultureInfo.InvariantCulture),
                    TotalAmount = extras.Count > 2 ? ToDecimal(extras[2]) : null
                };

                LogOut(HttpStatusCode.OK);
                return Ok(new IndApiResponse<object>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] DeleteExpenseSheetTicketLine: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = traceId
                });
            }
        }

        // Resolves ticket creation mode with backward-compatible default.
        private static int ResolveCreateTicketMode(CreateExpenseSheetTicketRequest body)
        {
            if (body == null || !body.mode.HasValue)
                return ModeCreateHeaderAndLines;

            return body.mode.Value;
        }

        // Validates create ticket body based on selected mode.
        private static void ValidateCreateTicketBody(CreateExpenseSheetTicketRequest body, int mode, List<IndValidationError> errors)
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

            var hasLines = body.lines != null && body.lines.Count > 0;
            if (body.gastoType.HasValue && !IsValidGastoType(body.gastoType.Value))
            {
                errors.Add(new IndValidationError
                {
                    Field = "gastoType",
                    Message = "gastoType invalido. Valores permitidos: 0, 1, 2, 3, 4, 5, 6, 7, 8, 14."
                });
            }

            if (mode == ModeCreateHeaderAndLines || mode == ModeCreateHeaderOnly)
            {
                if (string.IsNullOrWhiteSpace(body.description))
                    errors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio cuando mode es 0 o 1." });

                if (string.IsNullOrWhiteSpace(body.currencyCode))
                    errors.Add(new IndValidationError { Field = "currencyCode", Message = "currencyCode es obligatorio cuando mode es 0 o 1." });

                if (!TryNormalizeYmdDate(body.transDate, out _))
                    errors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser yyyyMMdd o yyyy-MM-dd cuando mode es 0 o 1." });

                if (string.IsNullOrWhiteSpace(body.urlFile))
                    errors.Add(new IndValidationError { Field = "urlFile", Message = "urlFile es obligatorio cuando mode es 0 o 1." });
            }

            if (mode == ModeCreateHeaderAndLines)
            {
                if (!hasLines)
                {
                    errors.Add(new IndValidationError { Field = "lines", Message = "lines es obligatorio cuando mode es 0." });
                    return;
                }

                ValidateTicketLines(body.lines, errors);
                return;
            }

            if (mode == ModeCreateHeaderOnly)
            {
                if (hasLines)
                    errors.Add(new IndValidationError { Field = "lines", Message = "lines debe ser null o vacio cuando mode es 1." });
                return;
            }

            if (string.IsNullOrWhiteSpace(body.existingFileId))
                errors.Add(new IndValidationError { Field = "existingFileId", Message = "existingFileId es obligatorio cuando mode es 2." });

            if (!hasLines)
            {
                errors.Add(new IndValidationError { Field = "lines", Message = "lines debe incluir al menos una linea cuando mode es 2." });
                return;
            }

            ValidateTicketLines(body.lines, errors);
        }

        // Validates ticket line input for create operations.
        private static void ValidateTicketLines(List<ExpenseSheetTicketLineRequest> lines, List<IndValidationError> errors)
        {
            for (int i = 0; i < lines.Count; i++)
            {
                ValidateTicketLineBody(lines[i], $"lines[{i}]", errors);
            }
        }

        // Validates one ticket line payload.
        private static void ValidateTicketLineBody(ExpenseSheetTicketLineRequest body, string prefix, List<IndValidationError> errors)
        {
            if (body == null)
            {
                errors.Add(new IndValidationError { Field = prefix, Message = "linea es obligatoria." });
                return;
            }

            if (string.IsNullOrWhiteSpace(body.description))
                errors.Add(new IndValidationError { Field = prefix + ".description", Message = "description es obligatorio." });

            if (!body.qty.HasValue || body.qty.Value <= 0)
                errors.Add(new IndValidationError { Field = prefix + ".qty", Message = "qty debe ser mayor que cero." });

            if (!body.price.HasValue || body.price.Value <= 0)
                errors.Add(new IndValidationError { Field = prefix + ".price", Message = "price debe ser mayor que cero." });
        }

        // Builds the IA update request and applies compatibility mapping when body comes wrapped in { Success, Message, Data }.
        private static bool TryBuildUpdateTicketFromIARequest(
            string rawBody,
            out UpdateExpenseSheetTicketFromIARequest request,
            out string errorMessage,
            out bool compatibilityMode)
        {
            request = null;
            errorMessage = string.Empty;
            compatibilityMode = false;

            if (string.IsNullOrWhiteSpace(rawBody))
                return true;

            try
            {
                var root = JsonConvert.DeserializeObject<JObject>(rawBody);
                if (root == null)
                {
                    errorMessage = "El JSON del body no es valido.";
                    return false;
                }

                var directRequest = root.ToObject<UpdateExpenseSheetTicketFromIARequest>();
                if (HasAnyUpdateTicketFromIAField(directRequest))
                {
                    request = directRequest;
                    return true;
                }

                var dataObject = GetJsonObjectIgnoreCase(root, "data");
                if (dataObject == null)
                {
                    request = directRequest;
                    return true;
                }

                request = BuildCompatibilityUpdateTicketFromIARequest(dataObject);
                compatibilityMode = true;
                return true;
            }
            catch (JsonException)
            {
                errorMessage = "El JSON del body no es valido.";
                return false;
            }
        }

        // Checks if the payload already matches the expected IA update contract.
        private static bool HasAnyUpdateTicketFromIAField(UpdateExpenseSheetTicketFromIARequest request)
        {
            if (request == null)
                return false;

            if (!string.IsNullOrWhiteSpace(request.description) ||
                !string.IsNullOrWhiteSpace(request.currencyCode) ||
                request.gastoType.HasValue ||
                request.totalAmount.HasValue ||
                !string.IsNullOrWhiteSpace(request.transDate) ||
                !string.IsNullOrWhiteSpace(request.comentario) ||
                !string.IsNullOrWhiteSpace(request.urlFile) ||
                !string.IsNullOrWhiteSpace(request.fileName) ||
                !string.IsNullOrWhiteSpace(request.fileExtension))
            {
                return true;
            }

            return request.lines != null && request.lines.Count > 0;
        }

        // Maps expensefromticket Data payload to the IA update contract.
        private static UpdateExpenseSheetTicketFromIARequest BuildCompatibilityUpdateTicketFromIARequest(JObject dataObject)
        {
            var mapped = new UpdateExpenseSheetTicketFromIARequest
            {
                description = GetJsonStringIgnoreCase(dataObject, "description"),
                currencyCode = NormalizeDraftCurrencyCode(
                    GetJsonStringIgnoreCase(dataObject, "currencyCode"),
                    GetJsonStringIgnoreCase(dataObject, "rawCurrency")),
                gastoType = GetJsonIntIgnoreCase(dataObject, "gastoType"),
                totalAmount = GetJsonDecimalIgnoreCase(dataObject, "totalAmount"),
                transDate = GetJsonStringIgnoreCase(dataObject, "transDate"),
                comentario = GetJsonStringIgnoreCase(dataObject, "comentario"),
                urlFile = GetJsonStringIgnoreCase(dataObject, "urlFile"),
                fileName = GetJsonStringIgnoreCase(dataObject, "fileName"),
                fileExtension = GetJsonStringIgnoreCase(dataObject, "fileExtension"),
                lines = new List<ExpenseSheetTicketLineRequest>()
            };

            if (string.IsNullOrWhiteSpace(mapped.comentario))
                mapped.comentario = GetJsonStringIgnoreCase(dataObject, "merchant");

            var linesArray = GetJsonArrayIgnoreCase(dataObject, "lines");
            if (linesArray != null)
            {
                foreach (var lineToken in linesArray.OfType<JObject>())
                {
                    var lineDescription = GetJsonStringIgnoreCase(lineToken, "description");
                    var lineQty = GetJsonDecimalIgnoreCase(lineToken, "qty");
                    var linePrice = GetJsonDecimalIgnoreCase(lineToken, "price");
                    var lineTotal = GetJsonDecimalIgnoreCase(lineToken, "totalAmount");
                    if (!lineTotal.HasValue && lineQty.HasValue && linePrice.HasValue)
                        lineTotal = lineQty.Value * linePrice.Value;

                    if (string.IsNullOrWhiteSpace(mapped.transDate))
                    {
                        var lineTransDate = GetJsonStringIgnoreCase(lineToken, "transDate");
                        if (TryNormalizeYmdDate(lineTransDate, out var normalizedDate))
                            mapped.transDate = normalizedDate;
                    }

                    mapped.lines.Add(new ExpenseSheetTicketLineRequest
                    {
                        description = lineDescription,
                        qty = lineQty,
                        price = linePrice,
                        totalAmount = lineTotal
                    });
                }
            }

            if (mapped.lines.Count == 0)
                mapped.lines = null;

            if (!mapped.gastoType.HasValue)
                mapped.gastoType = ResolveGastoTypeFromDraftLines(linesArray);

            if (!mapped.totalAmount.HasValue || mapped.totalAmount.Value <= 0m)
                mapped.totalAmount = CalculateTicketLinesTotal(mapped.lines);

            var ticketCreation = GetJsonObjectIgnoreCase(dataObject, "ticketCreation");
            if (ticketCreation != null)
            {
                if (string.IsNullOrWhiteSpace(mapped.urlFile))
                    mapped.urlFile = GetJsonStringIgnoreCase(ticketCreation, "urlFile");

                if (string.IsNullOrWhiteSpace(mapped.fileName))
                    mapped.fileName = GetJsonStringIgnoreCase(ticketCreation, "fileName");
            }

            if (string.IsNullOrWhiteSpace(mapped.fileExtension))
            {
                mapped.fileExtension = TryExtractFileExtension(mapped.fileName);
                if (string.IsNullOrWhiteSpace(mapped.fileExtension))
                    mapped.fileExtension = TryExtractFileExtension(mapped.urlFile);
            }

            return mapped;
        }

        // Infers header gastoType from IA draft lines when top-level gastoType is missing.
        private static int? ResolveGastoTypeFromDraftLines(JArray linesArray)
        {
            if (linesArray == null || linesArray.Count == 0)
                return null;

            var firstByType = new Dictionary<int, int>();
            for (int i = 0; i < linesArray.Count; i++)
            {
                var lineObject = linesArray[i] as JObject;
                var typeValue = GetJsonIntIgnoreCase(lineObject, "typeValue");
                if (!typeValue.HasValue || !AllowedGastoTypes.Contains(typeValue.Value))
                    continue;

                if (!firstByType.ContainsKey(typeValue.Value))
                    firstByType[typeValue.Value] = i;
            }

            var dominant = linesArray
                .OfType<JObject>()
                .Select(line => GetJsonIntIgnoreCase(line, "typeValue"))
                .Where(typeValue => typeValue.HasValue && AllowedGastoTypes.Contains(typeValue.Value))
                .GroupBy(typeValue => typeValue.Value)
                .Select(group => new
                {
                    TypeValue = group.Key,
                    Count = group.Count(),
                    FirstIndex = firstByType.ContainsKey(group.Key) ? firstByType[group.Key] : int.MaxValue
                })
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.FirstIndex)
                .FirstOrDefault();

            if (dominant != null)
                return dominant.TypeValue;

            return null;
        }

        // Normalizes currency code from IA draft metadata.
        private static string NormalizeDraftCurrencyCode(string currencyCode, string rawCurrency)
        {
            var normalizedCode = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
            if (!string.IsNullOrWhiteSpace(normalizedCode))
                return normalizedCode;

            var raw = (rawCurrency ?? string.Empty).Trim().ToLowerInvariant();
            switch (raw)
            {
                case "eur":
                case "euro":
                    return "EUR";
                case "usd":
                case "dolar":
                case "dollar":
                    return "USD";
                case "gbp":
                case "libra":
                case "pound":
                    return "GBP";
                default:
                    return string.Empty;
            }
        }

        // Gets a string value from JObject using case-insensitive key lookup.
        private static string GetJsonStringIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
                return string.Empty;

            var token = source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
            return token == null || token.Type == JTokenType.Null
                ? string.Empty
                : token.ToString().Trim();
        }

        // Gets a decimal value from JObject using case-insensitive key lookup.
        private static decimal? GetJsonDecimalIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            var token = source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<decimal>();

            return ToDecimal(token.ToString());
        }

        // Gets an integer value from JObject using case-insensitive key lookup.
        private static int? GetJsonIntIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            var token = source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase);
            if (token == null || token.Type == JTokenType.Null)
                return null;

            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            return ToInt(token.ToString());
        }

        // Gets an object value from JObject using case-insensitive key lookup.
        private static JObject GetJsonObjectIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            return source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase) as JObject;
        }

        // Gets an array value from JObject using case-insensitive key lookup.
        private static JArray GetJsonArrayIgnoreCase(JObject source, string propertyName)
        {
            if (source == null || string.IsNullOrWhiteSpace(propertyName))
                return null;

            return source.GetValue(propertyName, StringComparison.OrdinalIgnoreCase) as JArray;
        }

        // Extracts extension without dot from filename or URL.
        private static string TryExtractFileExtension(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var source = text.Trim();
            if (Uri.TryCreate(source, UriKind.Absolute, out var uri))
                source = uri.AbsolutePath;

            var extension = Path.GetExtension(source);
            if (string.IsNullOrWhiteSpace(extension))
                return string.Empty;

            return extension.Trim().TrimStart('.').ToLowerInvariant();
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

        // Validates allowed values for AX INDTicketStatus.
        private static bool IsValidTicketStatus(int status)
        {
            return status == TicketStatusPending || status == TicketStatusAssigned;
        }

        // Validates allowed values for AX CRMGastoType.
        private static bool IsValidGastoType(int gastoType)
        {
            return AllowedGastoTypes.Contains(gastoType);
        }

        // Normalizes extension text for generated ticket filenames.
        private static string NormalizeFileExtension(string extension, string defaultExtension)
        {
            var fallback = string.IsNullOrWhiteSpace(defaultExtension) ? "jpg" : defaultExtension.Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension))
                return fallback;

            var normalized = extension.Trim().TrimStart('.').ToLowerInvariant();
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        // Builds a temporary filename before FileId is available.
        private static string BuildProvisionalTicketFileName(string axUserId, string extension)
        {
            var safeUser = string.IsNullOrWhiteSpace(axUserId) ? "axuser" : axUserId.Trim();
            var ext = NormalizeFileExtension(extension, "jpg");
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1}_pending.{2}",
                DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                safeUser,
                ext);
        }

        // Builds final ticket filename with format yyyymmddhhmmss_axUserId_fileId.ext.
        private static string BuildTicketFileName(string axUserId, string fileId, string extension)
        {
            var safeUser = string.IsNullOrWhiteSpace(axUserId) ? "axuser" : axUserId.Trim();
            var safeFileId = string.IsNullOrWhiteSpace(fileId) ? "nofileid" : fileId.Trim();
            var ext = NormalizeFileExtension(extension, "jpg");
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0}_{1}_{2}.{3}",
                DateTime.UtcNow.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture),
                safeUser,
                safeFileId,
                ext);
        }

        // Finalizes filename by calling AX updateExpenseSheetTicket with full stable values.
        private bool TryFinalizeTicketFileName(
            Axapta2Class ax,
            string company,
            string axUserId,
            string fileId,
            CreateExpenseSheetTicketRequest source,
            int mode,
            string finalFileName,
            out string message)
        {
            message = string.Empty;

            if (ax == null || string.IsNullOrWhiteSpace(company) || string.IsNullOrWhiteSpace(axUserId) || string.IsNullOrWhiteSpace(fileId))
                return false;

            try
            {
                var currencyCode = (source?.currencyCode ?? string.Empty).Trim().ToUpperInvariant();
                var descripcion = source?.description?.Trim() ?? string.Empty;
                var comentario = source?.comentario?.Trim() ?? string.Empty;
                var urlFile = source?.urlFile?.Trim() ?? string.Empty;
                var transDate = NormalizeYmdDate(source?.transDate);

                var totalAmount = source?.totalAmount ?? 0m;
                if (mode == ModeCreateHeaderAndLines)
                    totalAmount = CalculateTicketLinesTotal(source?.lines);

                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(axUserId);
                con.Append(fileId);
                con.Append(descripcion);
                con.Append(currencyCode);
                con.Append(totalAmount);
                con.Append(TicketStatusPending);
                con.Append(transDate);
                con.Append(comentario);
                con.Append(urlFile);
                con.Append(finalFileName ?? string.Empty);
                con.Append(0);
                if (source?.gastoType.HasValue == true)
                    con.Append(source.gastoType.Value);

                var result = ax.CallStaticClassMethod("INDCRMExpenseSheetService", "updateExpenseSheetTicket", con);
                if (!TryReadHeader(result as IAxaptaContainer, out var success, out var axMessage, out _, out _))
                {
                    message = "No se pudo finalizar INDFilename.";
                    return false;
                }

                message = axMessage ?? string.Empty;
                return success;
            }
            catch (Exception ex)
            {
                Logger.Log($"[WARN] TryFinalizeTicketFileName: {ex.Message}");
                message = "No se pudo finalizar INDFilename.";
                return false;
            }
        }

        // Calculates total amount from ticket lines using qty*price or provided totalAmount.
        private static decimal CalculateTicketLinesTotal(List<ExpenseSheetTicketLineRequest> lines)
        {
            if (lines == null || lines.Count == 0)
                return 0m;

            decimal total = 0m;
            foreach (var line in lines)
            {
                if (line == null)
                    continue;

                var qty = line.qty ?? 0m;
                var price = line.price ?? 0m;
                if (qty <= 0m || price <= 0m)
                    continue;

                var lineTotal = line.totalAmount.HasValue && line.totalAmount.Value > 0m
                    ? line.totalAmount.Value
                    : qty * price;

                total += lineTotal;
            }

            return total;
        }

        // Obtiene detalle de ticket desde AX validando ownership por company y axUserId.
        private bool TryGetTicketDetailFromAx(
            Axapta2Class ax,
            string company,
            string axUserId,
            string fileId,
            string traceId,
            out ExpenseSheetTicketDetailDto detail,
            out IndApiResponse<object> error,
            out HttpStatusCode status)
        {
            detail = null;
            error = null;
            status = HttpStatusCode.OK;

            var getCon = ax.CreateContainer();
            getCon.Append(company);
            getCon.Append(axUserId);
            getCon.Append(fileId);

            var getResultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "getExpenseSheetTicket",
                getCon
            );

            if (!TryReadHeader(getResultObj as IAxaptaContainer, out var getSuccess, out var getMessage, out var getExtras, out var getLinesOut))
            {
                status = HttpStatusCode.InternalServerError;
                error = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error al procesar la respuesta de AX.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Data = null,
                    TraceId = traceId
                };
                return false;
            }

            if (!getSuccess)
            {
                error = BuildTicketActionError(getMessage, traceId, out status);
                return false;
            }

            detail = MapExpenseSheetTicketDetail(getExtras, getLinesOut);
            if (detail == null)
            {
                status = HttpStatusCode.InternalServerError;
                error = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error al procesar la respuesta de AX.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Data = null,
                    TraceId = traceId
                };
                return false;
            }

            return true;
        }

        // Actualiza cabecera ticket en AX reutilizando valores existentes y sobreescribiendo URL/nombre de archivo.
        private bool TryUpdateTicketFromExisting(
            Axapta2Class ax,
            string company,
            string axUserId,
            string fileId,
            ExpenseSheetTicketDetailDto existing,
            string mergedUrlFile,
            string mergedFileName,
            string traceId,
            out string message,
            out IndApiResponse<object> error,
            out HttpStatusCode status)
        {
            message = string.Empty;
            error = null;
            status = HttpStatusCode.OK;

            if (existing == null)
            {
                status = HttpStatusCode.InternalServerError;
                error = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "No se pudo actualizar el ticket porque no se pudo recuperar su estado.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Data = null,
                    TraceId = traceId
                };
                return false;
            }

            var normalizedTransDate = TryNormalizeYmdDate(existing.TransDate, out var parsedTransDate)
                ? parsedTransDate
                : DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            var statusValue = existing.Status ?? TicketStatusPending;
            if (!IsValidTicketStatus(statusValue))
                statusValue = TicketStatusPending;

            var con = ax.CreateContainer();
            con.Append(company);
            con.Append(axUserId);
            con.Append(fileId);
            con.Append((existing.Description ?? string.Empty).Trim());
            con.Append((existing.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant());
            con.Append(existing.TotalAmount ?? 0m);
            con.Append(statusValue);
            con.Append(normalizedTransDate);
            con.Append((existing.Comentario ?? string.Empty).Trim());
            con.Append((mergedUrlFile ?? string.Empty).Trim());
            con.Append((mergedFileName ?? string.Empty).Trim());
            con.Append((existing.ProcessedByAI ?? false) ? 1 : 0);
            if (existing.GastoType.HasValue)
                con.Append(existing.GastoType.Value);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "updateExpenseSheetTicket",
                con
            );

            if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var axMessage, out _, out _))
            {
                status = HttpStatusCode.InternalServerError;
                error = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error al procesar la respuesta de AX.",
                    ErrorCode = IndErrorCodes.AxComError,
                    Data = null,
                    TraceId = traceId
                };
                return false;
            }

            if (!success)
            {
                error = BuildTicketActionError(axMessage, traceId, out status);
                message = axMessage;
                return false;
            }

            message = axMessage ?? string.Empty;
            return true;
        }

        // Limpia comillas del nombre de archivo recibido por multipart.
        private static string UnquoteFileName(string fileName)
        {
            return string.IsNullOrWhiteSpace(fileName)
                ? string.Empty
                : fileName.Trim().Trim('"');
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

        // Builds a standard error response for ticket actions.
        private IndApiResponse<object> BuildTicketActionError(string message, string traceId, out HttpStatusCode status)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("no encontrada") || lower.Contains("no encontrado") || lower.Contains("no existe"))
            {
                status = HttpStatusCode.NotFound;
                return new IndApiResponse<object>
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(message) ? "Ticket no encontrado." : message,
                    ErrorCode = lower.Contains("linea") ? IndErrorCodes.CrmExpenseSheetTicketLineNotFound : IndErrorCodes.CrmExpenseSheetTicketNotFound,
                    Data = null,
                    TraceId = traceId
                };
            }

            if (lower.Contains("asignado"))
            {
                status = (HttpStatusCode)422;
                return new IndApiResponse<object>
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(message) ? "Ticket asignado a linea de gastos." : message,
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketAssigned,
                    Data = null,
                    TraceId = traceId
                };
            }

            status = (HttpStatusCode)422;
            return new IndApiResponse<object>
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message) ? "Error de validacion." : message,
                ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                Data = null,
                TraceId = traceId
            };
        }

        // Maps ticket header extras + lines to typed detail DTO.
        private static ExpenseSheetTicketDetailDto MapExpenseSheetTicketDetail(List<string> headerExtras, IAxaptaContainer linesCon)
        {
            if (headerExtras == null || headerExtras.Count < 6)
                return null;

            var detail = new ExpenseSheetTicketDetailDto
            {
                FileId = headerExtras.Count > 0 ? headerExtras[0] : string.Empty,
                Description = headerExtras.Count > 1 ? headerExtras[1] : string.Empty,
                Status = headerExtras.Count > 2 ? ToInt(headerExtras[2]) : null,
                // Keep null when AX omits optional fields.
                GastoType = headerExtras.Count > 11 ? ToInt(headerExtras[11]) : null,
                CurrencyCode = headerExtras.Count > 3 ? headerExtras[3] : string.Empty,
                TotalAmount = headerExtras.Count > 4 ? ToDecimal(headerExtras[4]) : null,
                CreatedByUserId = headerExtras.Count > 5 ? headerExtras[5] : string.Empty,
                TransDate = headerExtras.Count > 6 ? headerExtras[6] : string.Empty,
                Comentario = headerExtras.Count > 7 ? headerExtras[7] : string.Empty,
                UrlFile = headerExtras.Count > 8 ? headerExtras[8] : string.Empty,
                FileName = headerExtras.Count > 9 ? headerExtras[9] : string.Empty,
                ProcessedByAI = headerExtras.Count > 10 ? ToNullableBool(headerExtras[10]) : null,
                HojaGastosIdDisplay = headerExtras.Count > 12 ? headerExtras[12] : string.Empty,
                Lines = new List<ExpenseSheetTicketLineDto>()
            };

            var lineCount = AxContainerReadHelper.SafeLength(linesCon);
            for (int i = 1; i <= lineCount; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(linesCon, i);
                if (row == null || AxContainerReadHelper.SafeLength(row) < 7)
                    continue;

                detail.Lines.Add(new ExpenseSheetTicketLineDto
                {
                    RecId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    Qty = SafeDecimal(row, 3),
                    Price = SafeDecimal(row, 4),
                    TotalAmount = SafeDecimal(row, 5),
                    RefRecIdTable = AxContainerReadHelper.SafeString(row, 6),
                    CreatedByUserId = AxContainerReadHelper.SafeString(row, 7)
                });
            }

            return detail;
        }

        // Maps AX ticket list rows to typed DTO list.
        private static List<ExpenseSheetTicketListItemDto> MapExpenseSheetTicketList(IAxaptaContainer root, int page, int pageSize, out string message, out int total)
        {
            message = string.Empty;
            total = 0;
            var items = new List<ExpenseSheetTicketListItemDto>();

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
                if (row == null || AxContainerReadHelper.SafeLength(row) < 9)
                    continue;

                items.Add(new ExpenseSheetTicketListItemDto
                {
                    FileId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    Status = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                    GastoType = ToInt(AxContainerReadHelper.SafeString(row, 11)),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 4),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 5)),
                    CreatedByUserId = AxContainerReadHelper.SafeString(row, 6),
                    TransDate = AxContainerReadHelper.SafeString(row, 7),
                    UrlFile = AxContainerReadHelper.SafeString(row, 8),
                    FileName = AxContainerReadHelper.SafeString(row, 9),
                    ProcessedByAI = ToNullableBool(AxContainerReadHelper.SafeString(row, 10)),
                    HojaGastosIdDisplay = AxContainerReadHelper.SafeString(row, 12)
                });
            }

            return items;
        }

        private static List<long> MapRecIdList(IAxaptaContainer linesCon)
        {
            var list = new List<long>();
            var len = AxContainerReadHelper.SafeLength(linesCon);
            for (int i = 1; i <= len; i++)
            {
                var value = AxContainerReadHelper.SafeValue(linesCon, i);
                if (value != null && long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recId))
                    list.Add(recId);
            }

            return list;
        }


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

        private static bool ToBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (bool.TryParse(value, out var parsed))
                return parsed;

            return value == "1";
        }

        private static bool? ToNullableBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            if (bool.TryParse(trimmed, out var parsed))
                return parsed;

            if (trimmed == "1")
                return true;

            if (trimmed == "0")
                return false;

            return null;
        }


    }
}
