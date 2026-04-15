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
using System.Text.RegularExpressions;
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
        private const int DefaultMaxOutputTokens = 2200;
        private const int DefaultChunkMaxOutputTokens = 1200;
        private const int DefaultDirectRecordLimit = 400;
        private const int DefaultChunkSize = 250;
        private const int DefaultMaxChunks = 24;
        private const string DefaultServiceTier = "priority";
        private const string DefaultProfileTag = "dataset-answer-v1";
        private const string DefaultPromptCacheKey = "dataset-answer-v1";
        private const string DefaultReasoningEffort = "minimal";
        private const string StructuredAnswerSchemaVersion = "expense-chat-v2";
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
                return BuildNoRecordsResult(request);

            var effectiveRecords = request.Records;
            var truncated = false;
            var maxSupportedRecords = _chunkSize * _maxChunks;
            if (effectiveRecords.Count > maxSupportedRecords)
            {
                effectiveRecords = effectiveRecords.Take(maxSupportedRecords).ToList();
                truncated = true;
                warnings.Add(BuildLocalizedWarning(request, "dataset-trimmed"));
            }

            try
            {
                var result = effectiveRecords.Count <= _directRecordLimit
                    ? await AnswerDirectAsync(request, effectiveRecords, warnings, cancellationToken).ConfigureAwait(false)
                    : await AnswerChunkedAsync(request, effectiveRecords, warnings, cancellationToken).ConfigureAwait(false);

                result.Truncated = result.Truncated || truncated;
                result.RecordsSentToModel = effectiveRecords.Count;
                result.Warnings = MergeWarnings(warnings, result.Warnings);
                result.Answer = ReplaceWarningsInStructuredAnswer(result.Answer, request, result.Warnings);
                return result;
            }
            catch (IND_OpenAiRateLimitException)
            {
                throw;
            }
            catch (Exception ex)
            {
                var retrievalMode = effectiveRecords.Count <= _directRecordLimit ? "direct" : "chunked";
                var fallbackWarnings = MergeWarnings(warnings, new List<string>
                {
                    BuildLocalizedWarning(request, "safe-fallback")
                });

                _logger.Log(
                    "[OPENAI-DATASET] Falling back to structured markdown retrievalMode=" + retrievalMode +
                    " locale=" + ResolveRequestLocale(request) +
                    " fallbackKind=execution-failed" +
                    " reason=" + ex.Message,
                    AxaptaSessionManager.LogLevel.Warning);

                return BuildSafeFallbackResult(request, retrievalMode, effectiveRecords.Count, fallbackWarnings, "execution-failed");
            }
        }

        private async Task<AiDatasetAnswerResult> AnswerDirectAsync(
            AiDatasetAnswerRequest request,
            List<AiDatasetRecord> records,
            List<string> baseWarnings,
            CancellationToken cancellationToken)
        {
            var visualizationRequested = IsVisualizationRequested(request);
            var payload = await ExecuteStructuredRequestAsync(
                BuildStructuredAnswerInstructions(false, visualizationRequested),
                BuildDirectInputJson(request, records),
                BuildFinalAnswerSchema(visualizationRequested),
                visualizationRequested ? "expense_sheet_answer_direct" : "expense_sheet_answer_direct_markdown",
                _maxOutputTokens,
                cancellationToken).ConfigureAwait(false);

            return BuildStructuredResultFromPayload(payload, request, "direct", records.Count, baseWarnings);
        }

        private async Task<AiDatasetAnswerResult> AnswerChunkedAsync(
            AiDatasetAnswerRequest request,
            List<AiDatasetRecord> records,
            List<string> baseWarnings,
            CancellationToken cancellationToken)
        {
            var visualizationRequested = IsVisualizationRequested(request);
            var chunkCount = (int)Math.Ceiling((double)records.Count / _chunkSize);
            var partialSummaries = new JArray();
            var warnings = MergeWarnings(baseWarnings, null);

            for (var chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
            {
                var chunkRecords = records
                    .Skip(chunkIndex * _chunkSize)
                    .Take(_chunkSize)
                    .ToList();

                var payload = await ExecuteStructuredRequestAsync(
                    BuildChunkSummaryInstructions(),
                    BuildChunkInputJson(request, chunkRecords, chunkIndex + 1, chunkCount),
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
                        ["relevantRecordIds"] = NormalizeStringArrayToken(payload["relevantRecordIds"])
                    });
                }

                warnings = MergeWarnings(warnings, ExtractStringList(payload["warnings"]));
            }

            var finalPayload = await ExecuteStructuredRequestAsync(
                BuildStructuredAnswerInstructions(true, visualizationRequested),
                BuildFinalChunkInputJson(request, partialSummaries),
                BuildFinalAnswerSchema(visualizationRequested),
                visualizationRequested ? "expense_sheet_answer_final" : "expense_sheet_answer_final_markdown",
                _maxOutputTokens,
                cancellationToken).ConfigureAwait(false);

            return BuildStructuredResultFromPayload(finalPayload, request, "chunked", records.Count, warnings);
        }

        private AiDatasetAnswerResult BuildStructuredResultFromPayload(
            JObject payload,
            AiDatasetAnswerRequest request,
            string retrievalMode,
            int recordsSentToModel,
            List<string> baseWarnings)
        {
            var validation = ValidateStructuredAnswerPayload(payload, request);
            if (!validation.IsValid)
            {
                var fallbackWarnings = MergeWarnings(baseWarnings, validation.Warnings);
                fallbackWarnings = MergeWarnings(fallbackWarnings, new List<string>
                {
                    BuildLocalizedWarning(request, "invalid-structured-output")
                });

                _logger.Log(
                    "[OPENAI-DATASET] Structured validation failed retrievalMode=" + retrievalMode +
                    " locale=" + ResolveRequestLocale(request) +
                    " errors=" + string.Join(" | ", validation.Errors ?? new List<string>()),
                    AxaptaSessionManager.LogLevel.Warning);

                return BuildSafeFallbackResult(request, retrievalMode, recordsSentToModel, fallbackWarnings, "invalid-structured-output");
            }

            var mergedWarnings = MergeWarnings(baseWarnings, validation.Warnings);
            if (string.Equals(retrievalMode, "chunked", StringComparison.OrdinalIgnoreCase) &&
                ContainsVisualizationMessage(validation.Payload))
            {
                mergedWarnings = MergeWarnings(mergedWarnings, new List<string>
                {
                    BuildLocalizedWarning(request, "chunked-visualization-blocked")
                });

                _logger.Log(
                    "[OPENAI-DATASET] Visualization blocked retrievalMode=chunked locale=" + ResolveRequestLocale(request) +
                    " reason=exact-final-rows-unavailable",
                    AxaptaSessionManager.LogLevel.Warning);

                return BuildSafeFallbackResult(request, retrievalMode, recordsSentToModel, mergedWarnings, "chunked-visualization-blocked");
            }

            validation.Payload["warnings"] = BuildWarningsToken(mergedWarnings);

            return new AiDatasetAnswerResult
            {
                Answer = SerializeStructuredAnswerPayload(validation.Payload),
                Model = _model,
                RetrievalMode = retrievalMode,
                Truncated = false,
                RecordsSentToModel = recordsSentToModel,
                Warnings = mergedWarnings
            };
        }

        private AiDatasetAnswerResult BuildNoRecordsResult(AiDatasetAnswerRequest request)
        {
            return new AiDatasetAnswerResult
            {
                Answer = SerializeStructuredAnswerPayload(BuildSafeMarkdownFallback(request, "no-records", null)),
                Model = _model,
                RetrievalMode = "direct",
                Truncated = false,
                RecordsSentToModel = 0,
                Warnings = new List<string>()
            };
        }

        private AiDatasetAnswerResult BuildSafeFallbackResult(
            AiDatasetAnswerRequest request,
            string retrievalMode,
            int recordsSentToModel,
            List<string> warnings,
            string fallbackKind)
        {
            return new AiDatasetAnswerResult
            {
                Answer = SerializeStructuredAnswerPayload(BuildSafeMarkdownFallback(request, fallbackKind, warnings)),
                Model = _model,
                RetrievalMode = retrievalMode,
                Truncated = false,
                RecordsSentToModel = recordsSentToModel,
                Warnings = MergeWarnings(warnings, null)
            };
        }

        private async Task<JObject> ExecuteStructuredRequestAsync(
            string instructions,
            string inputJson,
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
                    var payloadJson = BuildTextPayloadJson(instructions, inputJson, schema, responseFormatName, requestOptions);
                    _logger.Log(
                        "[OPENAI-DATASET] Request attempt=" + attempt.ToString(CultureInfo.InvariantCulture) +
                        " name=" + responseFormatName +
                        " schemaKind=" + DescribeResponseSchemaKind(responseFormatName) +
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
                        var invalidSchemaContext = TryExtractInvalidSchemaContext(summary);
                        var invalidSchemaMissingProperty = TryExtractInvalidSchemaMissingProperty(summary);
                        _logger.Log(
                            "[OPENAI-DATASET] Failure status=" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture) +
                            " name=" + responseFormatName +
                            " schemaKind=" + DescribeResponseSchemaKind(responseFormatName) +
                            " invalidSchemaContext=" + (string.IsNullOrWhiteSpace(invalidSchemaContext) ? "-" : invalidSchemaContext) +
                            " missingProperty=" + (string.IsNullOrWhiteSpace(invalidSchemaMissingProperty) ? "-" : invalidSchemaMissingProperty) +
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
            string instructions,
            string inputJson,
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
                ["instructions"] = instructions,
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
                                ["text"] = inputJson
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

        private static string BuildDirectInputJson(AiDatasetAnswerRequest request, List<AiDatasetRecord> records)
        {
            var payload = new JObject
            {
                ["question"] = NormalizeText(request?.Question, string.Empty),
                ["answerInstructions"] = NormalizeText(request?.AnswerInstructions, null),
                ["context"] = new JObject
                {
                    ["mode"] = "direct",
                    ["sourceKey"] = request?.SourceKey ?? string.Empty,
                    ["recordCount"] = records?.Count ?? 0
                },
                ["dataset"] = BuildRecordsEnvelope(request?.SourceKey, records, null)
            };

            return payload.ToString(Formatting.None);
        }

        private static string BuildChunkInputJson(
            AiDatasetAnswerRequest request,
            List<AiDatasetRecord> records,
            int chunkIndex,
            int chunkCount)
        {
            var payload = new JObject
            {
                ["question"] = NormalizeText(request?.Question, string.Empty),
                ["answerInstructions"] = NormalizeText(request?.AnswerInstructions, null),
                ["context"] = new JObject
                {
                    ["mode"] = "chunk-summary",
                    ["sourceKey"] = request?.SourceKey ?? string.Empty,
                    ["chunkIndex"] = chunkIndex,
                    ["chunkCount"] = chunkCount,
                    ["recordCount"] = records?.Count ?? 0
                },
                ["dataset"] = BuildRecordsEnvelope(
                    request?.SourceKey,
                    records,
                    new JObject
                    {
                        ["chunkIndex"] = chunkIndex,
                        ["chunkCount"] = chunkCount
                    })
            };

            return payload.ToString(Formatting.None);
        }

        private static string BuildFinalChunkInputJson(AiDatasetAnswerRequest request, JArray partialSummaries)
        {
            var payload = new JObject
            {
                ["question"] = NormalizeText(request?.Question, string.Empty),
                ["answerInstructions"] = NormalizeText(request?.AnswerInstructions, null),
                ["context"] = new JObject
                {
                    ["mode"] = "chunked-final",
                    ["sourceKey"] = request?.SourceKey ?? string.Empty,
                    ["chunkSummaryCount"] = partialSummaries?.Count ?? 0,
                    ["hasFullDataset"] = false
                },
                ["chunkSummaries"] = partialSummaries ?? new JArray()
            };

            return payload.ToString(Formatting.None);
        }

        private static JObject BuildRecordsEnvelope(string sourceKey, List<AiDatasetRecord> records, JObject metadata)
        {
            var payload = new JObject
            {
                ["sourceKey"] = sourceKey ?? string.Empty,
                ["records"] = new JArray((records ?? new List<AiDatasetRecord>()).Select(record => ParseRecordJson(record?.JsonPayload)))
            };

            if (metadata != null)
                payload["metadata"] = metadata;

            return payload;
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

        // Builds the exact final UI contract enforced by Structured Outputs.
        private static JObject BuildFinalAnswerSchema(bool visualizationRequested)
        {
            return visualizationRequested
                ? BuildVisualizationAnswerSchema()
                : BuildMarkdownOnlyAnswerSchema();
        }

        private static JObject BuildVisualizationAnswerSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["schemaVersion"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray(StructuredAnswerSchemaVersion)
                    },
                    ["messages"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = BuildMessageSchema()
                    },
                    ["warnings"] = BuildStringArrayOrNullSchema()
                },
                ["required"] = new JArray("schemaVersion", "messages", "warnings")
            };
        }

        private static JObject BuildMarkdownOnlyAnswerSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["schemaVersion"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray(StructuredAnswerSchemaVersion)
                    },
                    ["messages"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["maxItems"] = 1,
                        ["items"] = BuildMarkdownMessageSchema()
                    },
                    ["warnings"] = BuildStringArrayOrNullSchema()
                },
                ["required"] = new JArray("schemaVersion", "messages", "warnings")
            };
        }

        private static JObject BuildMessageSchema()
        {
            return new JObject
            {
                ["anyOf"] = new JArray
                {
                    BuildMarkdownMessageSchema(),
                    BuildChartMessageSchema(),
                    BuildTableMessageSchema(),
                    BuildPickerMessageSchema()
                }
            };
        }

        private static JObject BuildMarkdownMessageSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["type"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("markdown")
                    },
                    ["markdown"] = new JObject
                    {
                        ["type"] = "string"
                    }
                },
                ["required"] = new JArray("type", "markdown")
            };
        }

        private static JObject BuildChartMessageSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["type"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("chart")
                    },
                    ["payload"] = new JObject
                    {
                        ["anyOf"] = new JArray
                        {
                            BuildBarOrLineChartPayloadSchema("bar"),
                            BuildBarOrLineChartPayloadSchema("line"),
                            BuildPieChartPayloadSchema()
                        }
                    }
                },
                ["required"] = new JArray("type", "payload")
            };
        }

        private static JObject BuildTableMessageSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["type"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("table")
                    },
                    ["payload"] = BuildTablePayloadSchema()
                },
                ["required"] = new JArray("type", "payload")
            };
        }

        private static JObject BuildPickerMessageSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["type"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("question-to-choose-chart-type")
                    },
                    ["question"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["originalPrompt"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["options"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 4,
                        ["maxItems"] = 4,
                        ["items"] = BuildPickerOptionSchema()
                    },
                    ["selectedType"] = new JObject
                    {
                        ["type"] = new JArray("string", "null"),
                        ["enum"] = new JArray("bar", "line", "pie", "table", null)
                    }
                },
                ["required"] = new JArray("type", "question", "originalPrompt", "options", "selectedType")
            };
        }

        private static JObject BuildBarOrLineChartPayloadSchema(string chartType)
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["chartType"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray(chartType)
                    },
                    ["title"] = BuildNullableStringSchema(),
                    ["subtitle"] = BuildNullableStringSchema(),
                    ["emptyStateLabel"] = BuildNullableStringSchema(),
                    ["data"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = BuildChartDatumSchema()
                    },
                    ["xKey"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["yKey"] = new JObject
                    {
                        ["type"] = "string"
                    }
                },
                ["required"] = new JArray("chartType", "title", "subtitle", "emptyStateLabel", "data", "xKey", "yKey")
            };
        }

        private static JObject BuildPieChartPayloadSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["chartType"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("pie")
                    },
                    ["title"] = BuildNullableStringSchema(),
                    ["subtitle"] = BuildNullableStringSchema(),
                    ["emptyStateLabel"] = BuildNullableStringSchema(),
                    ["data"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["maxItems"] = 6,
                        ["items"] = BuildChartDatumSchema()
                    },
                    ["nameKey"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["dataKey"] = new JObject
                    {
                        ["type"] = "string"
                    }
                },
                ["required"] = new JArray("chartType", "title", "subtitle", "emptyStateLabel", "data", "nameKey", "dataKey")
            };
        }

        private static JObject BuildChartDatumSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JObject
                {
                    ["type"] = new JArray("string", "number", "null")
                }
            };
        }

        private static JObject BuildTablePayloadSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["title"] = BuildNullableStringSchema(),
                    ["subtitle"] = BuildNullableStringSchema(),
                    ["emptyStateLabel"] = BuildNullableStringSchema(),
                    ["columns"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = BuildTableColumnSchema()
                    },
                    ["rows"] = new JObject
                    {
                        ["type"] = "array",
                        ["items"] = BuildTableRowSchema()
                    }
                },
                ["required"] = new JArray("title", "subtitle", "emptyStateLabel", "columns", "rows")
            };
        }

        private static JObject BuildTableColumnSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["key"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["header"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["align"] = BuildNullableEnumSchema("left", "center", "right")
                },
                ["required"] = new JArray("key", "header", "align")
            };
        }

        private static JObject BuildTableRowSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = new JObject
                {
                    ["type"] = new JArray("string", "number", "boolean", "null")
                }
            };
        }

        private static JObject BuildPickerOptionSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["value"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("bar", "line", "pie", "table")
                    },
                    ["label"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["description"] = BuildNullableStringSchema()
                },
                ["required"] = new JArray("value", "label", "description")
            };
        }

        // OpenAI strict schemas require every declared property to be present.
        private static JObject BuildNullableStringSchema()
        {
            return new JObject
            {
                ["type"] = new JArray("string", "null")
            };
        }

        // Enum-like optional fields stay deterministic by accepting null explicitly.
        private static JObject BuildNullableEnumSchema(params string[] values)
        {
            var enumValues = new JArray();
            foreach (var value in values ?? new string[0])
                enumValues.Add(value);

            enumValues.Add(JValue.CreateNull());

            return new JObject
            {
                ["type"] = new JArray("string", "null"),
                ["enum"] = enumValues
            };
        }

        private static JObject BuildStringArrayOrNullSchema()
        {
            return new JObject
            {
                ["type"] = new JArray("array", "null"),
                ["items"] = new JObject
                {
                    ["type"] = "string"
                }
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
                    ["warnings"] = BuildStringArrayOrNullSchema(),
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

        // Validates and normalizes the assistant payload before it reaches the frontend.
        private static StructuredAnswerValidationOutcome ValidateStructuredAnswerPayload(JObject payload, AiDatasetAnswerRequest request)
        {
            var outcome = new StructuredAnswerValidationOutcome
            {
                Payload = null,
                Warnings = new List<string>(),
                Errors = new List<string>()
            };

            if (payload == null)
            {
                outcome.Errors.Add("The payload was empty.");
                return outcome;
            }

            outcome.Warnings = MergeWarnings(outcome.Warnings, ExtractStringList(payload["warnings"]));

            var schemaVersion = NormalizeText(payload["schemaVersion"]?.ToString(), null);
            if (!string.Equals(schemaVersion, StructuredAnswerSchemaVersion, StringComparison.Ordinal))
            {
                outcome.Warnings = MergeWarnings(outcome.Warnings, new List<string>
                {
                    BuildLocalizedWarning(request, "schema-normalized")
                });
            }

            var messagesToken = payload["messages"] as JArray;
            if (messagesToken == null || messagesToken.Count == 0)
            {
                outcome.Errors.Add("messages must contain at least one message.");
                return outcome;
            }

            var normalizedMessages = new JArray();
            for (var index = 0; index < messagesToken.Count; index++)
            {
                var normalizedMessage = NormalizeStructuredMessage(messagesToken[index], index, outcome.Errors, request);
                if (normalizedMessage != null)
                    normalizedMessages.Add(normalizedMessage);
            }

            if (outcome.Errors.Count > 0)
                return outcome;

            outcome.Payload = new JObject
            {
                ["schemaVersion"] = StructuredAnswerSchemaVersion,
                ["messages"] = normalizedMessages,
                ["warnings"] = BuildWarningsToken(outcome.Warnings)
            };

            return outcome;
        }

        private static JObject NormalizeStructuredMessage(JToken token, int messageIndex, List<string> errors, AiDatasetAnswerRequest request)
        {
            var message = token as JObject;
            if (message == null)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " must be an object.");
                return null;
            }

            var type = NormalizeText(message["type"]?.ToString(), null);
            if (string.IsNullOrWhiteSpace(type))
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " requires type.");
                return null;
            }

            switch (type)
            {
                case "markdown":
                    return NormalizeMarkdownMessage(message, messageIndex, errors);
                case "chart":
                    return NormalizeChartMessage(message, messageIndex, errors);
                case "table":
                    return NormalizeTableMessage(message, messageIndex, errors);
                case "question-to-choose-chart-type":
                    return NormalizePickerMessage(message, messageIndex, errors, request);
                default:
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " has unsupported type '" + type + "'.");
                    return null;
            }
        }

        private static JObject NormalizeMarkdownMessage(JObject message, int messageIndex, List<string> errors)
        {
            string markdown;
            if (!TryReadRequiredString(message, "markdown", messageIndex, errors, out markdown))
                return null;

            return new JObject
            {
                ["type"] = "markdown",
                ["markdown"] = markdown
            };
        }

        private static JObject NormalizeChartMessage(JObject message, int messageIndex, List<string> errors)
        {
            var payload = message["payload"] as JObject;
            if (payload == null)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chart requires payload.");
                return null;
            }

            string chartType;
            if (!TryReadRequiredString(payload, "chartType", messageIndex, errors, out chartType))
                return null;

            chartType = chartType.Trim().ToLowerInvariant();
            JObject normalizedPayload;
            switch (chartType)
            {
                case "bar":
                case "line":
                    normalizedPayload = NormalizeBarOrLineChartPayload(payload, chartType, messageIndex, errors);
                    break;
                case "pie":
                    normalizedPayload = NormalizePieChartPayload(payload, messageIndex, errors);
                    break;
                default:
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chartType '" + chartType + "' is not supported.");
                    return null;
            }

            if (normalizedPayload == null)
                return null;

            return new JObject
            {
                ["type"] = "chart",
                ["payload"] = normalizedPayload
            };
        }

        private static JObject NormalizeBarOrLineChartPayload(JObject payload, string chartType, int messageIndex, List<string> errors)
        {
            string xKey;
            string yKey;
            if (!TryReadRequiredString(payload, "xKey", messageIndex, errors, out xKey) ||
                !TryReadRequiredString(payload, "yKey", messageIndex, errors, out yKey))
            {
                return null;
            }

            var normalizedData = NormalizeChartDataArray(payload["data"], messageIndex, errors);
            if (normalizedData == null)
                return null;

            for (var index = 0; index < normalizedData.Count; index++)
            {
                var row = normalizedData[index] as JObject;
                if (row == null)
                    continue;

                if (row.Property(xKey, StringComparison.Ordinal) == null)
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chart row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " is missing xKey '" + xKey + "'.");

                if (row.Property(yKey, StringComparison.Ordinal) == null)
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chart row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " is missing yKey '" + yKey + "'.");
                    continue;
                }

                decimal numericValue;
                if (!TryReadNumericValue(row[yKey], out numericValue))
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chart row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " has non-numeric yKey '" + yKey + "'.");
                    continue;
                }

                row[yKey] = CreateNumericJValue(numericValue);
            }

            if (errors.Count > 0)
                return null;

            return BuildNormalizedChartPayload(payload, chartType, normalizedData, xKey, yKey, null, null, errors, messageIndex);
        }

        private static JObject NormalizePieChartPayload(JObject payload, int messageIndex, List<string> errors)
        {
            string nameKey;
            string dataKey;
            if (!TryReadRequiredString(payload, "nameKey", messageIndex, errors, out nameKey) ||
                !TryReadRequiredString(payload, "dataKey", messageIndex, errors, out dataKey))
            {
                return null;
            }

            var normalizedData = NormalizeChartDataArray(payload["data"], messageIndex, errors);
            if (normalizedData == null)
                return null;

            if (normalizedData.Count > 6)
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " pie charts support at most 6 categories.");

            for (var index = 0; index < normalizedData.Count; index++)
            {
                var row = normalizedData[index] as JObject;
                if (row == null)
                    continue;

                if (row.Property(nameKey, StringComparison.Ordinal) == null)
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " pie row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " is missing nameKey '" + nameKey + "'.");

                if (row.Property(dataKey, StringComparison.Ordinal) == null)
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " pie row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " is missing dataKey '" + dataKey + "'.");
                    continue;
                }

                decimal numericValue;
                if (!TryReadNumericValue(row[dataKey], out numericValue))
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " pie row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " has non-numeric dataKey '" + dataKey + "'.");
                    continue;
                }

                if (numericValue < 0m)
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " pie charts do not support negative values.");
                    continue;
                }

                row[dataKey] = CreateNumericJValue(numericValue);
            }

            if (errors.Count > 0)
                return null;

            return BuildNormalizedChartPayload(payload, "pie", normalizedData, null, null, nameKey, dataKey, errors, messageIndex);
        }

        private static JObject BuildNormalizedChartPayload(
            JObject sourcePayload,
            string chartType,
            JArray normalizedData,
            string xKey,
            string yKey,
            string nameKey,
            string dataKey,
            List<string> errors,
            int messageIndex)
        {
            string title;
            string subtitle;
            string emptyStateLabel;
            if (!TryReadOptionalString(sourcePayload, "title", messageIndex, errors, out title) ||
                !TryReadOptionalString(sourcePayload, "subtitle", messageIndex, errors, out subtitle) ||
                !TryReadOptionalString(sourcePayload, "emptyStateLabel", messageIndex, errors, out emptyStateLabel))
            {
                return null;
            }

            if (errors.Count > 0)
                return null;

            var normalizedPayload = new JObject
            {
                ["chartType"] = chartType,
                ["data"] = normalizedData
            };

            AddOptionalString(normalizedPayload, "title", title);
            AddOptionalString(normalizedPayload, "subtitle", subtitle);
            AddOptionalString(normalizedPayload, "emptyStateLabel", emptyStateLabel);

            if (!string.IsNullOrWhiteSpace(xKey))
                normalizedPayload["xKey"] = xKey;
            if (!string.IsNullOrWhiteSpace(yKey))
                normalizedPayload["yKey"] = yKey;
            if (!string.IsNullOrWhiteSpace(nameKey))
                normalizedPayload["nameKey"] = nameKey;
            if (!string.IsNullOrWhiteSpace(dataKey))
                normalizedPayload["dataKey"] = dataKey;

            return normalizedPayload;
        }

        private static JArray NormalizeChartDataArray(JToken token, int messageIndex, List<string> errors)
        {
            var array = token as JArray;
            if (array == null || array.Count == 0)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chart requires a non-empty data array.");
                return null;
            }

            var normalized = new JArray();
            for (var index = 0; index < array.Count; index++)
            {
                var row = array[index] as JObject;
                if (row == null)
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chart row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " must be an object.");
                    continue;
                }

                var normalizedRow = new JObject();
                foreach (var property in row.Properties())
                {
                    if (!IsAllowedChartValue(property.Value))
                    {
                        errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " chart row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " contains unsupported value type for '" + property.Name + "'.");
                        continue;
                    }

                    normalizedRow[property.Name] = property.Value.DeepClone();
                }

                normalized.Add(normalizedRow);
            }

            return errors.Count > 0 ? null : normalized;
        }

        private static JObject NormalizeTableMessage(JObject message, int messageIndex, List<string> errors)
        {
            var payload = message["payload"] as JObject;
            if (payload == null)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table requires payload.");
                return null;
            }

            var normalizedPayload = NormalizeTablePayload(payload, messageIndex, errors);
            if (normalizedPayload == null)
                return null;

            return new JObject
            {
                ["type"] = "table",
                ["payload"] = normalizedPayload
            };
        }

        private static JObject NormalizeTablePayload(JObject payload, int messageIndex, List<string> errors)
        {
            string title;
            string subtitle;
            string emptyStateLabel;
            if (!TryReadOptionalString(payload, "title", messageIndex, errors, out title) ||
                !TryReadOptionalString(payload, "subtitle", messageIndex, errors, out subtitle) ||
                !TryReadOptionalString(payload, "emptyStateLabel", messageIndex, errors, out emptyStateLabel))
            {
                return null;
            }

            var columnsToken = payload["columns"] as JArray;
            if (columnsToken == null || columnsToken.Count == 0)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table requires a non-empty columns array.");
                return null;
            }

            var normalizedColumns = new JArray();
            var columnKeys = new List<string>();
            for (var index = 0; index < columnsToken.Count; index++)
            {
                var column = columnsToken[index] as JObject;
                if (column == null)
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table column " + (index + 1).ToString(CultureInfo.InvariantCulture) + " must be an object.");
                    continue;
                }

                string key;
                string header;
                string align;
                if (!TryReadRequiredString(column, "key", messageIndex, errors, out key) ||
                    !TryReadRequiredString(column, "header", messageIndex, errors, out header) ||
                    !TryReadOptionalString(column, "align", messageIndex, errors, out align))
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(align) &&
                    !string.Equals(align, "left", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(align, "center", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(align, "right", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table column '" + key + "' has unsupported align.");
                    continue;
                }

                columnKeys.Add(key);
                var normalizedColumn = new JObject
                {
                    ["key"] = key,
                    ["header"] = header
                };
                AddOptionalString(normalizedColumn, "align", string.IsNullOrWhiteSpace(align) ? null : align.ToLowerInvariant());
                normalizedColumns.Add(normalizedColumn);
            }

            var rowsToken = payload["rows"] as JArray;
            if (rowsToken == null)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table requires rows.");
                return null;
            }

            var normalizedRows = new JArray();
            for (var index = 0; index < rowsToken.Count; index++)
            {
                var row = rowsToken[index] as JObject;
                if (row == null)
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " must be an object.");
                    continue;
                }

                var normalizedRow = new JObject();
                foreach (var property in row.Properties())
                {
                    if (!IsAllowedTableValue(property.Value))
                    {
                        errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table row " + (index + 1).ToString(CultureInfo.InvariantCulture) + " contains unsupported value type for '" + property.Name + "'.");
                        continue;
                    }

                    normalizedRow[property.Name] = property.Value.DeepClone();
                }

                normalizedRows.Add(normalizedRow);
            }

            if (normalizedRows.Count > 0)
            {
                foreach (var key in columnKeys.Distinct(StringComparer.Ordinal))
                {
                    if (!normalizedRows.OfType<JObject>().Any(row => row.Property(key, StringComparison.Ordinal) != null))
                        errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " table column '" + key + "' does not exist in rows.");
                }
            }

            if (errors.Count > 0)
                return null;

            var normalizedPayload = new JObject
            {
                ["columns"] = normalizedColumns,
                ["rows"] = normalizedRows
            };
            AddOptionalString(normalizedPayload, "title", title);
            AddOptionalString(normalizedPayload, "subtitle", subtitle);
            AddOptionalString(normalizedPayload, "emptyStateLabel", emptyStateLabel);
            return normalizedPayload;
        }

        private static JObject NormalizePickerMessage(JObject message, int messageIndex, List<string> errors, AiDatasetAnswerRequest request)
        {
            string question;
            string originalPrompt;
            if (!TryReadRequiredString(message, "question", messageIndex, errors, out question) ||
                !TryReadRequiredString(message, "originalPrompt", messageIndex, errors, out originalPrompt))
            {
                return null;
            }

            string selectedType;
            if (!TryReadOptionalString(message, "selectedType", messageIndex, errors, out selectedType))
                return null;

            if (!string.IsNullOrWhiteSpace(selectedType))
            {
                selectedType = selectedType.Trim().ToLowerInvariant();
                if (selectedType != "bar" && selectedType != "line" && selectedType != "pie" && selectedType != "table")
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " selectedType '" + selectedType + "' is not supported.");
                    return null;
                }
            }

            if (errors.Count > 0)
                return null;

            var normalizedMessage = new JObject
            {
                ["type"] = "question-to-choose-chart-type",
                ["question"] = question,
                ["originalPrompt"] = originalPrompt,
                ["options"] = BuildFixedPickerOptions(ResolveRequestLocale(request)),
                ["selectedType"] = string.IsNullOrWhiteSpace(selectedType) ? (JToken)JValue.CreateNull() : selectedType
            };

            return normalizedMessage;
        }

        private static bool TryReadRequiredString(JObject source, string propertyName, int messageIndex, List<string> errors, out string value)
        {
            value = null;
            var token = source[propertyName];
            if (token == null || token.Type != JTokenType.String)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " requires string property '" + propertyName + "'.");
                return false;
            }

            value = NormalizeText(token.ToString(), null);
            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " property '" + propertyName + "' cannot be empty.");
                return false;
            }

            return true;
        }

        private static bool TryReadOptionalString(JObject source, string propertyName, int messageIndex, List<string> errors, out string value)
        {
            value = null;
            var token = source[propertyName];
            if (token == null || token.Type == JTokenType.Null)
                return true;

            if (token.Type != JTokenType.String)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " property '" + propertyName + "' must be a string when present.");
                return false;
            }

            value = NormalizeText(token.ToString(), null);
            return true;
        }

        private static void AddOptionalString(JObject target, string propertyName, string value)
        {
            if (!string.IsNullOrWhiteSpace(value))
                target[propertyName] = value;
        }

        private static bool IsAllowedChartValue(JToken token)
        {
            if (token == null)
                return true;

            switch (token.Type)
            {
                case JTokenType.String:
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.Null:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsAllowedTableValue(JToken token)
        {
            if (token == null)
                return true;

            switch (token.Type)
            {
                case JTokenType.String:
                case JTokenType.Integer:
                case JTokenType.Float:
                case JTokenType.Boolean:
                case JTokenType.Null:
                    return true;
                default:
                    return false;
            }
        }

        private static bool TryReadNumericValue(JToken token, out decimal value)
        {
            value = 0m;
            if (token == null || token.Type == JTokenType.Null)
                return false;

            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<decimal>();
                return true;
            }

            if (token.Type == JTokenType.String)
            {
                return decimal.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture, out value) ||
                       decimal.TryParse(token.ToString(), NumberStyles.Any, CultureInfo.CurrentCulture, out value);
            }

            return false;
        }

        private static JValue CreateNumericJValue(decimal value)
        {
            if (decimal.Truncate(value) == value)
                return new JValue(decimal.ToInt64(value));

            return new JValue(value);
        }

        private static string SerializeStructuredAnswerPayload(JObject payload)
        {
            return (payload ?? BuildSafeMarkdownFallback(null, "execution-failed", null)).ToString(Formatting.None);
        }

        private static bool ContainsVisualizationMessage(JObject payload)
        {
            var messages = payload?["messages"] as JArray;
            if (messages == null)
                return false;

            return messages
                .OfType<JObject>()
                .Any(message =>
                {
                    var type = NormalizeText(message["type"]?.ToString(), null);
                    return string.Equals(type, "chart", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(type, "table", StringComparison.OrdinalIgnoreCase);
                });
        }

        private static string ReplaceWarningsInStructuredAnswer(string serializedAnswer, AiDatasetAnswerRequest request, List<string> warnings)
        {
            if (string.IsNullOrWhiteSpace(serializedAnswer))
                return SerializeStructuredAnswerPayload(BuildSafeMarkdownFallback(request, "execution-failed", warnings));

            try
            {
                var payload = JObject.Parse(serializedAnswer);
                var validation = ValidateStructuredAnswerPayload(payload, request);
                if (!validation.IsValid)
                    return SerializeStructuredAnswerPayload(BuildSafeMarkdownFallback(request, "invalid-structured-output", MergeWarnings(warnings, validation.Errors)));

                validation.Payload["warnings"] = BuildWarningsToken(warnings);
                return SerializeStructuredAnswerPayload(validation.Payload);
            }
            catch
            {
                return SerializeStructuredAnswerPayload(BuildSafeMarkdownFallback(request, "execution-failed", warnings));
            }
        }

        private static JArray BuildFixedPickerOptions(string locale)
        {
            return new JArray(
                BuildFixedPickerOption("bar", LocalizeFixedPickerText(locale, "bar-label"), LocalizeFixedPickerText(locale, "bar-description")),
                BuildFixedPickerOption("line", LocalizeFixedPickerText(locale, "line-label"), LocalizeFixedPickerText(locale, "line-description")),
                BuildFixedPickerOption("pie", LocalizeFixedPickerText(locale, "pie-label"), LocalizeFixedPickerText(locale, "pie-description")),
                BuildFixedPickerOption("table", LocalizeFixedPickerText(locale, "table-label"), LocalizeFixedPickerText(locale, "table-description"))
            );
        }

        private static JObject BuildFixedPickerOption(string value, string label, string description)
        {
            var option = new JObject
            {
                ["value"] = value,
                ["label"] = label
            };

            AddOptionalString(option, "description", description);
            return option;
        }

        // Returns a safe markdown-only payload when the model output cannot be trusted.
        private static JObject BuildSafeMarkdownFallback(AiDatasetAnswerRequest request, string fallbackKind, List<string> warnings)
        {
            return new JObject
            {
                ["schemaVersion"] = StructuredAnswerSchemaVersion,
                ["messages"] = new JArray
                {
                    new JObject
                    {
                        ["type"] = "markdown",
                        ["markdown"] = ResolveFallbackMarkdown(request, fallbackKind)
                    }
                },
                ["warnings"] = BuildWarningsToken(warnings)
            };
        }

        private static JToken BuildWarningsToken(List<string> warnings)
        {
            var normalizedWarnings = MergeWarnings(warnings, null);
            return normalizedWarnings.Count == 0
                ? (JToken)JValue.CreateNull()
                : new JArray(normalizedWarnings);
        }

        private static JToken NormalizeStringArrayToken(JToken token)
        {
            var values = ExtractStringList(token);
            return values.Count == 0 ? (JToken)JValue.CreateNull() : new JArray(values);
        }

        private static string ResolveFallbackMarkdown(AiDatasetAnswerRequest request, string fallbackKind)
        {
            var locale = ResolveRequestLocale(request);
            switch (fallbackKind)
            {
                case "chunked-visualization-blocked":
                    return LocalizeVisibleText(locale,
                        "## No pude renderizar un grafico fiable\n- Solo habia resumenes parciales del dataset completo.\n- Prueba a acotar mas los filtros o pide un resumen breve.",
                        "## I could not render a reliable chart\n- Only partial summaries of the full dataset were available.\n- Try narrowing the filters or ask for a short summary.",
                        "## Ezin izan dut grafiko fidagarri bat erakutsi\n- Dataset osoaren laburpen partzialak besterik ez zeuden.\n- Saiatu filtroak gehiago murrizten edo eskatu laburpen labur bat.",
                        "## Nao consegui renderizar um grafico fiavel\n- So estavam disponiveis resumos parciais do dataset completo.\n- Tenta limitar mais os filtros ou pede um resumo curto.",
                        "## Non sono riuscito a renderizzare un grafico affidabile\n- Erano disponibili solo riepiloghi parziali del dataset completo.\n- Prova a restringere i filtri o chiedi un breve riepilogo.",
                        "## \\u65e0\\u6cd5\\u751f\\u6210\\u53ef\\u9760\\u7684\\u56fe\\u8868\\n- \\u76ee\\u524d\\u53ea\\u6709\\u5b8c\\u6574\\u6570\\u636e\\u96c6\\u7684\\u90e8\\u5206\\u6458\\u8981\\u3002\\n- \\u8bf7\\u5c1d\\u8bd5\\u7f29\\u5c0f\\u7b5b\\u9009\\u6761\\u4ef6\\uff0c\\u6216\\u8bf7\\u6c42\\u4e00\\u4e2a\\u7b80\\u77ed\\u6458\\u8981\\u3002");
                case "no-records":
                    return LocalizeVisibleText(locale,
                        "## Sin datos\n- No se encontraron registros para los filtros enviados.",
                        "## No data\n- No records were found for the current filters.",
                        "## Daturik ez\n- Ez da erregistrorik aurkitu bidalitako filtroekin.",
                        "## Sem dados\n- Nao foram encontrados registos para os filtros enviados.",
                        "## Nessun dato\n- Non sono stati trovati record per i filtri inviati.",
                        "## \\u65e0\\u6570\\u636e\\n- \\u672a\\u627e\\u5230\\u7b26\\u5408\\u5f53\\u524d\\u7b5b\\u9009\\u6761\\u4ef6\\u7684\\u8bb0\\u5f55\\u3002");
                case "invalid-structured-output":
                    return LocalizeVisibleText(locale,
                        "## No pude generar una visualizacion segura\n- La salida estructurada no fue valida.\n- Prueba a pedir un resumen breve o una tabla.",
                        "## I could not build a safe visualization\n- The structured output was not valid.\n- Try asking for a short summary or a table.",
                        "## Ezin izan dut bistaratze seguru bat sortu\n- Irteera egituratua ez zen baliozkoa.\n- Saiatu laburpen labur bat edo taula bat eskatzen.",
                        "## Nao consegui gerar uma visualizacao segura\n- A saida estruturada nao era valida.\n- Tenta pedir um resumo curto ou uma tabela.",
                        "## Non sono riuscito a creare una visualizzazione sicura\n- L'output strutturato non era valido.\n- Prova a chiedere un breve riepilogo o una tabella.",
                        "## \\u65e0\\u6cd5\\u751f\\u6210\\u5b89\\u5168\\u7684\\u53ef\\u89c6\\u5316\\n- \\u7ed3\\u6784\\u5316\\u8f93\\u51fa\\u65e0\\u6548\\u3002\\n- \\u8bf7\\u5c1d\\u8bd5\\u8be2\\u95ee\\u7b80\\u77ed\\u6458\\u8981\\u6216\\u8868\\u683c\\u3002");
                default:
                    return LocalizeVisibleText(locale,
                        "## No pude completar la respuesta\n- Devuelvo un mensaje seguro porque no hubo una salida estructurada valida.\n- Puedes reformular la pregunta o pedir una tabla simple.",
                        "## I could not complete the response\n- I am returning a safe message because no valid structured output was available.\n- You can rephrase the question or ask for a simple table.",
                        "## Ezin izan dut erantzuna osatu\n- Mezu seguru bat itzultzen dut, ez delako irteera egituratu baliodunik egon.\n- Galdera birformulatu edo taula soil bat eska dezakezu.",
                        "## Nao consegui completar a resposta\n- Estou a devolver uma mensagem segura porque nao havia uma saida estruturada valida disponivel.\n- Podes reformular a pergunta ou pedir uma tabela simples.",
                        "## Non sono riuscito a completare la risposta\n- Sto restituendo un messaggio sicuro perche non era disponibile un output strutturato valido.\n- Puoi riformulare la domanda o chiedere una tabella semplice.",
                        "## \\u65e0\\u6cd5\\u5b8c\\u6210\\u56de\\u7b54\\n- \\u7531\\u4e8e\\u6ca1\\u6709\\u53ef\\u7528\\u7684\\u6709\\u6548\\u7ed3\\u6784\\u5316\\u8f93\\u51fa\\uff0c\\u6211\\u8fd4\\u56de\\u4e00\\u6761\\u5b89\\u5168\\u6d88\\u606f\\u3002\\n- \\u4f60\\u53ef\\u4ee5\\u91cd\\u65b0\\u8868\\u8ff0\\u95ee\\u9898\\uff0c\\u6216\\u8bf7\\u6c42\\u4e00\\u4e2a\\u7b80\\u5355\\u8868\\u683c\\u3002");
            }
        }

        private static bool IsVisualizationRequested(AiDatasetAnswerRequest request)
        {
            var answerInstructions = StripDiacritics(NormalizeText(request?.AnswerInstructions, string.Empty)).ToLowerInvariant();
            if (answerInstructions.Contains("one compact markdown msg") ||
                answerInstructions.Contains("exactly 1 markdown") ||
                answerInstructions.Contains("text only"))
            {
                return false;
            }

            if (answerInstructions.Contains("md+") ||
                answerInstructions.Contains("chart") ||
                answerInstructions.Contains("graph") ||
                answerInstructions.Contains("table") ||
                answerInstructions.Contains("tabla"))
            {
                return true;
            }

            var sample = StripDiacritics((request?.Question ?? string.Empty) + " " + (request?.AnswerInstructions ?? string.Empty)).ToLowerInvariant();
            return sample.Contains("grafico") ||
                   sample.Contains("grafica") ||
                   sample.Contains("chart") ||
                   sample.Contains("graph") ||
                   sample.Contains("plot") ||
                   sample.Contains("lineas") ||
                   sample.Contains("barras") ||
                   sample.Contains("pie") ||
                   sample.Contains("tabla") ||
                   sample.Contains("table");
        }

        private static bool IsSpanishRequest(AiDatasetAnswerRequest request)
        {
            return string.Equals(ResolveRequestLocale(request), "es", StringComparison.Ordinal);
        }

        private static string ResolveRequestLocale(AiDatasetAnswerRequest request)
        {
            var originalSample = NormalizeText((request?.Question ?? string.Empty) + " " + (request?.AnswerInstructions ?? string.Empty), string.Empty);
            if (string.IsNullOrWhiteSpace(originalSample))
                return "es";

            foreach (var ch in originalSample)
            {
                if (ch >= 0x4E00 && ch <= 0x9FFF)
                    return "zhHans";
            }

            var sample = " " + StripDiacritics(originalSample).ToLowerInvariant() + " ";
            var scores = new Dictionary<string, int>(StringComparer.Ordinal)
            {
                ["es"] = ScoreLocale(sample, " el ", " la ", " los ", " las ", " que ", " para ", " con ", " grafico ", " grafica ", " tabla ", " resumen ", " gasto ", " gastos ", " hoja ", " hojas ", " quiero ", " necesito ", " mostrar ", " comparar ", " analisis ", " analitica "),
                ["en"] = ScoreLocale(sample, " the ", " and ", " for ", " with ", " chart ", " table ", " summary ", " expense ", " expenses ", " please ", " show ", " compare ", " display ", " total ", " analysis "),
                ["eu"] = ScoreLocale(sample, " eta ", " taula ", " grafiko ", " gastu ", " gastuak ", " laburpen ", " erakutsi ", " alderatu "),
                ["pt"] = ScoreLocale(sample, " o ", " a ", " os ", " as ", " para ", " com ", " grafico ", " tabela ", " resumo ", " despesa ", " despesas ", " mostrar ", " comparar "),
                ["it"] = ScoreLocale(sample, " il ", " lo ", " gli ", " le ", " per ", " con ", " grafico ", " tabella ", " riepilogo ", " spesa ", " spese ", " mostra ", " confronta ")
            };

            var bestMatch = scores
                .OrderByDescending(item => item.Value)
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .FirstOrDefault();

            if (bestMatch.Value > 0)
                return bestMatch.Key;

            return sample.Contains(" chart ") || sample.Contains(" table ") || sample.Contains(" summary ") ? "en" : "es";
        }

        private static int ScoreLocale(string sample, params string[] hints)
        {
            return (hints ?? new string[0]).Count(sample.Contains);
        }

        private static string StripDiacritics(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var normalized = value.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(normalized.Length);
            foreach (var ch in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
                    builder.Append(ch);
            }

            return builder.ToString().Normalize(NormalizationForm.FormC);
        }

        private static string BuildLocalizedWarning(AiDatasetAnswerRequest request, string key)
        {
            return LocalizeWarning(ResolveRequestLocale(request), key);
        }

        private static string LocalizeWarning(string locale, string key)
        {
            switch (key)
            {
                case "dataset-trimmed":
                    return LocalizeVisibleText(locale,
                        "El dataset se recorto al limite seguro de procesamiento por chunks.",
                        "The dataset was trimmed to the safe chunk processing limit.",
                        "Dataseta chunk bidez prozesatzeko muga segurura moztu da.",
                        "O dataset foi reduzido ao limite seguro de processamento por chunks.",
                        "Il dataset e stato ridotto al limite sicuro di elaborazione a blocchi.",
                        "\\u6570\\u636e\\u96c6\\u5df2\\u88ab\\u88c1\\u526a\\u5230\\u5206\\u5757\\u5904\\u7406\\u7684\\u5b89\\u5168\\u4e0a\\u9650\\u3002");
                case "safe-fallback":
                    return LocalizeVisibleText(locale,
                        "Se devolvio un fallback estructurado seguro porque la respuesta de OpenAI no pudo completarse.",
                        "A safe structured fallback was returned because the OpenAI response could not be completed.",
                        "Fallback egituratu seguru bat itzuli da, OpenAIren erantzuna ezin izan delako osatu.",
                        "Foi devolvido um fallback estruturado seguro porque a resposta da OpenAI nao conseguiu ser concluida.",
                        "E stato restituito un fallback strutturato sicuro perche la risposta di OpenAI non ha potuto essere completata.",
                        "\\u7531\\u4e8e OpenAI \\u54cd\\u5e94\\u65e0\\u6cd5\\u5b8c\\u6210\\uff0c\\u5df2\\u8fd4\\u56de\\u4e00\\u4e2a\\u5b89\\u5168\\u7684\\u7ed3\\u6784\\u5316 fallback\\u3002");
                case "invalid-structured-output":
                    return LocalizeVisibleText(locale,
                        "La salida del modelo no coincidio con el contrato estructurado de la UI.",
                        "The model output did not match the structured UI contract.",
                        "Modeloaren irteera ez dator bat UIaren kontratu egituratuarekin.",
                        "A saida do modelo nao correspondeu ao contrato estruturado da UI.",
                        "L'output del modello non corrispondeva al contratto strutturato della UI.",
                        "\\u6a21\\u578b\\u8f93\\u51fa\\u4e0e UI \\u7684\\u7ed3\\u6784\\u5316\\u5951\\u7ea6\\u4e0d\\u5339\\u914d\\u3002");
                case "chunked-visualization-blocked":
                    return LocalizeVisibleText(locale,
                        "No se puede emitir un grafico o tabla final en modo chunked porque no hay filas finales exactas disponibles.",
                        "A final chart or table cannot be emitted in chunked mode because exact final rows are not available.",
                        "Ezin da azken grafiko edo taularik sortu chunked moduan, ez daudelako azken errenkada zehatzak eskuragarri.",
                        "Nao e possivel emitir um grafico ou tabela final em modo chunked porque nao existem linhas finais exatas disponiveis.",
                        "Non e possibile emettere un grafico o una tabella finale in modalita chunked perche non sono disponibili le righe finali esatte.",
                        "\\u5728 chunked \\u6a21\\u5f0f\\u4e0b\\u65e0\\u6cd5\\u751f\\u6210\\u6700\\u7ec8\\u56fe\\u8868\\u6216\\u8868\\u683c\\uff0c\\u56e0\\u4e3a\\u6ca1\\u6709\\u53ef\\u7528\\u7684\\u7cbe\\u786e\\u6700\\u7ec8\\u884c\\u3002");
                case "schema-normalized":
                    return LocalizeVisibleText(locale,
                        "La schemaVersion del asistente se normalizo a expense-chat-v2.",
                        "The assistant schemaVersion was normalized to expense-chat-v2.",
                        "Laguntzailearen schemaVersion expense-chat-v2 baliora normalizatu da.",
                        "A schemaVersion do assistente foi normalizada para expense-chat-v2.",
                        "La schemaVersion dell'assistente e stata normalizzata a expense-chat-v2.",
                        "\\u52a9\\u624b\\u7684 schemaVersion \\u5df2\\u88ab\\u89c4\\u8303\\u5316\\u4e3a expense-chat-v2\\u3002");
                default:
                    return LocalizeVisibleText(locale,
                        "Se genero una advertencia interna del asistente.",
                        "An internal assistant warning was generated.",
                        "Laguntzailearen barne abisu bat sortu da.",
                        "Foi gerado um aviso interno do assistente.",
                        "E stato generato un avviso interno dell'assistente.",
                        "\\u5df2\\u751f\\u6210\\u4e00\\u6761\\u52a9\\u624b\\u5185\\u90e8\\u8b66\\u544a\\u3002");
            }
        }

        private static string LocalizeFixedPickerText(string locale, string key)
        {
            switch (key)
            {
                case "bar-label":
                    return LocalizeVisibleText(locale, "Barras", "Bars", "Barrak", "Barras", "Barre", "\\u67f1\\u72b6\\u56fe");
                case "bar-description":
                    return LocalizeVisibleText(locale, "Compara categorias rapidamente.", "Compare categories quickly.", "Kategoriak azkar alderatzen ditu.", "Compara categorias rapidamente.", "Confronta rapidamente le categorie.", "\\u5feb\\u901f\\u6bd4\\u8f83\\u5404\\u7c7b\\u522b\\u3002");
                case "line-label":
                    return LocalizeVisibleText(locale, "Lineas", "Lines", "Lerroak", "Linhas", "Linee", "\\u6298\\u7ebf\\u56fe");
                case "line-description":
                    return LocalizeVisibleText(locale, "Muestra una secuencia o evolucion temporal.", "Show a sequence or time trend.", "Denborazko segida edo bilakaera erakusten du.", "Mostra uma sequencia ou evolucao temporal.", "Mostra una sequenza o un andamento temporale.", "\\u663e\\u793a\\u5e8f\\u5217\\u6216\\u65f6\\u95f4\\u8d8b\\u52bf\\u3002");
                case "pie-label":
                    return LocalizeVisibleText(locale, "Pie", "Pie", "Pie", "Pie", "Torta", "\\u997c\\u56fe");
                case "pie-description":
                    return LocalizeVisibleText(locale, "Muestra proporcion entre pocas categorias.", "Show proportions across a few categories.", "Kategoria gutxiren arteko proportzioa erakusten du.", "Mostra proporcao entre poucas categorias.", "Mostra la proporzione tra poche categorie.", "\\u5c55\\u793a\\u5c11\\u91cf\\u7c7b\\u522b\\u4e4b\\u95f4\\u7684\\u5360\\u6bd4\\u3002");
                case "table-label":
                    return LocalizeVisibleText(locale, "Tabla", "Table", "Taula", "Tabela", "Tabella", "\\u8868\\u683c");
                case "table-description":
                    return LocalizeVisibleText(locale, "Prioriza detalle exacto y comparacion.", "Prioritize exact detail and comparison.", "Xehetasun zehatza eta konparazioa lehenesten ditu.", "Prioriza detalhe exato e comparacao.", "Privilegia il dettaglio esatto e il confronto.", "\\u4f18\\u5148\\u663e\\u793a\\u7cbe\\u786e\\u7ec6\\u8282\\u4e0e\\u5bf9\\u6bd4\\u3002");
                default:
                    return string.Empty;
            }
        }

        private static string LocalizeVisibleText(string locale, string es, string en, string eu, string pt, string it, string zhHans)
        {
            switch (locale)
            {
                case "en":
                    return en;
                case "eu":
                    return eu;
                case "pt":
                    return pt;
                case "it":
                    return it;
                case "zhHans":
                    return Regex.Unescape(zhHans);
                case "es":
                default:
                    return es;
            }
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

        private static string BuildStructuredAnswerInstructions(bool fromChunkSummaries, bool visualizationRequested)
        {
            var lines = new List<string>
            {
                "You are a UI answer composer for the expense sheet assistant.",
                "Return only valid JSON that matches the provided schema.",
                "Do not wrap the JSON in markdown fences.",
                "All visible text must be in the same language as the user question.",
                "If answerInstructions uses a different language, ignore that and follow the user question language.",
                "Use only the provided data and never invent fields, keys, ids, amounts, currencies, dates, categories, or conclusions.",
                "Do not embed raw JSON inside markdown.",
                "Do not embed ASCII tables or pipe tables inside markdown.",
                "Keep markdown short and useful: one short heading or sentence, plus 2 to 4 bullets at most.",
                "After the final JSON object, output nothing else."
            };

            if (!visualizationRequested)
            {
                lines.Add("The user asked for a textual analysis only.");
                lines.Add("Return exactly 1 markdown message.");
                lines.Add("Do not return chart, table, or question-to-choose-chart-type messages.");
                lines.Add("If information is missing, say so clearly inside the markdown answer.");
                return string.Join("\n", lines);
            }

            lines.Add("If information is missing, say so clearly in markdown and do not invent a chart.");
            lines.Add("If visualization was requested and the data is valid for it, return exactly 2 messages: one markdown summary and one chart or table.");
            lines.Add("If the requested visualization would be misleading, unsupported, or missing the minimum variables, return exactly 2 messages: one short markdown explanation asking only for the minimum missing data, and one question-to-choose-chart-type message.");
            lines.Add("The minimum missing data means one grouping or time field and one metric. Do not list multiple candidate analyses or many example charts.");
            lines.Add("The question-to-choose-chart-type message must keep exactly these 4 options in this order: bar, line, pie, table.");
            lines.Add("Do not force a chart only because the user mentioned a chart.");
            lines.Add("Never invent xKey, yKey, nameKey, or dataKey. These keys must exist exactly in the returned rows.");
            lines.Add("Use readable labels instead of bare numeric codes whenever readable labels are available in the data.");
            lines.Add("Pie charts: only for part-to-whole, maximum 6 categories, and never with negative values.");
            lines.Add("Bar charts: use for comparisons across categories.");
            lines.Add("Line charts: use only for ordered or temporal sequences.");
            lines.Add("Tables: use when precision matters or when there are too many details for a safe chart.");

            if (fromChunkSummaries)
            {
                lines.Add("You only have chunk summaries, not the full dataset rows.");
                lines.Add("Do not create a chart or table unless the chunk summaries already provide the exact final rows and exact keys needed by the visualization.");
            }

            return string.Join("\n", lines);
        }

        private static string BuildChunkSummaryInstructions()
        {
            return string.Join("\n", new[]
            {
                "You are summarizing one chunk of expense sheet records for a later final answer.",
                "Return only valid JSON that matches the provided schema.",
                "Use only the provided chunk records.",
                "Do not invent facts that are not present in the chunk.",
                "Focus on facts that are relevant to the question and mention when the chunk does not help.",
                "Keep the summary concise and factual.",
                "Do not produce charts, tables, or UI messages in this step."
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

        private static string DescribeResponseSchemaKind(string responseFormatName)
        {
            var normalized = NormalizeText(responseFormatName, string.Empty);
            if (string.IsNullOrWhiteSpace(normalized))
                return "unknown";

            if (normalized.IndexOf("chunk", StringComparison.OrdinalIgnoreCase) >= 0)
                return "chunk-summary";

            if (normalized.IndexOf("markdown", StringComparison.OrdinalIgnoreCase) >= 0)
                return "markdown-only";

            return "visual-structured";
        }

        private static string TryExtractInvalidSchemaContext(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return null;

            var match = Regex.Match(summary, @"In context=(?<context>\([^)]+\))", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["context"].Value : null;
        }

        private static string TryExtractInvalidSchemaMissingProperty(string summary)
        {
            if (string.IsNullOrWhiteSpace(summary))
                return null;

            var match = Regex.Match(summary, @"Missing '(?<property>[^']+)'", RegexOptions.CultureInvariant);
            return match.Success ? match.Groups["property"].Value : null;
        }

        private static string TryExtractOpenAiPayloadJson(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var root = JObject.Parse(responseBody);
                var nestedResponse = root["response"] as JObject;
                var directJson = TrySerializeJsonToken(root["output_json"]) ??
                                 TrySerializeJsonToken(root["parsed"]) ??
                                 TrySerializeJsonToken(nestedResponse != null ? nestedResponse["output_json"] : null);
                if (!string.IsNullOrWhiteSpace(directJson))
                    return directJson;

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
                            var structuredPart = TrySerializeJsonToken(part["json"]) ??
                                                 TrySerializeJsonToken(part["parsed"]) ??
                                                 TrySerializeJsonToken(part["value"]);
                            if (!string.IsNullOrWhiteSpace(structuredPart))
                                return structuredPart;

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

        private static string TrySerializeJsonToken(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null || token.Type == JTokenType.Undefined)
                return null;

            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                return token.ToString(Formatting.None);

            if (token.Type == JTokenType.String)
                return TrimJsonBlock(token.ToString());

            return null;
        }

        private sealed class DatasetAnswerRequestOptions
        {
            public int MaxOutputTokens { get; set; }

            public string ServiceTier { get; set; }

            public string PromptCacheKey { get; set; }

            public string ReasoningEffort { get; set; }
        }

        private sealed class StructuredAnswerValidationOutcome
        {
            public bool IsValid
            {
                get { return Errors == null || Errors.Count == 0; }
            }

            public JObject Payload { get; set; }

            public List<string> Warnings { get; set; }

            public List<string> Errors { get; set; }
        }
    }
}
