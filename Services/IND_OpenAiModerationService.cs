using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json.Linq;
using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Llama al endpoint de moderacion de OpenAI para revisar contenido.
    /// </summary>
    public sealed class IND_OpenAiModerationService : IND_ITextModerationService
    {
        private const string DefaultModel = "omni-moderation-latest";
        private const string ModerationUrl = "https://api.openai.com/v1/moderations";

        private static readonly HttpClient _http = CreateClient();
        private readonly IAxLogger _logger;

        public IND_OpenAiModerationService(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
        }

        public async Task<ModerationResult> ModerateAsync(string text, string openAiApiKey, string model, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(text))
                return new ModerationResult { IsFlagged = false, CategorySummary = null };

            if (string.IsNullOrWhiteSpace(openAiApiKey))
                throw new ArgumentException("OpenAI API key es obligatoria.", nameof(openAiApiKey));

            var modelToUse = string.IsNullOrWhiteSpace(model) ? DefaultModel : model.Trim();

            using (var request = new HttpRequestMessage(HttpMethod.Post, ModerationUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);
                request.Headers.UserAgent.Clear();
                request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IND_CRM_API", "1.0"));

                var payload = new
                {
                    model = modelToUse,
                    input = text
                };

                var json = Newtonsoft.Json.JsonConvert.SerializeObject(payload);
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");

                HttpResponseMessage response = null;
                string body = null;
                try
                {
                    ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                    response = await _http.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                        .ConfigureAwait(false);

                    body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        var summary = TryExtractOpenAiErrorSummary(body);
                        _logger.Log($"[OPENAI-MOD] Estado {(int)response.StatusCode} {summary}".Trim(), AxaptaSessionManager.LogLevel.Warning);
                        // En caso de duda, no bloquear por fallo de moderacion: seguimos como no flaggeado.
                        return new ModerationResult { IsFlagged = false, CategorySummary = null };
                    }

                    var flagged = TryParseFlagged(body, out var categories);
                    return new ModerationResult
                    {
                        IsFlagged = flagged,
                        CategorySummary = categories
                    };
                }
                catch (TaskCanceledException ex)
                {
                    _logger.Log("[OPENAI-MOD] Peticion cancelada: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                    return new ModerationResult { IsFlagged = false, CategorySummary = null };
                }
                catch (HttpRequestException ex)
                {
                    _logger.Log("[OPENAI-MOD] Error HTTP: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                    return new ModerationResult { IsFlagged = false, CategorySummary = null };
                }
                catch (Exception ex)
                {
                    _logger.Log("[OPENAI-MOD] Error inesperado: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                    return new ModerationResult { IsFlagged = false, CategorySummary = null };
                }
                finally
                {
                    response?.Dispose();
                }
            }
        }

        private static HttpClient CreateClient()
        {
            var client = new HttpClient();
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
        }

        private static bool TryParseFlagged(string json, out string categorySummary)
        {
            categorySummary = null;
            try
            {
                var obj = JObject.Parse(json);
                var first = obj["results"]?[0] as JObject;
                if (first == null) return false;

                var flagged = first["flagged"]?.Value<bool>() ?? false;

                var categories = first["categories"] as JObject;
                if (categories != null)
                {
                    var list = new System.Collections.Generic.List<string>();
                    foreach (var prop in categories.Properties())
                    {
                        var val = prop.Value?.Value<bool>() ?? false;
                        if (val) list.Add(prop.Name);
                    }
                    if (list.Count > 0)
                        categorySummary = string.Join(",", list);
                }

                return flagged;
            }
            catch
            {
                return false;
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
