using System;
using DiagnosticsStopwatch = global::System.Diagnostics.Stopwatch;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
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
        private const string BulkSelectionModeSelected = "selected";
        private const string BulkSelectionModeFiltered = "filtered";
        private const string QuickCreateStageTicketCreated = "ticket-created";
        private const string QuickCreateStageFileUploaded = "file-uploaded";
        private const string QuickCreateStageDraftExtracted = "draft-extracted";
        private const string QuickCreateStageTicketFinalized = "ticket-finalized";
        private const string QuickCreateStageSheetLinked = "sheet-linked";
        private static readonly HashSet<int> AllowedGastoTypes = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 14 };

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IExpenseTicketBlobStorageService _ticketBlobStorage;
        private readonly IND_IExpenseTicketDraftService _ticketDraft;

        /// <summary>
        /// Compatibility constructor when DI does not provide blob service explicitly.
        /// </summary>
        public CrmExpenseSheetTicketsController(
            IAxaptaSessionManager sessionManager,
            IAxLogger logger) : this(sessionManager, null, null, logger)
        {
        }

        /// <summary>
        /// Creates the controller with its dependencies.
        /// </summary>
        public CrmExpenseSheetTicketsController(
            IAxaptaSessionManager sessionManager,
            IExpenseTicketBlobStorageService ticketBlobStorage,
            IAxLogger logger) : this(sessionManager, ticketBlobStorage, null, logger)
        {
        }

        /// <summary>
        /// Creates the controller with its dependencies.
        /// </summary>
        public CrmExpenseSheetTicketsController(
            IAxaptaSessionManager sessionManager,
            IExpenseTicketBlobStorageService ticketBlobStorage,
            IND_IExpenseTicketDraftService ticketDraft,
            IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
            _ticketBlobStorage = ticketBlobStorage ?? new ExpenseTicketBlobStorageService(logger);
            _ticketDraft = ticketDraft;
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
                    : NormalizeApiDateToAxYmd(body.transDate);

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
                    var hasExtendedDocuRefJson = body.ocrJson != null || body.normalizedJson != null;
                    if (body.gastoType.HasValue || hasExtendedDocuRefJson)
                    {
                        headerCon.Append(body.gastoType ?? 0);

                        if (hasExtendedDocuRefJson)
                        {
                            headerCon.Append(body.ocrJson ?? string.Empty);
                            headerCon.Append(body.normalizedJson ?? string.Empty);
                        }
                    }
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
        /// Orquesta el alta rapida de ticket desde imagen en un solo request multipart.
        /// </summary>
        [HttpPost, Route("quick-create")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.Created, "Ticket creado y finalizado", typeof(IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Ticket u hoja no encontrada", typeof(IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>))]
        [SwaggerResponse((HttpStatusCode)429, "Limite de uso excedido", typeof(IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>))]
        public async Task<IHttpActionResult> QuickCreateExpenseSheetTicket(CancellationToken cancellationToken)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var totalSw = DiagnosticsStopwatch.StartNew();
            var resultData = new ExpenseSheetTicketQuickCreateResultDto
            {
                LinkedToSheet = false,
                ProcessedByAI = false,
                CompletedStage = string.Empty,
                StepTraceIds = new ExpenseSheetTicketQuickCreateStepTraceIdsDto()
            };
            long? readFormMs = null;
            long? createMs = null;
            long? uploadMs = null;
            long? draftMs = null;
            long? finalizeMs = null;
            long? linkMs = null;

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
            if (userError != null)
                return userError;

            var ticketAiProcessing = _ticketDraft as ITicketAIProcessingService;
            if (ticketAiProcessing == null)
            {
                return Content(HttpStatusCode.InternalServerError, new IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>
                {
                    Success = false,
                    Message = "El servicio de procesamiento IA no esta disponible.",
                    ErrorCode = IndErrorCodes.InternalError,
                    Data = null,
                    Errors = null,
                    TraceId = traceId
                });
            }

            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] QuickCreateExpenseSheetTicket {(int)statusCode} traceId={traceId}");
            }

            string PerfValue(long? value)
            {
                return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "na";
            }

            try
            {
                var readFormSw = DiagnosticsStopwatch.StartNew();
                var quickCreateForm = await ReadQuickCreateFormAsync(cancellationToken, traceId).ConfigureAwait(false);
                readFormMs = readFormSw.ElapsedMilliseconds;
                if (!quickCreateForm.Success)
                {
                    LogOut(quickCreateForm.StatusCode);
                    return Content(quickCreateForm.StatusCode, quickCreateForm.ErrorResponse);
                }

                var username = GetAuthenticatedUsername();
                var createStepTraceId = Guid.NewGuid().ToString("N");
                resultData.StepTraceIds.TicketCreate = createStepTraceId;
                resultData.HojaGastosId = string.IsNullOrWhiteSpace(quickCreateForm.ExistingHojaGastosId)
                    ? null
                    : quickCreateForm.ExistingHojaGastosId;

                Logger.Log(
                    $"[API-IN] QuickCreateExpenseSheetTicket user={username} axUserId={axUserId} company={company} " +
                    $"existingHojaGastosId={ToLogValue(resultData.HojaGastosId)} traceId={traceId}");

                var provisionalDescription = !string.IsNullOrWhiteSpace(quickCreateForm.Description)
                    ? quickCreateForm.Description.Trim()
                    : ExpenseTicketImageHelper.BuildDescriptionFromFileName(quickCreateForm.OriginalFileName);
                var provisionalCurrencyCode = NormalizeQuickCreateCurrencyCode(quickCreateForm.CurrencyCode);
                var provisionalComentario = (quickCreateForm.Comentario ?? string.Empty).Trim();
                var provisionalUrlFile = $"pending://ticket-upload/{traceId}";
                var provisionalCreateRequest = new CreateExpenseSheetTicketRequest
                {
                    mode = ModeCreateHeaderOnly,
                    description = provisionalDescription,
                    currencyCode = provisionalCurrencyCode,
                    totalAmount = 0m,
                    transDate = DateTime.Today.ToString("ddMMyyyy", CultureInfo.InvariantCulture),
                    comentario = provisionalComentario,
                    urlFile = provisionalUrlFile,
                    fileExtension = quickCreateForm.Extension,
                    lines = null
                };

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var createSw = DiagnosticsStopwatch.StartNew();
                if (!TryCreateQuickCreateProvisionalTicket(
                        ax,
                        company,
                        axUserId,
                        provisionalCreateRequest,
                        createStepTraceId,
                        out var createResult,
                        out var createError,
                        out var createStatus))
                {
                    LogOut(createStatus);
                    return Content(createStatus, createError);
                }
                createMs = createSw.ElapsedMilliseconds;

                resultData.FileId = createResult.FileId;
                resultData.FileName = createResult.FileName;
                resultData.UrlFile = provisionalUrlFile;
                resultData.CompletedStage = QuickCreateStageTicketCreated;

                var uploadStepTraceId = Guid.NewGuid().ToString("N");
                resultData.StepTraceIds.FileUpload = uploadStepTraceId;

                var uploadSw = DiagnosticsStopwatch.StartNew();
                if (!TryUploadQuickCreateTicketFile(
                        ax,
                        company,
                        axUserId,
                        quickCreateForm.ImageBytes,
                        quickCreateForm.ContentType,
                        quickCreateForm.Extension,
                        resultData.FileId,
                        uploadStepTraceId,
                        traceId,
                        out var fileUploadResult,
                        out var uploadMessage,
                        out var uploadErrorCode,
                        out var uploadStatus))
                {
                    LogOut(uploadStatus);
                    return Content(uploadStatus, BuildQuickCreateErrorResponse(
                        traceId,
                        uploadMessage,
                        uploadErrorCode,
                        resultData,
                        null));
                }
                uploadMs = uploadSw.ElapsedMilliseconds;

                resultData.UrlFile = fileUploadResult.UrlFile;
                resultData.FileName = fileUploadResult.FileName;
                resultData.CompletedStage = QuickCreateStageFileUploaded;

                Logger.Log(
                    $"[QUICKCREATE-AI-ARCH] mode=azure-docs-json azureDocumentIntelligence=true legacyOpenAiImage=false fileId={ToLogValue(resultData.FileId)} fileName={ToLogValue(resultData.FileName)} urlFile={ToLogValue(resultData.UrlFile)} traceId={traceId}");

                var draftStepTraceId = Guid.NewGuid().ToString("N");
                resultData.StepTraceIds.DraftExtract = draftStepTraceId;

                ExpenseSheetDraftResponse draft;
                QuickCreateDraftExtractionResult draftExtraction;
                var draftSw = DiagnosticsStopwatch.StartNew();
                try
                {
                    draftExtraction = await ExtractQuickCreateDraftWithFallbackAsync(
                        ticketAiProcessing,
                        quickCreateForm.OriginalFileName,
                        resultData.UrlFile,
                        resultData.UrlFile,
                        resultData.FileName,
                        quickCreateForm.Extension,
                        provisionalDescription,
                        provisionalCurrencyCode,
                        provisionalComentario,
                        traceId,
                        cancellationToken).ConfigureAwait(false);
                    draft = draftExtraction?.Draft;
                }
                catch (IND_OpenAiRateLimitException ex)
                {
                    LogOut((HttpStatusCode)429);
                    return BuildQuickCreateTooManyRequests(
                        traceId,
                        string.IsNullOrWhiteSpace(ex.Message) ? "Limite de uso IA excedido." : ex.Message,
                        IndErrorCodes.AiRateLimitExceeded,
                        ex.RetryAfterSeconds,
                        resultData);
                }
                catch (OperationCanceledException)
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, BuildQuickCreateErrorResponse(
                        traceId,
                        "Timeout o cancelacion en la extraccion del draft.",
                        IndErrorCodes.InternalError,
                        resultData,
                        null));
                }
                catch (Exception ex)
                {
                    Logger.Log($"[ERROR] QuickCreateExpenseSheetTicket draft: {ex}");
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, BuildQuickCreateErrorResponse(
                        traceId,
                        "Error de extraccion de borrador.",
                        IndErrorCodes.InternalError,
                        resultData,
                        null));
                }
                draftMs = draftSw.ElapsedMilliseconds;

                if (draft == null)
                {
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, BuildQuickCreateErrorResponse(
                        traceId,
                        "No se pudo generar el borrador desde el ticket.",
                        IndErrorCodes.InternalError,
                        resultData,
                        null));
                }

                resultData.CompletedStage = QuickCreateStageDraftExtracted;

                var updateRequest = draftExtraction.UpdateRequest;
                var transDateResolution = draftExtraction.TransDateResolution;

                Logger.Log(
                    $"[QUICKCREATE-DATE] rawTransDate={ToLogValue(transDateResolution.RawTransDate)} normalizedTransDate={ToLogValue(transDateResolution.NormalizedTransDateYmd)} fallback={transDateResolution.UsedFallback} fileId={ToLogValue(resultData.FileId)} completedStage={ToLogValue(resultData.CompletedStage)} reason={ToLogValue(transDateResolution.Reason)} traceId={traceId}",
                    transDateResolution.UsedFallback ? AxaptaSessionManager.LogLevel.Warning : AxaptaSessionManager.LogLevel.Info);

                var normalizationErrors = draftExtraction.ValidationErrors ?? new List<IndValidationError>();
                if (normalizationErrors.Any())
                {
                    LogOut((HttpStatusCode)422);
                    return Content((HttpStatusCode)422, BuildQuickCreateErrorResponse(
                        traceId,
                        "Error de validacion.",
                        IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                        resultData,
                        normalizationErrors));
                }

                var finalizeStepTraceId = Guid.NewGuid().ToString("N");
                resultData.StepTraceIds.TicketFinalize = finalizeStepTraceId;

                var finalizeSw = DiagnosticsStopwatch.StartNew();
                if (!TryApplyTicketFromIACore(
                        ax,
                        company,
                        axUserId,
                        resultData.FileId,
                        updateRequest,
                        traceId,
                        out var applyResult,
                        out var applyError,
                        out var applyStatus))
                {
                    LogOut(applyStatus);
                    return Content(applyStatus, BuildQuickCreateErrorResponse(
                        traceId,
                        applyError?.Message ?? "No se pudo finalizar el ticket.",
                        applyError?.ErrorCode ?? IndErrorCodes.InternalError,
                        resultData,
                        applyError?.Errors));
                }
                finalizeMs = finalizeSw.ElapsedMilliseconds;

                resultData.FileName = string.IsNullOrWhiteSpace(applyResult.FileName) ? resultData.FileName : applyResult.FileName;
                resultData.ProcessedByAI = applyResult.ProcessedByAI;
                resultData.CompletedStage = QuickCreateStageTicketFinalized;

                if (!string.IsNullOrWhiteSpace(quickCreateForm.ExistingHojaGastosId))
                {
                    var linkStepTraceId = Guid.NewGuid().ToString("N");
                    resultData.StepTraceIds.SheetLink = linkStepTraceId;

                    var linkSw = DiagnosticsStopwatch.StartNew();
                    if (!TryGetExpenseSheetTicketDetail(
                            ax,
                            company,
                            axUserId,
                            resultData.FileId,
                            traceId,
                            out var linkedTicketDetail,
                            out var linkedTicketMessage,
                            out var linkedTicketStatus))
                    {
                        LogOut(linkedTicketStatus);
                        return Content(linkedTicketStatus, BuildQuickCreateErrorResponse(
                            traceId,
                            NormalizeIssueReason(linkedTicketMessage, "No se pudo cargar el ticket final."),
                            linkedTicketStatus == HttpStatusCode.InternalServerError ? IndErrorCodes.AxComError : IndErrorCodes.CrmExpenseSheetTicketNotFound,
                            resultData,
                            null));
                    }

                    if (!TryLinkTicketToExpenseSheet(
                            ax,
                            company,
                            axUserId,
                            quickCreateForm.ExistingHojaGastosId,
                            linkedTicketDetail,
                            traceId,
                            quickCreateForm.ProjectId,
                            out var linkMessage,
                            out var linkStatus))
                    {
                        var linkError = BuildExpenseSheetActionError(linkMessage, traceId, out _);
                        LogOut(linkStatus);
                        return Content(linkStatus, BuildQuickCreateErrorResponse(
                            traceId,
                            linkError.Message,
                            linkError.ErrorCode,
                            resultData,
                            linkError.Errors));
                    }

                    linkMs = linkSw.ElapsedMilliseconds;
                    resultData.LinkedToSheet = true;
                    resultData.HojaGastosId = quickCreateForm.ExistingHojaGastosId;
                    resultData.CompletedStage = QuickCreateStageSheetLinked;
                }

                LogOut(HttpStatusCode.Created);
                return Content(HttpStatusCode.Created, new IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Errors = null,
                    Data = resultData,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] QuickCreateExpenseSheetTicket: {ex}");
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, BuildQuickCreateErrorResponse(
                    traceId,
                    "Error interno del servidor.",
                    ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    resultData.FileId == null ? null : resultData,
                    null));
            }
            finally
            {
                Logger.Log(
                    $"[PERF] QuickCreateExpenseSheetTicket totalMs={totalSw.ElapsedMilliseconds} readFormMs={PerfValue(readFormMs)} createMs={PerfValue(createMs)} uploadMs={PerfValue(uploadMs)} draftMs={PerfValue(draftMs)} finalizeMs={PerfValue(finalizeMs)} linkMs={PerfValue(linkMs)} completedStage={ToLogValue(resultData.CompletedStage)} fileId={ToLogValue(resultData.FileId)} traceId={traceId}");
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
                ValidateTicketListPagingAndDates(
                    body.page,
                    body.pageSize,
                    body.createdDateFrom,
                    body.createdDateTo,
                    validationErrors,
                    out createdDateFromYmd,
                    out createdDateToYmd);
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
                var statusValue = NormalizeTicketStatusOrNull(body.status);
                var currencyCodeValue = (body.currencyCode ?? string.Empty).Trim().ToUpperInvariant();
                var gastoTypeValue = NormalizeGastoTypeOrNull(body.gastoType);
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
        /// Lista tickets pendientes disponibles para vinculacion con filtros y paginacion.
        /// </summary>
        [HttpPost, Route("link/list")]
        [ResponseType(typeof(IndPagedResponse<ExpenseSheetTicketLinkListItemDto>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de tickets para vinculacion", typeof(IndPagedResponse<ExpenseSheetTicketLinkListItemDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetExpenseSheetTicketLinkList([FromBody] GetExpenseSheetTicketLinkListRequest body)
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
                ValidateTicketListPagingAndDates(
                    body.page,
                    body.pageSize,
                    body.createdDateFrom,
                    body.createdDateTo,
                    validationErrors,
                    out createdDateFromYmd,
                    out createdDateToYmd);
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
                Logger.Log($"[API-OUT] GetExpenseSheetTicketLinkList {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var pageValue = body.page;
                var pageSizeValue = body.pageSize;
                var searchKeyValue = (body.searchKey ?? body.filter ?? string.Empty).Trim();
                var currencyCodeValue = (body.currencyCode ?? string.Empty).Trim().ToUpperInvariant();
                var gastoTypeValue = NormalizeGastoTypeOrNull(body.gastoType);
                var processedByAIValue = body.processedByAI;
                Logger.Log(
                    $"[API-IN] GetExpenseSheetTicketLinkList searchKey={searchKeyValue} page={pageValue} pageSize={pageSizeValue} " +
                    $"createdDateFrom={createdDateFromYmd} createdDateTo={createdDateToYmd} currencyCode={currencyCodeValue} gastoType={ToLogValue(gastoTypeValue)} " +
                    $"processedByAI={(processedByAIValue.HasValue ? (processedByAIValue.Value ? "1" : "0") : "null")} " +
                    $"user={username} axUserId={axUserId} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = BuildExpenseSheetTicketLinkListRequestContainer(
                    ax,
                    company,
                    axUserId,
                    searchKeyValue,
                    createdDateFromYmd,
                    createdDateToYmd,
                    currencyCodeValue,
                    gastoTypeValue,
                    processedByAIValue);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getExpenseSheetTicketsLinkList",
                    con
                );

                var items = MapExpenseSheetTicketLinkList(resultObj as IAxaptaContainer, pageValue, pageSizeValue, out var message, out var total);

                LogOut(HttpStatusCode.OK);
                return Ok(new IndPagedResponse<ExpenseSheetTicketLinkListItemDto>
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
                Logger.Log($"[ERROR] GetExpenseSheetTicketLinkList: {ex}");
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
        /// Vincula multiples tickets a una hoja existente reutilizando createExpenseSheet en modo add-lines.
        /// </summary>
        [HttpPost, Route("link/bulk")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetTicketBulkLinkResultDto>))]
        [SwaggerOperation(Tags = new[] { "Tickets de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Resultado de vinculacion bulk", typeof(IndApiResponse<ExpenseSheetTicketBulkLinkResultDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Hoja de gastos no encontrada", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult BulkLinkExpenseSheetTickets([FromBody] BulkLinkExpenseSheetTicketsRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            var selectionMode = BulkSelectionModeSelected;
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
                ValidateBulkLinkRequest(
                    body,
                    validationErrors,
                    out selectionMode,
                    out createdDateFromYmd,
                    out createdDateToYmd);
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
                Logger.Log($"[API-OUT] BulkLinkExpenseSheetTickets {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var expenseSheetId = body.expenseSheetId.Trim();
                var requestedTicketIds = new List<string>();
                var ax = _sessionManager.GetAxInstanceForUser(username);
                if (!TryResolveBulkLinkTicketIds(
                        ax,
                        company,
                        axUserId,
                        body,
                        selectionMode,
                        createdDateFromYmd,
                        createdDateToYmd,
                        out requestedTicketIds,
                        out var resolveMessage,
                        out var resolveStatus))
                {
                    LogOut(resolveStatus);
                    return Content(resolveStatus, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = NormalizeIssueReason(resolveMessage, "Error al procesar la respuesta de AX."),
                        ErrorCode = resolveStatus == HttpStatusCode.InternalServerError ? IndErrorCodes.AxComError : IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                        Data = null,
                        TraceId = traceId
                    });
                }

                Logger.Log(
                    $"[API-IN] BulkLinkExpenseSheetTickets expenseSheetId={expenseSheetId} selectionMode={selectionMode} requested={requestedTicketIds.Count} " +
                    $"user={username} axUserId={axUserId} company={company} traceId={traceId}");

                if (!TryGetExpenseSheetTargetInfo(ax, company, axUserId, expenseSheetId, traceId, out var targetInfo, out var targetMessage, out var targetStatus))
                {
                    LogOut(targetStatus);
                    if (targetStatus == HttpStatusCode.InternalServerError)
                    {
                        return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                        {
                            Success = false,
                            Message = NormalizeIssueReason(targetMessage, "Error al procesar la respuesta de AX."),
                            ErrorCode = IndErrorCodes.AxComError,
                            Data = null,
                            TraceId = traceId
                        });
                    }

                    return Content(targetStatus, BuildExpenseSheetActionError(targetMessage, traceId, out _));
                }

                if (!string.IsNullOrWhiteSpace(targetInfo.Voucher))
                {
                    LogOut((HttpStatusCode)422);
                    return Content((HttpStatusCode)422, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "La hoja de gastos no esta abierta. No se pueden anadir lineas.",
                        ErrorCode = IndErrorCodes.CrmExpenseSheetLocked,
                        Data = null,
                        TraceId = traceId
                    });
                }

                var result = new ExpenseSheetTicketBulkLinkResultDto
                {
                    expenseSheetId = expenseSheetId,
                    requestedCount = requestedTicketIds.Count,
                    linkedTicketIds = new List<string>(),
                    skipped = new List<ExpenseSheetTicketBulkLinkIssueDto>(),
                    failed = new List<ExpenseSheetTicketBulkLinkIssueDto>()
                };

                string terminalSheetReason = null;

                foreach (var ticketId in requestedTicketIds)
                {
                    if (!string.IsNullOrWhiteSpace(terminalSheetReason))
                    {
                        result.failed.Add(new ExpenseSheetTicketBulkLinkIssueDto
                        {
                            ticketId = ticketId,
                            reason = terminalSheetReason
                        });
                        continue;
                    }

                    if (!TryGetExpenseSheetTicketDetail(ax, company, axUserId, ticketId, traceId, out var ticketDetail, out var ticketMessage, out var ticketStatus))
                    {
                        result.failed.Add(new ExpenseSheetTicketBulkLinkIssueDto
                        {
                            ticketId = ticketId,
                            reason = NormalizeIssueReason(ticketMessage, ticketStatus == HttpStatusCode.NotFound ? "Ticket not found." : "Ticket could not be loaded.")
                        });
                        continue;
                    }

                    var skipReason = GetBulkLinkSkipReason(ticketDetail);
                    if (!string.IsNullOrWhiteSpace(skipReason))
                    {
                        result.skipped.Add(new ExpenseSheetTicketBulkLinkIssueDto
                        {
                            ticketId = ticketId,
                            reason = skipReason
                        });
                        continue;
                    }

                    if (!TryLinkTicketToExpenseSheet(ax, company, axUserId, expenseSheetId, ticketDetail, traceId, null, out var linkMessage, out var linkStatus))
                    {
                        if (IsTicketAlreadyLinkedMessage(linkMessage))
                        {
                            result.skipped.Add(new ExpenseSheetTicketBulkLinkIssueDto
                            {
                                ticketId = ticketId,
                                reason = NormalizeIssueReason(linkMessage, "Ticket is already linked to an expense sheet.")
                            });
                            continue;
                        }

                        var normalizedFailure = NormalizeIssueReason(linkMessage, "Ticket could not be linked.");
                        result.failed.Add(new ExpenseSheetTicketBulkLinkIssueDto
                        {
                            ticketId = ticketId,
                            reason = normalizedFailure
                        });

                        if (IsTerminalExpenseSheetLinkError(linkMessage, linkStatus))
                            terminalSheetReason = normalizedFailure;

                        continue;
                    }

                    result.linkedTicketIds.Add(ticketId);
                }

                result.linkedCount = result.linkedTicketIds.Count;
                result.skippedCount = result.skipped.Count;
                result.failedCount = result.failed.Count;

                var responseMessage = result.linkedCount > 0
                    ? $"Linked {result.linkedCount} of {result.requestedCount} requested tickets."
                    : "No tickets were linked.";

                LogOut(HttpStatusCode.OK);
                return Ok(new IndApiResponse<ExpenseSheetTicketBulkLinkResultDto>
                {
                    Success = true,
                    Message = responseMessage,
                    ErrorCode = null,
                    Errors = null,
                    Data = result,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] BulkLinkExpenseSheetTickets: {ex}");
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

                if (!string.IsNullOrWhiteSpace(body.transDate) && !TryNormalizeApiDateToAxYmd(body.transDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser DDMMYYYY o DD.MM.YYYY." });

                if (string.IsNullOrWhiteSpace(body.description) &&
                    string.IsNullOrWhiteSpace(body.currencyCode) &&
                    !body.gastoType.HasValue &&
                    !body.totalAmount.HasValue &&
                    !body.status.HasValue &&
                    !body.processedByAI.HasValue &&
                    string.IsNullOrWhiteSpace(body.transDate) &&
                    body.comentario == null &&
                    body.urlFile == null &&
                    body.ocrJson == null &&
                    body.normalizedJson == null &&
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
                var mergedOcrJson = body.ocrJson ?? existing.OcrJson;
                var mergedNormalizedJson = body.normalizedJson ?? existing.NormalizedJson;
                var mergedTransDateRaw = body.transDate ?? existing.TransDate;
                var mergedTransDate = NormalizeAnyDateToAxYmdOrToday(mergedTransDateRaw, out var usedTransDateFallback);
                if (usedTransDateFallback)
                {
                    Logger.Log(
                        $"[WARN] UpdateExpenseSheetTicket transDate fallback-to-today raw={ToLogValue(mergedTransDateRaw)} fileId={ToLogValue(fileId)} traceId={traceId}");
                }
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

                var shouldAppendExtendedDocuRefJson = body.gastoType.HasValue || body.ocrJson != null || body.normalizedJson != null;
                if (shouldAppendExtendedDocuRefJson)
                {
                    updateCon.Append(mergedGastoType);

                    if (body.ocrJson != null || body.normalizedJson != null)
                    {
                        updateCon.Append(mergedOcrJson ?? string.Empty);
                        updateCon.Append(mergedNormalizedJson ?? string.Empty);
                    }
                }

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
                if (!string.IsNullOrWhiteSpace(body.transDate) && !TryNormalizeApiDateToAxYmd(body.transDate, out _))
                    validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser DDMMYYYY o DD.MM.YYYY." });

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
                var mergedTransDate = NormalizeAnyDateToAxYmdOrToday(mergedTransDateRaw, out var usedTransDateFallback);
                if (usedTransDateFallback)
                {
                    Logger.Log(
                        $"[WARN] UpdateExpenseSheetTicketFromIA transDate fallback-to-today raw={ToLogValue(mergedTransDateRaw)} fileId={ToLogValue(fileId)} traceId={traceId}");
                }
                var mergedComentario = (body.comentario ?? existing.Comentario ?? string.Empty).Trim();
                var mergedUrlFile = (body.urlFile ?? existing.UrlFile ?? string.Empty).Trim();
                var mergedFileName = (body.fileName ?? string.Empty).Trim();
                var mergedOcrJson = body.ocrJson ?? existing.OcrJson;
                var mergedNormalizedJson = body.normalizedJson ?? existing.NormalizedJson;

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

                LogTicketIaJsonPayload(
                    "endpoint-update-from-ia",
                    fileId,
                    body,
                    mergedOcrJson,
                    mergedNormalizedJson,
                    traceId);

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
                headerCon.Append(mergedOcrJson ?? string.Empty);
                headerCon.Append(mergedNormalizedJson ?? string.Empty);
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
            var totalSw = DiagnosticsStopwatch.StartNew();
            var validationErrors = new List<IndValidationError>();
            long? multipartReadMs = null;
            long? ticketLookupMs = null;
            long? blobUploadMs = null;
            long? axSyncMs = null;

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

            string PerfValue(long? value)
            {
                return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "na";
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var cleanFileId = fileId.Trim();
                Logger.Log(
                    $"[API-IN] UploadExpenseSheetTicketFile fileId={cleanFileId} user={username} axUserId={axUserId} traceId={traceId}");

                var provider = new MultipartMemoryStreamProvider();
                var multipartSw = DiagnosticsStopwatch.StartNew();
                Request.Content.ReadAsMultipartAsync(provider).GetAwaiter().GetResult();
                multipartReadMs = multipartSw.ElapsedMilliseconds;

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
                var ticketLookupSw = DiagnosticsStopwatch.StartNew();
                if (!TryGetTicketDetailFromAx(ax, company, axUserId, cleanFileId, traceId, out var existingTicket, out var getError, out var getStatus))
                {
                    LogOut(getStatus);
                    return Content(getStatus, getError);
                }
                ticketLookupMs = ticketLookupSw.ElapsedMilliseconds;

                TicketBlobUploadResult uploadResult;
                var blobUploadSw = DiagnosticsStopwatch.StartNew();
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
                blobUploadMs = blobUploadSw.ElapsedMilliseconds;

                var axSyncSw = DiagnosticsStopwatch.StartNew();
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
                axSyncMs = axSyncSw.ElapsedMilliseconds;

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
            finally
            {
                Logger.Log(
                    $"[PERF] UploadExpenseSheetTicketFile totalMs={totalSw.ElapsedMilliseconds} multipartReadMs={PerfValue(multipartReadMs)} ticketLookupMs={PerfValue(ticketLookupMs)} blobUploadMs={PerfValue(blobUploadMs)} axSyncMs={PerfValue(axSyncMs)} traceId={traceId}");
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

        // Carries validated multipart data for the quick-create flow.
        private sealed class QuickCreateFormReadResult
        {
            public bool Success { get; set; }
            public HttpStatusCode StatusCode { get; set; }
            public IndApiResponse<ExpenseSheetTicketQuickCreateResultDto> ErrorResponse { get; set; }
            public string OriginalFileName { get; set; }
            public string ContentType { get; set; }
            public string Extension { get; set; }
            public byte[] ImageBytes { get; set; }
            public string CurrencyCode { get; set; }
            public string Description { get; set; }
            public string Comentario { get; set; }
            public string ExistingHojaGastosId { get; set; }
            public string ProjectId { get; set; }
        }

        // Minimal create result reused by the quick-create orchestration.
        private sealed class TicketCreateCoreResult
        {
            public string FileId { get; set; }
            public string TicketRecId { get; set; }
            public List<long> LineRecIds { get; set; }
            public string FileName { get; set; }
            public bool FileNameFinalized { get; set; }
        }

        // Minimal upload result reused by the quick-create orchestration.
        private sealed class TicketFileSyncResult
        {
            public string UrlFile { get; set; }
            public string FileName { get; set; }
            public string BlobName { get; set; }
            public string ContentType { get; set; }
        }

        // Minimal AX apply-from-IA result reused by the quick-create orchestration.
        private sealed class TicketIaApplyResult
        {
            public string FileId { get; set; }
            public string TicketRecId { get; set; }
            public decimal? TotalAmount { get; set; }
            public bool? ProcessedByAI { get; set; }
            public int? GastoType { get; set; }
            public string FileName { get; set; }
            public List<long> LineRecIds { get; set; }
        }

        // Carries the quick-create OCR date decision for validation, fallback and audit logs.
        private sealed class QuickCreateTransDateResolution
        {
            public string RawTransDate { get; set; }
            public string NormalizedTransDateYmd { get; set; }
            public bool UsedFallback { get; set; }
            public string Reason { get; set; }
        }

        // Carries one quick-create extraction attempt plus validation data for fallback decisions.
        private sealed class QuickCreateDraftExtractionResult
        {
            public ExpenseTicketDraftProfile ProfileUsed { get; set; }
            public ExpenseSheetDraftResponse Draft { get; set; }
            public UpdateExpenseSheetTicketFromIARequest UpdateRequest { get; set; }
            public QuickCreateTransDateResolution TransDateResolution { get; set; }
            public List<IndValidationError> ValidationErrors { get; set; }
            public int SourceLineCount { get; set; }
            public int ValidLineCount { get; set; }
            public bool UsedFullFallback { get; set; }
            public string FallbackReason { get; set; }
        }

        // Reads and validates the multipart contract for the quick-create flow.
        private async Task<QuickCreateFormReadResult> ReadQuickCreateFormAsync(CancellationToken cancellationToken, string traceId)
        {
            if (Request?.Content == null || !Request.Content.IsMimeMultipartContent())
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = HttpStatusCode.UnsupportedMediaType,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "Se requiere multipart/form-data.",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "contentType", Message = "Se requiere multipart/form-data." }
                        })
                };
            }

            var provider = new MultipartMemoryStreamProvider();
            await Request.Content.ReadAsMultipartAsync(provider, cancellationToken).ConfigureAwait(false);

            var filePart = FindFilePart(provider, "ticketImage");
            if (filePart == null)
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = (HttpStatusCode)422,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "ticketImage es obligatorio.",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "ticketImage", Message = "ticketImage es obligatorio." }
                        })
                };
            }

            var originalFileName = GetFileName(filePart);
            if (string.IsNullOrWhiteSpace(originalFileName))
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = (HttpStatusCode)422,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "ticketImage debe incluir nombre de archivo.",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "ticketImage", Message = "ticketImage debe incluir nombre de archivo." }
                        })
                };
            }

            var extension = Path.GetExtension(originalFileName);
            if (!ExpenseTicketImageHelper.IsAllowedExtension(extension))
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = (HttpStatusCode)422,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "Formato de imagen no soportado. Permitidos: .jpg, .jpeg, .png, .webp",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "ticketImage", Message = "Formato de imagen no soportado. Permitidos: .jpg, .jpeg, .png, .webp" }
                        })
                };
            }

            var contentType = filePart.Headers?.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(contentType) && !ExpenseTicketImageHelper.IsAllowedContentType(contentType))
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = (HttpStatusCode)422,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "Content-Type de imagen no soportado.",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "ticketImage", Message = "Content-Type de imagen no soportado." }
                        })
                };
            }

            var contentLength = filePart.Headers?.ContentLength;
            if (contentLength.HasValue && contentLength.Value > ExpenseTicketImageHelper.MaxImageBytes)
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = (HttpStatusCode)422,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "ticketImage supera el limite de 50 MB.",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "ticketImage", Message = "ticketImage supera el limite de 50 MB." }
                        })
                };
            }

            var imageBytes = await filePart.ReadAsByteArrayAsync().ConfigureAwait(false);
            if (imageBytes == null || imageBytes.Length <= 0)
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = (HttpStatusCode)422,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "ticketImage esta vacio.",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "ticketImage", Message = "ticketImage esta vacio." }
                        })
                };
            }

            if (imageBytes.Length > ExpenseTicketImageHelper.MaxImageBytes)
            {
                return new QuickCreateFormReadResult
                {
                    Success = false,
                    StatusCode = (HttpStatusCode)422,
                    ErrorResponse = BuildQuickCreateErrorResponse(
                        traceId,
                        "ticketImage supera el limite de 50 MB.",
                        IndErrorCodes.ValidationError,
                        null,
                        new List<IndValidationError>
                        {
                            new IndValidationError { Field = "ticketImage", Message = "ticketImage supera el limite de 50 MB." }
                        })
                };
            }

            return new QuickCreateFormReadResult
            {
                Success = true,
                StatusCode = HttpStatusCode.OK,
                OriginalFileName = originalFileName,
                ContentType = contentType,
                Extension = NormalizeFileExtension(extension, "jpg"),
                ImageBytes = imageBytes,
                CurrencyCode = await ReadFormFieldAsync(provider, "currencyCode").ConfigureAwait(false),
                Description = await ReadFormFieldAsync(provider, "description").ConfigureAwait(false),
                Comentario = await ReadFormFieldAsync(provider, "comentario").ConfigureAwait(false),
                ExistingHojaGastosId = (await ReadFormFieldAsync(provider, "existingHojaGastosId").ConfigureAwait(false) ?? string.Empty).Trim(),
                ProjectId = (await ReadFormFieldAsync(provider, "projectId").ConfigureAwait(false) ?? string.Empty).Trim()
            };
        }

        // Builds the standard quick-create error envelope, preserving partial data when available.
        private static IndApiResponse<ExpenseSheetTicketQuickCreateResultDto> BuildQuickCreateErrorResponse(
            string traceId,
            string message,
            string errorCode,
            ExpenseSheetTicketQuickCreateResultDto data,
            List<IndValidationError> errors)
        {
            return new IndApiResponse<ExpenseSheetTicketQuickCreateResultDto>
            {
                Success = false,
                Message = string.IsNullOrWhiteSpace(message) ? "Error interno del servidor." : message,
                ErrorCode = errorCode,
                Errors = errors,
                Data = data,
                TraceId = traceId
            };
        }

        // Returns a 429 quick-create response while preserving partial data.
        private IHttpActionResult BuildQuickCreateTooManyRequests(
            string traceId,
            string message,
            string errorCode,
            int? retryAfterSeconds,
            ExpenseSheetTicketQuickCreateResultDto data)
        {
            var payload = BuildQuickCreateErrorResponse(traceId, message, errorCode, data, null);
            var response = Request.CreateResponse((HttpStatusCode)429, payload);
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
                response.Headers.Add("Retry-After", retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture));

            return ResponseMessage(response);
        }

        // Normalizes optional currency input for the quick-create flow.
        private static string NormalizeQuickCreateCurrencyCode(string currencyCode)
        {
            var normalized = (currencyCode ?? string.Empty).Trim().ToUpperInvariant();
            return string.IsNullOrWhiteSpace(normalized) ? "EUR" : normalized;
        }

        private static async Task<string> ReadFormFieldAsync(MultipartMemoryStreamProvider provider, string fieldName)
        {
            if (provider == null || string.IsNullOrWhiteSpace(fieldName))
                return null;

            foreach (var part in provider.Contents)
            {
                var name = part.Headers?.ContentDisposition?.Name?.Trim('"');
                if (!string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = await part.ReadAsStringAsync().ConfigureAwait(false);
                return value?.Trim();
            }

            return null;
        }

        private static HttpContent FindFilePart(MultipartMemoryStreamProvider provider, string expectedName)
        {
            if (provider == null)
                return null;

            var byName = provider.Contents.FirstOrDefault(content =>
            {
                var name = content.Headers?.ContentDisposition?.Name?.Trim('"');
                var fileName = content.Headers?.ContentDisposition?.FileName;
                return !string.IsNullOrWhiteSpace(fileName) &&
                       string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase);
            });

            return byName ?? provider.Contents.FirstOrDefault(content => !string.IsNullOrWhiteSpace(content.Headers?.ContentDisposition?.FileName));
        }

        private static string GetFileName(HttpContent filePart)
        {
            try
            {
                return filePart?.Headers?.ContentDisposition?.FileName?.Trim('"');
            }
            catch
            {
                return null;
            }
        }

        // Creates the provisional ticket using the existing createExpenseSheetTicket AX contract.
        private bool TryCreateQuickCreateProvisionalTicket(
            Axapta2Class ax,
            string company,
            string axUserId,
            CreateExpenseSheetTicketRequest body,
            string stepTraceId,
            out TicketCreateCoreResult result,
            out IndApiResponse<object> error,
            out HttpStatusCode status)
        {
            result = null;
            error = null;
            status = HttpStatusCode.Created;

            try
            {
                var modeValue = ResolveCreateTicketMode(body);
                var extension = NormalizeFileExtension(body?.fileExtension, "jpg");
                var provisionalFileName = BuildProvisionalTicketFileName(axUserId, extension);
                var normalizedTransDate = modeValue == ModeAddLinesToExisting
                    ? string.Empty
                    : NormalizeApiDateToAxYmd(body?.transDate);

                var rootCon = ax.CreateContainer();
                rootCon.Append(company);

                var headerCon = ax.CreateContainer();
                headerCon.Append(axUserId);
                headerCon.Append(body?.description?.Trim() ?? string.Empty);
                headerCon.Append((body?.currencyCode ?? string.Empty).Trim().ToUpperInvariant());
                headerCon.Append(body?.totalAmount ?? 0m);
                headerCon.Append(normalizedTransDate);
                headerCon.Append(body?.comentario?.Trim() ?? string.Empty);
                headerCon.Append(body?.urlFile?.Trim() ?? string.Empty);
                headerCon.Append(provisionalFileName);
                var hasExtendedDocuRefJson = body?.ocrJson != null || body?.normalizedJson != null;
                if (body?.gastoType.HasValue == true || hasExtendedDocuRefJson)
                {
                    headerCon.Append(body?.gastoType ?? 0);

                    if (hasExtendedDocuRefJson)
                    {
                        headerCon.Append(body?.ocrJson ?? string.Empty);
                        headerCon.Append(body?.normalizedJson ?? string.Empty);
                    }
                }
                rootCon.Append(headerCon);

                var linesCon = ax.CreateContainer();
                if (body?.lines != null)
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
                optionsCon.Append(body?.existingFileId?.Trim() ?? string.Empty);
                rootCon.Append(optionsCon);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "createExpenseSheetTicket",
                    rootCon);

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
                {
                    status = HttpStatusCode.InternalServerError;
                    error = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = stepTraceId
                    };
                    return false;
                }

                if (!success)
                {
                    error = BuildTicketActionError(message, stepTraceId, out status);
                    return false;
                }

                var fileId = extras.Count > 0 ? extras[0] : string.Empty;
                var ticketRecId = extras.Count > 1 ? extras[1] : string.Empty;
                var lineRecIds = MapRecIdList(linesOut);

                var finalFileName = provisionalFileName;
                var fileNameFinalized = false;
                if (!string.IsNullOrWhiteSpace(fileId))
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
                        Logger.Log(
                            $"[WARN] QuickCreate provisional filename finalize failed fileId={fileId} stepTraceId={stepTraceId} msg={finalizeMessage}");
                    }
                }

                result = new TicketCreateCoreResult
                {
                    FileId = fileId,
                    TicketRecId = ticketRecId,
                    LineRecIds = lineRecIds,
                    FileName = fileNameFinalized ? finalFileName : provisionalFileName,
                    FileNameFinalized = fileNameFinalized
                };

                return true;
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] TryCreateQuickCreateProvisionalTicket: {ex}");
                status = HttpStatusCode.InternalServerError;
                error = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError,
                    Data = null,
                    TraceId = stepTraceId
                };
                return false;
            }
        }

        // Uploads the ticket image and syncs the final blob URL into AX.
        private bool TryUploadQuickCreateTicketFile(
            Axapta2Class ax,
            string company,
            string axUserId,
            byte[] imageBytes,
            string contentType,
            string extension,
            string fileId,
            string stepTraceId,
            string traceId,
            out TicketFileSyncResult result,
            out string message,
            out string errorCode,
            out HttpStatusCode status)
        {
            result = null;
            message = string.Empty;
            errorCode = null;
            status = HttpStatusCode.OK;

            try
            {
                if (!TryGetTicketDetailFromAx(ax, company, axUserId, fileId, traceId, out var existingTicket, out var getError, out var getStatus))
                {
                    status = getStatus;
                    message = getError?.Message ?? "No se pudo cargar el ticket provisional.";
                    errorCode = getError?.ErrorCode ?? (getStatus == HttpStatusCode.InternalServerError ? IndErrorCodes.AxComError : IndErrorCodes.CrmExpenseSheetTicketNotFound);
                    return false;
                }

                TicketBlobUploadResult uploadResult;
                var finalFileName = BuildTicketFileName(axUserId, fileId, extension);
                using (var stream = new MemoryStream(imageBytes, false))
                {
                    uploadResult = _ticketBlobStorage.UploadTicketFile(
                        company,
                        axUserId,
                        fileId,
                        finalFileName,
                        stream,
                        contentType);
                }

                if (!TryUpdateTicketFromExisting(
                        ax,
                        company,
                        axUserId,
                        fileId,
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
                        Logger.Log($"[WARN] QuickCreate upload rollback failed: {rollbackEx.Message} stepTraceId={stepTraceId} traceId={traceId}");
                    }

                    status = updateStatus;
                    message = updateError?.Message ?? updateMessage ?? "No se pudo sincronizar el archivo del ticket.";
                    errorCode = updateError?.ErrorCode ?? (updateStatus == HttpStatusCode.InternalServerError
                        ? IndErrorCodes.AxComError
                        : IndErrorCodes.CrmExpenseSheetTicketFileUploadFailed);
                    return false;
                }

                result = new TicketFileSyncResult
                {
                    UrlFile = uploadResult.BlobUrl,
                    FileName = finalFileName,
                    BlobName = uploadResult.BlobName,
                    ContentType = contentType
                };
                message = updateMessage ?? string.Empty;
                errorCode = null;
                return true;
            }
            catch (InvalidOperationException ex)
            {
                Logger.Log($"[ERROR] QuickCreate upload storage configuration: {ex.Message} stepTraceId={stepTraceId} traceId={traceId}");
                status = HttpStatusCode.InternalServerError;
                message = "No se pudo acceder a la configuracion de Azure Blob Storage.";
                errorCode = IndErrorCodes.CrmExpenseSheetTicketFileStorageNotConfigured;
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] QuickCreate upload: {ex}");
                status = HttpStatusCode.InternalServerError;
                message = "Error interno al cargar archivo del ticket.";
                errorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.CrmExpenseSheetTicketFileUploadFailed;
                return false;
            }
        }

        // Runs the quick-create profile first and falls back to the full profile only when needed.
        private async Task<QuickCreateDraftExtractionResult> ExtractQuickCreateDraftWithFallbackAsync(
            ITicketAIProcessingService ticketAiProcessing,
            string originalFileName,
            string blobUrl,
            string urlFile,
            string fileName,
            string fileExtension,
            string fallbackDescription,
            string fallbackCurrencyCode,
            string fallbackComentario,
            string traceId,
            CancellationToken cancellationToken)
        {
            Logger.Log(
                $"[QUICKCREATE-DRAFT-START] architecture=azure-docs-json azureDocumentIntelligence=true legacyOpenAiImage=false requestedProfile={ExpenseTicketDraftProfile.QuickCreate} blobUrl={ToLogValue(blobUrl)} fileName={ToLogValue(originalFileName)} traceId={traceId}");

            QuickCreateDraftExtractionResult quickAttempt;
            try
            {
                quickAttempt = await BuildQuickCreateDraftExtractionAsync(
                    ticketAiProcessing,
                    originalFileName,
                    blobUrl,
                    urlFile,
                    fileName,
                    fileExtension,
                    fallbackDescription,
                    fallbackCurrencyCode,
                    fallbackComentario,
                    ExpenseTicketDraftProfile.QuickCreate,
                    cancellationToken).ConfigureAwait(false);
                LogQuickCreateDraftAttempt("quick-profile", traceId, quickAttempt);
            }
            catch (IND_OpenAiRateLimitException)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Logger.Log(
                    $"[QUICKCREATE-DRAFT-FALLBACK] reason=quick-profile-exception msg={ToLogValue(ex.Message)} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Warning);

                var fallbackFromException = await BuildQuickCreateDraftExtractionAsync(
                    ticketAiProcessing,
                    originalFileName,
                    blobUrl,
                    urlFile,
                    fileName,
                    fileExtension,
                    fallbackDescription,
                    fallbackCurrencyCode,
                    fallbackComentario,
                    ExpenseTicketDraftProfile.FullDraft,
                    cancellationToken).ConfigureAwait(false);

                fallbackFromException.UsedFullFallback = true;
                fallbackFromException.FallbackReason = "quick-profile-exception";
                LogQuickCreateDraftAttempt("full-profile-from-exception", traceId, fallbackFromException);
                return fallbackFromException;
            }

            if (!ShouldFallbackToFullDraft(quickAttempt, out var fallbackReason))
                return quickAttempt;

            Logger.Log(
                $"[QUICKCREATE-DRAFT-FALLBACK] reason={ToLogValue(fallbackReason)} sourceLines={quickAttempt.SourceLineCount} validLines={quickAttempt.ValidLineCount} traceId={traceId}",
                AxaptaSessionManager.LogLevel.Warning);

            var fullAttempt = await BuildQuickCreateDraftExtractionAsync(
                ticketAiProcessing,
                originalFileName,
                blobUrl,
                urlFile,
                fileName,
                fileExtension,
                fallbackDescription,
                fallbackCurrencyCode,
                fallbackComentario,
                ExpenseTicketDraftProfile.FullDraft,
                cancellationToken).ConfigureAwait(false);

            fullAttempt.UsedFullFallback = true;
            fullAttempt.FallbackReason = fallbackReason;
            LogQuickCreateDraftAttempt("full-profile-from-validation", traceId, fullAttempt);
            return fullAttempt;
        }

        // Builds one extraction attempt and the normalized update request used by quick-create.
        private async Task<QuickCreateDraftExtractionResult> BuildQuickCreateDraftExtractionAsync(
            ITicketAIProcessingService ticketAiProcessing,
            string originalFileName,
            string blobUrl,
            string urlFile,
            string fileName,
            string fileExtension,
            string fallbackDescription,
            string fallbackCurrencyCode,
            string fallbackComentario,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken)
        {
            if (ticketAiProcessing == null)
                throw new InvalidOperationException("ITicketAIProcessingService no esta disponible.");

            var processingResult = await ticketAiProcessing.ProcessFromStoredBlobAsync(
                blobUrl,
                originalFileName,
                profile,
                cancellationToken).ConfigureAwait(false);
            var draft = processingResult?.Draft;

            var result = new QuickCreateDraftExtractionResult
            {
                ProfileUsed = profile,
                Draft = draft,
                ValidationErrors = new List<IndValidationError>(),
                SourceLineCount = draft?.lines?.Count ?? 0,
                ValidLineCount = 0
            };

            if (draft == null)
                return result;

            result.UpdateRequest = BuildQuickCreateUpdateRequestFromDraft(
                draft,
                urlFile,
                fileName,
                fileExtension,
                fallbackDescription,
                fallbackCurrencyCode,
                fallbackComentario,
                out var transDateResolution);
            if (result.UpdateRequest != null)
            {
                result.UpdateRequest.ocrJson = processingResult?.OcrJson;
                result.UpdateRequest.normalizedJson = processingResult?.NormalizedJson;
            }
            result.TransDateResolution = transDateResolution;
            result.ValidLineCount = result.UpdateRequest?.lines?.Count ?? 0;

            ValidateUpdateTicketFromIABody(result.UpdateRequest, result.ValidationErrors);
            return result;
        }

        // Logs one quick-create extraction attempt so the active AI pipeline is visible in app logs.
        private void LogQuickCreateDraftAttempt(string stage, string traceId, QuickCreateDraftExtractionResult attempt)
        {
            var request = attempt?.UpdateRequest;
            Logger.Log(
                $"[QUICKCREATE-DRAFT-RESULT] stage={ToLogValue(stage)} profile={ToLogValue(attempt?.ProfileUsed.ToString())} usedFullFallback={(attempt?.UsedFullFallback ?? false)} fallbackReason={ToLogValue(attempt?.FallbackReason)} sourceLines={(attempt?.SourceLineCount ?? 0)} validLines={(attempt?.ValidLineCount ?? 0)} validationErrors={(attempt?.ValidationErrors?.Count ?? 0)} ocrJsonChars={ToLogLength(request?.ocrJson)} normalizedJsonChars={ToLogLength(request?.normalizedJson)} traceId={traceId}");
        }

        // Logs the JSON payload state before sending the final IA update to AX.
        private void LogTicketIaJsonPayload(
            string operation,
            string fileId,
            UpdateExpenseSheetTicketFromIARequest body,
            string mergedOcrJson,
            string mergedNormalizedJson,
            string traceId)
        {
            Logger.Log(
                $"[TICKET-IA-JSON] operation={ToLogValue(operation)} fileId={ToLogValue(fileId)} requestOcrJsonChars={ToLogLength(body?.ocrJson)} requestNormalizedJsonChars={ToLogLength(body?.normalizedJson)} mergedOcrJsonChars={ToLogLength(mergedOcrJson)} mergedNormalizedJsonChars={ToLogLength(mergedNormalizedJson)} lines={(body?.lines?.Count ?? 0)} traceId={traceId}");
        }

        // Falls back when the fast profile cannot produce a complete ticket payload for AX.
        private static bool ShouldFallbackToFullDraft(QuickCreateDraftExtractionResult attempt, out string reason)
        {
            reason = string.Empty;

            if (attempt == null)
            {
                reason = "quick-attempt-null";
                return true;
            }

            if (attempt.Draft == null)
            {
                reason = "draft-null";
                return true;
            }

            if (attempt.UpdateRequest == null)
            {
                reason = "update-request-null";
                return true;
            }

            if (attempt.SourceLineCount <= 0)
            {
                reason = "no-lines-detected";
                return true;
            }

            if (attempt.ValidLineCount <= 0)
            {
                reason = "no-valid-lines";
                return true;
            }

            if (attempt.ValidLineCount != attempt.SourceLineCount)
            {
                reason = "dropped-invalid-lines";
                return true;
            }

            if (attempt.ValidationErrors != null && attempt.ValidationErrors.Count > 0)
            {
                reason = "validation-failed";
                return true;
            }

            return false;
        }

        // Maps an IA draft into the existing update-from-IA request contract.
        private static UpdateExpenseSheetTicketFromIARequest BuildQuickCreateUpdateRequestFromDraft(
            ExpenseSheetDraftResponse draft,
            string urlFile,
            string fileName,
            string fileExtension,
            string fallbackDescription,
            string fallbackCurrencyCode,
            string fallbackComentario,
            out QuickCreateTransDateResolution transDateResolution)
        {
            var validLines = MapQuickCreateDraftLines(draft?.lines);
            var linesTotal = CalculateTicketLinesTotal(validLines);
            var currencyCode = NormalizeDraftCurrencyCode(draft?.currencyCode, draft?.RawCurrency);
            if (string.IsNullOrWhiteSpace(currencyCode))
                currencyCode = NormalizeQuickCreateCurrencyCode(fallbackCurrencyCode);

            var comentario = !string.IsNullOrWhiteSpace(fallbackComentario)
                ? fallbackComentario.Trim()
                : (draft?.Merchant ?? string.Empty).Trim();

            transDateResolution = ResolveQuickCreateDraftTransDate(draft);

            return new UpdateExpenseSheetTicketFromIARequest
            {
                description = string.IsNullOrWhiteSpace(draft?.description) ? fallbackDescription : draft.description.Trim(),
                currencyCode = currencyCode,
                gastoType = ResolveQuickCreateDraftGastoType(draft),
                totalAmount = linesTotal > 0m ? (decimal?)linesTotal : null,
                transDate = FormatApiDate(transDateResolution.NormalizedTransDateYmd),
                comentario = comentario,
                urlFile = (urlFile ?? string.Empty).Trim(),
                fileName = (fileName ?? string.Empty).Trim(),
                fileExtension = NormalizeFileExtension(fileExtension, "jpg"),
                lines = validLines
            };
        }

        // Normalizes IA draft lines into ticket line payloads with positive qty/price only.
        private static List<ExpenseSheetTicketLineRequest> MapQuickCreateDraftLines(IEnumerable<CreateExpenseSheetLineRequest> lines)
        {
            var mapped = new List<ExpenseSheetTicketLineRequest>();
            if (lines == null)
                return mapped;

            foreach (var line in lines)
            {
                if (line == null)
                    continue;

                var description = (line.description ?? string.Empty).Trim();
                var qty = line.qty ?? 0m;
                var price = line.price ?? 0m;
                if (string.IsNullOrWhiteSpace(description) || qty <= 0m || price <= 0m)
                    continue;

                mapped.Add(new ExpenseSheetTicketLineRequest
                {
                    description = description,
                    qty = qty,
                    price = price,
                    totalAmount = qty * price
                });
            }

            return mapped;
        }

        // Resolves a safe quick-create date from OCR, using robust parsing and fallback to today.
        private static QuickCreateTransDateResolution ResolveQuickCreateDraftTransDate(ExpenseSheetDraftResponse draft)
        {
            var rawCandidates = new List<string>();
            if (draft?.lines != null)
            {
                foreach (var line in draft.lines)
                {
                    if (line == null || string.IsNullOrWhiteSpace(line.transDate))
                        continue;

                    var rawTransDate = line.transDate.Trim();
                    rawCandidates.Add(rawTransDate);

                    if (TryNormalizeQuickCreateDraftDateToAxYmd(rawTransDate, out var normalized, out var reason))
                    {
                        return new QuickCreateTransDateResolution
                        {
                            RawTransDate = rawTransDate,
                            NormalizedTransDateYmd = normalized,
                            UsedFallback = false,
                            Reason = reason
                        };
                    }
                }
            }

            return new QuickCreateTransDateResolution
            {
                RawTransDate = rawCandidates.FirstOrDefault() ?? string.Empty,
                NormalizedTransDateYmd = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
                UsedFallback = true,
                Reason = rawCandidates.Count == 0
                    ? "fallback-today-no-date-detected"
                    : "fallback-today-invalid-or-unreasonable-date"
            };
        }

        // Tries exact formats first and then OCR-safe compact heuristics for quick-create dates.
        private static bool TryNormalizeQuickCreateDraftDateToAxYmd(string input, out string normalized, out string reason)
        {
            normalized = string.Empty;
            reason = "empty-date";
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();
            var sanitized = SanitizeQuickCreateDraftDateInput(trimmed);
            if (TryParseReasonableQuickCreateDateExact(sanitized, out var exactDate))
            {
                normalized = exactDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                reason = string.Equals(trimmed, sanitized, StringComparison.Ordinal)
                    ? "exact-format"
                    : "ocr-sanitized-exact-format";
                return true;
            }

            var digits = new string(sanitized.Where(char.IsDigit).ToArray());
            if (TryNormalizeQuickCreateDraftDateDigits(digits, out normalized, out reason))
                return true;

            reason = string.Equals(trimmed, sanitized, StringComparison.Ordinal)
                ? "unsupported-or-unreasonable-date"
                : "ocr-sanitized-but-unresolved";
            return false;
        }

        // Normalizes common OCR substitutions before parsing the draft date.
        private static string SanitizeQuickCreateDraftDateInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return string.Empty;

            var chars = input.Trim().ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                switch (char.ToUpperInvariant(chars[i]))
                {
                    case 'O':
                    case 'Q':
                        chars[i] = '0';
                        break;
                    case 'I':
                    case 'L':
                    case '|':
                        chars[i] = '1';
                        break;
                    case 'Z':
                        chars[i] = '2';
                        break;
                    case 'S':
                        chars[i] = '5';
                        break;
                    case 'B':
                        chars[i] = '8';
                        break;
                }
            }

            return new string(chars).Replace(" ", string.Empty);
        }

        // Tries explicit known formats while rejecting implausible business years.
        private static bool TryParseReasonableQuickCreateDateExact(string input, out DateTime date)
        {
            date = default(DateTime);
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var acceptedFormats = new[]
            {
                "ddMMyyyy",
                "dd.MM.yyyy",
                "d.M.yyyy",
                "dd/MM/yyyy",
                "d/M/yyyy",
                "dd-MM-yyyy",
                "d-M-yyyy",
                "yyyyMMdd",
                "yyyy-MM-dd"
            };

            if (!DateTime.TryParseExact(input, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return false;

            if (!IsReasonableQuickCreateTicketDate(parsed))
                return false;

            date = parsed;
            return true;
        }

        // Tries compact digit-only OCR dates, including two-digit year pivoting.
        private static bool TryNormalizeQuickCreateDraftDateDigits(string digits, out string normalized, out string reason)
        {
            normalized = string.Empty;
            reason = "unsupported-digit-shape";
            if (string.IsNullOrWhiteSpace(digits))
                return false;

            if (digits.Length == 8)
            {
                if (TryBuildReasonableQuickCreateDate(digits.Substring(0, 2), digits.Substring(2, 2), digits.Substring(4, 4), out var ddMMyyyyDate))
                {
                    normalized = ddMMyyyyDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    reason = "compact-ddMMyyyy";
                    return true;
                }

                if (TryBuildReasonableQuickCreateDate(digits.Substring(6, 2), digits.Substring(4, 2), digits.Substring(0, 4), out var yyyyMMddDate))
                {
                    normalized = yyyyMMddDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    reason = "compact-yyyyMMdd";
                    return true;
                }

                if (TryBuildReasonableQuickCreateDateWithPivot(digits.Substring(0, 2), digits.Substring(2, 2), digits.Substring(6, 2), out var pivotDate))
                {
                    normalized = pivotDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    reason = "ocr-year-pivot";
                    return true;
                }
            }

            if (digits.Length == 6)
            {
                if (TryBuildReasonableQuickCreateDateWithPivot(digits.Substring(0, 2), digits.Substring(2, 2), digits.Substring(4, 2), out var ddMMyyDate))
                {
                    normalized = ddMMyyDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    reason = "two-digit-year-pivot";
                    return true;
                }

                if (TryBuildReasonableQuickCreateDateWithPivot(digits.Substring(4, 2), digits.Substring(2, 2), digits.Substring(0, 2), out var yyMMddDate))
                {
                    normalized = yyMMddDate.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
                    reason = "compact-yyMMdd-pivot";
                    return true;
                }
            }

            return false;
        }

        // Builds a strict calendar date and rejects values outside the expected ticket-capture range.
        private static bool TryBuildReasonableQuickCreateDate(string dayText, string monthText, string yearText, out DateTime date)
        {
            date = default(DateTime);
            if (!int.TryParse(dayText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var day) ||
                !int.TryParse(monthText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var month) ||
                !int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var year))
                return false;

            if (year < 100)
                return false;

            try
            {
                var parsed = new DateTime(year, month, day);
                if (!IsReasonableQuickCreateTicketDate(parsed))
                    return false;

                date = parsed;
                return true;
            }
            catch
            {
                return false;
            }
        }

        // Applies a rolling pivot for OCR two-digit years close to the current business period.
        private static bool TryBuildReasonableQuickCreateDateWithPivot(string dayText, string monthText, string yearText, out DateTime date)
        {
            date = default(DateTime);
            if (!int.TryParse(yearText, NumberStyles.Integer, CultureInfo.InvariantCulture, out var twoDigitYear))
                return false;

            var resolvedYear = ResolveQuickCreateTwoDigitYear(twoDigitYear);
            return TryBuildReasonableQuickCreateDate(
                dayText,
                monthText,
                resolvedYear.ToString("0000", CultureInfo.InvariantCulture),
                out date);
        }

        // Rejects medieval or implausibly future dates that can appear in OCR mistakes.
        private static bool IsReasonableQuickCreateTicketDate(DateTime date)
        {
            var minDate = new DateTime(2000, 1, 1);
            var maxDate = DateTime.Today.AddYears(1);
            return date >= minDate && date <= maxDate;
        }

        // Resolves a two-digit year around the current year and otherwise falls back to the previous century.
        private static int ResolveQuickCreateTwoDigitYear(int twoDigitYear)
        {
            if (twoDigitYear < 0)
                twoDigitYear = 0;
            else if (twoDigitYear > 99)
                twoDigitYear %= 100;

            var currentYear = DateTime.Today.Year;
            var currentCentury = (currentYear / 100) * 100;
            var currentYearTwoDigits = currentYear % 100;
            return twoDigitYear <= currentYearTwoDigits + 1
                ? currentCentury + twoDigitYear
                : currentCentury - 100 + twoDigitYear;
        }

        // Resolves gastoType from draft header first and then from the dominant draft line type.
        private static int? ResolveQuickCreateDraftGastoType(ExpenseSheetDraftResponse draft)
        {
            if (draft != null && draft.gastoType.HasValue && IsValidGastoType(draft.gastoType.Value))
                return draft.gastoType.Value;

            if (draft?.lines == null || draft.lines.Count == 0)
                return null;

            var firstByType = new Dictionary<int, int>();
            for (int i = 0; i < draft.lines.Count; i++)
            {
                var typeValue = draft.lines[i]?.typeValue;
                if (!typeValue.HasValue || !IsValidGastoType(typeValue.Value))
                    continue;

                if (!firstByType.ContainsKey(typeValue.Value))
                    firstByType[typeValue.Value] = i;
            }

            var dominant = draft.lines
                .Where(line => line != null && line.typeValue.HasValue && IsValidGastoType(line.typeValue.Value))
                .GroupBy(line => line.typeValue.Value)
                .Select(group => new
                {
                    TypeValue = group.Key,
                    Count = group.Count(),
                    FirstIndex = firstByType.ContainsKey(group.Key) ? firstByType[group.Key] : int.MaxValue
                })
                .OrderByDescending(group => group.Count)
                .ThenBy(group => group.FirstIndex)
                .FirstOrDefault();

            return dominant?.TypeValue;
        }

        // Validates shared request rules before calling AX updateExpenseSheetTicketFromIA.
        private static void ValidateUpdateTicketFromIABody(UpdateExpenseSheetTicketFromIARequest body, List<IndValidationError> validationErrors)
        {
            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
                return;
            }

            if (!string.IsNullOrWhiteSpace(body.transDate) && !TryNormalizeApiDateToAxYmd(body.transDate, out _))
                validationErrors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser DDMMYYYY o DD.MM.YYYY." });

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

        // Executes the existing AX atomic replace flow used by UpdateExpenseSheetTicketFromIA.
        private bool TryApplyTicketFromIACore(
            Axapta2Class ax,
            string company,
            string axUserId,
            string fileId,
            UpdateExpenseSheetTicketFromIARequest body,
            string traceId,
            out TicketIaApplyResult result,
            out IndApiResponse<object> error,
            out HttpStatusCode status)
        {
            result = null;
            error = null;
            status = HttpStatusCode.OK;

            if (!TryGetTicketDetailFromAx(ax, company, axUserId, fileId.Trim(), traceId, out var existing, out error, out status))
                return false;

            var mergedDescription = (body.description ?? existing.Description ?? string.Empty).Trim();
            var mergedCurrencyCode = (body.currencyCode ?? existing.CurrencyCode ?? string.Empty).Trim().ToUpperInvariant();
            var mergedGastoType = body.gastoType ?? existing.GastoType ?? 0;
            var linesTotalAmount = CalculateTicketLinesTotal(body.lines);
            var mergedTotalAmount = body.totalAmount.HasValue && body.totalAmount.Value > 0m
                ? body.totalAmount.Value
                : (linesTotalAmount > 0m ? linesTotalAmount : (existing.TotalAmount ?? 0m));
            var mergedTransDateRaw = string.IsNullOrWhiteSpace(body.transDate) ? existing.TransDate : body.transDate;
            var mergedTransDate = TryNormalizeAnyDateToAxYmd(mergedTransDateRaw, out var normalizedTransDate)
                ? normalizedTransDate
                : DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            var mergedComentario = (body.comentario ?? existing.Comentario ?? string.Empty).Trim();
            var mergedUrlFile = (body.urlFile ?? existing.UrlFile ?? string.Empty).Trim();
            var mergedFileName = (body.fileName ?? string.Empty).Trim();
            var mergedOcrJson = body.ocrJson ?? existing.OcrJson;
            var mergedNormalizedJson = body.normalizedJson ?? existing.NormalizedJson;

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

            var validationErrors = new List<IndValidationError>();
            if (string.IsNullOrWhiteSpace(mergedDescription))
                validationErrors.Add(new IndValidationError { Field = "description", Message = "description es obligatorio para aplicar IA." });
            if (string.IsNullOrWhiteSpace(mergedCurrencyCode))
                validationErrors.Add(new IndValidationError { Field = "currencyCode", Message = "currencyCode es obligatorio para aplicar IA." });
            if (string.IsNullOrWhiteSpace(mergedUrlFile))
                validationErrors.Add(new IndValidationError { Field = "urlFile", Message = "urlFile es obligatorio para aplicar IA." });
            if (string.IsNullOrWhiteSpace(mergedFileName))
                validationErrors.Add(new IndValidationError { Field = "fileName", Message = "fileName o fileExtension es obligatorio para aplicar IA." });

            LogTicketIaJsonPayload(
                "quick-create-apply",
                fileId,
                body,
                mergedOcrJson,
                mergedNormalizedJson,
                traceId);

            if (validationErrors.Any())
            {
                status = (HttpStatusCode)422;
                error = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return false;
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
            headerCon.Append(mergedOcrJson ?? string.Empty);
            headerCon.Append(mergedNormalizedJson ?? string.Empty);
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
                rootCon);

            if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
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
                error = BuildTicketActionError(message, traceId, out status);
                return false;
            }

            result = new TicketIaApplyResult
            {
                FileId = extras.Count > 0 ? extras[0] : fileId.Trim(),
                TicketRecId = extras.Count > 1 ? extras[1] : string.Empty,
                TotalAmount = extras.Count > 2 ? ToDecimal(extras[2]) : mergedTotalAmount,
                ProcessedByAI = extras.Count > 3 ? (ToNullableBool(extras[3]) ?? true) : true,
                GastoType = mergedGastoType,
                FileName = mergedFileName,
                LineRecIds = MapRecIdList(linesOut)
            };

            return true;
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

                if (!TryNormalizeApiDateToAxYmd(body.transDate, out _))
                    errors.Add(new IndValidationError { Field = "transDate", Message = "transDate debe ser DDMMYYYY o DD.MM.YYYY cuando mode es 0 o 1." });

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
                !string.IsNullOrWhiteSpace(request.ocrJson) ||
                !string.IsNullOrWhiteSpace(request.normalizedJson) ||
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
                ocrJson = GetJsonStringIgnoreCase(dataObject, "ocrJson"),
                normalizedJson = GetJsonStringIgnoreCase(dataObject, "normalizedJson"),
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
                        if (TryNormalizeAnyDateToAxYmd(lineTransDate, out var normalizedDate))
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

        // Parses known date shapes to AX format for compatibility paths.
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

        // Uses today when an internal compatibility date cannot be normalized safely for AX.
        private static string NormalizeAnyDateToAxYmdOrToday(string input, out bool usedFallback)
        {
            if (TryNormalizeAnyDateToAxYmd(input, out var normalized))
            {
                usedFallback = false;
                return normalized;
            }

            usedFallback = true;
            return DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
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

        // Formats known incoming AX/API date values to DD.MM.YYYY for response payloads.
        private static string FormatApiDate(string input)
        {
            var normalizedYmd = NormalizeAnyDateToAxYmdOrToday(input, out _);

            if (!DateTime.TryParseExact(normalizedYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
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

        // Standard enum normalization: invalid values are treated as null.
        private static int? NormalizeTicketStatusOrNull(int? status)
        {
            if (!status.HasValue || !IsValidTicketStatus(status.Value))
                return null;

            return status.Value;
        }

        // Standard enum normalization: invalid values are treated as null.
        private static int? NormalizeGastoTypeOrNull(int? gastoType)
        {
            if (!gastoType.HasValue || !IsValidGastoType(gastoType.Value))
                return null;

            return gastoType.Value;
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
                var transDate = NormalizeApiDateToAxYmd(source?.transDate);

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
                var hasExtendedDocuRefJson = source?.ocrJson != null || source?.normalizedJson != null;
                if (source?.gastoType.HasValue == true || hasExtendedDocuRefJson)
                {
                    con.Append(source?.gastoType ?? 0);

                    if (hasExtendedDocuRefJson)
                    {
                        con.Append(source?.ocrJson ?? string.Empty);
                        con.Append(source?.normalizedJson ?? string.Empty);
                    }
                }

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

            var normalizedTransDate = NormalizeAnyDateToAxYmdOrToday(existing.TransDate, out var usedTransDateFallback);
            if (usedTransDateFallback)
            {
                Logger.Log(
                    $"[WARN] TryUpdateTicketFromExisting transDate fallback-to-today raw={ToLogValue(existing.TransDate)} fileId={ToLogValue(fileId)} traceId={traceId}");
            }

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
            var shouldAppendExtendedDocuRefJson = existing.GastoType.HasValue || existing.OcrJson != null || existing.NormalizedJson != null;
            if (shouldAppendExtendedDocuRefJson)
            {
                con.Append(existing.GastoType ?? 0);

                if (existing.OcrJson != null || existing.NormalizedJson != null)
                {
                    con.Append(existing.OcrJson ?? string.Empty);
                    con.Append(existing.NormalizedJson ?? string.Empty);
                }
            }

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

        // Formats nullable strings for logs.
        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "null" : value.Trim();
        }

        // Formats string length for logs so null and empty JSON payloads are distinguishable.
        private static string ToLogLength(string value)
        {
            return value == null
                ? "null"
                : value.Length.ToString(CultureInfo.InvariantCulture);
        }

        // Converts bool to AX int (1/0).
        private static int ToAxBool(bool? value)
        {
            return value.HasValue && value.Value ? 1 : 0;
        }

        // Minimal target sheet data required by bulk-link validation.
        private sealed class ExpenseSheetTargetInfo
        {
            public string Voucher { get; set; }
        }

        // Validates shared paging and date filters for ticket list endpoints.
        private static void ValidateTicketListPagingAndDates(
            int page,
            int pageSize,
            string createdDateFrom,
            string createdDateTo,
            List<IndValidationError> validationErrors,
            out string createdDateFromYmd,
            out string createdDateToYmd)
        {
            createdDateFromYmd = string.Empty;
            createdDateToYmd = string.Empty;

            if (page <= 0)
                validationErrors.Add(new IndValidationError { Field = "page", Message = "page debe ser mayor que cero." });
            if (pageSize <= 0)
                validationErrors.Add(new IndValidationError { Field = "pageSize", Message = "pageSize debe ser mayor que cero." });
            if (pageSize > MaxPageSize)
                validationErrors.Add(new IndValidationError { Field = "pageSize", Message = $"pageSize no puede ser mayor que {MaxPageSize}." });

            ValidateTicketDateRange(createdDateFrom, createdDateTo, validationErrors, out createdDateFromYmd, out createdDateToYmd);
        }

        // Validates shared createdDate range rules used by ticket filters.
        private static void ValidateTicketDateRange(
            string createdDateFrom,
            string createdDateTo,
            List<IndValidationError> validationErrors,
            out string createdDateFromYmd,
            out string createdDateToYmd)
        {
            createdDateFromYmd = string.Empty;
            createdDateToYmd = string.Empty;

            if (!string.IsNullOrWhiteSpace(createdDateFrom) && !TryNormalizeApiDateToAxYmd(createdDateFrom, out createdDateFromYmd))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "createdDateFrom",
                    Message = "createdDateFrom debe ser DDMMYYYY o DD.MM.YYYY."
                });
            }

            if (!string.IsNullOrWhiteSpace(createdDateTo) && !TryNormalizeApiDateToAxYmd(createdDateTo, out createdDateToYmd))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "createdDateTo",
                    Message = "createdDateTo debe ser DDMMYYYY o DD.MM.YYYY."
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

        // Validates bulk-link request semantics while keeping backward compatibility.
        private static void ValidateBulkLinkRequest(
            BulkLinkExpenseSheetTicketsRequest body,
            List<IndValidationError> validationErrors,
            out string selectionMode,
            out string createdDateFromYmd,
            out string createdDateToYmd)
        {
            selectionMode = NormalizeBulkSelectionMode(body?.selectionMode);
            createdDateFromYmd = string.Empty;
            createdDateToYmd = string.Empty;

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
                return;
            }

            if (string.IsNullOrWhiteSpace(body.expenseSheetId))
                validationErrors.Add(new IndValidationError { Field = "expenseSheetId", Message = "expenseSheetId es obligatorio." });

            if (!IsValidBulkSelectionMode(selectionMode))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "selectionMode",
                    Message = "selectionMode invalido. Valores permitidos: selected, filtered."
                });
                return;
            }

            if (string.Equals(selectionMode, BulkSelectionModeSelected, StringComparison.Ordinal))
            {
                if (NormalizeDistinctTicketIds(body.ticketIds).Count == 0)
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "ticketIds",
                        Message = "ticketIds debe incluir al menos un ticket valido."
                    });
                }

                if (body.filters != null)
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "filters",
                        Message = "filters no aplica cuando selectionMode es selected."
                    });
                }

                if (body.excludedIds != null && body.excludedIds.Count > 0)
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "excludedIds",
                        Message = "excludedIds no aplica cuando selectionMode es selected."
                    });
                }

                return;
            }

            if (body.filters == null)
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "filters",
                    Message = "filters es obligatorio cuando selectionMode es filtered."
                });
            }
            else
            {
                ValidateTicketDateRange(
                    body.filters.createdDateFrom,
                    body.filters.createdDateTo,
                    validationErrors,
                    out createdDateFromYmd,
                    out createdDateToYmd);
            }

            if (body.ticketIds != null && body.ticketIds.Count > 0)
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "ticketIds",
                    Message = "ticketIds no aplica cuando selectionMode es filtered."
                });
            }
        }

        // Returns the normalized bulk selection mode, defaulting to the legacy selected behavior.
        private static string NormalizeBulkSelectionMode(string selectionMode)
        {
            return string.IsNullOrWhiteSpace(selectionMode)
                ? BulkSelectionModeSelected
                : selectionMode.Trim();
        }

        // Validates the allowed bulk selection modes.
        private static bool IsValidBulkSelectionMode(string selectionMode)
        {
            return string.Equals(selectionMode, BulkSelectionModeSelected, StringComparison.Ordinal) ||
                   string.Equals(selectionMode, BulkSelectionModeFiltered, StringComparison.Ordinal);
        }

        // Normalizes ticket ids by trimming, removing empties and deduplicating case-insensitively.
        private static List<string> NormalizeDistinctTicketIds(IEnumerable<string> ticketIds)
        {
            var items = new List<string>();
            if (ticketIds == null)
                return items;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rawTicketId in ticketIds)
            {
                var ticketId = (rawTicketId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(ticketId))
                    continue;

                if (seen.Add(ticketId))
                    items.Add(ticketId);
            }

            return items;
        }

        // Resolves the final candidate ids for bulk-link, either from explicit selection or from server-side filters.
        private static bool TryResolveBulkLinkTicketIds(
            Axapta2Class ax,
            string company,
            string axUserId,
            BulkLinkExpenseSheetTicketsRequest body,
            string selectionMode,
            string createdDateFromYmd,
            string createdDateToYmd,
            out List<string> requestedTicketIds,
            out string message,
            out HttpStatusCode status)
        {
            requestedTicketIds = new List<string>();
            message = string.Empty;
            status = HttpStatusCode.OK;

            if (string.Equals(selectionMode, BulkSelectionModeSelected, StringComparison.Ordinal))
            {
                requestedTicketIds = NormalizeDistinctTicketIds(body?.ticketIds);
                return true;
            }

            var filters = body?.filters ?? new BulkLinkExpenseSheetTicketFiltersRequest();
            var excludedIds = new HashSet<string>(NormalizeDistinctTicketIds(body?.excludedIds), StringComparer.OrdinalIgnoreCase);
            var con = BuildExpenseSheetTicketLinkListRequestContainer(
                ax,
                company,
                axUserId,
                (filters.searchKey ?? filters.filter ?? string.Empty).Trim(),
                createdDateFromYmd,
                createdDateToYmd,
                (filters.currencyCode ?? string.Empty).Trim().ToUpperInvariant(),
                NormalizeGastoTypeOrNull(filters.gastoType),
                filters.processedByAI);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "getExpenseSheetTicketsLinkList",
                con
            );

            var items = MapAllExpenseSheetTicketLinkList(resultObj as IAxaptaContainer, out message, out _);
            if (items == null)
            {
                status = HttpStatusCode.InternalServerError;
                message = "Error al procesar la respuesta de AX.";
                return false;
            }

            var seenTicketIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in items)
            {
                var ticketId = (item?.FileId ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(ticketId))
                    continue;

                if (excludedIds.Contains(ticketId))
                    continue;

                if (seenTicketIds.Add(ticketId))
                    requestedTicketIds.Add(ticketId);
            }

            return true;
        }

        // Builds the AX container used by the ticket-link list query.
        private static IAxaptaContainer BuildExpenseSheetTicketLinkListRequestContainer(
            Axapta2Class ax,
            string company,
            string axUserId,
            string searchKeyValue,
            string createdDateFromYmd,
            string createdDateToYmd,
            string currencyCodeValue,
            int? gastoTypeValue,
            bool? processedByAIValue)
        {
            var con = ax.CreateContainer();
            const string NoFilterToken = "null";
            con.Append(company);
            con.Append(axUserId);
            con.Append(searchKeyValue);
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

            return con;
        }

        // Returns a readable issue reason for bulk-link responses.
        private static string NormalizeIssueReason(string message, string fallback)
        {
            return string.IsNullOrWhiteSpace(message) ? fallback : message.Trim();
        }

        // Returns a skip reason when a ticket is not eligible for bulk linking.
        private static string GetBulkLinkSkipReason(ExpenseSheetTicketDetailDto ticketDetail)
        {
            if (ticketDetail == null)
                return "Ticket data is empty.";

            if (!ticketDetail.Status.HasValue || ticketDetail.Status.Value != TicketStatusPending)
                return "Ticket is not pending.";

            if (!ticketDetail.TotalAmount.HasValue || ticketDetail.TotalAmount.Value <= 0m)
                return "Ticket total amount must be greater than zero.";

            if (!ticketDetail.GastoType.HasValue || !IsValidGastoType(ticketDetail.GastoType.Value))
                return "Ticket gastoType is not valid for linking.";

            if (string.IsNullOrWhiteSpace(ticketDetail.CurrencyCode))
                return "Ticket currencyCode is empty.";

            if (!TryNormalizeAnyDateToAxYmd(ticketDetail.TransDate, out _))
                return "Ticket transDate is not valid.";

            return null;
        }

        // Detects duplicate-link business messages returned by AX.
        private static bool IsTicketAlreadyLinkedMessage(string message)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            return lower.Contains("ya esta asignado") || lower.Contains("ticket asignado");
        }

        // Detects target-sheet errors that make remaining link attempts fail as well.
        private static bool IsTerminalExpenseSheetLinkError(string message, HttpStatusCode status)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            if (status == HttpStatusCode.NotFound || status == HttpStatusCode.Forbidden)
                return true;

            return lower.Contains("hoja de gastos") &&
                   (lower.Contains("no existe") ||
                    lower.Contains("no encontrada") ||
                    lower.Contains("no esta abierta") ||
                    lower.Contains("no tiene permisos"));
        }

        // Loads target expense sheet data needed by the bulk-link flow.
        private static bool TryGetExpenseSheetTargetInfo(
            Axapta2Class ax,
            string company,
            string axUserId,
            string expenseSheetId,
            string traceId,
            out ExpenseSheetTargetInfo targetInfo,
            out string message,
            out HttpStatusCode status)
        {
            targetInfo = null;
            message = string.Empty;
            status = HttpStatusCode.OK;

            var con = ax.CreateContainer();
            con.Append(company);
            con.Append(axUserId);
            con.Append(expenseSheetId);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "getExpenseSheet",
                con
            );

            if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var axMessage, out var extras, out _))
            {
                status = HttpStatusCode.InternalServerError;
                message = "Error al procesar la respuesta de AX.";
                return false;
            }

            if (!success)
            {
                message = axMessage;
                BuildExpenseSheetActionError(axMessage, traceId, out status);
                return false;
            }

            targetInfo = MapExpenseSheetTargetInfo(extras);
            if (targetInfo == null)
            {
                status = HttpStatusCode.InternalServerError;
                message = "Error al procesar la respuesta de AX.";
                return false;
            }

            message = axMessage ?? string.Empty;
            return true;
        }

        // Loads a ticket detail from AX for bulk-link validation.
        private static bool TryGetExpenseSheetTicketDetail(
            Axapta2Class ax,
            string company,
            string axUserId,
            string fileId,
            string traceId,
            out ExpenseSheetTicketDetailDto detail,
            out string message,
            out HttpStatusCode status)
        {
            detail = null;
            message = string.Empty;
            status = HttpStatusCode.OK;

            var con = ax.CreateContainer();
            con.Append(company);
            con.Append(axUserId);
            con.Append(fileId);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "getExpenseSheetTicket",
                con
            );

            if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var axMessage, out var extras, out var linesOut))
            {
                status = HttpStatusCode.InternalServerError;
                message = "Error al procesar la respuesta de AX.";
                return false;
            }

            if (!success)
            {
                message = axMessage;
                BuildTicketActionError(axMessage, traceId, out status);
                return false;
            }

            detail = MapExpenseSheetTicketDetail(extras, linesOut);
            if (detail == null)
            {
                status = HttpStatusCode.InternalServerError;
                message = "Error al procesar la respuesta de AX.";
                return false;
            }

            message = axMessage ?? string.Empty;
            return true;
        }

        // Links one ticket into an existing expense sheet by reusing AX createExpenseSheet mode 2.
        private static bool TryLinkTicketToExpenseSheet(
            Axapta2Class ax,
            string company,
            string axUserId,
            string expenseSheetId,
            ExpenseSheetTicketDetailDto ticketDetail,
            string traceId,
            string projectId,
            out string message,
            out HttpStatusCode status)
        {
            message = string.Empty;
            status = HttpStatusCode.OK;

            if (ticketDetail == null)
            {
                status = (HttpStatusCode)422;
                message = "Ticket data is empty.";
                return false;
            }

            var transDateYmd = NormalizeAnyDateToAxYmdOrToday(ticketDetail.TransDate, out _);

            var rootCon = ax.CreateContainer();
            rootCon.Append(company);

            var headerCon = ax.CreateContainer();
            headerCon.Append(axUserId);
            rootCon.Append(headerCon);

            var linesCon = ax.CreateContainer();
            var lineCon = ax.CreateContainer();
            lineCon.Append(transDateYmd);
            lineCon.Append(ticketDetail.GastoType ?? 0);
            lineCon.Append((ticketDetail.Description ?? ticketDetail.FileId ?? string.Empty).Trim());
            lineCon.Append(ToAxBool(false));
            lineCon.Append((ticketDetail.FileId ?? string.Empty).Trim());
            lineCon.Append(1m);
            lineCon.Append(ticketDetail.TotalAmount ?? 0m);
            lineCon.Append((projectId ?? string.Empty).Trim());
            linesCon.Append(lineCon);
            rootCon.Append(linesCon);

            var optionsCon = ax.CreateContainer();
            optionsCon.Append(ModeAddLinesToExisting);
            optionsCon.Append(expenseSheetId);
            rootCon.Append(optionsCon);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "createExpenseSheet",
                rootCon
            );

            if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var axMessage, out _, out _))
            {
                status = HttpStatusCode.InternalServerError;
                message = "Error al procesar la respuesta de AX.";
                return false;
            }

            if (!success)
            {
                message = axMessage;
                BuildExpenseSheetActionError(axMessage, traceId, out status);
                return false;
            }

            message = axMessage ?? string.Empty;
            return true;
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
        private static IndApiResponse<object> BuildTicketActionError(string message, string traceId, out HttpStatusCode status)
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

        // Builds a standard error response for expense sheet actions reused by bulk link.
        private static IndApiResponse<object> BuildExpenseSheetActionError(string message, string traceId, out HttpStatusCode status)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();
            if (lower.Contains("no encontrada") || lower.Contains("no encontrado") || lower.Contains("no existe"))
            {
                status = HttpStatusCode.NotFound;
                return new IndApiResponse<object>
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(message) ? "Hoja de gastos no encontrada." : message,
                    ErrorCode = IndErrorCodes.CrmExpenseSheetNotFound,
                    Data = null,
                    TraceId = traceId
                };
            }

            if (lower.Contains("no tiene permisos"))
            {
                status = HttpStatusCode.Forbidden;
                return new IndApiResponse<object>
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(message) ? "No tiene permisos para operar sobre la hoja de gastos." : message,
                    ErrorCode = IndErrorCodes.AuthForbidden,
                    Data = null,
                    TraceId = traceId
                };
            }

            if (lower.Contains("no esta abierta") || lower.Contains("bloqueada"))
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

        // Maps ticket header extras + lines to typed detail DTO.
        private static ExpenseSheetTicketDetailDto MapExpenseSheetTicketDetail(List<string> headerExtras, IAxaptaContainer linesCon)
        {
            if (headerExtras == null || headerExtras.Count < 6)
                return null;

            var detail = new ExpenseSheetTicketDetailDto
            {
                FileId = headerExtras.Count > 0 ? headerExtras[0] : string.Empty,
                Description = headerExtras.Count > 1 ? headerExtras[1] : string.Empty,
                Status = headerExtras.Count > 2 ? NormalizeTicketStatusOrNull(ToInt(headerExtras[2])) : null,
                GastoType = headerExtras.Count > 11 ? NormalizeGastoTypeOrNull(ToInt(headerExtras[11])) : null,
                CurrencyCode = headerExtras.Count > 3 ? headerExtras[3] : string.Empty,
                TotalAmount = headerExtras.Count > 4 ? ToDecimal(headerExtras[4]) : null,
                CreatedByUserId = headerExtras.Count > 5 ? headerExtras[5] : string.Empty,
                TransDate = headerExtras.Count > 6 ? FormatApiDate(headerExtras[6]) : string.Empty,
                Comentario = headerExtras.Count > 7 ? headerExtras[7] : string.Empty,
                UrlFile = headerExtras.Count > 8 ? headerExtras[8] : string.Empty,
                FileName = headerExtras.Count > 9 ? headerExtras[9] : string.Empty,
                ProcessedByAI = headerExtras.Count > 10 ? ToNullableBool(headerExtras[10]) : null,
                HojaGastosIdDisplay = headerExtras.Count > 12 ? headerExtras[12] : string.Empty,
                OcrJson = headerExtras.Count > 13 ? headerExtras[13] : null,
                NormalizedJson = headerExtras.Count > 14 ? headerExtras[14] : null,
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
                    Status = NormalizeTicketStatusOrNull(ToInt(AxContainerReadHelper.SafeString(row, 3))),
                    ProcessedByAI = ToNullableBool(AxContainerReadHelper.SafeString(row, 4)),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 5),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                    TransDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 7)),
                    FileName = AxContainerReadHelper.SafeString(row, 8),
                    GastoType = NormalizeGastoTypeOrNull(ToInt(AxContainerReadHelper.SafeString(row, 9)))
                });
            }

            return items;
        }

        // Maps AX ticket-link rows to typed DTO list.
        private static List<ExpenseSheetTicketLinkListItemDto> MapExpenseSheetTicketLinkList(IAxaptaContainer root, int page, int pageSize, out string message, out int total)
        {
            return MapExpenseSheetTicketLinkListInternal(root, page, pageSize, true, out message, out total);
        }

        // Maps all AX ticket-link rows without applying pagination.
        private static List<ExpenseSheetTicketLinkListItemDto> MapAllExpenseSheetTicketLinkList(IAxaptaContainer root, out string message, out int total)
        {
            return MapExpenseSheetTicketLinkListInternal(root, 1, int.MaxValue, false, out message, out total);
        }

        // Shared mapper for paged and non-paged ticket-link list responses.
        private static List<ExpenseSheetTicketLinkListItemDto> MapExpenseSheetTicketLinkListInternal(
            IAxaptaContainer root,
            int page,
            int pageSize,
            bool applyPaging,
            out string message,
            out int total)
        {
            message = string.Empty;
            total = 0;
            var items = new List<ExpenseSheetTicketLinkListItemDto>();

            if (root == null || AxContainerReadHelper.SafeLength(root) == 0)
                return items;

            if (AxContainerReadHelper.IsSinDatos(root, out message))
                return items;

            total = AxContainerReadHelper.SafeLength(root);
            if (total <= 0)
                return items;

            var start = 1;
            var end = total;
            if (applyPaging)
            {
                var skipLong = ((long)page - 1L) * pageSize;
                if (skipLong < 0L)
                    skipLong = 0L;

                if (skipLong >= total)
                    return items;

                start = (int)skipLong + 1;
                end = Math.Min(total, start + pageSize - 1);
            }

            for (int i = start; i <= end; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(root, i);
                if (row == null || AxContainerReadHelper.SafeLength(row) < 8)
                    continue;

                items.Add(new ExpenseSheetTicketLinkListItemDto
                {
                    FileId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 3),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 4)),
                    TransDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 5)),
                    FileName = AxContainerReadHelper.SafeString(row, 6),
                    ProcessedByAI = ToNullableBool(AxContainerReadHelper.SafeString(row, 7)),
                    GastoType = NormalizeGastoTypeOrNull(ToInt(AxContainerReadHelper.SafeString(row, 8)))
                });
            }

            return items;
        }

        // Maps the minimum expense sheet data needed by bulk-link validation.
        private static ExpenseSheetTargetInfo MapExpenseSheetTargetInfo(List<string> headerExtras)
        {
            if (headerExtras == null || headerExtras.Count == 0)
                return null;

            var targetInfo = new ExpenseSheetTargetInfo();

            if (headerExtras.Count >= 12)
            {
                targetInfo.Voucher = NormalizeVoucher(headerExtras[10]);
                return targetInfo;
            }

            if (headerExtras.Count == 11)
            {
                if (IsLikelyDateValue(headerExtras[10]))
                {
                    targetInfo.Voucher = NormalizeVoucher(headerExtras[9]);
                }
                else
                {
                    targetInfo.Voucher = NormalizeVoucher(headerExtras[10]);
                }

                return targetInfo;
            }

            if (headerExtras.Count == 10)
            {
                targetInfo.Voucher = NormalizeVoucher(headerExtras[9]);
                return targetInfo;
            }

            if (headerExtras.Count == 8)
            {
                targetInfo.Voucher = NormalizeVoucher(headerExtras[7]);
                return targetInfo;
            }

            if (headerExtras.Count == 7)
            {
                targetInfo.Voucher = NormalizeVoucher(headerExtras[6]);
                return targetInfo;
            }

            return null;
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

            var trimmed = value.Trim();

            if (int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            var lowered = trimmed.ToLowerInvariant();
            switch (lowered)
            {
                case "pending":
                case "pendiente":
                    return 0;
                case "assigned":
                case "asignado":
                    return 1;
                case "none":
                    return 0;
                case "peaje":
                    return 1;
                case "parking":
                    return 2;
                case "km":
                    return 3;
                case "desayuno":
                    return 4;
                case "comida":
                    return 5;
                case "cena":
                    return 6;
                case "hotel":
                    return 7;
                case "varios":
                    return 8;
                case "taxi":
                    return 14;
            }

            if (decimal.TryParse(NormalizeDecimalValue(trimmed), NumberStyles.Any, CultureInfo.InvariantCulture, out var decimalParsed))
            {
                if (decimal.Truncate(decimalParsed) == decimalParsed &&
                    decimalParsed >= int.MinValue &&
                    decimalParsed <= int.MaxValue)
                {
                    return (int)decimalParsed;
                }
            }

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

        // Detects common date strings to distinguish them from numeric values.
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

            var lowered = trimmed.ToLowerInvariant();
            if (lowered == "yes" || lowered == "si" || lowered == "s")
                return true;
            if (lowered == "no")
                return false;

            return null;
        }


    }
}
