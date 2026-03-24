using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Answers questions over structured record datasets using OpenAI Responses API.
    /// </summary>
    public sealed class IND_OpenAiDatasetAnswerService : IAiDatasetAnswerService
    {
        private const string DefaultModel = "gpt-5-mini";
        private const int DefaultTimeoutSeconds = 180;
        private const int DefaultMaxOutputTokens = 1200;
        private const int DefaultChunkMaxOutputTokens = 700;
        private const int DefaultDirectRecordLimit = 400;
        private const int DefaultChunkSize = 250;
        private const int DefaultMaxChunks = 24;
        private const string DefaultServiceTier = "priority";
        private const string DefaultProfileTag = "dataset-answer-v1";
        private const string DefaultPromptCacheKey = "dataset-answer-v1";
        private const string DefaultReasoningEffort = "minimal";
        private const string ResponsesUrl = "https://api.openai.com/v1/responses";
        private const string ModelSettingKey = "OpenAI:ExpenseSheetAskModel";
        private const string TimeoutSettingKey = "OpenAI:ExpenseSheetAskTimeoutSeconds";
        private const string MaxOutputTokensSettingKey = "OpenAI:ExpenseSheetAskMaxOutputTokens";
        private const string ChunkMaxOutputTokensSettingKey = "OpenAI:ExpenseSheetAskChunkMaxOutputTokens";
        private const string DirectRecordLimitSettingKey = "OpenAI:ExpenseSheetAskDirectRecordLimit";
        private const string ChunkSizeSettingKey = "OpenAI:ExpenseSheetAskChunkSize";
        private const string MaxChunksSettingKey = "OpenAI:ExpenseSheetAskMaxChunks";
        private const string ServiceTierSettingKey = "OpenAI:ExpenseSheetAskServiceTier";
        private const string ProfileTagSettingKey = "OpenAI:ExpenseSheetAskProfileTag";
        private const string PromptCacheKeySettingKey = "OpenAI:ExpenseSheetAskPromptCacheKey";
        private const string ReasoningEffortSettingKey = "OpenAI:ExpenseSheetAskReasoningEffort";

        private static readonly int TimeoutSeconds = ReadPositiveIntSetting(TimeoutSettingKey, DefaultTimeoutSeconds);
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private readonly IAxLogger _logger;
        private readonly string _model;
        private readonly int _maxOutputTokens;
        private readonly int _chunkMaxOutputTokens;
        private readonly int _directRecordLimit;
        private readonly int _chunkSize;
        private readonly int _maxChunks;
        private readonly string _serviceTier;
        private readonly string _profileTag;
        private readonly string _promptCacheKey;
        private readonly string _reasoningEffort;

        public IND_OpenAiDatasetAnswerService(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _model = ReadStringSetting(ModelSettingKey, DefaultModel);
            _maxOutputTokens = ReadPositiveIntSetting(MaxOutputTokensSettingKey, DefaultMaxOutputTokens, 256);
            _chunkMaxOutputTokens = ReadPositiveIntSetting(ChunkMaxOutputTokensSettingKey, DefaultChunkMaxOutputTokens, 256);
            _directRecordLimit = ReadPositiveIntSetting(DirectRecordLimitSettingKey, DefaultDirectRecordLimit, 50);
            _chunkSize = ReadPositiveIntSetting(ChunkSizeSettingKey, DefaultChunkSize, 50);
            _maxChunks = ReadPositiveIntSetting(MaxChunksSettingKey, DefaultMaxChunks, 1);
            _serviceTier = NormalizeServiceTier(ReadStringSetting(ServiceTierSettingKey, DefaultServiceTier));
            _profileTag = ReadStringSetting(ProfileTagSettingKey, DefaultProfileTag);
            _promptCacheKey = ReadStringSetting(PromptCacheKeySettingKey, DefaultPromptCacheKey);
            _reasoningEffort = NormalizeReasoningEffort(ReadStringSetting(ReasoningEffortSettingKey, DefaultReasoningEffort));
        }

        public async Task<AiDatasetAnswerResult> AnswerAsync(AiDatasetAnswerRequest request, CancellationToken cancellationToken)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));

            if (string.IsNullOrWhiteSpace(request.Question))
                throw new ArgumentException("question es obligatorio.", nameof(request));

            var warnings = new List<string>();
            if (request.Records == null || request.Records.Count == 0)
            {
                return new AiDatasetAnswerResult
                {
                    Answer = "No se encontraron registros para los filtros enviados.",
                    Model = _model,
                    RetrievalMode = "direct",
                    Truncated = false,
                    RecordsSentToModel = 0,
                    Warnings = warnings
                };
            }

            var effectiveRecords = request.Records;
            var truncated = false;
            var maxSupportedRecords = _chunkSize * _maxChunks;
            if (effectiveRecords.Count > maxSupportedRecords)
            {
                effectiveRecords = effectiveRecords.Take(maxSupportedRecords).ToList();
                truncated = true;
                warnings.Add("The dataset was trimmed to the safe chunk processing limit.");
            }

            if (effectiveRecords.Count <= _directRecordLimit)
            {
                var directResult = await AnswerDirectAsync(request, effectiveRecords, cancellationToken).ConfigureAwait(false);
                directResult.Truncated = directResult.Truncated || truncated;
                directResult.RecordsSentToModel = effectiveRecords.Count;
                directResult.Warnings = MergeWarnings(warnings, directResult.Warnings);
                return directResult;
            }

            var chunkedResult = await AnswerChunkedAsync(request, effectiveRecords, cancellationToken).ConfigureAwait(false);
            chunkedResult.Truncated = chunkedResult.Truncated || truncated;
            chunkedResult.RecordsSentToModel = effectiveRecords.Count;
            chunkedResult.Warnings = MergeWarnings(warnings, chunkedResult.Warnings);
            return chunkedResult;
        }

        private async Task<AiDatasetAnswerResult> AnswerDirectAsync(
            AiDatasetAnswerRequest request,
            List<AiDatasetRecord> records,
            CancellationToken cancellationToken)
        {
            var payload = await ExecuteStructuredRequestAsync(
                BuildDirectPrompt(request, records.Count),
                BuildRecordsEnvelopeJson(request.SourceKey, records, null),
                BuildFinalAnswerSchema(),
                "expense_sheet_answer_direct",
                _maxOutputTokens,
                cancellationToken).ConfigureAwait(false);

            return new AiDatasetAnswerResult
            {
                Answer = NormalizeText(payload["answer"]?.ToString(), "No fue posible generar una respuesta."),
                Model = _model,
                RetrievalMode = "direct",
                Truncated = false,
                RecordsSentToModel = records.Count,
                Warnings = ExtractStringList(payload["warnings"])
            };
        }

        private async Task<AiDatasetAnswerResult> AnswerChunkedAsync(
            AiDatasetAnswerRequest request,
            List<AiDatasetRecord> records,
            CancellationToken cancellationToken)
        {
            var chunkCount = (int)Math.Ceiling((double)records.Count / _chunkSize);
            var partialSummaries = new JArray();
            var warnings = new List<string>();

            for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunkRecords = records
                    .Skip(chunkIndex * _chunkSize)
                    .Take(_chunkSize)
                    .ToList();

                var payload = await ExecuteStructuredRequestAsync(
                    BuildChunkPrompt(request, chunkIndex + 1, chunkCount, chunkRecords.Count),
                    BuildRecordsEnvelopeJson(
                        request.SourceKey,
                        chunkRecords,
                        new JObject
                        {
                            ["chunkIndex"] = chunkIndex + 1,
                            ["chunkCount"] = chunkCount
                        }),
                    BuildChunkSummarySchema(),
                    "expense_sheet_answer_chunk",
                    _chunkMaxOutputTokens,
                    cancellationToken).ConfigureAwait(false);

                var summaryText = NormalizeText(payload["summary"]?.ToString(), string.Empty);
                if (!string.IsNullOrWhiteSpace(summaryText))
                {
                    partialSummaries.Add(new JObject
                    {
                        ["chunkIndex"] = chunkIndex + 1,
                        ["summary"] = summaryText,
                        ["relevantRecordIds"] = payload["relevantRecordIds"] ?? new JArray()
                    });
                }

                warnings = MergeWarnings(warnings, ExtractStringList(payload["warnings"]));
            }

            var finalPayload = await ExecuteStructuredRequestAsync(
                BuildFinalChunkPrompt(request, partialSummaries.Count),
                new JObject
                {
                    ["sourceKey"] = request.SourceKey ?? string.Empty,
                    ["question"] = request.Question ?? string.Empty,
                    ["partialSummaries"] = partialSummaries
                }.ToString(Formatting.None),
                BuildFinalAnswerSchema(),
                "expense_sheet_answer_final",
                _maxOutputTokens,
                cancellationToken).ConfigureAwait(false);

            return new AiDatasetAnswerResult
            {
                Answer = NormalizeText(finalPayload["answer"]?.ToString(), "No fue posible generar una respuesta."),
                Model = _model,
                RetrievalMode = "chunked",
                Truncated = false,
                RecordsSentToModel = records.Count,
                Warnings = MergeWarnings(warnings, ExtractStringList(finalPayload["warnings"]))
            };
        }

        private async Task<JObject> ExecuteStructuredRequestAsync(
            string prompt,
            string dataJson,
            JObject schema,
            string responseFormatName,
            int maxOutputTokens,
            CancellationToken cancellationToken)
        {
            var openAiApiKey = GetOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(openAiApiKey))
                throw new InvalidOperationException("OpenAI API key no esta configurada.");

            HttpResponseMessage response = null;
            string responseBody = null;
            var requestOptions = new DatasetAnswerRequestOptions
            {
                MaxOutputTokens = maxOutputTokens,
                ServiceTier = _serviceTier,
                PromptCacheKey = _promptCacheKey,
                ReasoningEffort = _reasoningEffort
            };
            var retriedWithoutServiceTier = false;
            var attempt = 0;

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                while (true)
                {
                    attempt++;
                    var payloadJson = BuildTextPayloadJson(prompt, dataJson, schema, responseFormatName, requestOptions);
                    _logger.Log(
                        "[OPENAI-DATASET] Request attempt=" + attempt.ToString(CultureInfo.InvariantCulture) +
                        " name=" + responseFormatName +
                        " model=" + _model +
                        " maxOut=" + requestOptions.MaxOutputTokens.ToString(CultureInfo.InvariantCulture) +
                        " tier=" + (requestOptions.ServiceTier ?? "auto"),
                        AxaptaSessionManager.LogLevel.Info);

                    response?.Dispose();
                    response = null;

                    using (var request = CreateRequestMessage(payloadJson, openAiApiKey))
                    {
                        response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken)
                            .ConfigureAwait(false);
                        responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    }

                    if (!response.IsSuccessStatusCode &&
                        !retriedWithoutServiceTier &&
                        ShouldRetryWithoutServiceTier(response.StatusCode, responseBody, requestOptions.ServiceTier))
                    {
                        retriedWithoutServiceTier = true;
                        requestOptions.ServiceTier = "auto";
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var summary = TryExtractOpenAiErrorSummary(responseBody);
                        var retryAfterSeconds = IND_OpenAiErrorHandling.GetRetryAfterSeconds(response);
                        _logger.Log(
                            "[OPENAI-DATASET] Failure status=" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                            " summary=" + summary,
                            AxaptaSessionManager.LogLevel.Warning);

                        if (IND_OpenAiErrorHandling.IsRateLimit(response.StatusCode, responseBody))
                        {
                            throw new IND_OpenAiRateLimitException(
                                "OpenAI rate limit exceeded while answering dataset question.",
                                retryAfterSeconds,
                                summary);
                        }

                        throw new Exception("Error en servicio de respuestas IA.");
                    }

                    var json = TryExtractOpenAiPayloadJson(responseBody);
                    if (string.IsNullOrWhiteSpace(json))
                        throw new Exception("OpenAI no devolvio un JSON valido para la respuesta.");

                    return JObject.Parse(json);
                }
            }
            finally
            {
                response?.Dispose();
            }
        }

        private string BuildTextPayloadJson(
            string prompt,
            string dataJson,
            JObject schema,
            string responseFormatName,
            DatasetAnswerRequestOptions requestOptions)
        {
            var format = new JObject
            {
                ["type"] = "json_schema",
                ["name"] = responseFormatName,
                ["schema"] = schema,
                ["strict"] = true
            };

            var payload = new JObject
            {
                ["model"] = _model,
                ["store"] = false,
                ["input"] = new JArray
                {
                    new JObject
                    {
                        ["role"] = "user",
                        ["content"] = new JArray
                        {
                            new JObject
                            {
                                ["type"] = "input_text",
                                ["text"] = prompt
                            },
                            new JObject
                            {
                                ["type"] = "input_text",
                                ["text"] = dataJson
                            }
                        }
                    }
                },
                ["text"] = new JObject
                {
                    ["format"] = format
                },
                ["max_output_tokens"] = requestOptions.MaxOutputTokens,
                ["metadata"] = new JObject
                {
                    ["dataset_answer_profile"] = _profileTag,
                    ["dataset_answer_requested_tier"] = requestOptions.ServiceTier ?? "auto",
                    ["dataset_answer_reasoning_effort"] = requestOptions.ReasoningEffort ?? "na"
                }
            };

            if (!string.IsNullOrWhiteSpace(requestOptions.ServiceTier))
                payload["service_tier"] = requestOptions.ServiceTier;

            if (!string.IsNullOrWhiteSpace(requestOptions.ReasoningEffort))
                payload["reasoning"] = new JObject { ["effort"] = requestOptions.ReasoningEffort };

            if (!string.IsNullOrWhiteSpace(requestOptions.PromptCacheKey))
                payload["prompt_cache_key"] = requestOptions.PromptCacheKey;

            return JsonConvert.SerializeObject(payload);
        }

        private static string BuildRecordsEnvelopeJson(string sourceKey, List<AiDatasetRecord> records, JObject metadata)
        {
            var payload = new JObject
            {
                ["sourceKey"] = sourceKey ?? string.Empty,
                ["records"] = new JArray(records.Select(record => ParseRecordJson(record?.JsonPayload)))
            };

            if (metadata != null)
                payload["metadata"] = metadata;

            return payload.ToString(Formatting.None);
        }

        private static JToken ParseRecordJson(string jsonPayload)
        {
            if (string.IsNullOrWhiteSpace(jsonPayload))
                return new JObject();

            try
            {
                return JToken.Parse(jsonPayload);
            }
            catch
            {
                return new JObject
                {
                    ["raw"] = jsonPayload
                };
            }
        }

        private static JObject BuildFinalAnswerSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["answer"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["warnings"] = new JObject
                    {
                        ["type"] = new JArray("array", "null"),
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    }
                },
                ["required"] = new JArray("answer", "warnings")
            };
        }

        private static JObject BuildChunkSummarySchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["summary"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["warnings"] = new JObject
                    {
                        ["type"] = new JArray("array", "null"),
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    },
                    ["relevantRecordIds"] = new JObject
                    {
                        ["type"] = new JArray("array", "null"),
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    }
                },
                ["required"] = new JArray("summary", "warnings", "relevantRecordIds")
            };
        }

        private static List<string> ExtractStringList(JToken token)
        {
            var items = new List<string>();
            if (!(token is JArray array))
                return items;

            foreach (var value in array)
            {
                var text = NormalizeText(value?.ToString(), null);
                if (!string.IsNullOrWhiteSpace(text))
                    items.Add(text);
            }

            return items;
        }

        private static List<string> MergeWarnings(List<string> first, List<string> second)
        {
            return (first ?? new List<string>())
                .Concat(second ?? new List<string>())
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string BuildDirectPrompt(AiDatasetAnswerRequest request, int recordCount)
        {
            return string.Join("\n", new[]
            {
                "You are answering a business question from structured records.",
                "Use only the provided data.",
                "Do not invent fields, values, or conclusions.",
                "If the data is not enough, say so clearly.",
                "Prefer exact amounts and exact counts when the data supports them.",
                "Keep the answer concise and useful.",
                "Question: " + (request.Question ?? string.Empty),
                "Answer instructions: " + NormalizeText(request.AnswerInstructions, "None"),
                "Record count: " + recordCount.ToString(CultureInfo.InvariantCulture),
                "The next input_text contains JSON with the records to analyze."
            });
        }

        private static string BuildChunkPrompt(AiDatasetAnswerRequest request, int chunkIndex, int chunkCount, int recordCount)
        {
            return string.Join("\n", new[]
            {
                "You are summarizing one chunk of structured records for a later final answer.",
                "Use only this chunk.",
                "Focus only on facts relevant to the question.",
                "If the chunk does not help, say so in the summary.",
                "Question: " + (request.Question ?? string.Empty),
                "Answer instructions: " + NormalizeText(request.AnswerInstructions, "None"),
                "Chunk: " + chunkIndex.ToString(CultureInfo.InvariantCulture) + "/" + chunkCount.ToString(CultureInfo.InvariantCulture),
                "Chunk record count: " + recordCount.ToString(CultureInfo.InvariantCulture),
                "The next input_text contains JSON with the current chunk records."
            });
        }

        private static string BuildFinalChunkPrompt(AiDatasetAnswerRequest request, int chunkSummaryCount)
        {
            return string.Join("\n", new[]
            {
                "You are merging chunk summaries into one final business answer.",
                "Use only the provided chunk summaries.",
                "Do not invent data that is not present in the summaries.",
                "If the summaries are not enough, say so clearly.",
                "Question: " + (request.Question ?? string.Empty),
                "Answer instructions: " + NormalizeText(request.AnswerInstructions, "None"),
                "Chunk summary count: " + chunkSummaryCount.ToString(CultureInfo.InvariantCulture),
                "The next input_text contains JSON with the chunk summaries."
            });
        }

        private static HttpClient CreateHttpClient()
        {
            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(TimeoutSeconds)
            };
            client.DefaultRequestHeaders.Accept.Clear();
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            return client;
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

        private static HttpRequestMessage CreateRequestMessage(string payloadJson, string openAiApiKey)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, ResponsesUrl);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);
            request.Headers.UserAgent.Clear();
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("IND_CRM_API", "1.0"));
            request.Headers.ExpectContinue = false;
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");
            return request;
        }

        private static int ReadPositiveIntSetting(string settingKey, int fallbackValue, int minValue = 1)
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(settingKey);
                if (int.TryParse(value, out var parsed) && parsed >= minValue)
                    return parsed;
            }
            catch
            {
                // Ignore and use fallback.
            }

            return fallbackValue;
        }

        private static string ReadStringSetting(string settingKey, string fallbackValue)
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(settingKey);
                return string.IsNullOrWhiteSpace(configured) ? fallbackValue : configured.Trim();
            }
            catch
            {
                return fallbackValue;
            }
        }

        private static string NormalizeServiceTier(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = value.Trim().ToLowerInvariant();
            return normalized == "auto" || normalized == "priority" ? normalized : null;
        }

        private static string NormalizeReasoningEffort(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DefaultReasoningEffort;

            var normalized = value.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "minimal":
                case "low":
                case "medium":
                case "high":
                    return normalized;
                default:
                    return DefaultReasoningEffort;
            }
        }

        private static string NormalizeText(string value, string fallbackValue)
        {
            var trimmed = string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? fallbackValue : trimmed;
        }

        private static bool ShouldRetryWithoutServiceTier(HttpStatusCode statusCode, string responseBody, string requestedServiceTier)
        {
            if (string.IsNullOrWhiteSpace(requestedServiceTier) ||
                string.Equals(requestedServiceTier, "auto", StringComparison.OrdinalIgnoreCase))
                return false;

            if (statusCode != HttpStatusCode.BadRequest && (int)statusCode != 422)
                return false;

            var summary = TryExtractOpenAiErrorSummary(responseBody);
            if (string.IsNullOrWhiteSpace(summary))
                summary = responseBody ?? string.Empty;

            return summary.IndexOf("service_tier", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   summary.IndexOf("priority", StringComparison.OrdinalIgnoreCase) >= 0;
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
                var message = err["message"]?.ToString();
                return string.Join(" ", new[] { type, code, message }.Where(part => !string.IsNullOrWhiteSpace(part)));
            }
            catch
            {
                return string.Empty;
            }
        }

        private static string TryExtractOpenAiPayloadJson(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var root = JObject.Parse(responseBody);
                var direct = root["output_text"]?.ToString();
                if (!string.IsNullOrWhiteSpace(direct))
                    return TrimJsonBlock(direct);

                var output = root["output"] as JArray;
                if (output != null)
                {
                    foreach (var item in output)
                    {
                        var content = item["content"] as JArray;
                        if (content == null)
                            continue;

                        foreach (var part in content)
                        {
                            var type = part["type"]?.ToString();
                            if (!string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase) &&
                                !string.Equals(type, "text", StringComparison.OrdinalIgnoreCase))
                                continue;

                            var text = part["text"]?.ToString();
                            var extracted = TrimJsonBlock(text);
                            if (!string.IsNullOrWhiteSpace(extracted))
                                return extracted;
                        }
                    }
                }

                return TrimJsonBlock(responseBody);
            }
            catch
            {
                return null;
            }
        }

        private static string TrimJsonBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim();
            if (trimmed.StartsWith("```json", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(7).Trim();
            else if (trimmed.StartsWith("```", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(3).Trim();

            if (trimmed.EndsWith("```", StringComparison.OrdinalIgnoreCase))
                trimmed = trimmed.Substring(0, trimmed.Length - 3).Trim();

            return trimmed;
        }

        private sealed class DatasetAnswerRequestOptions
        {
            public int MaxOutputTokens { get; set; }

            public string ServiceTier { get; set; }

            public string PromptCacheKey { get; set; }

            public string ReasoningEffort { get; set; }
        }
    }
}
