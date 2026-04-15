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
                warnings.Add("The dataset was trimmed to the safe chunk processing limit.");
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
                    "A safe structured fallback was returned because the OpenAI response could not be completed."
                });

                _logger.Log(
                    "[OPENAI-DATASET] Falling back to structured markdown retrievalMode=" + retrievalMode +
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
            var payload = await ExecuteStructuredRequestAsync(
                BuildStructuredAnswerInstructions(false),
                BuildDirectInputJson(request, records),
                BuildFinalAnswerSchema(),
                "expense_sheet_answer_direct",
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
                BuildStructuredAnswerInstructions(true),
                BuildFinalChunkInputJson(request, partialSummaries),
                BuildFinalAnswerSchema(),
                "expense_sheet_answer_final",
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
            var validation = ValidateStructuredAnswerPayload(payload);
            if (!validation.IsValid)
            {
                var fallbackWarnings = MergeWarnings(baseWarnings, validation.Warnings);
                fallbackWarnings = MergeWarnings(fallbackWarnings, validation.Errors.Select(item => "Structured validation: " + item).ToList());
                fallbackWarnings = MergeWarnings(fallbackWarnings, new List<string>
                {
                    "The model output did not match the structured UI contract."
                });

                return BuildSafeFallbackResult(request, retrievalMode, recordsSentToModel, fallbackWarnings, "invalid-structured-output");
            }

            var mergedWarnings = MergeWarnings(baseWarnings, validation.Warnings);
            if (string.Equals(retrievalMode, "chunked", StringComparison.OrdinalIgnoreCase) &&
                ContainsVisualizationMessage(validation.Payload))
            {
                mergedWarnings = MergeWarnings(mergedWarnings, new List<string>
                {
                    "Chunked final answers cannot emit chart or table payloads because exact final rows are not available."
                });

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
        private static JObject BuildFinalAnswerSchema()
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

        private static JObject BuildMessageSchema()
        {
            return new JObject
            {
                ["oneOf"] = new JArray
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
                        ["oneOf"] = new JArray
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
                        ["minItems"] = 2,
                        ["maxItems"] = 3,
                        ["items"] = BuildPickerOptionSchema()
                    },
                    ["selectedType"] = new JObject
                    {
                        ["type"] = new JArray("string", "null"),
                        ["enum"] = new JArray("bar", "line", "pie", "table", null)
                    }
                },
                ["required"] = new JArray("type", "question", "originalPrompt", "options")
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
                    ["title"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["subtitle"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["emptyStateLabel"] = new JObject
                    {
                        ["type"] = "string"
                    },
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
                ["required"] = new JArray("chartType", "data", "xKey", "yKey")
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
                    ["title"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["subtitle"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["emptyStateLabel"] = new JObject
                    {
                        ["type"] = "string"
                    },
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
                ["required"] = new JArray("chartType", "data", "nameKey", "dataKey")
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
                    ["title"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["subtitle"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["emptyStateLabel"] = new JObject
                    {
                        ["type"] = "string"
                    },
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
                ["required"] = new JArray("columns", "rows")
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
                    ["align"] = new JObject
                    {
                        ["type"] = "string",
                        ["enum"] = new JArray("left", "center", "right")
                    }
                },
                ["required"] = new JArray("key", "header")
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
                    ["description"] = new JObject
                    {
                        ["type"] = "string"
                    }
                },
                ["required"] = new JArray("value", "label")
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
        private static StructuredAnswerValidationOutcome ValidateStructuredAnswerPayload(JObject payload)
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
                    "The assistant schemaVersion was normalized to expense-chat-v2."
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
                var normalizedMessage = NormalizeStructuredMessage(messagesToken[index], index, outcome.Errors);
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

        private static JObject NormalizeStructuredMessage(JToken token, int messageIndex, List<string> errors)
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
                    return NormalizePickerMessage(message, messageIndex, errors);
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

        private static JObject NormalizePickerMessage(JObject message, int messageIndex, List<string> errors)
        {
            string question;
            string originalPrompt;
            if (!TryReadRequiredString(message, "question", messageIndex, errors, out question) ||
                !TryReadRequiredString(message, "originalPrompt", messageIndex, errors, out originalPrompt))
            {
                return null;
            }

            var optionsToken = message["options"] as JArray;
            if (optionsToken == null || optionsToken.Count < 2 || optionsToken.Count > 3)
            {
                errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " picker requires 2 or 3 options.");
                return null;
            }

            var normalizedOptions = new JArray();
            var seenValues = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < optionsToken.Count; index++)
            {
                var option = optionsToken[index] as JObject;
                if (option == null)
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " picker option " + (index + 1).ToString(CultureInfo.InvariantCulture) + " must be an object.");
                    continue;
                }

                string value;
                string label;
                string description;
                if (!TryReadRequiredString(option, "value", messageIndex, errors, out value) ||
                    !TryReadRequiredString(option, "label", messageIndex, errors, out label) ||
                    !TryReadOptionalString(option, "description", messageIndex, errors, out description))
                {
                    continue;
                }

                value = value.Trim().ToLowerInvariant();
                if (value != "bar" && value != "line" && value != "pie" && value != "table")
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " picker option '" + value + "' is not supported.");
                    continue;
                }

                if (!seenValues.Add(value))
                {
                    errors.Add("Message " + (messageIndex + 1).ToString(CultureInfo.InvariantCulture) + " picker options must be unique.");
                    continue;
                }

                var normalizedOption = new JObject
                {
                    ["value"] = value,
                    ["label"] = label
                };
                AddOptionalString(normalizedOption, "description", description);
                normalizedOptions.Add(normalizedOption);
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
                ["options"] = normalizedOptions,
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
                var validation = ValidateStructuredAnswerPayload(payload);
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
            var useSpanish = IsSpanishRequest(request);
            switch (fallbackKind)
            {
                case "chunked-visualization-blocked":
                    return useSpanish
                        ? "## No pude renderizar un grafico fiable\n- Solo habia resumenes parciales del dataset completo.\n- Prueba a acotar mas los filtros o pide un resumen breve."
                        : "## I could not render a reliable chart\n- Only partial summaries of the full dataset were available.\n- Try narrowing the filters or ask for a short summary.";
                case "no-records":
                    return useSpanish
                        ? "## Sin datos\n- No se encontraron registros para los filtros enviados."
                        : "## No data\n- No records were found for the current filters.";
                case "invalid-structured-output":
                    return useSpanish
                        ? "## No pude generar una visualizacion segura\n- La salida estructurada no fue valida.\n- Prueba a pedir un resumen breve o una tabla."
                        : "## I could not build a safe visualization\n- The structured output was not valid.\n- Try asking for a short summary or a table.";
                default:
                    return useSpanish
                        ? "## No pude completar la respuesta\n- Devuelvo un mensaje seguro porque no hubo una salida estructurada valida.\n- Puedes reformular la pregunta o pedir una tabla simple."
                        : "## I could not complete the response\n- I am returning a safe message because no valid structured output was available.\n- You can rephrase the question or ask for a simple table.";
            }
        }

        private static bool IsSpanishRequest(AiDatasetAnswerRequest request)
        {
            var sample = ((request?.Question ?? string.Empty) + " " + (request?.AnswerInstructions ?? string.Empty)).ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(sample))
                return true;

            var padded = " " + sample + " ";
            var spanishHints = new[]
            {
                " el ", " la ", " los ", " las ", " que ", " para ", " con ", " grafico ", " grafica ", " tabla ", " resumen ",
                " gasto ", " gastos ", " hoja ", " hojas ", " quiero ", " necesito ", " mostrar ", " comparar "
            };
            var englishHints = new[]
            {
                " the ", " and ", " for ", " with ", " chart ", " table ", " summary ", " expense ", " expenses ", " please ",
                " show ", " compare ", " display ", " total "
            };

            var spanishScore = spanishHints.Count(padded.Contains);
            var englishScore = englishHints.Count(padded.Contains);
            return spanishScore >= englishScore;
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

        private static string BuildStructuredAnswerInstructions(bool fromChunkSummaries)
        {
            var lines = new List<string>
            {
                "You are a UI answer composer for the expense sheet assistant.",
                "Return only valid JSON that matches the provided schema.",
                "Do not wrap the JSON in markdown fences.",
                "All visible text must be in the same language used by the user question and answerInstructions.",
                "Use only the provided data and never invent fields, keys, ids, amounts, currencies, dates, categories, or conclusions.",
                "If information is missing, say so clearly in markdown and do not invent a chart.",
                "If visualization was not requested, return exactly 1 markdown message.",
                "If visualization was requested and the data is valid for it, return exactly 2 messages: one markdown summary and one chart or table.",
                "If the requested visualization would be misleading, unsupported, or not backed by exact data, return exactly 2 messages: one short markdown explanation and one question-to-choose-chart-type message with 2 or 3 better options.",
                "Do not force a chart only because the user mentioned a chart.",
                "Never invent xKey, yKey, nameKey, or dataKey. These keys must exist exactly in the returned rows.",
                "Do not embed raw JSON inside markdown.",
                "Do not embed ASCII tables or pipe tables inside markdown.",
                "Keep markdown short and useful: one short heading or sentence, plus 2 to 4 bullets at most.",
                "Use readable labels instead of bare numeric codes whenever readable labels are available in the data.",
                "Pie charts: only for part-to-whole, maximum 6 categories, and never with negative values.",
                "Bar charts: use for comparisons across categories.",
                "Line charts: use only for ordered or temporal sequences.",
                "Tables: use when precision matters or when there are too many details for a safe chart.",
                "After the final JSON object, output nothing else."
            };

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
