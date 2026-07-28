using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Provides global text correction without storing content or opening Axapta sessions.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/ia/service/text")]
    public sealed class INDTextFormattingController : ApiController
    {
        private const int DefaultMaxInputChars = 20000;
        private const string MaxInputCharsSettingKey = "OpenAI:TextFormattingMaxInputChars";
        private const string ModerationModelSettingKey = "OpenAI:ModerationModel";
        private static readonly Regex LanguageIdPattern = new Regex(
            @"^(auto|[a-zA-Z]{2,3}(?:-[a-zA-Z]{4})?(?:-(?:[a-zA-Z]{2}|[0-9]{3}))?)$",
            RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly int MaxInputChars = ReadPositiveIntSetting(
            MaxInputCharsSettingKey,
            DefaultMaxInputChars);

        private readonly IND_ITextFormattingService _formattingService;
        private readonly IND_ITextModerationService _moderationService;
        private readonly IAxLogger _logger;

        public INDTextFormattingController(
            IND_ITextFormattingService formattingService,
            IND_ITextModerationService moderationService,
            IAxLogger logger)
        {
            _formattingService = formattingService ?? throw new ArgumentNullException(nameof(formattingService));
            _moderationService = moderationService ?? throw new ArgumentNullException(nameof(moderationService));
            _logger = logger ?? new FileAxLogger();
        }

        /// <summary>
        /// Corrects spelling, grammar and layout while preserving language, meaning and all source data.
        /// </summary>
        /// <remarks>
        /// This operation does not translate, summarize, answer, persist or update the supplied content.
        /// The server owns all editorial instructions. The maximum input length is configurable and defaults
        /// to 20,000 characters. Example: {"text":"hola hemos ablado con el cliente","languageId":"auto"}.
        /// </remarks>
        [HttpPost]
        [Route("format")]
        [ResponseType(typeof(IndApiResponse<FormatTextResponse>))]
        [SwaggerOperation(Tags = new[] { "IA Text" })]
        [SwaggerResponse(HttpStatusCode.OK, "Texto corregido y formateado", typeof(IndApiResponse<FormatTextResponse>))]
        [SwaggerResponse(HttpStatusCode.Unauthorized, "Autenticacion requerida", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Body, texto, idioma o contenido no valido", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)429, "Limite de uso de IA excedido", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Proveedor IA no disponible", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public async Task<IHttpActionResult> Format(
            [FromBody] FormatTextRequest body,
            CancellationToken cancellationToken)
        {
            var traceId = IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
            var stopwatch = Stopwatch.StartNew();
            var username = User?.Identity?.Name ?? "unknown";
            var method = Request?.Method?.Method ?? "POST";
            var path = Request?.RequestUri?.AbsolutePath ?? "/api/ia/service/text/format";
            var inputLength = body?.Text?.Length ?? 0;
            var statusCode = HttpStatusCode.InternalServerError;

            _logger.Log(
                $"[API-IN] {method} {path} user={username} inputChars={inputLength} traceId={traceId}",
                AxaptaSessionManager.LogLevel.Info);

            try
            {
                var validationErrors = ValidateRequest(body);
                AddModelStateErrors(validationErrors);
                if (validationErrors.Count > 0)
                {
                    statusCode = (HttpStatusCode)422;
                    return Content(statusCode, BuildErrorResponse(
                        traceId,
                        "Error de validacion.",
                        IndErrorCodes.ValidationError,
                        validationErrors));
                }

                var languageId = string.IsNullOrWhiteSpace(body.LanguageId) ? "auto" : body.LanguageId.Trim();
                var apiKey = GetOpenAiApiKey();
                if (string.IsNullOrWhiteSpace(apiKey))
                {
                    statusCode = HttpStatusCode.ServiceUnavailable;
                    return Content(statusCode, BuildErrorResponse(
                        traceId,
                        "El servicio de formato de texto IA no esta disponible en este momento.",
                        IndErrorCodes.AiServiceUnavailable));
                }

                var moderation = await _moderationService.ModerateAsync(
                    body.Text,
                    apiKey,
                    ReadStringSetting(ModerationModelSettingKey),
                    cancellationToken).ConfigureAwait(false);
                if (moderation?.IsFlagged == true)
                {
                    statusCode = (HttpStatusCode)422;
                    _logger.Log(
                        $"[TEXT-FORMAT] Moderation rejected user={username} inputChars={inputLength} traceId={traceId}",
                        AxaptaSessionManager.LogLevel.Warning);
                    return Content(statusCode, BuildErrorResponse(
                        traceId,
                        "El texto no puede procesarse debido a la politica de contenido.",
                        IndErrorCodes.ValidationError,
                        new List<IndValidationError>
                        {
                            new IndValidationError
                            {
                                Field = "text",
                                Message = "El contenido no puede procesarse."
                            }
                        }));
                }

                var result = await _formattingService.FormatAsync(body.Text, languageId, cancellationToken)
                    .ConfigureAwait(false);
                statusCode = HttpStatusCode.OK;

                _logger.Log(
                    $"[TEXT-FORMAT] Completed user={username} inputChars={inputLength}"
                    + $" outputChars={result.FormattedText.Length} warnings={result.Warnings.Count}"
                    + $" model={_formattingService.ModelProfile} status=200 traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Info);

                return Ok(new IndApiResponse<FormatTextResponse>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Data = result,
                    Errors = null,
                    TraceId = traceId
                });
            }
            catch (OperationCanceledException)
            {
                statusCode = HttpStatusCode.ServiceUnavailable;
                _logger.Log(
                    $"[TEXT-FORMAT] Cancelled user={username} inputChars={inputLength} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Warning);
                throw;
            }
            catch (Exception ex) when (IND_KnownExceptionMapper.TryMap(
                ex,
                out var mappedStatus,
                out var mappedMessage,
                out var mappedErrorCode,
                out var retryAfterSeconds))
            {
                statusCode = mappedStatus;
                LogSanitizedFailure(ex, username, traceId, inputLength, mappedStatus);
                return BuildMappedError(traceId, mappedStatus, mappedMessage, mappedErrorCode, retryAfterSeconds);
            }
            catch (Exception ex)
            {
                statusCode = HttpStatusCode.InternalServerError;
                LogSanitizedFailure(ex, username, traceId, inputLength, statusCode);
                return Content(statusCode, BuildErrorResponse(
                    traceId,
                    "Error interno al formatear el texto.",
                    IndErrorCodes.InternalError));
            }
            finally
            {
                stopwatch.Stop();
                _logger.Log(
                    $"[API-OUT] {method} {path} user={username} status={(int)statusCode}"
                    + $" durationMs={stopwatch.ElapsedMilliseconds} traceId={traceId}",
                    AxaptaSessionManager.LogLevel.Info);
            }
        }

        private static List<IndValidationError> ValidateRequest(FormatTextRequest body)
        {
            var errors = new List<IndValidationError>();
            if (body == null)
            {
                errors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
                return errors;
            }

            if (string.IsNullOrWhiteSpace(body.Text))
                errors.Add(new IndValidationError { Field = "text", Message = "text es obligatorio." });
            else if (body.Text.Length > MaxInputChars)
                errors.Add(new IndValidationError
                {
                    Field = "text",
                    Message = $"text supera el maximo permitido de {MaxInputChars} caracteres."
                });

            if (!string.IsNullOrWhiteSpace(body.LanguageId)
                && !LanguageIdPattern.IsMatch(body.LanguageId.Trim()))
            {
                errors.Add(new IndValidationError
                {
                    Field = "languageId",
                    Message = "languageId debe ser auto o un identificador de idioma BCP 47 valido."
                });
            }

            return errors;
        }

        private void AddModelStateErrors(List<IndValidationError> errors)
        {
            if (ModelState == null || ModelState.IsValid)
                return;

            foreach (var entry in ModelState)
            {
                foreach (var modelError in entry.Value.Errors)
                {
                    errors.Add(new IndValidationError
                    {
                        Field = string.IsNullOrWhiteSpace(entry.Key) ? "body" : entry.Key,
                        Message = string.IsNullOrWhiteSpace(modelError.ErrorMessage)
                            ? "Valor invalido."
                            : modelError.ErrorMessage
                    });
                }
            }
        }

        private IHttpActionResult BuildMappedError(
            string traceId,
            HttpStatusCode status,
            string message,
            string errorCode,
            int? retryAfterSeconds)
        {
            var response = Request.CreateResponse(status, BuildErrorResponse(traceId, message, errorCode));
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
                response.Headers.Add("Retry-After", retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture));
            return ResponseMessage(response);
        }

        private static IndApiResponse<object> BuildErrorResponse(
            string traceId,
            string message,
            string errorCode,
            List<IndValidationError> errors = null)
        {
            return new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Data = null,
                Errors = errors,
                TraceId = traceId
            };
        }

        private void LogSanitizedFailure(
            Exception ex,
            string username,
            string traceId,
            int inputLength,
            HttpStatusCode status)
        {
            var summary = ex is IND_ExternalServiceException external
                ? external.ProviderSummary
                : ex is IND_OpenAiRateLimitException rateLimit
                    ? rateLimit.ProviderSummary
                    : ex.GetType().Name;
            _logger.Log(
                $"[TEXT-FORMAT] Failed user={username} inputChars={inputLength}"
                + $" model={_formattingService.ModelProfile} status={(int)status}"
                + $" reason={SanitizeLogValue(summary)} traceId={traceId}",
                AxaptaSessionManager.LogLevel.Warning);
        }

        private static string SanitizeLogValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "unavailable";
            var normalized = Regex.Replace(value, @"[\r\n\t]+", " ").Trim();
            return normalized.Length <= 160 ? normalized : normalized.Substring(0, 160);
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

        private static string ReadStringSetting(string key)
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(key);
                return string.IsNullOrWhiteSpace(value) || value.StartsWith("%", StringComparison.Ordinal)
                    ? null
                    : value.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static int ReadPositiveIntSetting(string key, int defaultValue)
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(key);
                if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0)
                    return parsed;
            }
            catch
            {
                // Configuration failures use the safe default.
            }

            return defaultValue;
        }
    }
}
