using IND_CRM_API.Models.Responses;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Endpoints de voz para uso interno (IND_CRM_APP -> IND_CRM_API).
    /// La API key de OpenAI se mantiene solo en servidor y nunca se expone al cliente.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/ia/service")]
    public class INDSpeechController : ApiController
    {
        private const int MaxAudioBytes = 25 * 1024 * 1024; // 25 MB (limite interno alineado con OpenAI)
        private const int DefaultMaxPromptWords = 500; // limite local por defecto para mantener el prompt acotado
        private const int MaxExpenseTicketImageBytes = 50 * 1024 * 1024; // 50 MB (align OpenAI payload limit)

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

        private static readonly HashSet<string> AllowedTicketImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp"
        };

        private static readonly HashSet<string> AllowedTicketImageContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg",
            "image/pjpeg",
            "image/png",
            "image/webp"
        };

        private readonly IND_IAudioTranscriptionService _transcription;
        private readonly IND_ITextModerationService _moderation;
        private readonly IND_IExpenseTicketDraftService _ticketDraft;
        private readonly IAxLogger _logger;

        public INDSpeechController(
            IND_IAudioTranscriptionService transcription,
            IND_ITextModerationService moderation,
            IND_IExpenseTicketDraftService ticketDraft,
            IAxLogger logger)
        {
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
                    return ReturnError(HttpStatusCode.InternalServerError, traceId, "Error interno del servidor.", IndErrorCodes.InternalError, null);
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
                var moderationModel = ConfigurationManager.AppSettings["OpenAI:ModerationModel"];
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
        /// - languageId (opcional): codigo de idioma, default es
        /// - currencyHint (opcional)
        /// - prompt (opcional): instrucciones adicionales para la IA
        /// </remarks>
        [HttpPost, Route("expensefromticket")]
        [SwaggerResponse(HttpStatusCode.OK, "Borrador de hoja de gastos generado", typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<ExpenseSheetDraftResponse>))]
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

                var languageId = await ReadFormFieldAsync(provider, "languageId");
                if (string.IsNullOrWhiteSpace(languageId))
                    languageId = "es";

                var currencyHint = await ReadFormFieldAsync(provider, "currencyHint");
                var prompt = await ReadFormFieldAsync(provider, "prompt");

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
                if (string.IsNullOrWhiteSpace(extension) || !AllowedTicketImageExtensions.Contains(extension))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "Formato de imagen no soportado. Permitidos: .jpg, .jpeg, .png, .webp", IndErrorCodes.ValidationError, "ticketImage");
                }

                var mediaType = filePart.Headers?.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(mediaType) && !AllowedTicketImageContentTypes.Contains(mediaType))
                {
                    return ReturnError((HttpStatusCode)422, traceId, "Content-Type de imagen no soportado.", IndErrorCodes.ValidationError, "ticketImage");
                }

                var contentLength = filePart.Headers?.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MaxExpenseTicketImageBytes)
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
                if (imageBytes.Length > MaxExpenseTicketImageBytes)
                {
                    return ReturnError((HttpStatusCode)422, traceId, "ticketImage supera el limite de 50 MB.", IndErrorCodes.ValidationError, "ticketImage");
                }

                _logger.Log($"[IA-DRAFT] Image read bytes={imageBytes.Length} ms={fileReadSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                var openAiApiKey = GetOpenAiApiKey();
                if (string.IsNullOrWhiteSpace(openAiApiKey))
                {
                    _logger.Log("[IA-DRAFT] OpenAI API key no esta configurada.", AxaptaSessionManager.LogLevel.Error);
                    return ReturnError(HttpStatusCode.InternalServerError, traceId, "Error interno del servidor.", IndErrorCodes.InternalError, null);
                }

                var draftSw = Stopwatch.StartNew();
                var draft = await _ticketDraft.ExtractFromTicketImageAsync(
                    imageBytes,
                    originalFileName,
                    mediaType,
                    languageId,
                    currencyHint,
                    prompt,
                    cancellationToken);
                draftSw.Stop();
                _logger.Log($"[IA-DRAFT] OpenAI draft generated ms={draftSw.ElapsedMilliseconds} traceId={traceId}", AxaptaSessionManager.LogLevel.Info);

                if (draft == null)
                {
                    return ReturnError(HttpStatusCode.InternalServerError, traceId, "No se pudo generar el borrador desde el ticket.", IndErrorCodes.InternalError, null);
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
                var envPath = Environment.GetEnvironmentVariable(DefaultPromptPathEnvVar);
                var fromEnvFile = TryReadPromptFromFile(envPath);
                if (!string.IsNullOrWhiteSpace(fromEnvFile))
                    return fromEnvFile;

                var envValue = Environment.GetEnvironmentVariable(DefaultPromptEnvVar);
                if (!string.IsNullOrWhiteSpace(envValue))
                    return envValue;

                var cfgPath = ConfigurationManager.AppSettings[DefaultPromptPathAppSettingKey];
                var fromCfgFile = TryReadPromptFromFile(cfgPath);
                if (!string.IsNullOrWhiteSpace(fromCfgFile))
                    return fromCfgFile;

                var cfgValue = ConfigurationManager.AppSettings[DefaultPromptAppSettingKey];
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
                // En produccion, preferir variable de entorno.
                var env = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
                if (!string.IsNullOrWhiteSpace(env))
                    return env.Trim();

                var cfg = ConfigurationManager.AppSettings["OpenAI:ApiKey"];
                return string.IsNullOrWhiteSpace(cfg) ? null : cfg.Trim();
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
                var cfg = ConfigurationManager.AppSettings[PromptMaxWordsAppSettingKey];
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
