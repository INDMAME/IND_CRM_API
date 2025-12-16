using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;
using System;
using System.Collections.Generic;
using System.Configuration;
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
    /// Speech endpoints for internal use (IND_CRM_APP -> IND_CRM_API).
    /// This controller keeps the OpenAI API key on the server and never exposes it to clients.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/speech")]
    public class INDSpeechController : ApiController
    {
        private const int MaxAudioBytes = 10 * 1024 * 1024; // 10 MB internal limit

        private static readonly HashSet<string> AllowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".mp3",
            ".m4a"
            // Future extensions supported by OpenAI: mp4, mpeg, mpga, wav, webm, ogg, flac
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
            "application/octet-stream"
        };

        private readonly IND_IAudioTranscriptionService _transcription;
        private readonly IAxLogger _logger;

        public INDSpeechController(IND_IAudioTranscriptionService transcription, IAxLogger logger)
        {
            _transcription = transcription ?? throw new ArgumentNullException(nameof(transcription));
            _logger = logger ?? new FileAxLogger();
        }

        /// <summary>
        /// Transcribes a short audio note to plain text using OpenAI speech-to-text.
        /// </summary>
        /// <remarks>
        /// Input: multipart/form-data with fields:
        /// - languageId: "auto" or a language code like "es" / "es-ES" / "en"
        /// - audioFile: mp3 or m4a audio file
        /// Output: IndApiResponse&lt;string&gt; with Data = transcribed text only (no OpenAI metadata).
        ///
        /// Recommended client format (voice notes):
        /// - M4A (AAC) or MP3, mono, 32-64 kbps.
        /// - Target size ~150 KB for short notes.
        ///
        /// Security notes:
        /// - OpenAI API key is read from server config/env only and never returned.
        /// - Consider adding per-user/company rate limiting in the future to avoid abuse.
        /// - Ensure usage complies with OpenAI policies (privacy, illegal content, etc.).
        /// </remarks>
        [HttpPost, Route("transcribe")]
        [ResponseType(typeof(IndApiResponse<string>))]
        [SwaggerOperation(Tags = new[] { "Speech" })]
        public async Task<IHttpActionResult> Transcribe(CancellationToken cancellationToken)
        {
            var traceId = Guid.NewGuid().ToString("N");

            try
            {
                if (Request?.Content == null || !Request.Content.IsMimeMultipartContent())
                {
                    return Content(
                        HttpStatusCode.UnsupportedMediaType,
                        BuildError(traceId, "multipart/form-data is required.", IndErrorCodes.ValidationError, "contentType"));
                }

                var provider = new MultipartMemoryStreamProvider();
                await Request.Content.ReadAsMultipartAsync(provider, cancellationToken);

                var languageId = await ReadFormFieldAsync(provider, "languageId");
                if (string.IsNullOrWhiteSpace(languageId))
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "languageId is required.", IndErrorCodes.ValidationError, "languageId"));
                }

                var filePart = FindFilePart(provider, "audioFile");
                if (filePart == null)
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "audioFile is required.", IndErrorCodes.ValidationError, "audioFile"));
                }

                var originalFileName = GetFileName(filePart);
                if (string.IsNullOrWhiteSpace(originalFileName))
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "audioFile must have a file name.", IndErrorCodes.ValidationError, "audioFile"));
                }

                var extension = Path.GetExtension(originalFileName);
                if (string.IsNullOrWhiteSpace(extension) || !AllowedExtensions.Contains(extension))
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "Unsupported audio file extension. Only .mp3 and .m4a are allowed.", IndErrorCodes.ValidationError, "audioFile"));
                }

                var mediaType = filePart.Headers?.ContentType?.MediaType;
                if (!string.IsNullOrWhiteSpace(mediaType) && !AllowedContentTypes.Contains(mediaType))
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "Unsupported audio content type.", IndErrorCodes.ValidationError, "audioFile"));
                }

                var contentLength = filePart.Headers?.ContentLength;
                if (contentLength.HasValue && contentLength.Value > MaxAudioBytes)
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "audioFile exceeds the 10 MB limit.", IndErrorCodes.ValidationError, "audioFile"));
                }

                var audioBytes = await filePart.ReadAsByteArrayAsync();
                if (audioBytes == null || audioBytes.Length <= 0)
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "audioFile is empty.", IndErrorCodes.ValidationError, "audioFile"));
                }

                if (audioBytes.Length > MaxAudioBytes)
                {
                    return Content(
                        (HttpStatusCode)422,
                        BuildError(traceId, "audioFile exceeds the 10 MB limit.", IndErrorCodes.ValidationError, "audioFile"));
                }

                // Read OpenAI API key from server config only (never from client).
                // The user provided an API key in chat; do NOT hardcode it in source control.
                var openAiApiKey = GetOpenAiApiKey();
                if (string.IsNullOrWhiteSpace(openAiApiKey))
                {
                    _logger.Log("[SPEECH] OpenAI API key not configured.", AxaptaSessionManager.LogLevel.Error);
                    return Content(
                        HttpStatusCode.InternalServerError,
                        BuildError(traceId, "OpenAI API key is not configured.", IndErrorCodes.InternalError, null));
                }

                string text;
                using (var audioStream = new MemoryStream(audioBytes, writable: false))
                {
                    text = await _transcription.TranscribeAsync(
                        audioStream,
                        Path.GetFileName(originalFileName),
                        openAiApiKey,
                        languageId.Trim(),
                        cancellationToken);
                }

                var ok = new IndApiResponse<string>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Errors = null,
                    Data = text ?? string.Empty,
                    TraceId = traceId
                };
                return Ok(ok);
            }
            catch (Exception ex)
            {
                // Never log the OpenAI API key. Log only the exception summary.
                _logger.Log("[SPEECH] Transcribe error: " + ex.GetType().FullName + " " + ex.Message, AxaptaSessionManager.LogLevel.Error);

                return Content(
                    HttpStatusCode.InternalServerError,
                    BuildError(traceId, "Audio transcription error.", IndErrorCodes.InternalError, null));
            }
        }

        private static IndApiResponse<string> BuildError(string traceId, string message, string errorCode, string field)
        {
            return new IndApiResponse<string>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Data = null,
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
                var name = part.Headers?.ContentDisposition?.Name?.Trim('\"');
                if (!string.Equals(name, fieldName, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Use ReadAsStringAsync; keep it simple and defensive.
                var value = await part.ReadAsStringAsync().ConfigureAwait(false);
                return value?.Trim();
            }

            return null;
        }

        private static HttpContent FindFilePart(MultipartMemoryStreamProvider provider, string expectedName)
        {
            if (provider == null) return null;

            // Prefer the named file part.
            var byName = provider.Contents.FirstOrDefault(c =>
            {
                var name = c.Headers?.ContentDisposition?.Name?.Trim('\"');
                var fileName = c.Headers?.ContentDisposition?.FileName;
                return !string.IsNullOrWhiteSpace(fileName) &&
                       string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase);
            });
            if (byName != null) return byName;

            // Fallback: first content that looks like a file upload.
            return provider.Contents.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Headers?.ContentDisposition?.FileName));
        }

        private static string GetFileName(HttpContent filePart)
        {
            try
            {
                return filePart?.Headers?.ContentDisposition?.FileName?.Trim('\"');
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
                // Prefer environment variable in production.
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
    }
}
