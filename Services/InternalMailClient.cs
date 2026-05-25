using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using IND_CRM_API.Contracts.Notifications;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Authenticates against IND_INTERNAL_API and sends generic mail commands.
    /// </summary>
    public class InternalMailClient : IInternalMailClient
    {
        private static readonly HttpClient SharedHttpClient = CreateHttpClient();

        private readonly IAxLogger _logger;
        private readonly string _baseUrl;
        private readonly string _clientId;
        private readonly string _clientSecret;

        public InternalMailClient(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _baseUrl = NormalizeBaseUrl(AppSettingsHelper.GetSetting("InternalMail:BaseUrl", "INDCRM_INTERNAL_API_BASE_URL"));
            _clientId = AppSettingsHelper.GetSetting("InternalMail:ClientId", "INDCRM_INTERNAL_API_CLIENT_ID");
            _clientSecret = AppSettingsHelper.GetSetting("InternalMail:ClientSecret", "INDCRM_INTERNAL_API_CLIENT_SECRET");
        }

        public bool IsConfigured
        {
            get
            {
                return !string.IsNullOrWhiteSpace(_baseUrl) &&
                       !string.IsNullOrWhiteSpace(_clientId) &&
                       !string.IsNullOrWhiteSpace(_clientSecret);
            }
        }

        public InternalMailResponse Send(InternalMailRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (!IsConfigured)
            {
                _logger.Log("[EXPENSE-NOTIFY] Internal mail client is not configured.", AxaptaSessionManager.LogLevel.Warning);
                return new InternalMailResponse
                {
                    AcceptedByProvider = false,
                    ErrorCode = IndErrorCodes.ExternalServiceUnavailable,
                    Message = "Internal mail client is not configured."
                };
            }

            try
            {
                var token = Login();
                return SendMail(request, token);
            }
            catch (Exception ex)
            {
                _logger.Log($"[EXPENSE-NOTIFY] Internal mail send failed error={ex.Message}", AxaptaSessionManager.LogLevel.Warning);
                return new InternalMailResponse
                {
                    AcceptedByProvider = false,
                    ErrorCode = IndErrorCodes.ExternalServiceUnavailable,
                    Message = ex.Message
                };
            }
        }

        private string Login()
        {
            var loginUrl = CombineUrl(_baseUrl, "/api/auth/login");
            var payload = JsonConvert.SerializeObject(new
            {
                Username = _clientId,
                Password = _clientSecret
            });

            using (var request = new HttpRequestMessage(HttpMethod.Post, loginUrl))
            {
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using (var response = SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult())
                {
                    var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    if (!response.IsSuccessStatusCode)
                        throw new InvalidOperationException($"Internal API auth failed status={(int)response.StatusCode}");

                    var token = ExtractToken(responseBody);
                    if (string.IsNullOrWhiteSpace(token))
                        throw new InvalidOperationException("Internal API auth response did not contain a token.");

                    return token;
                }
            }
        }

        private InternalMailResponse SendMail(InternalMailRequest mailRequest, string token)
        {
            var mailUrl = CombineUrl(_baseUrl, "/api/internal/v1/mail/messages");
            var payload = JsonConvert.SerializeObject(
                mailRequest,
                Formatting.None,
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

            using (var request = new HttpRequestMessage(HttpMethod.Post, mailUrl))
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

                using (var response = SharedHttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead).GetAwaiter().GetResult())
                {
                    var responseBody = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    var result = ParseMailResponse(responseBody);
                    result.ProviderStatusCode = (int)response.StatusCode;
                    result.RawResponse = responseBody;

                    if (!response.IsSuccessStatusCode)
                    {
                        result.AcceptedByProvider = false;
                        if (string.IsNullOrWhiteSpace(result.ErrorCode))
                            result.ErrorCode = IndErrorCodes.ExternalServiceUnavailable;
                        if (string.IsNullOrWhiteSpace(result.Message))
                            result.Message = $"Internal API mail failed status={(int)response.StatusCode}";
                    }

                    return result;
                }
            }
        }

        private static InternalMailResponse ParseMailResponse(string responseBody)
        {
            var result = new InternalMailResponse();
            if (string.IsNullOrWhiteSpace(responseBody))
                return result;

            try
            {
                var root = JObject.Parse(responseBody);
                var data = root["Data"] as JObject ?? root["data"] as JObject ?? root;

                result.AcceptedByProvider = ReadBool(data, "acceptedByProvider") ?? ReadBool(data, "AcceptedByProvider") ?? false;
                result.Provider = ReadString(data, "provider") ?? ReadString(data, "Provider");
                result.RecipientCount = ReadInt(data, "recipientCount") ?? ReadInt(data, "RecipientCount") ?? 0;
                result.CorrelationId = ReadString(data, "correlationId") ?? ReadString(data, "CorrelationId");
                result.IdempotencyKey = ReadString(data, "idempotencyKey") ?? ReadString(data, "IdempotencyKey");
                result.ErrorCode = ReadString(root, "ErrorCode") ?? ReadString(root, "errorCode");
                result.Message = ReadString(root, "Message") ?? ReadString(root, "message");
            }
            catch
            {
                result.Message = "Internal API mail returned a non-JSON response.";
            }

            return result;
        }

        private static string ExtractToken(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            var root = JObject.Parse(responseBody);
            var paths = new[]
            {
                "token",
                "Token",
                "access_token",
                "Data.token",
                "Data.Token",
                "Data.access_token",
                "data.token",
                "data.Token",
                "data.access_token"
            };

            return paths
                .Select(path => root.SelectToken(path)?.ToString())
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        }

        private static string ReadString(JToken token, string propertyName)
        {
            var value = token?[propertyName];
            return value == null || value.Type == JTokenType.Null ? null : value.ToString();
        }

        private static bool? ReadBool(JToken token, string propertyName)
        {
            var value = token?[propertyName];
            if (value == null || value.Type == JTokenType.Null)
                return null;

            bool parsed;
            return bool.TryParse(value.ToString(), out parsed) ? parsed : (bool?)null;
        }

        private static int? ReadInt(JToken token, string propertyName)
        {
            var value = token?[propertyName];
            if (value == null || value.Type == JTokenType.Null)
                return null;

            int parsed;
            return int.TryParse(value.ToString(), out parsed) ? parsed : (int?)null;
        }

        private static string CombineUrl(string baseUrl, string relativePath)
        {
            return (baseUrl ?? string.Empty).TrimEnd('/') + "/" + (relativePath ?? string.Empty).TrimStart('/');
        }

        private static string NormalizeBaseUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            Uri uri;
            return Uri.TryCreate(trimmed, UriKind.Absolute, out uri) ? trimmed.TrimEnd('/') : null;
        }

        private static HttpClient CreateHttpClient()
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            return new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
        }
    }
}
