using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Calls Azure Document Intelligence receipt OCR using a blob URL source.
    /// </summary>
    public sealed class AzureReceiptAnalyzerService : IAzureReceiptAnalyzerService
    {
        private const string EndpointSettingKey = "AzureDocsIA:Endpoint";
        private const string EndpointEnvVar = "AZURE_DOCS_IA_ENDPOINT";
        private const string KeySettingKey = "AzureDocsIA:Key";
        private const string KeyEnvVar = "AZURE_DOCS_IA_KEY";
        private const string ModelSettingKey = "AzureDocsIA:Model";
        private const string ModelEnvVar = "AZURE_DOCS_IA_MODEL";
        private const string ApiVersionSettingKey = "AzureDocsIA:ApiVersion";
        private const string PollIntervalSettingKey = "AzureDocsIA:PollIntervalMs";
        private const string TimeoutSettingKey = "AzureDocsIA:TimeoutSeconds";
        private const string DefaultModel = "prebuilt-receipt";
        private const string DefaultApiVersion = "2023-07-31";
        private const int DefaultPollIntervalMs = 1000;
        private const int DefaultTimeoutSeconds = 120;

        private static readonly HttpClient HttpClient = CreateHttpClient();

        private readonly IAxLogger _logger;
        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _modelId;
        private readonly string _apiVersion;
        private readonly int _pollIntervalMs;
        private readonly int _timeoutSeconds;

        public AzureReceiptAnalyzerService(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _endpoint = NormalizeEndpoint(AppSettingsHelper.GetSetting(EndpointSettingKey, EndpointEnvVar));
            _apiKey = AppSettingsHelper.GetSetting(KeySettingKey, KeyEnvVar);
            _modelId = ReadStringSetting(ModelSettingKey, ModelEnvVar, DefaultModel);
            _apiVersion = ReadStringSetting(ApiVersionSettingKey, null, DefaultApiVersion);
            _pollIntervalMs = ReadIntSetting(PollIntervalSettingKey, DefaultPollIntervalMs);
            _timeoutSeconds = ReadIntSetting(TimeoutSettingKey, DefaultTimeoutSeconds);
        }

        public async Task<AzureReceiptAnalysisResult> AnalyzeReceiptFromBlobUrlAsync(string blobReadUrl, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(blobReadUrl))
                throw new ArgumentException("blobReadUrl es obligatorio.", nameof(blobReadUrl));

            if (string.IsNullOrWhiteSpace(_endpoint) || string.IsNullOrWhiteSpace(_apiKey))
                throw new InvalidOperationException("Azure Document Intelligence no esta configurado.");

            var analyzeUri = BuildAnalyzeUri();
            var analyzeRequestBody = JsonConvert.SerializeObject(new JObject
            {
                ["urlSource"] = blobReadUrl.Trim()
            });

            using (var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(_timeoutSeconds));

                var sw = System.Diagnostics.Stopwatch.StartNew();
                _logger.Log(
                    $"[AZDOCS] AnalyzeReceipt start model={_modelId} apiVersion={_apiVersion} blobUrlLength={blobReadUrl.Length}",
                    AxaptaSessionManager.LogLevel.Info);

                using (var request = new HttpRequestMessage(HttpMethod.Post, analyzeUri))
                {
                    request.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _apiKey);
                    request.Content = new StringContent(analyzeRequestBody, Encoding.UTF8, "application/json");

                    using (var response = await HttpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false))
                    {
                        var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        if (!response.IsSuccessStatusCode && response.StatusCode != System.Net.HttpStatusCode.Accepted)
                            throw BuildAzureDocsException("POST", responseBody, (int)response.StatusCode);

                        var operationLocation = response.Headers.Contains("Operation-Location")
                            ? response.Headers.GetValues("Operation-Location").FirstOrDefault()
                            : null;

                        if (response.StatusCode != System.Net.HttpStatusCode.Accepted || string.IsNullOrWhiteSpace(operationLocation))
                        {
                            var directResult = TryBuildAnalysisResult(responseBody);
                            if (directResult != null)
                            {
                                _logger.Log($"[AZDOCS] AnalyzeReceipt completed-direct ms={sw.ElapsedMilliseconds} items={directResult.ItemCount}", AxaptaSessionManager.LogLevel.Info);
                                return directResult;
                            }

                            throw new InvalidOperationException("Azure Document Intelligence no devolvio Operation-Location.");
                        }

                        while (true)
                        {
                            timeoutCts.Token.ThrowIfCancellationRequested();
                            await Task.Delay(_pollIntervalMs, timeoutCts.Token).ConfigureAwait(false);

                            using (var pollRequest = new HttpRequestMessage(HttpMethod.Get, operationLocation))
                            {
                                pollRequest.Headers.TryAddWithoutValidation("Ocp-Apim-Subscription-Key", _apiKey);

                                using (var pollResponse = await HttpClient.SendAsync(pollRequest, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false))
                                {
                                    var pollBody = await pollResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
                                    if (!pollResponse.IsSuccessStatusCode)
                                        throw BuildAzureDocsException("GET", pollBody, (int)pollResponse.StatusCode);

                                    var status = TryReadStatus(pollBody);
                                    if (string.Equals(status, "succeeded", StringComparison.OrdinalIgnoreCase))
                                    {
                                        var result = TryBuildAnalysisResult(pollBody);
                                        if (result == null)
                                            throw new InvalidOperationException("Azure Document Intelligence devolvio un resultado vacio.");

                                        _logger.Log(
                                            $"[AZDOCS] AnalyzeReceipt completed ms={sw.ElapsedMilliseconds} items={result.ItemCount} merchant={ToLogValue(result.MerchantName)} total={ToLogDecimal(result.TotalAmount)}",
                                            AxaptaSessionManager.LogLevel.Info);
                                        return result;
                                    }

                                    if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                                        throw BuildAzureDocsException("GET", pollBody, 200);
                                }
                            }
                        }
                    }
                }
            }
        }

        private Uri BuildAnalyzeUri()
        {
            var baseEndpoint = _endpoint.TrimEnd('/');
            return new Uri($"{baseEndpoint}/formrecognizer/documentModels/{Uri.EscapeDataString(_modelId)}:analyze?api-version={Uri.EscapeDataString(_apiVersion)}");
        }

        private static AzureReceiptAnalysisResult TryBuildAnalysisResult(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            var root = JObject.Parse(rawJson);
            var analyzeResult = root["analyzeResult"] as JObject;
            if (analyzeResult == null)
                return null;

            var documents = analyzeResult["documents"] as JArray;
            var firstDocument = documents?.OfType<JObject>().FirstOrDefault();
            var fields = firstDocument?["fields"] as JObject;
            var items = BuildCompactItems(fields?["Items"]);
            var totalToken = ProjectMoneyField(fields?["Total"] as JObject);
            var subtotalToken = ProjectMoneyField(fields?["Subtotal"] as JObject);
            var taxToken = ProjectMoneyField(fields?["TotalTax"] as JObject);
            var tipToken = ProjectMoneyField(fields?["Tip"] as JObject);
            var transactionDate = ReadFieldScalar(fields?["TransactionDate"] as JObject);
            var merchantName = ReadFieldScalar(fields?["MerchantName"] as JObject);
            var merchantAddress = ReadFieldScalar(fields?["MerchantAddress"] as JObject);
            var merchantPhone = ReadFieldScalar(fields?["MerchantPhoneNumber"] as JObject);
            var projected = new JObject
            {
                ["source"] = "azure-document-intelligence",
                ["modelId"] = analyzeResult["modelId"]?.ToString() ?? firstDocument?["docType"]?.ToString(),
                ["receiptType"] = firstDocument?["docType"]?.ToString(),
                ["merchant"] = new JObject
                {
                    ["name"] = merchantName == null ? JValue.CreateNull() : new JValue(merchantName),
                    ["address"] = merchantAddress == null ? JValue.CreateNull() : new JValue(merchantAddress),
                    ["phone"] = merchantPhone == null ? JValue.CreateNull() : new JValue(merchantPhone)
                },
                ["transactionDate"] = transactionDate == null ? JValue.CreateNull() : new JValue(transactionDate),
                ["currencyCode"] = ReadProjectedCurrencyCode(totalToken),
                ["totals"] = new JObject
                {
                    ["subtotal"] = subtotalToken ?? JValue.CreateNull(),
                    ["tax"] = taxToken ?? JValue.CreateNull(),
                    ["tip"] = tipToken ?? JValue.CreateNull(),
                    ["total"] = totalToken ?? JValue.CreateNull()
                },
                ["items"] = items,
                ["itemCount"] = items.Count
            };

            return new AzureReceiptAnalysisResult
            {
                RawJson = rawJson,
                PromptJson = JsonConvert.SerializeObject(projected),
                MerchantName = merchantName,
                TransactionDate = transactionDate,
                CurrencyCode = ReadProjectedCurrencyCode(totalToken),
                TotalAmount = ReadProjectedAmount(totalToken),
                ItemCount = items.Count,
                Warnings = new List<string>()
            };
        }

        private static JArray BuildCompactItems(JToken itemsToken)
        {
            var items = new JArray();
            if (!(itemsToken is JObject itemsField) || !(itemsField["valueArray"] is JArray itemsArray))
                return items;

            foreach (var item in itemsArray.OfType<JObject>())
            {
                var valueObject = item["valueObject"] as JObject;
                if (valueObject == null)
                    continue;

                items.Add(new JObject
                {
                    ["description"] = ToNullableValue(ReadFieldScalar(valueObject["Description"] as JObject)),
                    ["quantity"] = ToNullableNumber(ReadFieldNumber(valueObject["Quantity"] as JObject)),
                    ["unitPrice"] = ProjectMoneyField(valueObject["Price"] as JObject) ?? JValue.CreateNull(),
                    ["amount"] = ProjectMoneyField(valueObject["TotalPrice"] as JObject) ?? JValue.CreateNull()
                });
            }

            return items;
        }

        private static JToken ProjectMoneyField(JObject field)
        {
            if (field == null)
                return JValue.CreateNull();

            if (field["valueCurrency"] is JObject valueCurrency)
            {
                return new JObject
                {
                    ["amount"] = valueCurrency["amount"],
                    ["currencyCode"] = valueCurrency["currencyCode"] ?? field["content"]
                };
            }

            var amount = ReadFieldNumber(field);
            if (!amount.HasValue)
                return JValue.CreateNull();

            return new JObject
            {
                ["amount"] = amount.Value,
                ["currencyCode"] = field["content"]?.ToString()
            };
        }

        private static string ReadFieldScalar(JObject field)
        {
            if (field == null)
                return null;

            var scalar = field["valueString"]
                ?? field["valueDate"]
                ?? field["valueTime"]
                ?? field["valuePhoneNumber"]
                ?? field["valueCountryRegion"]
                ?? field["content"];

            var value = scalar?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static decimal? ReadFieldNumber(JObject field)
        {
            if (field == null)
                return null;

            var token = field["valueNumber"] ?? field["valueInteger"] ?? field["valueCurrency"]?["amount"];
            if (token == null)
                return null;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
                return token.Value<decimal>();

            return decimal.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (decimal?)null;
        }

        private static JToken ToNullableValue(string value)
        {
            return value == null ? JValue.CreateNull() : new JValue(value);
        }

        private static JToken ToNullableNumber(decimal? value)
        {
            return value.HasValue ? new JValue(value.Value) : JValue.CreateNull();
        }

        private static string ReadProjectedCurrencyCode(JToken token)
        {
            if (!(token is JObject currencyObject))
                return null;

            var value = currencyObject["currencyCode"]?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static decimal? ReadProjectedAmount(JToken token)
        {
            if (!(token is JObject currencyObject))
                return null;

            var amountToken = currencyObject["amount"];
            if (amountToken == null)
                return null;

            if (amountToken.Type == JTokenType.Float || amountToken.Type == JTokenType.Integer)
                return amountToken.Value<decimal>();

            if (decimal.TryParse(amountToken.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }

        private static string TryReadStatus(string rawJson)
        {
            if (string.IsNullOrWhiteSpace(rawJson))
                return null;

            try
            {
                var root = JObject.Parse(rawJson);
                return root["status"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static Exception BuildAzureDocsException(string operation, string responseBody, int statusCode)
        {
            var summary = string.Empty;
            if (!string.IsNullOrWhiteSpace(responseBody))
            {
                try
                {
                    var root = JObject.Parse(responseBody);
                    summary = root["error"]?["message"]?.ToString()
                        ?? root["message"]?.ToString()
                        ?? root["status"]?.ToString()
                        ?? string.Empty;
                }
                catch
                {
                    summary = responseBody.Trim();
                }
            }

            return new InvalidOperationException(
                $"Azure Document Intelligence fallo en {operation} status={statusCode} detail={summary}".Trim());
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
                return null;

            return endpoint.Trim().TrimEnd('/');
        }

        private static string ReadStringSetting(string key, string envVarName, string defaultValue)
        {
            var value = AppSettingsHelper.GetSetting(key, envVarName);
            return string.IsNullOrWhiteSpace(value) ? defaultValue : value.Trim();
        }

        private static int ReadIntSetting(string key, int defaultValue)
        {
            var raw = AppSettingsHelper.GetSetting(key);
            return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : defaultValue;
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            client.DefaultRequestHeaders.ExpectContinue = false;
            return client;
        }

        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "null" : value.Trim();
        }

        private static string ToLogDecimal(decimal? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "null";
        }
    }
}
