using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Implementacion de OpenAI para transcripcion de audio.
    /// Notas:
    /// - Usa Audio Transcriptions API: POST https://api.openai.com/v1/audio/transcriptions
    /// - El modelo se lee desde App.config (OpenAI:AudioModel). Por defecto: "gpt-4o-transcribe".
    /// </summary>
    public sealed class IND_OpenAiAudioTranscriptionService : IND_IAudioTranscriptionService
    {
        private const string DefaultModel = "gpt-4o-transcribe";
        private const int DefaultTimeoutSeconds = 600;
        private const string TranscriptionsUrl = "https://api.openai.com/v1/audio/transcriptions";

        // Reutilizar HttpClient para evitar agotamiento de sockets.
        private static readonly int _timeoutSeconds = ReadTimeoutSecondsFromConfig();
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private readonly IAxLogger _logger;
        private readonly string _model;

        public IND_OpenAiAudioTranscriptionService(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _model = ReadModelFromConfig();
        }

        public async Task<string> TranscribeAsync(
            Stream audioStream,
            string fileName,
            string openAiApiKey,
            string languageId,
            double temperature,
            string prompt,
            CancellationToken cancellationToken)
        {
            if (audioStream == null) throw new ArgumentNullException(nameof(audioStream));
            if (!audioStream.CanRead) throw new ArgumentException("audioStream debe ser legible.", nameof(audioStream));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName es obligatorio.", nameof(fileName));
            if (string.IsNullOrWhiteSpace(openAiApiKey)) throw new ArgumentException("OpenAI API key es obligatoria.", nameof(openAiApiKey));
            if (string.IsNullOrWhiteSpace(languageId)) throw new ArgumentException("languageId es obligatorio.", nameof(languageId));
            if (temperature < 0 || temperature > 1) throw new ArgumentOutOfRangeException(nameof(temperature), "temperature debe estar entre 0 y 1.");

            var totalSw = Stopwatch.StartNew();
            long sendMs = -1;
            long readMs = -1;
            string statusCode = "na";
            var timeoutSeconds = _timeoutSeconds;

            if (audioStream.CanSeek)
                audioStream.Position = 0;

            // El controlador valida extension y Content-Type. Aqui asumimos un flujo valido.
            using (var form = new MultipartFormDataContent())
            using (var request = new HttpRequestMessage(HttpMethod.Post, TranscriptionsUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);

                // Identificar la aplicacion en el User-Agent.
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IND_CRM_API", "1.0"));
                // Avoid 100-continue delays on large uploads.
                request.Headers.ExpectContinue = false;

                _logger.Log($"[OPENAI] Request model={_model} timeoutSeconds={timeoutSeconds}", AxaptaSessionManager.LogLevel.Info);

                var fileContent = new StreamContent(audioStream);
                if (audioStream.CanSeek)
                    fileContent.Headers.ContentLength = audioStream.Length;
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(fileContent, "file", SanitizeFileName(fileName));

                form.Add(new StringContent(_model, Encoding.UTF8), "model");

                // gpt-4o-transcribe devuelve JSON; extraemos solo el campo "text".
                form.Add(new StringContent("json", Encoding.UTF8), "response_format");

                // Si languageId es "auto", no enviar el parametro language.
                if (!string.Equals(languageId, "auto", StringComparison.OrdinalIgnoreCase))
                    form.Add(new StringContent(languageId, Encoding.UTF8), "language");

                // Temperatura: controla aleatoriedad. Por defecto 0 para salida mas determinista.
                form.Add(new StringContent(temperature.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8), "temperature");

                // Prompt opcional para guiar vocabulario.
                if (!string.IsNullOrWhiteSpace(prompt))
                    form.Add(new StringContent(prompt, Encoding.UTF8), "prompt");

                request.Content = form;

                HttpResponseMessage response = null;
                string responseBody = null;
                try
                {
                    // Asegurar TLS 1.2 en entornos antiguos.
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    var sendSw = Stopwatch.StartNew();
                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);
                    sendSw.Stop();
                    sendMs = sendSw.ElapsedMilliseconds;

                    var readSw = Stopwatch.StartNew();
                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    readSw.Stop();
                    readMs = readSw.ElapsedMilliseconds;
                    statusCode = ((int)response.StatusCode).ToString();
                    totalSw.Stop();
                    _logger.Log($"[OPENAI] Timings status={statusCode} sendMs={sendMs} readMs={readMs} totalMs={totalSw.ElapsedMilliseconds}", AxaptaSessionManager.LogLevel.Info);

                    if (!response.IsSuccessStatusCode)
                    {
                        var summary = TryExtractOpenAiErrorSummary(responseBody);
                        var retryAfterSeconds = IND_OpenAiErrorHandling.GetRetryAfterSeconds(response);
                        _logger.Log(
                            $"[OPENAI] Fallo transcripcion. Status={(int)response.StatusCode} retryAfter={(retryAfterSeconds.HasValue ? retryAfterSeconds.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "na")} {summary}".Trim(),
                            AxaptaSessionManager.LogLevel.Error);

                        if (IND_OpenAiErrorHandling.IsRateLimit(response.StatusCode, responseBody))
                        {
                            throw new IND_OpenAiRateLimitException(
                                "OpenAI rate limit exceeded while transcribing audio.",
                                retryAfterSeconds,
                                summary);
                        }

                        throw new Exception("Fallo al transcribir con OpenAI.");
                    }

                    var text = TryExtractText(responseBody);
                    if (string.IsNullOrWhiteSpace(text))
                        throw new Exception("OpenAI devolvio texto vacio.");

                    return text;
                }
                catch (TaskCanceledException ex)
                {
                    totalSw.Stop();
                    var reason = cancellationToken.IsCancellationRequested ? "token" : "timeout";
                    _logger.Log("[OPENAI] Peticion cancelada reason=" + reason + " sendMs=" + sendMs + " readMs=" + readMs + " totalMs=" + totalSw.ElapsedMilliseconds + " msg=" + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    totalSw.Stop();
                    _logger.Log("[OPENAI] Error HTTP: " + ex.Message + " sendMs=" + sendMs + " readMs=" + readMs + " totalMs=" + totalSw.ElapsedMilliseconds, AxaptaSessionManager.LogLevel.Error);
                    throw;
                }
                catch (Exception ex)
                {
                    totalSw.Stop();
                    _logger.Log("[OPENAI] Error inesperado: " + ex.Message + " sendMs=" + sendMs + " readMs=" + readMs + " totalMs=" + totalSw.ElapsedMilliseconds, AxaptaSessionManager.LogLevel.Error);
                    throw;
                }
                finally
                {
                    response?.Dispose();
                }
            }
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static string ReadModelFromConfig()
        {
            try
            {
                var model = ConfigurationManager.AppSettings["OpenAI:AudioModel"];
                return string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();
            }
            catch
            {
                return DefaultModel;
            }
        }

        // Lee el timeout de OpenAI desde configuracion con fallback seguro.
        private static int ReadTimeoutSecondsFromConfig()
        {
            try
            {
                var cfg = ConfigurationManager.AppSettings["OpenAI:TimeoutSeconds"];
                if (int.TryParse(cfg, out var value) && value > 0)
                    return value;
            }
            catch
            {
                // Ignorar y aplicar valor por defecto.
            }

            return DefaultTimeoutSeconds;
        }

        private static string TryExtractText(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return null;

            try
            {
                var obj = JObject.Parse(json);
                return obj["text"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static string SanitizeFileName(string fileName)
        {
            // Mantener solo el nombre del archivo para evitar path injection.
            try
            {
                return Path.GetFileName(fileName) ?? "audio";
            }
            catch
            {
                return "audio";
            }
        }

        private static string TryExtractOpenAiErrorSummary(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return string.Empty;

            try
            {
                var root = JObject.Parse(json);
                var err = root["error"] as JObject;
                if (err == null)
                    return string.Empty;

                var type = err["type"]?.ToString();
                var code = err["code"]?.ToString();

                var parts = new System.Collections.Generic.List<string>();
                if (!string.IsNullOrWhiteSpace(type)) parts.Add("type=" + type);
                if (!string.IsNullOrWhiteSpace(code)) parts.Add("code=" + code);

                return parts.Count == 0 ? string.Empty : string.Join(" ", parts);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
