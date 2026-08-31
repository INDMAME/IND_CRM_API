using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
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
        private const int MaxPromptOcrTextChars = 6000;
        private const int MaxPromptOcrLines = 160;
        private const int MaxPromptOcrLineChars = 180;

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
            {
                throw new IND_ExternalServiceException(
                    "Azure Document Intelligence",
                    "El servicio de analisis OCR no esta disponible porque no esta configurado correctamente.",
                    IndErrorCodes.ExternalServiceUnavailable,
                    HttpStatusCode.ServiceUnavailable,
                    "not-configured");
            }

            var analyzeUri = BuildAnalyzeUri();
            var analyzeRequestBody = JsonConvert.SerializeObject(new JObject
            {
                ["urlSource"] = blobReadUrl.Trim()
            });

            try
            {
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

                                throw new IND_ExternalServiceException(
                                    "Azure Document Intelligence",
                                    "El servicio de analisis OCR devolvio una respuesta incompleta.",
                                    IndErrorCodes.ExternalServiceUnavailable,
                                    HttpStatusCode.ServiceUnavailable,
                                    "missing-operation-location");
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
                                                throw new IND_ExternalServiceException(
                                                    "Azure Document Intelligence",
                                                    "El servicio de analisis OCR devolvio un resultado vacio.",
                                                    IndErrorCodes.ExternalServiceUnavailable,
                                                    HttpStatusCode.ServiceUnavailable,
                                                    "empty-result");

                                            _logger.Log(
                                                $"[AZDOCS] AnalyzeReceipt completed ms={sw.ElapsedMilliseconds} items={result.ItemCount} merchant={ToLogValue(result.MerchantName)} total={ToLogDecimal(result.TotalAmount)} currencyCode={ToLogValue(result.CurrencyCode)} rawCurrency={ToLogValue(result.RawCurrency)} currencyHints={ToLogValue(result.CurrencyHints == null ? null : string.Join("|", result.CurrencyHints))}",
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
            catch (OperationCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw;

                throw new IND_ExternalServiceException(
                    "Azure Document Intelligence",
                    "El analisis OCR tardo demasiado y el servicio externo no respondio a tiempo.",
                    IndErrorCodes.ExternalServiceTimeout,
                    HttpStatusCode.GatewayTimeout,
                    "timeout",
                    ex);
            }
            catch (HttpRequestException ex)
            {
                throw new IND_ExternalServiceException(
                    "Azure Document Intelligence",
                    "No se pudo conectar con el servicio de analisis OCR.",
                    IndErrorCodes.ExternalServiceUnavailable,
                    HttpStatusCode.ServiceUnavailable,
                    ex.Message,
                    ex);
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
            var firstDocument = documents != null && documents.Count > 0
                ? documents[0] as JObject
                : null;
            var fields = firstDocument?["fields"] as JObject;
            var items = BuildCompactItems(fields?["Items"]);
            var totalField = fields?["Total"] as JObject;
            var totalContent = totalField?["content"]?.ToString();
            var authoritativeVndTotalAmount = TryExtractUnambiguousVndGroupedTotal(totalContent);
            var totalToken = ProjectMoneyField(totalField);
            var subtotalToken = ProjectMoneyField(fields?["Subtotal"] as JObject);
            var taxToken = ProjectMoneyField(fields?["TotalTax"] as JObject);
            var tipToken = ProjectMoneyField(fields?["Tip"] as JObject);
            var transactionDate = ReadFieldScalar(fields?["TransactionDate"] as JObject);
            var merchantName = ReadFieldScalar(fields?["MerchantName"] as JObject);
            var merchantAddress = ReadFieldScalar(fields?["MerchantAddress"] as JObject);
            var merchantPhone = ReadFieldScalar(fields?["MerchantPhoneNumber"] as JObject);
            var receiptContent = analyzeResult["content"]?.ToString();
            var ocrText = NormalizeOcrTextForPrompt(receiptContent);
            var ocrLines = BuildCompactOcrLines(analyzeResult["pages"], receiptContent);
            var currencyHints = BuildCurrencyHints(receiptContent, totalToken, subtotalToken, taxToken, tipToken, items);
            var resolvedCurrencyCode = authoritativeVndTotalAmount.HasValue
                ? "VND"
                : ResolveCurrencyCode(receiptContent, totalToken, subtotalToken, taxToken, tipToken, items);
            var resolvedRawCurrency = authoritativeVndTotalAmount.HasValue
                ? ReadNonEmpty(CurrencyCodeHelper.ResolveRawHint(totalContent)) ?? "VND"
                : ResolveRawCurrency(currencyHints, totalToken, subtotalToken, taxToken, tipToken, items);
            if (authoritativeVndTotalAmount.HasValue)
                currencyHints = new List<string> { resolvedRawCurrency };

            var structuredTotalAmount = ReadProjectedAmount(totalToken);
            var explicitTextTotalAmount = TryExtractExplicitTotalAmountFromReceiptContent(receiptContent);
            var structuredTotalWasOverridden =
                !authoritativeVndTotalAmount.HasValue &&
                explicitTextTotalAmount.HasValue &&
                structuredTotalAmount.HasValue &&
                Math.Abs(explicitTextTotalAmount.Value - structuredTotalAmount.Value) > 0.02m;
            var fallbackTotalAmount = authoritativeVndTotalAmount ?? (structuredTotalAmount.HasValue
                ? (structuredTotalWasOverridden ? explicitTextTotalAmount : structuredTotalAmount)
                : (explicitTextTotalAmount ?? TryExtractTotalAmountFromReceiptContent(receiptContent)));
            var effectiveTotalToken = totalToken;
            if (authoritativeVndTotalAmount.HasValue)
            {
                effectiveTotalToken = BuildFallbackMoneyToken(
                    authoritativeVndTotalAmount.Value,
                    resolvedCurrencyCode,
                    resolvedRawCurrency);
            }
            else if (fallbackTotalAmount.HasValue &&
                ((effectiveTotalToken == null || effectiveTotalToken.Type == JTokenType.Null) || structuredTotalWasOverridden))
            {
                effectiveTotalToken = BuildFallbackMoneyToken(fallbackTotalAmount.Value, resolvedCurrencyCode, resolvedRawCurrency);
            }
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
                ["currencyCode"] = ToNullableValue(resolvedCurrencyCode),
                ["rawCurrency"] = ToNullableValue(resolvedRawCurrency),
                ["currencyHints"] = new JArray(currencyHints.Select(h => new JValue(h))),
                ["totals"] = new JObject
                {
                    ["subtotal"] = subtotalToken ?? JValue.CreateNull(),
                    ["tax"] = taxToken ?? JValue.CreateNull(),
                    ["tip"] = tipToken ?? JValue.CreateNull(),
                    ["total"] = effectiveTotalToken ?? JValue.CreateNull()
                },
                ["items"] = items,
                ["itemCount"] = items.Count,
                ["ocrText"] = ToNullableValue(ocrText),
                ["ocrLines"] = ocrLines
            };

            return new AzureReceiptAnalysisResult
            {
                RawJson = rawJson,
                PromptJson = JsonConvert.SerializeObject(projected),
                MerchantName = merchantName,
                TransactionDate = transactionDate,
                CurrencyCode = resolvedCurrencyCode,
                RawCurrency = resolvedRawCurrency,
                TotalAmount = fallbackTotalAmount,
                HasAuthoritativeVndTotal = authoritativeVndTotalAmount.HasValue,
                ItemCount = items.Count,
                Warnings = new List<string>(),
                CurrencyHints = currencyHints
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

        private static string NormalizeOcrTextForPrompt(string receiptContent)
        {
            if (string.IsNullOrWhiteSpace(receiptContent))
                return null;

            var lines = receiptContent
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(NormalizePromptLine)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();

            if (lines.Count == 0)
                return null;

            var normalized = string.Join("\n", lines);
            return normalized.Length > MaxPromptOcrTextChars
                ? normalized.Substring(0, MaxPromptOcrTextChars)
                : normalized;
        }

        private static JArray BuildCompactOcrLines(JToken pagesToken, string receiptContent)
        {
            var lines = new List<string>();

            if (pagesToken is JArray pages)
            {
                foreach (var lineToken in pages
                    .OfType<JObject>()
                    .SelectMany(page => (page["lines"] as JArray)?.OfType<JObject>() ?? Enumerable.Empty<JObject>()))
                {
                    AddPromptLine(lines, lineToken["content"]?.ToString());
                    if (lines.Count >= MaxPromptOcrLines)
                        break;
                }
            }

            if (lines.Count == 0 && !string.IsNullOrWhiteSpace(receiptContent))
            {
                foreach (var rawLine in receiptContent.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    AddPromptLine(lines, rawLine);
                    if (lines.Count >= MaxPromptOcrLines)
                        break;
                }
            }

            return new JArray(lines.Select(line => new JValue(line)));
        }

        private static void AddPromptLine(List<string> lines, string value)
        {
            if (lines == null)
                return;

            var normalized = NormalizePromptLine(value);
            if (string.IsNullOrWhiteSpace(normalized))
                return;

            if (normalized.Length > MaxPromptOcrLineChars)
                normalized = normalized.Substring(0, MaxPromptOcrLineChars);

            lines.Add(normalized);
        }

        private static string NormalizePromptLine(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return Regex.Replace(value.Trim(), @"\s+", " ");
        }

        private static JToken ProjectMoneyField(JObject field)
        {
            if (field == null)
                return JValue.CreateNull();

            if (field["valueCurrency"] is JObject valueCurrency)
            {
                var rawCurrency = ReadNonEmpty(CurrencyCodeHelper.ResolveRawHint(
                    valueCurrency["currencyCode"]?.ToString(),
                    field["content"]?.ToString()));
                return new JObject
                {
                    ["amount"] = valueCurrency["amount"],
                    ["currencyCode"] = ToNullableValue(CurrencyCodeHelper.NormalizeToIso4217(rawCurrency)),
                    ["rawCurrency"] = ToNullableValue(rawCurrency)
                };
            }

            var amount = ReadFieldNumber(field);
            if (!amount.HasValue)
                return JValue.CreateNull();

            var fallbackRawCurrency = ReadNonEmpty(CurrencyCodeHelper.ResolveRawHint(field["content"]?.ToString()));
            return new JObject
            {
                ["amount"] = amount.Value,
                ["currencyCode"] = ToNullableValue(CurrencyCodeHelper.NormalizeToIso4217(fallbackRawCurrency)),
                ["rawCurrency"] = ToNullableValue(fallbackRawCurrency)
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
            var trimmed = ReadNonEmpty(value);
            return trimmed == null ? JValue.CreateNull() : new JValue(trimmed);
        }

        private static JToken ToNullableNumber(decimal? value)
        {
            return value.HasValue ? new JValue(value.Value) : JValue.CreateNull();
        }

        private static JToken BuildFallbackMoneyToken(decimal amount, string currencyCode, string rawCurrency)
        {
            return new JObject
            {
                ["amount"] = amount,
                ["currencyCode"] = ToNullableValue(currencyCode),
                ["rawCurrency"] = ToNullableValue(rawCurrency)
            };
        }

        private static string ReadProjectedCurrencyCode(JToken token)
        {
            if (!(token is JObject currencyObject))
                return null;

            var value = currencyObject["currencyCode"]?.ToString();
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static string ReadProjectedRawCurrency(JToken token)
        {
            if (!(token is JObject currencyObject))
                return null;

            var value = currencyObject["rawCurrency"]?.ToString();
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

        //MMS - Normalizes only unambiguous grouped VND integers from the first document Total content - 2026.08.31
        private static decimal? TryExtractUnambiguousVndGroupedTotal(string totalContent)
        {
            if (string.IsNullOrWhiteSpace(totalContent) || totalContent.IndexOf('$') >= 0)
                return null;

            var normalizedCurrencies = CurrencyCodeHelper.ExtractHints(totalContent)
                .Select(CurrencyCodeHelper.NormalizeToIso4217)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (normalizedCurrencies.Count != 1 ||
                !string.Equals(normalizedCurrencies[0], "VND", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var groupedAmounts = Regex.Matches(
                totalContent,
                @"(?<![\d.,+\-])(?<amount>\d{1,3}(?<separator>[.,])\d{3}(?:\k<separator>\d{3})*)(?![\d.,+\-])",
                RegexOptions.CultureInvariant);
            if (groupedAmounts.Count != 1)
                return null;

            var groupedAmount = groupedAmounts[0];
            var amountText = groupedAmount.Groups["amount"].Value;
            var separator = groupedAmount.Groups["separator"].Value;
            var remainingContent = totalContent.Remove(groupedAmount.Index, groupedAmount.Length);
            if (remainingContent.Any(ch => char.IsDigit(ch) || "+-()".IndexOf(ch) >= 0))
                return null;

            var normalizedAmount = amountText.Replace(separator, string.Empty);
            return decimal.TryParse(normalizedAmount, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed > 0m
                ? parsed
                : (decimal?)null;
        }

        // Returns the best payable, plain-total, or generic amount candidate from OCR text.
        private static decimal? TryExtractTotalAmountFromReceiptContent(string receiptContent)
        {
            return TryExtractTotalAmountFromReceiptContentCore(receiptContent, false);
        }

        // Returns only a payable-total label so it can safely override a misclassified Azure Total field.
        private static decimal? TryExtractExplicitTotalAmountFromReceiptContent(string receiptContent)
        {
            return TryExtractTotalAmountFromReceiptContentCore(receiptContent, true);
        }

        // Scans the complete OCR text and keeps the last candidate at each confidence tier.
        private static decimal? TryExtractTotalAmountFromReceiptContentCore(string receiptContent, bool explicitOnly)
        {
            if (string.IsNullOrWhiteSpace(receiptContent))
                return null;

            var lines = receiptContent
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => (line ?? string.Empty).Trim())
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToList();
            if (lines.Count == 0)
                return null;

            decimal? payableTotal = null;
            decimal? plainTotal = null;
            decimal? genericTotal = null;
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                var priority = GetReceiptTotalCandidatePriority(line);
                if (priority == ReceiptTotalCandidatePriority.None)
                    continue;

                var amount = TryExtractAmountFromLine(line);
                if ((!amount.HasValue || amount.Value <= 0m) && i + 1 < lines.Count)
                {
                    var nextLine = lines[i + 1];
                    if (!IsReceiptTotalExcludedLine(nextLine))
                        amount = TryExtractAmountFromLine(nextLine);
                }

                if (!amount.HasValue || amount.Value <= 0m)
                    continue;

                if (priority == ReceiptTotalCandidatePriority.Payable)
                    payableTotal = amount.Value;
                else if (priority == ReceiptTotalCandidatePriority.PlainTotal)
                    plainTotal = amount.Value;
                else if (!explicitOnly)
                    genericTotal = amount.Value;
            }

            return payableTotal ?? plainTotal ?? (explicitOnly ? null : genericTotal);
        }

        //MMS - Ranks explicit payable totals above generic amount labels across the complete OCR text - 2026.08.03
        private static ReceiptTotalCandidatePriority GetReceiptTotalCandidatePriority(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return ReceiptTotalCandidatePriority.None;

            if (ContainsReceiptBaseOrSubtotalLabel(line))
                return ReceiptTotalCandidatePriority.None;

            if (IsReceiptTotalExcludedLine(line))
                return ReceiptTotalCandidatePriority.None;

            var isPayableTotal = line.IndexOf("amount due", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("balance due", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("grand total", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("importe a pagar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("importo da pagare", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("total a pagar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("totale da pagare", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("a pagar", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("total factura", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("total final", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                 line.IndexOf("totale fattura", StringComparison.OrdinalIgnoreCase) >= 0;
            if (isPayableTotal)
                return ReceiptTotalCandidatePriority.Payable;

            if (IsReceiptPlainTotalLabel(line))
                return ReceiptTotalCandidatePriority.PlainTotal;

            return line.IndexOf("importe", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("zenbatekoa", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   line.IndexOf("amount", StringComparison.OrdinalIgnoreCase) >= 0
                ? ReceiptTotalCandidatePriority.Generic
                : ReceiptTotalCandidatePriority.None;
        }

        private static bool IsReceiptTotalExcludedLine(string line)
        {
            return ContainsReceiptBaseOrSubtotalLabel(line) || IsReceiptNonPayableComponentLine(line);
        }

        // Accepts a standalone total label and rejects product or merchant names that merely contain "total".
        private static bool IsReceiptPlainTotalLabel(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            return Regex.IsMatch(
                line,
                @"^\s*(?:(?:(?:importe|importo)\s+)?(?:total|totale)|guztira)(?:\s+(?:general|generale|geral|factura|fattura|final|finale|ttc|(?:iva|vat)\s+incl\w*))?\s*[:\-]?\s*(?:(?:EUR|USD|GBP)\s*)?(?:€|\$|£)?\s*(?:\d|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static bool ContainsReceiptBaseOrSubtotalLabel(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var excludedKeywords = new[]
            {
                "subtotal",
                "sub-total",
                "sub total",
                "base imponible",
                "importe base",
                "base amount",
                "taxable amount",
                "net amount",
                "importe neto"
            };
            return excludedKeywords.Any(keyword => line.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        // Rejects tax, tip, discount, savings, tendered, paid, and change components as payable totals.
        private static bool IsReceiptNonPayableComponentLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return false;

            var containsTaxOrTip = Regex.IsMatch(
                                       line,
                                       @"\b(?:tax|taxes|iva|vat|impuesto|impuestos|imposta|imposte|tip|tips|propina|propinas|mancia)\b",
                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant) ||
                                   line.IndexOf('%') >= 0;
            if (containsTaxOrTip && line.IndexOf("incl", StringComparison.OrdinalIgnoreCase) < 0)
                return true;

            return Regex.IsMatch(
                line,
                @"\b(?:descuento|descuentos|discount|discounts|sconto|sconti|ahorro|ahorros|saving|savings|risparmio|risparmi|cambio|change|resto|pagado|paid|pagato|pagata|entregado|tendered|recibido|received|ricevuto|ricevuta)\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        }

        private static decimal? TryExtractAmountFromLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line))
                return null;

            if (line.IndexOf('%') >= 0)
                return null;

            var matches = Regex.Matches(
                line,
                @"(?<!\d)(\d{1,3}(?:[.,]\d{3})*[.,]\d{2}|\d+[.,]\d{2}|\d+)(?!\d)");
            if (matches.Count == 0)
                return null;

            for (int i = matches.Count - 1; i >= 0; i--)
            {
                var parsed = TryParseAmountText(matches[i].Value);
                if (parsed.HasValue && parsed.Value > 0m)
                    return parsed.Value;
            }

            return null;
        }

        private static decimal? TryParseAmountText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var candidate = new string(raw
                .Trim()
                .Where(ch => char.IsDigit(ch) || ch == '.' || ch == ',' || ch == '-' || ch == '+')
                .ToArray());
            if (string.IsNullOrWhiteSpace(candidate))
                return null;

            var lastComma = candidate.LastIndexOf(',');
            var lastDot = candidate.LastIndexOf('.');
            if (lastComma >= 0 && lastDot >= 0)
            {
                candidate = lastComma > lastDot
                    ? candidate.Replace(".", string.Empty).Replace(',', '.')
                    : candidate.Replace(",", string.Empty);
            }
            else if (lastComma >= 0)
            {
                candidate = candidate.Replace(',', '.');
            }

            if (decimal.TryParse(candidate, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            if (decimal.TryParse(candidate, NumberStyles.Number, CultureInfo.GetCultureInfo("es-ES"), out parsed))
                return parsed;

            return null;
        }

        private static List<string> BuildCurrencyHints(string receiptContent, params JToken[] currencyTokens)
        {
            var hints = new List<string>();

            foreach (var hint in CurrencyCodeHelper.ExtractHints(receiptContent))
                AddDistinct(hints, hint);

            foreach (var token in EnumerateCurrencyTokens(currencyTokens))
            {
                AddDistinct(hints, ReadProjectedCurrencyCode(token));
                AddDistinct(hints, ReadProjectedRawCurrency(token));
            }

            return hints;
        }

        private static IEnumerable<JToken> EnumerateCurrencyTokens(IEnumerable<JToken> tokens)
        {
            if (tokens == null)
                yield break;

            foreach (var token in tokens)
            {
                if (token == null || token.Type == JTokenType.Null)
                    continue;

                if (token is JArray array)
                {
                    foreach (var childObject in array.OfType<JObject>())
                    {
                        var unitPrice = childObject["unitPrice"];
                        if (unitPrice != null && unitPrice.Type != JTokenType.Null)
                            yield return unitPrice;

                        var amount = childObject["amount"];
                        if (amount != null && amount.Type != JTokenType.Null)
                            yield return amount;
                    }

                    continue;
                }

                yield return token;
            }
        }

        private static string ResolveCurrencyCode(string receiptContent, params JToken[] currencyTokens)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(receiptContent))
                candidates.Add(receiptContent);

            foreach (var token in EnumerateCurrencyTokens(currencyTokens))
            {
                candidates.Add(ReadProjectedCurrencyCode(token));
                candidates.Add(ReadProjectedRawCurrency(token));
            }

            return CurrencyCodeHelper.ResolveToIso4217(candidates.ToArray());
        }

        private static string ResolveRawCurrency(List<string> currencyHints, params JToken[] currencyTokens)
        {
            var fromHints = currencyHints?.FirstOrDefault(h => !string.IsNullOrWhiteSpace(h));
            if (!string.IsNullOrWhiteSpace(fromHints))
                return fromHints;

            foreach (var token in EnumerateCurrencyTokens(currencyTokens))
            {
                var rawCurrency = ReadProjectedRawCurrency(token);
                if (!string.IsNullOrWhiteSpace(rawCurrency))
                    return rawCurrency;

                var isoCurrency = ReadProjectedCurrencyCode(token);
                if (!string.IsNullOrWhiteSpace(isoCurrency))
                    return isoCurrency;
            }

            return null;
        }

        private static string ReadNonEmpty(string value)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
        }

        private static void AddDistinct(List<string> target, string value)
        {
            var trimmed = ReadNonEmpty(value);
            if (trimmed == null || target == null)
                return;

            if (target.Any(existing => string.Equals(existing, trimmed, StringComparison.OrdinalIgnoreCase)))
                return;

            target.Add(trimmed);
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

            return new IND_ExternalServiceException(
                "Azure Document Intelligence",
                "El servicio de analisis OCR devolvio un error y no pudo completar la operacion.",
                IndErrorCodes.ExternalServiceUnavailable,
                HttpStatusCode.ServiceUnavailable,
                $"operation={operation} status={statusCode} detail={summary}".Trim());
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

        private enum ReceiptTotalCandidatePriority
        {
            None = 0,
            Generic = 1,
            PlainTotal = 2,
            Payable = 3
        }
    }
}
