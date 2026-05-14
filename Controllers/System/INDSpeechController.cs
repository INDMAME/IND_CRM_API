using IND_CRM_API.Models.Responses;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Controllers;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Endpoints de voz para uso interno (IND_CRM_APP -> IND_CRM_API).
    /// La API key de OpenAI se mantiene solo en servidor y nunca se expone al cliente.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/ia/service")]
    public class INDSpeechController : BaseCrmController
    {
        private const int MaxAudioBytes = 25 * 1024 * 1024; // 25 MB (limite interno alineado con OpenAI)
        private const int DefaultMaxPromptWords = 500; // limite local por defecto para mantener el prompt acotado
        private const string DefaultPromptEnvVar = "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT";
        private const string DefaultPromptPathEnvVar = "OPENAI_TRANSCRIPTION_DEFAULT_PROMPT_PATH";
        private const string DefaultPromptAppSettingKey = "OpenAI:TranscriptionDefaultPrompt";
        private const string DefaultPromptPathAppSettingKey = "OpenAI:TranscriptionDefaultPromptFile";
        private const string PromptMaxWordsAppSettingKey = "OpenAI:TranscriptionPromptMaxWords";

        private static readonly int MaxPromptWords = ReadPromptMaxWordsFromConfig();

        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".m4a",
            ".wav",
            ".flac"
            // OpenAI tambien soporta: mp4, mpeg, mpga, webm, ogg
        };

        private static readonly HashSet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "audio/mpeg",
            "audio/mp3",
            "audio/x-mp3",
            "audio/mp4",
            "audio/x-m4a",
            "audio/aac",
            "audio/x-aac",
            "audio/wav",
            "audio/x-wav",
            "audio/wave",
            "audio/vnd.wave",
            "audio/flac",
            "application/octet-stream"
        };

        private static readonly HashSet<int> AllowedTicketGastoTypes = new HashSet<int> { 0, 1, 2, 3, 4, 5, 6, 7, 8, 14 };

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IND_IAudioTranscriptionService _transcription;
        private readonly IND_ITextModerationService _moderation;
        private readonly IND_IExpenseTicketDraftService _ticketDraft;
        private readonly IAxLogger _logger;

        public INDSpeechController(
            IAxaptaSessionManager sessionManager,
            IND_IAudioTranscriptionService transcription,
            IND_ITextModerationService moderation,
            IND_IExpenseTicketDraftService ticketDraft,
            IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
            _moderation = moderation ?? throw new ArgumentNullException(nameof(moderation));
            _ticketDraft = ticketDraft ?? throw new ArgumentNullException(nameof(ticketDraft));
            _logger = logger ?? new FileAxLogger();
        }

        /// <summary>
        /// Transcribe un audio a texto usando OpenAI (speech-to-text).
        /// </summary>
        /// <remarks>
        /// Entrada: multipart/form-data con campos:
        /// - languageId: "auto" o codigo de idioma (ej: "es" / "es-ES" / "en")
        /// - audioFile: archivo .mp3/.m4a/.wav/.flac (max 25 MB)
        /// - temperature (opcional): numero entre 0 y 1 (por defecto 0)
        /// - prompt/context (opcional): contexto para mejorar vocabulario (max configurado en OpenAI:TranscriptionPromptMaxWords; por defecto 500)
        /// Output: IndPagedResponse&lt;string&gt; with Items containing the transcript (no OpenAI metadata).
        ///
        /// Seguridad:
        /// - La API key de OpenAI se lee de configuracion/entorno y nunca se devuelve.
        /// - A futuro, se recomienda rate limiting por usuario/empresa para evitar abuso.
        /// </remarks>
        [HttpPost, Route("speech")]
        [SwaggerResponse(HttpStatusCode.OK, "Transcripcion correcta", typeof(IndPagedResponse<string>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<string>))]
        [SwaggerResponse((HttpStatusCode)429, "Limite de uso excedido", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.Unauthorized, "Autenticacion requerida", typeof(IndApiResponse<string>))]
        [SwaggerResponse(HttpStatusCode.UnsupportedMediaType, "Tipo de contenido no soportado", typeof(IndApiResponse<string>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<string>))]
        [ResponseType(typeof(IndPagedResponse<string>))]
        [SwaggerOperation(Tags = new[] { "Voz" })]
        public async Task<IHttpActionResult> Transcribe(CancellationToken cancellationToken)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var totalSw = Stopwatch.StartNew();

            try
            {
                var username = User?.Identity?.Name ?? "unknown";
                var method = Request?.Method?.Method ?? "POST";
                var path = Request?.RequestUri?.AbsolutePath ?? "/api/ia/service/speech";
                _logger.Log($"[API-IN] {method} {path} user={username} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                if (Request?.Content == null || !Request.Content.IsMimeMultipartContent())
                {
                    return ReturnError(HttpStatusCode.UnsupportedMediaType, traceId, "Se requiere multipart/form-data.", IndErrorCodes.ValidationError, "contentType");
                }

                var provider = new MultipartMemoryStreamProvider();
                var multipartSw = Stopwatch.StartNew();
                await Request.Content.ReadAsMultipartAsync(provider, cancellationToken);
                multipartSw.Stop();
                _logger.Log($"[SPEECH] Multipart leido ms={multipartSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                var fieldsSw = Stopwatch.StartNew();
                var languageId = await ReadFormFieldAsync(provider, "languageId");
                if (string.IsNullOrWhiteSpace(languageId))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "languageId es obligatorio.", IndErrorCodes.ValidationError, "languageId");
                }

                // Controles opcionales
                var temperatureValue = await ReadFormFieldAsync(provider, "temperature");
                double temperature = 0.0;
                if (!string.IsNullOrWhiteSpace(temperatureValue))
                {
                    if (!double.TryParse(temperatureValue, NumberStyles.Float, CultureInfo.InvariantCulture, out temperature) ||
                        temperature < 0 || temperature > 1)
                    {
                        return ReturnError((HttpStatusCode)422, traceId, "temperature debe ser un numero entre 0 y 1.", IndErrorCodes.ValidationError, "temperature");
                    }
                }

                var prompt = await ReadFormFieldAsync(provider, "prompt");
                if (string.IsNullOrWhiteSpace(prompt))
                    prompt = await ReadFormFieldAsync(provider, "context");
                var promptProvided = !string.IsNullOrWhiteSpace(prompt);
                if (!promptProvided)
                    prompt = GetDefaultTranscriptionPrompt();
                fieldsSw.Stop();
                _logger.Log($"[SPEECH] Campos leidos ms={fieldsSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                if (!string.IsNullOrWhiteSpace(prompt))
                {
                    var wordCount = CountWords(prompt);
                    if (wordCount > MaxPromptWords)
                    {
                        if (promptProvided)
                        {
                            return ReturnError((HttpStatusCode)422, traceId, $"prompt es demasiado largo (max {MaxPromptWords} palabras).", IndErrorCodes.ValidationError, "prompt");
                        }

                        // Si el prompt por defecto esta mal configurado, seguimos sin prompt para no romper el endpoint.
                        _logger.Log($"[SPEECH] Prompt por defecto supera {MaxPromptWords} palabras. Se continua sin prompt. traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                        prompt = null;
                    }
                }

                var filePart = FindFilePart(provider, "audioFile");
                if (filePart == null)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "audioFile es obligatorio.", IndErrorCodes.ValidationError, "audioFile");
                }

                var originalFileName = GetFileName(filePart);
                if (string.IsNullOrWhiteSpace(originalFileName))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "audioFile debe incluir nombre de archivo.", IndErrorCodes.ValidationError, "audioFile");
                }

                var extension = Path.GetExtension(originalFileName);
                if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "Extension de audio no soportada. Permitidas: .mp3, .m4a, .wav, .flac.", IndErrorCodes.ValidationError, "audioFile");
                }

                var mediaType = filePart.Headers?.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(mediaType) && !AllowedContentTypes.Contains(mediaType))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "Content-Type de audio no soportado.", IndErrorCodes.ValidationError, "audioFile");
                }

                var contentLength = filePart.Headers?.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MaxAudioBytes)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "audioFile supera el limite de 25 MB.", IndErrorCodes.ValidationError, "audioFile");
                }

                var fileReadSw = Stopwatch.StartNew();
                var audioBytes = await filePart.ReadAsByteArrayAsync();
                fileReadSw.Stop();
                if (audioBytes == null || audioBytes.Length <= 0)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "audioFile esta vacio.", IndErrorCodes.ValidationError, "audioFile");
                }
                _logger.Log($"[SPEECH] Audio leido bytes={audioBytes.Length} ms={fileReadSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                if (audioBytes.Length > MaxAudioBytes)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "audioFile supera el limite de 25 MB.", IndErrorCodes.ValidationError, "audioFile");
                }

                // Leer API key de OpenAI solo desde servidor (nunca desde el cliente).
                var openAiApiKey = GetOpenAiApiKey();
                if (string.IsNullOrWhiteSpace(openAiApiKey))
                {
                    _logger.Log("[SPEECH] OpenAI API key no esta configurada.", AxaptaSessionManager.LogLevel.Error);
                    return ReturnError(HttpStatusCode.ServiceUnavailable, traceId, "El servicio de transcripcion IA no esta disponible en este momento.", IndErrorCodes.AiServiceUnavailable, null);
                }

                string text;
                var transcribeSw = Stopwatch.StartNew();
                using (var audioStream = new MemoryStream(audioBytes, writable: false))
                {
                    text = await _transcription.TranscribeAsync(
                        audioStream,
                        Path.GetFileName(originalFileName),
                        openAiApiKey,
                        languageId.Trim(),
                        temperature,
                        prompt,
                        cancellationToken);
                }
                transcribeSw.Stop();
                _logger.Log($"[SPEECH] OpenAI transcribe ms={transcribeSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                // Moderacion del texto resultante para bloquear contenido ofensivo/ilícito.
                var moderationSw = Stopwatch.StartNew();
                var moderationModel = AppSettingsHelper.GetSetting("OpenAI:ModerationModel");
                var modResult = await _moderation.ModerateAsync(text, openAiApiKey, moderationModel, cancellationToken);
                moderationSw.Stop();
                _logger.Log($"[SPEECH] OpenAI moderation ms={moderationSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);
                if (modResult?.IsFlagged == true)
                {
                    var categorySummary = modResult.CategorySummary ?? string.Empty;
                    _logger.Log($"[OPENAI-MOD] Contenido bloqueado categories={categorySummary} traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                    return ReturnError((HttpStatusCode)422, traceId, "Contenido rechazado por politicas de uso.", IndErrorCodes.ValidationError, "text");
                }

                var ok = new IndPagedResponse<string>
                {
                    Success = true,
                    Message = "OK",
                    Items = new List<string> { text ?? string.Empty },
                    TraceId = traceId
                };

                totalSw.Stop();
                _logger.Log($"[SPEECH] Total ms={totalSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);
                LogApiOut(HttpStatusCode.OK, traceId, AxaptaSessionManager.LogLevel.Info);
                return Ok(ok);
            }
            catch (IND_OpenAiRateLimitException ex)
            {
                totalSw.Stop();
                _logger.Log(
                    "[SPEECH] OpenAI rate limit: " + ex.Message +
                    " retryAfter=" + (ex.RetryAfterSeconds.HasValue ? ex.RetryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture) : "na") +
                    " summary=" + (ex.ProviderSummary ?? string.Empty) +
                    " totalMs=" + totalSw.ElapsedMilliseconds +
                    " traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Warning);

                return ReturnTooManyRequests(
                    traceId,
                    "Se excedio el limite de solicitudes de IA. Intente de nuevo en unos segundos.",
                    IndErrorCodes.AiRateLimitExceeded,
                    ex.RetryAfterSeconds);
            }
            catch (IND_ExternalServiceException ex)
            {
                totalSw.Stop();
                _logger.Log("[SPEECH] External dependency error: " + ex.ServiceName + " " + ex.Message + " summary=" + (ex.ProviderSummary ?? string.Empty) + " totalMs=" + totalSw.ElapsedMilliseconds + " traceId=" + traceId, AxaptaSessionManager.LogLevel.Error);
                LogApiOut(ex.StatusCode, traceId, AxaptaSessionManager.LogLevel.Error);

                return Content(
                    ex.StatusCode,
                    BuildError<string>(traceId, ex.UserMessage, ex.ErrorCode, null));
            }
            catch (Exception ex)
            {
                // Nunca loguear la API key. Solo registrar el resumen del error.
                totalSw.Stop();
                _logger.Log("[SPEECH] Transcribe error: " + ex.GetType().FullName + " " + ex.Message + " totalMs=" + totalSw.ElapsedMilliseconds + " traceId=" + traceId, AxaptaSessionManager.LogLevel.Error);
                LogApiOut(HttpStatusCode.InternalServerError, traceId, AxaptaSessionManager.LogLevel.Error);

                return Content(
                    HttpStatusCode.InternalServerError,
                    BuildError<string>(traceId, "Error de transcripcion de audio.", IndErrorCodes.InternalError, null));
            }
        }

        /// <summary>
        /// Extrae un borrador de hoja de gastos desde una imagen de ticket.
        /// </summary>
        /// <remarks>
        /// Entrada: multipart/form-data con campos:
        /// - ticketImage: archivo .jpg/.jpeg/.png/.webp (max 50 MB)
        /// - persistTicket (opcional): true/false. Si true, persiste ticket en AX.
        /// - ticketUrlFile (opcional): URL del archivo en blob. Si no viene y persistTicket=true, se usa URL temporal.
        /// </remarks>
        [HttpPost, Route("expensefromticket")]
        [SwaggerResponse(HttpStatusCode.OK, "Borrador de hoja de gastos generado", typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
        [SwaggerResponse((HttpStatusCode)429, "Limite de uso excedido", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.Unauthorized, "Autenticacion requerida", typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
        [SwaggerResponse(HttpStatusCode.UnsupportedMediaType, "Tipo de contenido no soportado", typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
        [SwaggerOperation(Tags = new[] { "Voz" })]
        public async Task<IHttpActionResult> ExtractExpenseTicketDraft(CancellationToken cancellationToken)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var totalSw = Stopwatch.StartNew();

            try
            {
                var username = User?.Identity?.Name ?? "unknown";
                var method = Request?.Method?.Method ?? "POST";
                var path = Request?.RequestUri?.AbsolutePath ?? "/api/ia/service/expensefromticket";
                _logger.Log($"[API-IN] {method} {path} user={username} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                if (Request?.Content == null || !Request.Content.IsMimeMultipartContent())
                {
                    return ReturnError(HttpStatusCode.UnsupportedMediaType, traceId, "Se requiere multipart/form-data.", IndErrorCodes.ValidationError, "contentType");
                }

                var provider = new MultipartMemoryStreamProvider();
                await Request.Content.ReadAsMultipartAsync(provider, cancellationToken);

                var persistTicket = ParseBooleanFlag(await ReadFormFieldAsync(provider, "persistTicket"));
                var ticketUrlFile = await ReadFormFieldAsync(provider, "ticketUrlFile");
                if (string.IsNullOrWhiteSpace(ticketUrlFile))
                    ticketUrlFile = await ReadFormFieldAsync(provider, "urlFile");

                var filePart = FindFilePart(provider, "ticketImage");
                if (filePart == null)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "ticketImage es obligatorio.", IndErrorCodes.ValidationError, "ticketImage");
                }

                var originalFileName = GetFileName(filePart);
                if (string.IsNullOrWhiteSpace(originalFileName))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "ticketImage debe incluir nombre de archivo.", IndErrorCodes.ValidationError, "ticketImage");
                }

                var extension = Path.GetExtension(originalFileName);
                if (!ExpenseTicketImageHelper.IsAllowedExtension(extension))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "Formato de imagen no soportado. Permitidos: .jpg, .jpeg, .png, .webp", IndErrorCodes.ValidationError, "ticketImage");
                }

                var mediaType = filePart.Headers?.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(mediaType) && !ExpenseTicketImageHelper.IsAllowedContentType(mediaType))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "Content-Type de imagen no soportado.", IndErrorCodes.ValidationError, "ticketImage");
                }

                var contentLength = filePart.Headers?.ContentLength;
                if (contentLength.HasValue && contentLength.Value > ExpenseTicketImageHelper.MaxImageBytes)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "ticketImage supera el limite de 50 MB.", IndErrorCodes.ValidationError, "ticketImage");
                }

                var fileReadSw = Stopwatch.StartNew();
                var imageBytes = await filePart.ReadAsByteArrayAsync();
                fileReadSw.Stop();
                if (imageBytes == null || imageBytes.Length <= 0)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "ticketImage esta vacio.", IndErrorCodes.ValidationError, "ticketImage");
                }
                if (imageBytes.Length > ExpenseTicketImageHelper.MaxImageBytes)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "ticketImage supera el limite de 50 MB.", IndErrorCodes.ValidationError, "ticketImage");
                }

                _logger.Log($"[IA-DRAFT] Image read bytes={imageBytes.Length} ms={fileReadSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);
                var ticketAiProcessing = _ticketDraft as ITicketAIProcessingService;
                if (ticketAiProcessing == null)
                {
                    _logger.Log("[IA-DRAFT] ITicketAIProcessingService no esta disponible.", AxaptaSessionManager.LogLevel.Error);
                    return ReturnError(HttpStatusCode.InternalServerError, traceId, "Error interno del servidor.", IndErrorCodes.InternalError, null);
                }

                _logger.Log(
                    $"[IA-DRAFT-ARCH] mode=azure-docs-json azureDocumentIntelligence=true legacyOpenAiImage=false persistTicket={persistTicket} hasTicketUrlFile={!string.IsNullOrWhiteSpace(ticketUrlFile)} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Info);

                var draftSw = Stopwatch.StartNew();
                var processingResult = await ticketAiProcessing.ProcessFromImageAsync(
                    imageBytes,
                    originalFileName,
                    mediaType,
                    ExpenseTicketDraftProfile.FullDraft,
                    cancellationToken);
                var draft = processingResult?.Draft;
                draftSw.Stop();
                _logger.Log(
                    $"[IA-DRAFT] AzureOCR+OpenAI draft generated ms={draftSw.ElapsedMilliseconds} ocrJsonChars={(processingResult?.OcrJson == null ? "null" : processingResult.OcrJson.Length.ToString(CultureInfo.InvariantCulture))} normalizedJsonChars={(processingResult?.NormalizedJson == null ? "null" : processingResult.NormalizedJson.Length.ToString(CultureInfo.InvariantCulture))} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Info);

                if (draft == null)
                {
                    return ReturnError(HttpStatusCode.InternalServerError, traceId, "No se pudo generar el borrador desde el ticket.", IndErrorCodes.InternalError, null);
                }

                if (persistTicket)
                {
                    var company = RequireCompanyOrReturn422(out var companyError, traceId);
                    if (companyError != null)
                        return companyError;

                    var axUserId = RequireAxUserIdOrReturn422(out var userError, traceId, IndErrorCodes.CrmExpenseSheetTicketMissingFields);
                    if (userError != null)
                        return userError;

                    if (!TryPersistTicketFromDraft(
                        username,
                        company,
                        axUserId,
                        draft,
                        processingResult?.OcrJson,
                        processingResult?.NormalizedJson,
                        extension,
                        ticketUrlFile,
                        traceId,
                        out var ticketCreation,
                        out var persistMessage,
                        out var persistErrorCode,
                        out var persistStatus))
                    {
                        LogApiOut(persistStatus, traceId, AxaptaSessionManager.LogLevel.Warning);
                        return Content(persistStatus, new IndApiResponse<ExpenseSheetDraftResponse>
                        {
                            Success = false,
                            Message = persistMessage,
                            ErrorCode = persistErrorCode,
                            Data = draft,
                            Errors = null,
                            TraceId = traceId
                        });
                    }

                    draft.TicketCreation = ticketCreation;
                    if (ticketCreation != null &&
                        ticketCreation.Persisted &&
                        !string.IsNullOrWhiteSpace(ticketCreation.FileId) &&
                        draft.lines != null)
                    {
                        foreach (var line in draft.lines)
                        {
                            if (line != null && string.IsNullOrWhiteSpace(line.fileId))
                                line.fileId = ticketCreation.FileId;
                        }
                    }
                }

                totalSw.Stop();
                _logger.Log($"[IA-DRAFT] Total ms={totalSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);
                LogApiOut(HttpStatusCode.OK, traceId, AxaptaSessionManager.LogLevel.Info);
                return Ok(new IndApiResponse<ExpenseSheetDraftResponse>
                {
                    Success = true,
                    Message = "OK",
                    Data = draft,
                    ErrorCode = null,
                    Errors = null,
                    TraceId = traceId
                });
            }
            catch (ArgumentException ex)
            {
                _logger.Log("[IA-DRAFT] Validacion: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                LogApiOut((HttpStatusCode)422, traceId, AxaptaSessionManager.LogLevel.Warning);
                return ReturnError((HttpStatusCode)422, traceId, ex.Message, IndErrorCodes.ValidationError, null);
            }
            catch (IND_OpenAiRateLimitException ex)
            {
                _logger.Log(
                    "[IA-DRAFT] OpenAI rate limit: " + ex.Message +
                    " retryAfter=" + (ex.RetryAfterSeconds.HasValue ? ex.RetryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture) : "na") +
                    " summary=" + (ex.ProviderSummary ?? string.Empty),
                    AxaptaSessionManager.LogLevel.Warning);

                return ReturnTooManyRequests(
                    traceId,
                    "Se excedio el limite de solicitudes de IA. Intente de nuevo en unos segundos.",
                    IndErrorCodes.AiRateLimitExceeded,
                    ex.RetryAfterSeconds);
            }
            catch (IND_ExternalServiceException ex)
            {
                _logger.Log("[IA-DRAFT] External dependency error: " + ex.ServiceName + " " + ex.Message + " summary=" + (ex.ProviderSummary ?? string.Empty), AxaptaSessionManager.LogLevel.Error);
                LogApiOut(ex.StatusCode, traceId, AxaptaSessionManager.LogLevel.Error);
                return ReturnError(ex.StatusCode, traceId, ex.UserMessage, ex.ErrorCode, null);
            }
            catch (TaskCanceledException ex)
            {
                _logger.Log("[IA-DRAFT] Request cancelado: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                LogApiOut(HttpStatusCode.InternalServerError, traceId, AxaptaSessionManager.LogLevel.Error);
                return ReturnError(HttpStatusCode.InternalServerError, traceId, "Timeout o cancelacion en la extraccion del draft.", IndErrorCodes.InternalError, null);
            }
            catch (Exception ex)
            {
                _logger.Log("[IA-DRAFT] Error: " + ex.Message, AxaptaSessionManager.LogLevel.Error);
                LogApiOut(HttpStatusCode.InternalServerError, traceId, AxaptaSessionManager.LogLevel.Error);
                return ReturnError(HttpStatusCode.InternalServerError, traceId, "Error de extraccion de borrador.", IndErrorCodes.InternalError, null);
            }
        }

        // Persiste ticket en AX usando el draft extraido por IA.
        private bool TryPersistTicketFromDraft(
            string username,
            string company,
            string axUserId,
            ExpenseSheetDraftResponse draft,
            string ocrJson,
            string normalizedJson,
            string imageExtension,
            string ticketUrlFile,
            string traceId,
            out ExpenseSheetDraftTicketCreationResult ticketCreation,
            out string errorMessage,
            out string errorCode,
            out HttpStatusCode errorStatus)
        {
            ticketCreation = null;
            errorMessage = null;
            errorCode = null;
            errorStatus = HttpStatusCode.OK;

            if (draft == null)
            {
                errorMessage = "No existe draft para persistir.";
                errorCode = IndErrorCodes.ValidationError;
                errorStatus = (HttpStatusCode)422;
                return false;
            }

            try
            {
                var ax = _sessionManager.GetAxInstanceForUser(username);
                var extension = NormalizeFileExtension(imageExtension, "jpg");
                var effectiveUrl = string.IsNullOrWhiteSpace(ticketUrlFile)
                    ? $"pending://expense-ticket/{traceId}"
                    : ticketUrlFile.Trim();

                if (string.IsNullOrWhiteSpace(ticketUrlFile))
                    EnsureDraftWarning(draft, "ticketUrlFile no fue enviado. Se uso URL temporal pendiente de blob.");

                var validLines = new List<ExpenseSheetTicketLineRequest>();
                if (draft.lines != null)
                {
                    foreach (var line in draft.lines)
                    {
                        if (line == null)
                            continue;

                        var qty = line.qty ?? 0m;
                        var price = line.price ?? 0m;
                        var description = (line.description ?? string.Empty).Trim();
                        var lineRequest = new ExpenseSheetTicketLineRequest
                        {
                            description = description,
                            qty = qty,
                            price = price
                        };
                        var lineTotal = CalculateTicketLineTotal(lineRequest);
                        if (!IsValidTicketLineAmount(lineRequest) || string.IsNullOrWhiteSpace(description))
                            continue;

                        validLines.Add(new ExpenseSheetTicketLineRequest
                        {
                            description = description,
                            qty = qty,
                            price = price,
                            totalAmount = lineTotal
                        });
                    }
                }

                var mode = validLines.Count > 0 ? 0 : 1;
                if (mode == 1)
                    EnsureDraftWarning(draft, "No se detectaron lineas validas para ticket; se persistio solo cabecera.");

                var descriptionValue = string.IsNullOrWhiteSpace(draft.description) ? "Ticket" : draft.description.Trim();
                var currencyCodeValue = CurrencyCodeHelper.ResolveToIso4217(draft.currencyCode, draft.RawCurrency);
                if (string.IsNullOrWhiteSpace(currencyCodeValue))
                {
                    currencyCodeValue = "EUR";
                    EnsureDraftWarning(draft, "No se detecto currencyCode. Se uso EUR por defecto para persistencia.");
                }

                var comentarioValue = string.IsNullOrWhiteSpace(draft.Merchant) ? "Ticket IA" : draft.Merchant.Trim();
                var transDateValue = ResolveDraftTransDate(draft);
                var totalAmountValue = CalculateTicketLinesTotal(validLines);
                if (totalAmountValue < 0m)
                {
                    errorMessage = "El total del ticket no puede ser negativo.";
                    errorCode = IndErrorCodes.ValidationError;
                    errorStatus = (HttpStatusCode)422;
                    return false;
                }

                var gastoTypeValue = ResolveDraftGastoType(draft);
                var provisionalFileName = BuildProvisionalTicketFileName(axUserId, extension);
                _logger.Log(
                    $"[IA-DRAFT] Persist draft to AX mode={mode} gastoType={gastoTypeValue} ocrJsonChars={(ocrJson == null ? "null" : ocrJson.Length.ToString(CultureInfo.InvariantCulture))} normalizedJsonChars={(normalizedJson == null ? "null" : normalizedJson.Length.ToString(CultureInfo.InvariantCulture))} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Info);

                var rootCon = ax.CreateContainer();
                rootCon.Append(company);

                var headerCon = ax.CreateContainer();
                headerCon.Append(axUserId);
                headerCon.Append(descriptionValue);
                headerCon.Append(currencyCodeValue);
                headerCon.Append(totalAmountValue);
                headerCon.Append(transDateValue);
                headerCon.Append(comentarioValue);
                headerCon.Append(effectiveUrl);
                headerCon.Append(provisionalFileName);
                headerCon.Append(gastoTypeValue);
                headerCon.Append(ocrJson ?? string.Empty);
                headerCon.Append(normalizedJson ?? string.Empty);
                rootCon.Append(headerCon);

                var linesCon = ax.CreateContainer();
                if (mode == 0)
                {
                    foreach (var ticketLine in validLines)
                    {
                        var lineCon = ax.CreateContainer();
                        lineCon.Append(ticketLine.description ?? string.Empty);
                        lineCon.Append(ticketLine.qty ?? 0m);
                        lineCon.Append(ticketLine.price ?? 0m);
                        lineCon.Append(ticketLine.totalAmount ?? 0m);
                        linesCon.Append(lineCon);
                    }
                }
                rootCon.Append(linesCon);

                var optionsCon = ax.CreateContainer();
                optionsCon.Append(mode);
                optionsCon.Append(string.Empty);
                // Flag IA processing at creation time (0/1) to keep ProcessedByAI consistent.
                optionsCon.Append(1);
                rootCon.Append(optionsCon);

                var resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "createExpenseSheetTicket",
                    rootCon);

                if (!TryReadHeader(resultObj as IAxaptaContainer, out var success, out var message, out var extras, out var linesOut))
                {
                    errorMessage = "Error al procesar la respuesta de AX al crear ticket.";
                    errorCode = IndErrorCodes.AxComError;
                    errorStatus = HttpStatusCode.InternalServerError;
                    return false;
                }

                if (!success)
                {
                    ResolveTicketPersistError(message, out errorStatus, out errorCode);
                    errorMessage = string.IsNullOrWhiteSpace(message) ? "No se pudo crear el ticket en AX." : message;
                    return false;
                }

                var fileId = extras.Count > 0 ? extras[0] : string.Empty;
                var ticketRecId = extras.Count > 1 ? extras[1] : string.Empty;
                var lineRecIds = MapRecIdList(linesOut);

                var finalFileName = BuildTicketFileName(axUserId, fileId, extension);
                var fileNameFinalized = false;
                var processedByAI = true;
                var finalizeMessage = string.Empty;

                if (!string.IsNullOrWhiteSpace(fileId))
                {
                    var updateCon = ax.CreateContainer();
                    updateCon.Append(company);
                    updateCon.Append(axUserId);
                    updateCon.Append(fileId);
                    updateCon.Append(descriptionValue);
                    updateCon.Append(currencyCodeValue);
                    updateCon.Append(totalAmountValue);
                    updateCon.Append(0);
                    updateCon.Append(transDateValue);
                    updateCon.Append(comentarioValue);
                    updateCon.Append(effectiveUrl);
                    updateCon.Append(finalFileName);
                    // updateExpenseSheetTicket supports optional _data[12] = processedByAI (0/1)
                    updateCon.Append(1);
                    updateCon.Append(gastoTypeValue);
                    updateCon.Append(ocrJson ?? string.Empty);
                    updateCon.Append(normalizedJson ?? string.Empty);

                    var updateObj = ax.CallStaticClassMethod(
                        "INDCRMExpenseSheetService",
                        "updateExpenseSheetTicket",
                        updateCon);

                    if (TryReadHeader(updateObj as IAxaptaContainer, out var updateSuccess, out var updateMessage, out _, out _))
                    {
                        fileNameFinalized = updateSuccess;
                        finalizeMessage = updateMessage ?? string.Empty;
                    }
                }

                if (!fileNameFinalized)
                {
                    EnsureDraftWarning(draft, "No se pudo aplicar el nombre final del archivo en AX; quedo nombre provisional.");
                }

                ticketCreation = new ExpenseSheetDraftTicketCreationResult
                {
                    Persisted = true,
                    ProcessedByAI = processedByAI,
                    GastoType = gastoTypeValue,
                    FileId = fileId,
                    TicketRecId = ticketRecId,
                    LineRecIds = lineRecIds,
                    UrlFile = effectiveUrl,
                    FileName = fileNameFinalized ? finalFileName : provisionalFileName,
                    FileNameFinalized = fileNameFinalized,
                    Message = string.IsNullOrWhiteSpace(finalizeMessage) ? message : finalizeMessage
                };

                return true;
            }
            catch (Exception ex)
            {
                _logger.Log("[IA-DRAFT] Error persistiendo ticket en AX: " + ex.Message, AxaptaSessionManager.LogLevel.Error);
                errorMessage = "Error interno al persistir ticket en AX.";
                errorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.InternalError;
                errorStatus = HttpStatusCode.InternalServerError;
                return false;
            }
        }

        // Parsea bool desde texto de formulario.
        private static bool ParseBooleanFlag(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (bool.TryParse(value.Trim(), out var parsed))
                return parsed;

            if (int.TryParse(value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var asInt))
                return asInt != 0;

            return string.Equals(value.Trim(), "yes", StringComparison.OrdinalIgnoreCase);
        }

        // Agrega warning al draft si no existe.
        private static void EnsureDraftWarning(ExpenseSheetDraftResponse draft, string warning)
        {
            if (draft == null || string.IsNullOrWhiteSpace(warning))
                return;

            if (draft.Warnings == null)
                draft.Warnings = new List<string>();

            draft.Warnings.Add(warning.Trim());
        }

        // Resuelve fecha cabecera del ticket desde cabecera, lineas o fecha actual.
        private static string ResolveDraftTransDate(ExpenseSheetDraftResponse draft)
        {
            if (draft != null && !string.IsNullOrWhiteSpace(draft.transDate))
            {
                if (TryNormalizeYmdDate(draft.transDate, out var normalizedHeader))
                    return normalizedHeader;
            }

            if (draft?.lines != null)
            {
                foreach (var line in draft.lines)
                {
                    if (line == null || string.IsNullOrWhiteSpace(line.transDate))
                        continue;

                    if (TryNormalizeYmdDate(line.transDate, out var normalized))
                        return normalized;
                }
            }

            return DateTime.UtcNow.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        }

        // Resolves ticket header gastoType from draft value or dominant line type.
        private static int ResolveDraftGastoType(ExpenseSheetDraftResponse draft)
        {
            if (draft != null && draft.gastoType.HasValue && AllowedTicketGastoTypes.Contains(draft.gastoType.Value))
                return draft.gastoType.Value;

            if (draft?.lines != null && draft.lines.Count > 0)
            {
                var firstByType = new Dictionary<int, int>();
                for (int i = 0; i < draft.lines.Count; i++)
                {
                    var typeValue = draft.lines[i]?.typeValue;
                    if (!typeValue.HasValue || !AllowedTicketGastoTypes.Contains(typeValue.Value))
                        continue;

                    if (!firstByType.ContainsKey(typeValue.Value))
                        firstByType[typeValue.Value] = i;
                }

                var dominant = draft.lines
                    .Where(l => l != null && l.typeValue.HasValue && AllowedTicketGastoTypes.Contains(l.typeValue.Value))
                    .GroupBy(l => l.typeValue.Value)
                    .Select(g => new
                    {
                        TypeValue = g.Key,
                        Count = g.Count(),
                        FirstIndex = firstByType.ContainsKey(g.Key) ? firstByType[g.Key] : int.MaxValue
                    })
                    .OrderByDescending(g => g.Count)
                    .ThenBy(g => g.FirstIndex)
                    .FirstOrDefault();

                if (dominant != null)
                    return dominant.TypeValue;
            }

            return 8;
        }

        // Calculates the signed line amount while preserving zero-quantity discounts.
        private static decimal CalculateTicketLineTotal(ExpenseSheetTicketLineRequest line)
        {
            if (line == null)
                return 0m;

            if (line.totalAmount.HasValue)
                return line.totalAmount.Value;

            var qty = line.qty ?? 0m;
            var price = line.price ?? 0m;
            if (!line.price.HasValue)
                return 0m;

            if (qty == 0m && price < 0m)
                return price;

            return qty * price;
        }

        // Allows qty 0 only when the signed line total represents a discount.
        private static bool IsValidTicketLineAmount(ExpenseSheetTicketLineRequest line)
        {
            if (line == null || !line.qty.HasValue || !line.price.HasValue)
                return false;

            if (line.qty.Value < 0m || line.price.Value == 0m)
                return false;

            if (line.qty.Value > 0m)
                return true;

            return CalculateTicketLineTotal(line) < 0m;
        }

        // Total de lineas de ticket.
        private static decimal CalculateTicketLinesTotal(List<ExpenseSheetTicketLineRequest> lines)
        {
            if (lines == null || lines.Count == 0)
                return 0m;

            decimal total = 0m;
            foreach (var line in lines)
            {
                if (line == null)
                    continue;

                if (!IsValidTicketLineAmount(line))
                    continue;

                total += CalculateTicketLineTotal(line);
            }

            return total;
        }

        // Construye nombre temporal previo a obtener FileId.
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

        // Construye nombre final yyyymmddhhmmss_axUserId_fileId.ext.
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

        // Normaliza extension para filename.
        private static string NormalizeFileExtension(string extension, string defaultExtension)
        {
            var fallback = string.IsNullOrWhiteSpace(defaultExtension) ? "jpg" : defaultExtension.Trim().TrimStart('.').ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(extension))
                return fallback;

            var normalized = extension.Trim().TrimStart('.').ToLowerInvariant();
            return string.IsNullOrWhiteSpace(normalized) ? fallback : normalized;
        }

        // Normaliza fechas conocidas al formato AX yyyyMMdd para persistencia interna.
        private static bool TryNormalizeYmdDate(string input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();
            var acceptedFormats = new[] { "ddMMyyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy" };
            if (!DateTime.TryParseExact(trimmed, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return false;

            normalized = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return true;
        }

        // Lee header AX [success, message, extras...] y container de lineas opcional.
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

        // Convierte header de AX success string a bool.
        private static bool ToBool(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            if (bool.TryParse(value, out var parsed))
                return parsed;

            return value == "1";
        }

        // Extrae RecIds (lineas) desde container AX.
        private static List<long> MapRecIdList(IAxaptaContainer linesCon)
        {
            var list = new List<long>();
            var len = AxContainerReadHelper.SafeLength(linesCon);
            for (int i = 1; i <= len; i++)
            {
                var value = AxContainerReadHelper.SafeValue(linesCon, i);
                if (value == null)
                    continue;

                if (long.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recId))
                    list.Add(recId);
            }

            return list;
        }

        // Mapea mensaje AX de ticket a HTTP status + errorCode.
        private static void ResolveTicketPersistError(string message, out HttpStatusCode status, out string errorCode)
        {
            var lower = (message ?? string.Empty).ToLowerInvariant();

            if (lower.Contains("no encontrada") || lower.Contains("no encontrado"))
            {
                status = HttpStatusCode.NotFound;
                errorCode = lower.Contains("linea")
                    ? IndErrorCodes.CrmExpenseSheetTicketLineNotFound
                    : IndErrorCodes.CrmExpenseSheetTicketNotFound;
                return;
            }

            if (lower.Contains("asignado"))
            {
                status = (HttpStatusCode)422;
                errorCode = IndErrorCodes.CrmExpenseSheetTicketAssigned;
                return;
            }

            status = (HttpStatusCode)422;
            errorCode = IndErrorCodes.CrmExpenseSheetTicketMissingFields;
        }

        private IHttpActionResult ReturnTooManyRequests(string traceId, string message, string errorCode, int? retryAfterSeconds)
        {
            LogApiOut((HttpStatusCode)429, traceId, AxaptaSessionManager.LogLevel.Warning);

            var payload = new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Data = null,
                Errors = null,
                TraceId = traceId
            };

            var response = Request.CreateResponse((HttpStatusCode)429, payload);
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
            {
                response.Headers.Add("Retry-After", retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture));
            }

            return ResponseMessage(response);
        }

        private IHttpActionResult ReturnError(HttpStatusCode statusCode, string traceId, string message, string errorCode, string field)
        {
            var level = statusCode == HttpStatusCode.InternalServerError
                ? AxaptaSessionManager.LogLevel.Error
                : AxaptaSessionManager.LogLevel.Warning;

            LogApiOut(statusCode, traceId, level);
            return Content(statusCode, BuildError<string>(traceId, message, errorCode, field));
        }

        private void LogApiOut(HttpStatusCode statusCode, string traceId, AxaptaSessionManager.LogLevel level)
        {
            var method = Request?.Method?.Method ?? "POST";
            var path = Request?.RequestUri?.AbsolutePath ?? "/api/ia/service/speech";
            _logger.Log($"[API-OUT] {method} {path} {(int)statusCode} traceId={traceId}", level);
        }

        private static IndApiResponse<T> BuildError<T>(string traceId, string message, string errorCode, string field)
        {
            return new IndApiResponse<T>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Data = default,
                Errors = string.IsNullOrWhiteSpace(field)
                    ? null
                    : new List<IndValidationError> { new IndValidationError { Field = field, Message = message } },
                TraceId = traceId
            };
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

                // Leer como texto de forma simple y defensiva.
                var value = await part.ReadAsStringAsync().ConfigureAwait(false);
                return value?.Trim();
            }

            return null;
        }

        private static HttpContent FindFilePart(MultipartMemoryStreamProvider provider, string expectedName)
        {
            if (provider == null) return null;

            // Priorizar la parte de archivo con el nombre esperado.
            var byName = provider.Contents.FirstOrDefault(c =>
            {
                var name = c.Headers?.ContentDisposition?.Name?.Trim('"');
                var fileName = c.Headers?.ContentDisposition?.FileName;
                return !string.IsNullOrWhiteSpace(fileName) &&
                       string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase);
            });
            if (byName != null) return byName;

            // Alternativa: primera parte que parezca un fichero (multipart file upload).
            return provider.Contents.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Headers?.ContentDisposition?.FileName));
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

        private static int CountWords(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;

            // Conteo simple por espacios para acotar el prompt.
            return text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).Length;
        }

        private static string GetDefaultTranscriptionPrompt()
        {
            // No hardcodear prompts confidenciales en codigo fuente.
            // Preferir leer desde fichero externo o variable de entorno en el servidor.
            try
            {
                var envPath = AppSettingsHelper.GetMachineEnvironmentVariable(DefaultPromptPathEnvVar);
                var fromEnvFile = TryReadPromptFromFile(envPath);
                if (!string.IsNullOrWhiteSpace(fromEnvFile))
                    return fromEnvFile;

                var envValue = AppSettingsHelper.GetMachineEnvironmentVariable(DefaultPromptEnvVar);
                if (!string.IsNullOrWhiteSpace(envValue))
                    return envValue;

                var cfgPath = AppSettingsHelper.GetConfigSetting(DefaultPromptPathAppSettingKey, DefaultPromptPathEnvVar);
                var fromCfgFile = TryReadPromptFromFile(cfgPath);
                if (!string.IsNullOrWhiteSpace(fromCfgFile))
                    return fromCfgFile;

                var cfgValue = AppSettingsHelper.GetConfigSetting(DefaultPromptAppSettingKey, DefaultPromptEnvVar);
                return string.IsNullOrWhiteSpace(cfgValue) ? null : cfgValue;
            }
            catch
            {
                return null;
            }
        }

        private static string TryReadPromptFromFile(string path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                    return null;

                if (!File.Exists(path))
                    return null;

                return File.ReadAllText(path);
            }
            catch
            {
                return null;
            }
        }

        private static string GetOpenAiApiKey()
        {
            try
            {
                return AppSettingsHelper.GetSetting("OpenAI:ApiKey", "OPENAI_API_KEY");
            }
            catch
            {
                return null;
            }
        }

        private static int ReadPromptMaxWordsFromConfig()
        {
            try
            {
                var cfg = AppSettingsHelper.GetSetting(PromptMaxWordsAppSettingKey);
                if (int.TryParse(cfg, out var value) && value > 0)
                    return value;
            }
            catch
            {
                // Ignorar y aplicar valor por defecto.
            }

            return DefaultMaxPromptWords;
        }
    }
}
