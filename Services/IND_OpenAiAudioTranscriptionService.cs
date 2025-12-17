using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Configuration;
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
    /// OpenAI implementation for audio transcription.
    /// Notes:
    /// - This uses the OpenAI Audio Transcriptions API: POST https://api.openai.com/v1/audio/transcriptions
    /// - The recommended model is configured via App.config (OpenAI:AudioModel), default "gpt-4o-transcribe".
    /// - Comments are ASCII-only by project agreement.
    /// </summary>
    public sealed class IND_OpenAiAudioTranscriptionService : IND_IAudioTranscriptionService
    {
        private const string DefaultModel = "gpt-4o-transcribe";
        private const string TranscriptionsUrl = "https://api.openai.com/v1/audio/transcriptions";

        // Reuse HttpClient for the whole process to avoid socket exhaustion.
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
            if (!audioStream.CanRead) throw new ArgumentException("audioStream must be readable.", nameof(audioStream));
            if (string.IsNullOrWhiteSpace(fileName)) throw new ArgumentException("fileName is required.", nameof(fileName));
            if (string.IsNullOrWhiteSpace(openAiApiKey)) throw new ArgumentException("OpenAI API key is required.", nameof(openAiApiKey));
            if (string.IsNullOrWhiteSpace(languageId)) throw new ArgumentException("languageId is required.", nameof(languageId));
            if (temperature < 0 || temperature > 1) throw new ArgumentOutOfRangeException(nameof(temperature), "temperature must be between 0 and 1.");

            if (audioStream.CanSeek)
                audioStream.Position = 0;

            // OpenAI accepts mp3, mp4, mpeg, mpga, m4a, wav, webm, ogg, flac.
            // We validate formats in the controller; the service assumes it receives a valid audio stream.
            using (var form = new MultipartFormDataContent())
            using (var request = new HttpRequestMessage(HttpMethod.Post, TranscriptionsUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);

                // Identify the application in a simple way.
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IND_CRM_API", "1.0"));

                var fileContent = new StreamContent(audioStream);
                fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(fileContent, "file", SanitizeFileName(fileName));

                form.Add(new StringContent(_model, Encoding.UTF8), "model");

                // Per OpenAI docs, gpt-4o-transcribe expects JSON response format.
                form.Add(new StringContent("json", Encoding.UTF8), "response_format");

                // If languageId is "auto", do not send the language parameter.
                if (!string.Equals(languageId, "auto", StringComparison.OrdinalIgnoreCase))
                    form.Add(new StringContent(languageId, Encoding.UTF8), "language");

                // Temperature: controls randomness. Default 0 for deterministic.
                form.Add(new StringContent(temperature.ToString(System.Globalization.CultureInfo.InvariantCulture), Encoding.UTF8), "temperature");

                // Optional prompt / context to bias transcription vocabulary.
                if (!string.IsNullOrWhiteSpace(prompt))
                    form.Add(new StringContent(prompt, Encoding.UTF8), "prompt");

                request.Content = form;

                HttpResponseMessage response = null;
                string responseBody = null;
                try
                {
                    // Ensure TLS 1.2 on older environments.
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                    response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);

                    responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

                    if (!response.IsSuccessStatusCode)
                    {
                        _logger.Log($"[OPENAI] Transcription failed. Status={(int)response.StatusCode} Body={TruncateForLog(responseBody, 1024)}", AxaptaSessionManager.LogLevel.Error);
                        throw new Exception("OpenAI transcription request failed.");
                    }

                    var text = TryExtractText(responseBody);
                    if (string.IsNullOrWhiteSpace(text))
                        throw new Exception("OpenAI transcription returned empty text.");

                    return text;
                }
                catch (TaskCanceledException ex)
                {
                    // This can be client cancellation or request timeout.
                    _logger.Log("[OPENAI] Transcription request canceled: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    _logger.Log("[OPENAI] HTTP error calling OpenAI: " + ex.Message, AxaptaSessionManager.LogLevel.Error);
                    throw;
                }
                catch (Exception ex)
                {
                    _logger.Log("[OPENAI] Unexpected transcription error: " + ex.Message, AxaptaSessionManager.LogLevel.Error);
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
            client.Timeout = TimeSpan.FromSeconds(90);
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
            // Keep only the file name component to avoid path injection.
            try
            {
                return Path.GetFileName(fileName) ?? "audio";
            }
            catch
            {
                return "audio";
            }
        }

        private static string TruncateForLog(string value, int maxChars)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var cleaned = value.Replace("\r", " ").Replace("\n", " ").Trim();
            if (cleaned.Length <= maxChars)
                return cleaned;

            return cleaned.Substring(0, maxChars) + "...";
        }
    }
}
