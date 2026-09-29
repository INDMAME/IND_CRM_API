using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Extracts draft expense-sheet payload from ticket images using OpenAI Responses API.
    /// </summary>
    public sealed class IND_OpenAiExpenseTicketDraftService : IND_IExpenseTicketDraftService, IOpenAITicketNormalizationService
    {
        private const string DefaultModel = "gpt-5-nano";
        private const int DefaultTimeoutSeconds = 180;
        private const int DefaultMaxImageBytes = 50 * 1024 * 1024;
        private const int DefaultMaxOutputTokens = 1024;
        private const int DefaultQuickCreateMaxOutputTokens = 768;
        private const int MaxRetryOutputTokens = 4096;
        private const string DefaultImageDetail = "high";
        private const string DefaultQuickCreateImageDetail = "auto";
        private const string DefaultServiceTier = "priority";
        private const string DefaultProfileTag = "ticket-fast-v1";
        private const string DefaultPromptCacheKey = "expense-ticket-draft-v4";
        private const string DefaultQuickCreateProfileTag = "ticket-quick-create-v1";
        private const string DefaultQuickCreatePromptCacheKey = "expense-ticket-quick-create-v3";
        private const string DefaultReasoningEffort = "low";
        private const string ResponsesUrl = "https://api.openai.com/v1/responses";
        private const string ModelSettingKey = "OpenAI:ExpenseTicketModel";
        private const string TimeoutSettingKey = "OpenAI:ExpenseTicketTimeoutSeconds";
        private const string MaxImageBytesSettingKey = "OpenAI:ExpenseTicketMaxImageBytes";
        private const string MaxOutputTokensSettingKey = "OpenAI:ExpenseTicketMaxOutputTokens";
        private const string ImageDetailSettingKey = "OpenAI:ExpenseTicketImageDetail";
        private const string ServiceTierSettingKey = "OpenAI:ExpenseTicketServiceTier";
        private const string ProfileTagSettingKey = "OpenAI:ExpenseTicketProfileTag";
        private const string PromptCacheKeySettingKey = "OpenAI:ExpenseTicketPromptCacheKey";
        private const string QuickCreateMaxOutputTokensSettingKey = "OpenAI:ExpenseTicketQuickCreateMaxOutputTokens";
        private const string QuickCreateImageDetailSettingKey = "OpenAI:ExpenseTicketQuickCreateImageDetail";
        private const string QuickCreateServiceTierSettingKey = "OpenAI:ExpenseTicketQuickCreateServiceTier";
        private const string QuickCreateProfileTagSettingKey = "OpenAI:ExpenseTicketQuickCreateProfileTag";
        private const string QuickCreatePromptCacheKeySettingKey = "OpenAI:ExpenseTicketQuickCreatePromptCacheKey";
        private const string ReasoningEffortSettingKey = "OpenAI:ExpenseTicketReasoningEffort";
        private const string QuickCreateReasoningEffortSettingKey = "OpenAI:ExpenseTicketQuickCreateReasoningEffort";

        private const int DefaultGastoTypeValue = 8;
        private const decimal OcrTotalTolerance = 0.02m;
        private const string OcrTotalAdjustmentDescription = "AJUSTE AL TOTAL OCR";
        private static readonly string CrmGastoTypePromptCatalog = BuildCrmGastoTypePromptCatalog();
        private static readonly int TimeoutSeconds = ReadTimeoutFromConfig();
        private static readonly int MaxImageBytes = ReadMaxImageBytesFromConfig();
        private static readonly HttpClient _httpClient = CreateHttpClient();

        private readonly IAxLogger _logger;
        private readonly string _model;
        private readonly int _maxOutputTokens;
        private readonly string _imageDetail;
        private readonly string _serviceTier;
        private readonly string _profileTag;
        private readonly string _promptCacheKey;
        private readonly int _quickCreateMaxOutputTokens;
        private readonly string _quickCreateImageDetail;
        private readonly string _quickCreateServiceTier;
        private readonly string _quickCreateProfileTag;
        private readonly string _quickCreatePromptCacheKey;
        private readonly string _reasoningEffort;
        private readonly string _quickCreateReasoningEffort;

        public IND_OpenAiExpenseTicketDraftService(IAxLogger logger)
        {
            _logger = logger ?? new FileAxLogger();
            _model = ReadModelFromConfig();
            _maxOutputTokens = ReadMaxOutputTokensFromConfig();
            _imageDetail = ReadImageDetailFromConfig();
            _serviceTier = ReadServiceTierFromConfig();
            _profileTag = ReadProfileTagFromConfig();
            _promptCacheKey = ReadPromptCacheKeyFromConfig();
            _quickCreateMaxOutputTokens = ReadQuickCreateMaxOutputTokensFromConfig();
            _quickCreateImageDetail = ReadQuickCreateImageDetailFromConfig();
            _quickCreateServiceTier = ReadQuickCreateServiceTierFromConfig();
            _quickCreateProfileTag = ReadQuickCreateProfileTagFromConfig();
            _quickCreatePromptCacheKey = ReadQuickCreatePromptCacheKeyFromConfig();
            _reasoningEffort = ReadReasoningEffortFromConfig();
            _quickCreateReasoningEffort = ReadQuickCreateReasoningEffortFromConfig();
        }

        public async Task<ExpenseSheetDraftResponse> ExtractFromTicketImageAsync(
            byte[] imageBytes,
            string fileName,
            string contentType,
            CancellationToken cancellationToken,
            ExpenseTicketDraftProfile profile = ExpenseTicketDraftProfile.FullDraft)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("ticketImage no puede estar vacio.", nameof(imageBytes));

            if (string.IsNullOrWhiteSpace(fileName))
                throw new ArgumentException("fileName es obligatorio.", nameof(fileName));

            if (imageBytes.Length > MaxImageBytes)
                throw new ArgumentException($"La imagen supera el maximo permitido ({MaxImageBytes} bytes).", nameof(imageBytes));

            var openAiApiKey = GetOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(openAiApiKey))
            {
                throw new IND_ExternalServiceException(
                    "OpenAI",
                    "La extraccion del ticket no esta disponible porque el servicio de IA no esta configurado correctamente.",
                    IndErrorCodes.AiServiceUnavailable,
                    HttpStatusCode.ServiceUnavailable,
                    "api-key-missing");
            }

            var imageBase64 = Convert.ToBase64String(imageBytes);
            var normalizedContentType = GetNormalizedDataContentType(contentType);
            var promptText = BuildPayloadPromptText(profile);
            var requestOptions = BuildRequestOptions(profile, null);
            HttpResponseMessage response = null;
            string responseBody = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var attempt = 0;
            var retriedWithoutServiceTier = false;

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                while (true)
                {
                    attempt++;

                    var payloadJson = BuildPayloadJson(imageBase64, normalizedContentType, fileName, promptText, requestOptions);
                    var payloadBytes = Encoding.UTF8.GetByteCount(payloadJson);

                    _logger.Log(
                        $"[OPENAI] Expense draft request attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} detail={requestOptions.ImageDetail} maxOut={requestOptions.MaxOutputTokens} requestedTier={requestOptions.ServiceTier ?? "auto"} cacheKey={(string.IsNullOrWhiteSpace(requestOptions.PromptCacheKey) ? "na" : requestOptions.PromptCacheKey)} imageBytes={imageBytes.Length} payloadBytes={payloadBytes}",
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
                        _logger.Log(
                            $"[OPENAI] Expense draft retry without priority attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} requestedTier={requestOptions.ServiceTier}",
                            AxaptaSessionManager.LogLevel.Warning);

                        requestOptions = BuildRequestOptions(requestOptions.DraftProfile, "auto", requestOptions.MaxOutputTokens);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var summary = TryExtractOpenAiErrorSummary(responseBody);
                        var retryAfterSeconds = IND_OpenAiErrorHandling.GetRetryAfterSeconds(response);
                        _logger.Log(
                            $"[OPENAI] Expense draft failed attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} status={(int)response.StatusCode} retryAfter={(retryAfterSeconds.HasValue ? retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture) : "na")} summary={summary}",
                            AxaptaSessionManager.LogLevel.Warning);

                        if (IND_OpenAiErrorHandling.IsRateLimit(response.StatusCode, responseBody))
                        {
                            throw new IND_OpenAiRateLimitException(
                                "OpenAI rate limit exceeded while extracting expense ticket draft.",
                                retryAfterSeconds,
                                summary);
                        }

                        throw new IND_ExternalServiceException(
                            "OpenAI",
                            "No se pudo extraer el borrador porque el servicio de IA devolvio un error.",
                            IndErrorCodes.AiServiceUnavailable,
                            HttpStatusCode.ServiceUnavailable,
                            summary);
                    }

                    var incompleteReason = TryExtractIncompleteReason(responseBody);
                    if (string.Equals(incompleteReason, "max_output_tokens", StringComparison.OrdinalIgnoreCase))
                    {
                        var metrics = TryReadResponseMetrics(responseBody);
                        if (TryBuildExpandedRequestOptions(requestOptions, out var expandedRequestOptions))
                        {
                            _logger.Log(
                                $"[OPENAI] Draft truncado por max_output_tokens attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} maxOut={requestOptions.MaxOutputTokens} outputTokens={ToMetricText(metrics.OutputTokens)} retryMaxOut={expandedRequestOptions.MaxOutputTokens}",
                                AxaptaSessionManager.LogLevel.Warning);

                            requestOptions = expandedRequestOptions;
                            continue;
                        }

                        _logger.Log(
                            $"[OPENAI] Draft truncado por max_output_tokens attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} maxOut={requestOptions.MaxOutputTokens} outputTokens={ToMetricText(metrics.OutputTokens)}",
                            AxaptaSessionManager.LogLevel.Warning);
                        throw new IND_ExternalServiceException(
                            "OpenAI",
                            "No se pudo completar el borrador porque el servicio de IA recorto la respuesta antes de terminar.",
                            IndErrorCodes.AiServiceUnavailable,
                            HttpStatusCode.ServiceUnavailable,
                            "max_output_tokens");
                    }

                    var extracted = TryParseExpenseDraft(responseBody);
                    if (extracted == null)
                    {
                        _logger.Log("[OPENAI] Respuesta sin json valido de draft de ticket.", AxaptaSessionManager.LogLevel.Warning);
                        throw new IND_ExternalServiceException(
                            "OpenAI",
                            "No se pudo extraer el borrador porque el servicio de IA devolvio una respuesta invalida.",
                            IndErrorCodes.AiServiceUnavailable,
                            HttpStatusCode.ServiceUnavailable,
                            "invalid-json");
                    }

                    var successMetrics = TryReadResponseMetrics(responseBody);
                    _logger.Log(
                        $"[OPENAI] Draft extraido exitosamente ms={sw.ElapsedMilliseconds} attempts={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} detail={requestOptions.ImageDetail} requestedTier={requestOptions.ServiceTier ?? "auto"} actualTier={successMetrics.ActualServiceTier ?? "na"} inputTokens={ToMetricText(successMetrics.InputTokens)} cachedTokens={ToMetricText(successMetrics.CachedTokens)} outputTokens={ToMetricText(successMetrics.OutputTokens)} reasoningTokens={ToMetricText(successMetrics.ReasoningTokens)} totalTokens={ToMetricText(successMetrics.TotalTokens)}",
                        AxaptaSessionManager.LogLevel.Info);
                    return extracted;
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.Log("[OPENAI] Peticion cancelada: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                if (cancellationToken.IsCancellationRequested)
                    throw;

                throw new IND_ExternalServiceException(
                    "OpenAI",
                    "La extraccion del ticket tardo demasiado y el servicio de IA no respondio a tiempo.",
                    IndErrorCodes.ExternalServiceTimeout,
                    HttpStatusCode.GatewayTimeout,
                    "timeout",
                    ex);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.Log("[OPENAI] Error extrayendo draft: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                if (ex is IND_OpenAiRateLimitException || ex is IND_ExternalServiceException)
                    throw;

                throw;
            }
            finally
            {
                response?.Dispose();
            }
        }

        public async Task<OpenAITicketNormalizationResult> NormalizeReceiptAsync(
            AzureReceiptAnalysisResult receiptAnalysis,
            string fileName,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken)
        {
            if (receiptAnalysis == null)
                throw new ArgumentNullException(nameof(receiptAnalysis));

            if (string.IsNullOrWhiteSpace(receiptAnalysis.PromptJson))
                throw new ArgumentException("receiptAnalysis.PromptJson es obligatorio.", nameof(receiptAnalysis));

            var openAiApiKey = GetOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(openAiApiKey))
            {
                throw new IND_ExternalServiceException(
                    "OpenAI",
                    "La normalizacion del ticket no esta disponible porque el servicio de IA no esta configurado correctamente.",
                    IndErrorCodes.AiServiceUnavailable,
                    HttpStatusCode.ServiceUnavailable,
                    "api-key-missing");
            }

            var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "ticket" : fileName.Trim();
            var promptText = BuildStructuredOcrPayloadPromptText(profile);
            var requestOptions = BuildRequestOptions(profile, null);
            HttpResponseMessage response = null;
            string responseBody = null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var attempt = 0;
            var retriedWithoutServiceTier = false;

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                while (true)
                {
                    attempt++;

                    var payloadJson = BuildTextPayloadJson(promptText, receiptAnalysis.PromptJson, safeFileName, requestOptions);
                    var payloadBytes = Encoding.UTF8.GetByteCount(payloadJson);

                    _logger.Log(
                        $"[OPENAI-NORMALIZE] Receipt normalization request attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} maxOut={requestOptions.MaxOutputTokens} requestedTier={requestOptions.ServiceTier ?? "auto"} cacheKey={(string.IsNullOrWhiteSpace(requestOptions.PromptCacheKey) ? "na" : requestOptions.PromptCacheKey)} ocrBytes={Encoding.UTF8.GetByteCount(receiptAnalysis.PromptJson)} payloadBytes={payloadBytes} fileName={safeFileName}",
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
                        _logger.Log(
                            $"[OPENAI-NORMALIZE] Retry without priority attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} requestedTier={requestOptions.ServiceTier}",
                            AxaptaSessionManager.LogLevel.Warning);

                        requestOptions = BuildRequestOptions(requestOptions.DraftProfile, "auto", requestOptions.MaxOutputTokens);
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        var summary = TryExtractOpenAiErrorSummary(responseBody);
                        var retryAfterSeconds = IND_OpenAiErrorHandling.GetRetryAfterSeconds(response);
                        _logger.Log(
                            $"[OPENAI-NORMALIZE] Receipt normalization failed attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} status={(int)response.StatusCode} retryAfter={(retryAfterSeconds.HasValue ? retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture) : "na")} summary={summary}",
                            AxaptaSessionManager.LogLevel.Warning);

                        if (IND_OpenAiErrorHandling.IsRateLimit(response.StatusCode, responseBody))
                        {
                            throw new IND_OpenAiRateLimitException(
                                "OpenAI rate limit exceeded while normalizing Azure receipt OCR.",
                                retryAfterSeconds,
                                summary);
                        }

                        throw new IND_ExternalServiceException(
                            "OpenAI",
                            "No se pudo normalizar el ticket porque el servicio de IA devolvio un error.",
                            IndErrorCodes.AiServiceUnavailable,
                            HttpStatusCode.ServiceUnavailable,
                            summary);
                    }

                    var incompleteReason = TryExtractIncompleteReason(responseBody);
                    if (string.Equals(incompleteReason, "max_output_tokens", StringComparison.OrdinalIgnoreCase))
                    {
                        var metrics = TryReadResponseMetrics(responseBody);
                        if (TryBuildExpandedRequestOptions(requestOptions, out var expandedRequestOptions))
                        {
                            _logger.Log(
                                $"[OPENAI-NORMALIZE] Draft truncado por max_output_tokens attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} maxOut={requestOptions.MaxOutputTokens} outputTokens={ToMetricText(metrics.OutputTokens)} retryMaxOut={expandedRequestOptions.MaxOutputTokens}",
                                AxaptaSessionManager.LogLevel.Warning);

                            requestOptions = expandedRequestOptions;
                            continue;
                        }

                        _logger.Log(
                            $"[OPENAI-NORMALIZE] Draft truncado por max_output_tokens attempt={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} maxOut={requestOptions.MaxOutputTokens} outputTokens={ToMetricText(metrics.OutputTokens)}",
                            AxaptaSessionManager.LogLevel.Warning);
                        throw new IND_ExternalServiceException(
                            "OpenAI",
                            "No se pudo normalizar el ticket porque el servicio de IA recorto la respuesta antes de terminar.",
                            IndErrorCodes.AiServiceUnavailable,
                            HttpStatusCode.ServiceUnavailable,
                            "max_output_tokens");
                    }

                    var extracted = TryParseExpenseDraft(responseBody);
                    if (extracted == null)
                    {
                        _logger.Log("[OPENAI-NORMALIZE] Respuesta sin json valido de normalizacion de ticket.", AxaptaSessionManager.LogLevel.Warning);
                        throw new IND_ExternalServiceException(
                            "OpenAI",
                            "No se pudo normalizar el ticket porque el servicio de IA devolvio una respuesta invalida.",
                            IndErrorCodes.AiServiceUnavailable,
                            HttpStatusCode.ServiceUnavailable,
                            "invalid-json");
                    }

                    ApplyCurrencyFallbackFromOcr(extracted, receiptAnalysis);
                    ReconcileDraftTotalFromOcr(extracted, receiptAnalysis);
                    extracted.gastoType = ResolveDraftGastoType(extracted.gastoType, extracted.lines);

                    var successMetrics = TryReadResponseMetrics(responseBody);
                    var normalizedJson = BuildNormalizedDraftJson(extracted, requestOptions.DraftProfile);
                    _logger.Log(
                        $"[OPENAI-NORMALIZE] Receipt normalization completed ms={sw.ElapsedMilliseconds} attempts={attempt} draftProfile={GetDraftProfileText(requestOptions.DraftProfile)} profile={requestOptions.ProfileTag} model={requestOptions.Model} requestedTier={requestOptions.ServiceTier ?? "auto"} reasoningEffort={requestOptions.ReasoningEffort ?? "na"} actualTier={successMetrics.ActualServiceTier ?? "na"} inputTokens={ToMetricText(successMetrics.InputTokens)} cachedTokens={ToMetricText(successMetrics.CachedTokens)} outputTokens={ToMetricText(successMetrics.OutputTokens)} reasoningTokens={ToMetricText(successMetrics.ReasoningTokens)} totalTokens={ToMetricText(successMetrics.TotalTokens)} inputCurrency={ToMetricText(receiptAnalysis.CurrencyCode)} outputCurrency={ToMetricText(extracted.currencyCode)} rawCurrency={ToMetricText(extracted.RawCurrency)} normalizedJsonChars={normalizedJson.Length}",
                        AxaptaSessionManager.LogLevel.Info);

                    return new OpenAITicketNormalizationResult
                    {
                        Draft = extracted,
                        NormalizedJson = normalizedJson,
                        Attempts = attempt
                    };
                }
            }
            catch (TaskCanceledException ex)
            {
                _logger.Log("[OPENAI-NORMALIZE] Peticion cancelada: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                if (cancellationToken.IsCancellationRequested)
                    throw;

                throw new IND_ExternalServiceException(
                    "OpenAI",
                    "La normalizacion del ticket tardo demasiado y el servicio de IA no respondio a tiempo.",
                    IndErrorCodes.ExternalServiceTimeout,
                    HttpStatusCode.GatewayTimeout,
                    "timeout",
                    ex);
            }
            catch (Exception ex) when (!(ex is InvalidOperationException))
            {
                _logger.Log("[OPENAI-NORMALIZE] Error normalizando OCR: " + ex.Message, AxaptaSessionManager.LogLevel.Warning);
                if (ex is IND_OpenAiRateLimitException || ex is IND_ExternalServiceException)
                    throw;

                throw;
            }
            finally
            {
                response?.Dispose();
            }
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

        private static int ReadTimeoutFromConfig()
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(TimeoutSettingKey);
                if (int.TryParse(value, out var parsed) && parsed > 0)
                    return parsed;
            }
            catch
            {
                // Ignore and return default.
            }

            return DefaultTimeoutSeconds;
        }

        private static int ReadMaxImageBytesFromConfig()
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(MaxImageBytesSettingKey);
                if (int.TryParse(value, out var parsed) && parsed > 0)
                    return parsed;
            }
            catch
            {
                // Ignore and return default.
            }

            return DefaultMaxImageBytes;
        }

        private static int ReadMaxOutputTokensFromConfig()
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(MaxOutputTokensSettingKey);
                if (int.TryParse(value, out var parsed) && parsed >= 256)
                    return parsed;
            }
            catch
            {
                // Ignore and return default.
            }

            return DefaultMaxOutputTokens;
        }

        private static int ReadQuickCreateMaxOutputTokensFromConfig()
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(QuickCreateMaxOutputTokensSettingKey);
                if (int.TryParse(value, out var parsed) && parsed >= 256)
                    return parsed;
            }
            catch
            {
                // Ignore and return default.
            }

            return DefaultQuickCreateMaxOutputTokens;
        }

        private static string ReadModelFromConfig()
        {
            try
            {
                var value = AppSettingsHelper.GetSetting(ModelSettingKey);
                return string.IsNullOrWhiteSpace(value) ? DefaultModel : value.Trim();
            }
            catch
            {
                return DefaultModel;
            }
        }

        private static string ReadImageDetailFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(ImageDetailSettingKey);
                return NormalizeImageDetail(configured);
            }
            catch
            {
                return DefaultImageDetail;
            }
        }

        private static string ReadQuickCreateImageDetailFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(QuickCreateImageDetailSettingKey);
                if (string.IsNullOrWhiteSpace(configured))
                    return DefaultQuickCreateImageDetail;

                return NormalizeImageDetail(configured);
            }
            catch
            {
                return DefaultQuickCreateImageDetail;
            }
        }

        private static string ReadServiceTierFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(ServiceTierSettingKey);
                return NormalizeServiceTier(configured);
            }
            catch
            {
                return DefaultServiceTier;
            }
        }

        private static string ReadQuickCreateServiceTierFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(QuickCreateServiceTierSettingKey);
                if (string.IsNullOrWhiteSpace(configured))
                    return DefaultServiceTier;

                return NormalizeServiceTier(configured);
            }
            catch
            {
                return DefaultServiceTier;
            }
        }

        private static string ReadProfileTagFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(ProfileTagSettingKey);
                return string.IsNullOrWhiteSpace(configured) ? DefaultProfileTag : configured.Trim();
            }
            catch
            {
                return DefaultProfileTag;
            }
        }

        private static string ReadQuickCreateProfileTagFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(QuickCreateProfileTagSettingKey);
                return string.IsNullOrWhiteSpace(configured) ? DefaultQuickCreateProfileTag : configured.Trim();
            }
            catch
            {
                return DefaultQuickCreateProfileTag;
            }
        }

        private static string ReadPromptCacheKeyFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(PromptCacheKeySettingKey);
                return string.IsNullOrWhiteSpace(configured) ? DefaultPromptCacheKey : configured.Trim();
            }
            catch
            {
                return DefaultPromptCacheKey;
            }
        }

        private static string ReadQuickCreatePromptCacheKeyFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(QuickCreatePromptCacheKeySettingKey);
                return string.IsNullOrWhiteSpace(configured) ? DefaultQuickCreatePromptCacheKey : configured.Trim();
            }
            catch
            {
                return DefaultQuickCreatePromptCacheKey;
            }
        }

        private static string ReadReasoningEffortFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(ReasoningEffortSettingKey);
                return NormalizeReasoningEffort(configured);
            }
            catch
            {
                return DefaultReasoningEffort;
            }
        }

        private static string ReadQuickCreateReasoningEffortFromConfig()
        {
            try
            {
                var configured = AppSettingsHelper.GetSetting(QuickCreateReasoningEffortSettingKey);
                if (string.IsNullOrWhiteSpace(configured))
                    return _NormalizeQuickCreateReasoningFallback();

                return NormalizeReasoningEffort(configured);
            }
            catch
            {
                return _NormalizeQuickCreateReasoningFallback();
            }
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

        private static string GetNormalizedDataContentType(string contentType)
        {
            if (string.IsNullOrWhiteSpace(contentType))
                return "image/jpeg";

            var normalized = contentType;
            if (normalized.IndexOf(';') >= 0)
                normalized = normalized.Split(';')[0];

            normalized = normalized.Trim().ToLowerInvariant();

            switch (normalized)
            {
                case "image/pjpeg":
                    return "image/jpeg";
                default:
                    return normalized;
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

        // Builds the effective request profile for one OpenAI draft attempt.
        private ExpenseTicketRequestOptions BuildRequestOptions(
            ExpenseTicketDraftProfile draftProfile,
            string serviceTierOverride,
            int? maxOutputTokensOverride = null)
        {
            var maxOutputTokens = _maxOutputTokens;
            var imageDetail = _imageDetail;
            var serviceTier = _serviceTier;
            var profileTag = _profileTag;
            var promptCacheKey = _promptCacheKey;
            var reasoningEffort = _reasoningEffort;

            if (draftProfile == ExpenseTicketDraftProfile.QuickCreate)
            {
                maxOutputTokens = _quickCreateMaxOutputTokens;
                imageDetail = _quickCreateImageDetail;
                serviceTier = _quickCreateServiceTier;
                profileTag = _quickCreateProfileTag;
                promptCacheKey = _quickCreatePromptCacheKey;
                reasoningEffort = _quickCreateReasoningEffort;
            }

            var normalizedServiceTier = NormalizeServiceTier(string.IsNullOrWhiteSpace(serviceTierOverride) ? serviceTier : serviceTierOverride);
            return new ExpenseTicketRequestOptions
            {
                DraftProfile = draftProfile,
                Model = _model,
                ImageDetail = NormalizeImageDetail(imageDetail),
                MaxOutputTokens = maxOutputTokensOverride ?? maxOutputTokens,
                ProfileTag = profileTag,
                PromptCacheKey = promptCacheKey,
                ServiceTier = normalizedServiceTier,
                ReasoningEffort = NormalizeReasoningEffort(reasoningEffort)
            };
        }

        // Expands output budget once when the model reports truncation.
        private static bool TryBuildExpandedRequestOptions(ExpenseTicketRequestOptions currentOptions, out ExpenseTicketRequestOptions expandedOptions)
        {
            expandedOptions = null;
            if (currentOptions == null)
                return false;

            var expandedMaxOutputTokens = GetExpandedMaxOutputTokens(currentOptions.MaxOutputTokens);
            if (expandedMaxOutputTokens <= currentOptions.MaxOutputTokens)
                return false;

            expandedOptions = new ExpenseTicketRequestOptions
            {
                DraftProfile = currentOptions.DraftProfile,
                Model = currentOptions.Model,
                ImageDetail = currentOptions.ImageDetail,
                MaxOutputTokens = expandedMaxOutputTokens,
                ProfileTag = currentOptions.ProfileTag,
                PromptCacheKey = currentOptions.PromptCacheKey,
                ServiceTier = currentOptions.ServiceTier,
                ReasoningEffort = currentOptions.ReasoningEffort
            };
            return true;
        }

        private static int GetExpandedMaxOutputTokens(int currentMaxOutputTokens)
        {
            if (currentMaxOutputTokens <= 0)
                return DefaultMaxOutputTokens;

            var doubled = currentMaxOutputTokens >= (MaxRetryOutputTokens / 2)
                ? MaxRetryOutputTokens
                : currentMaxOutputTokens * 2;

            return Math.Min(MaxRetryOutputTokens, doubled);
        }

        private static string BuildPayloadJson(string base64Image, string contentType, string fileName, string prompt, ExpenseTicketRequestOptions requestOptions)
        {
            var format = new JObject
            {
                ["type"] = "json_schema",
                ["name"] = BuildResponseFormatName(requestOptions.DraftProfile),
                ["schema"] = BuildResponseSchema(requestOptions.DraftProfile),
                ["strict"] = true
            };

            var payload = new JObject
            {
                ["model"] = requestOptions.Model,
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
                                ["type"] = "input_image",
                                ["image_url"] = $"data:{contentType};base64,{base64Image}",
                                ["detail"] = requestOptions.ImageDetail
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
                    ["expense_ticket_profile"] = requestOptions.ProfileTag,
                    ["expense_ticket_draft_profile"] = GetDraftProfileText(requestOptions.DraftProfile),
                    ["expense_ticket_detail"] = requestOptions.ImageDetail,
                    ["expense_ticket_requested_tier"] = requestOptions.ServiceTier ?? "auto",
                    ["expense_ticket_reasoning_effort"] = requestOptions.ReasoningEffort ?? "na"
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

        private static string BuildTextPayloadJson(string prompt, string structuredOcrJson, string fileName, ExpenseTicketRequestOptions requestOptions)
        {
            var format = new JObject
            {
                ["type"] = "json_schema",
                ["name"] = BuildResponseFormatName(requestOptions.DraftProfile),
                ["schema"] = BuildResponseSchema(requestOptions.DraftProfile),
                ["strict"] = true
            };

            var payload = new JObject
            {
                ["model"] = requestOptions.Model,
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
                                ["text"] = structuredOcrJson
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
                    ["expense_ticket_profile"] = requestOptions.ProfileTag,
                    ["expense_ticket_draft_profile"] = GetDraftProfileText(requestOptions.DraftProfile),
                    ["expense_ticket_input_source"] = "azure-docs-json",
                    ["expense_ticket_requested_tier"] = requestOptions.ServiceTier ?? "auto",
                    ["expense_ticket_reasoning_effort"] = requestOptions.ReasoningEffort ?? "na"
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

        private static string BuildResponseFormatName(ExpenseTicketDraftProfile profile)
        {
            return profile == ExpenseTicketDraftProfile.QuickCreate
                ? "expense_ticket_quick_create_draft"
                : "expense_ticket_draft";
        }

        private static string BuildPayloadPromptText(ExpenseTicketDraftProfile profile)
        {
            return profile == ExpenseTicketDraftProfile.QuickCreate
                ? BuildQuickCreatePayloadPromptText()
                : BuildFullDraftPayloadPromptText();
        }

        private static string BuildStructuredOcrPayloadPromptText(ExpenseTicketDraftProfile profile)
        {
            return
                @"Recibiras un JSON OCR compacto de Azure Document Intelligence.
- Tu tarea es convertir ese JSON al contrato CRM.
- Responde SOLO JSON valido.
- Usa el OCR como fuente principal.
- El JSON puede incluir items estructurados y tambien ocrText/ocrLines con texto OCR completo; usa ocrText/ocrLines solo para recuperar conceptos, importes, cantidades, fecha, hora y moneda.
- totals.total representa el total bruto final a pagar; nunca uses totals.subtotal, base imponible, IVA/VAT/tax o propina aislados como totalAmount.
- totalAmount debe representar totals.total. El backend lo validara y fijara de forma determinista desde el OCR.
- En VND, interpreta los enteros agrupados con punto o coma como separadores de miles (82.000 VND = 82000 VND); no apliques esta regla a otras monedas.
- Si aparece currencyCode, rawCurrency o currencyHints, usalos para devolver currencyCode en ISO-4217 (EUR, USD, GBP, etc.).
- No inventes datos ni lineas.
- Omite metadatos opcionales si no aportan valor.
- Prioriza exactitud estructural y salida breve."
                .Trim() + Environment.NewLine + BuildPayloadPromptText(profile);
        }

        private static string BuildFullDraftPayloadPromptText()
        {
            return @"Eres un extractor para construir un borrador de hoja de gasto y lineas con este esquema.
- Responde SOLO JSON valido, sin markdown.
- Si un campo no se puede inferir con confianza, usa null y agrega advertencia en warnings.
- typeValue debe ser siempre el valor numerico real de CRMGastoType segun la tabla fija incluida abajo.
- gastoType en cabecera debe usar el mismo enum AX numerico.
- gastoType representa el tipo de gasto dominante del ticket.
- totalAmount debe ser el total bruto final a pagar del ticket, nunca la base imponible, subtotal o impuesto aislado.
- Si no hay evidencia clara para gastoType, usa 8.
- Si no hay evidencia clara de tipo, usa 8.
- qty debe ser la cantidad real de la linea (admite decimales). Solo puede ser 0 cuando la linea sea un descuento con lineTotal negativo visible.
- price debe representar el precio unitario de la linea.
- lineTotal debe representar el total bruto de la linea (qty * price) cuando sea visible.
- Si la linea es un descuento, devuelve price y lineTotal en negativo.
- Si detectas lineTotal y qty > 0, asegura coherencia: price = lineTotal / qty, incluso cuando lineTotal sea negativo.
- Usa punto como separador decimal en todos los numeros del JSON (ej: 3.50, 12.00).
- No uses separadores de miles en los numeros del JSON.
- Si solo detectas un importe unico para la linea y qty=1, usa ese valor como price y lineTotal.
- ticketDate debe ser la fecha impresa del ticket, en formato DD.MM.YYYY o null si no se puede inferir.
- ticketTime debe ser la hora impresa del ticket, en formato HH:mm:ss de 24 horas o null si no se puede inferir.
- transDate de cabecera debe usar la misma fecha que ticketDate por compatibilidad; si no hay fecha confiable, usa null.
- transDate de cada linea debe usar la misma fecha que ticketDate por compatibilidad; si no hay fecha confiable, usa null.
- fileId debe ser null en todas las lineas (se asigna despues en backend).
- qty por defecto 1 salvo evidencia fuerte; no uses 0 salvo descuentos visibles con total negativo.
- internacional true solo si hay evidencia de gasto internacional.
- description corto y util para una linea de gasto.
- currencyCode en cabecera debe ir siempre en ISO-4217 uppercase si existe evidencia suficiente.
- Usa rawCurrency y currencyHints para normalizar simbolos o nombres a ISO-4217.
- Si detectas simbolos o nombres de moneda, conserva la mejor pista en rawCurrency.
- metadata adicionales: confidence, warnings, rawCurrency y merchant.
- Deduce la moneda y el valor monetario de la imagen, sin soporte externo.
- Si un campo es imposible de inferir con calidad suficiente, usa null y deja una advertencia clara."
                .Trim() + Environment.NewLine + CrmGastoTypePromptCatalog;
        }

        private static string BuildQuickCreatePayloadPromptText()
        {
            return @"Eres un extractor para alta rapida de tickets de gasto.
- Responde SOLO JSON valido, sin markdown.
- Devuelve SIEMPRE TODAS las lineas detectables del ticket.
- No agrupes, no resumas y no combines multiples conceptos en una sola linea si aparecen separados en el ticket.
- Si el ticket muestra varios conceptos o importes parciales, devuelve una linea por cada concepto visible.
- gastoType en cabecera es obligatorio y debe reflejar el tipo dominante del ticket usando el valor numerico real de CRMGastoType segun la tabla fija incluida abajo.
- totalAmount debe ser el total bruto final a pagar del ticket, nunca la base imponible, subtotal o impuesto aislado.
- No devuelvas typeValue por linea. Solo resuelve gastoType en cabecera.
- Cada linea debe incluir como minimo description, qty y price.
- ticketDate debe ser la fecha impresa del ticket y debe ir solo en cabecera, en formato DD.MM.YYYY o null.
- ticketTime debe ser la hora impresa del ticket y debe ir solo en cabecera, en formato HH:mm:ss de 24 horas o null.
- transDate debe ir solo en cabecera y debe usar la misma fecha que ticketDate por compatibilidad; si no hay fecha confiable, usa null.
- No incluyas transDate por linea.
- Incluye lineTotal solo si aporta algo distinto de qty*price.
- Si qty no es visible, usa 1.
- Solo usa qty 0 cuando la linea sea un descuento con lineTotal negativo visible.
- price debe ser el precio unitario, negativo cuando la linea sea un descuento.
- Si hay total visible de linea y qty > 0, asegura coherencia: price = total / qty, incluso cuando total sea negativo.
- Usa punto como separador decimal en todos los numeros del JSON.
- No uses separadores de miles en los numeros del JSON.
- description debe ser corta y util para la linea.
- description de cabecera debe ser corta y util para el ticket.
- currencyCode en cabecera debe ir en ISO-4217 uppercase si existe evidencia monetaria razonable.
- Usa currencyHints, rawCurrency y los importes OCR para normalizar simbolos o nombres a ISO-4217.
- Si detectas una pista util de moneda no ISO, guardala tambien en rawCurrency.
- Omite warnings y merchant salvo que aporten valor real.
- Si un campo de cabecera no se puede inferir con confianza, usa null.
- No inventes lineas ni importes. Pero si una linea es visible, debes devolverla."
                .Trim() + Environment.NewLine + CrmGastoTypePromptCatalog;
        }

        //MMS - Centraliza la tabla fija de CRMGastoType para prompts IA - 2026.06.26
        private static string BuildCrmGastoTypePromptCatalog()
        {
            return "- Tabla fija CRMGastoType: 0 None, 1 Peajes, 2 Parking, 3 Km, 4 Desayuno, 5 Comida, 6 Cena, 7 Hotel, 8 Varios, 9 Montaje Nacional, 10 Montaje Nacional Festivo, 11 Montaje Internacional, 12 Montaje Internacional Festivo, 13 Dia de Viaje Nacional, 14 Taxi, 15 Dia Viaje Festivo Nacional, 16 Dia Viaje Internacional, 17 Dia Viaje Festivo Internacional, 18 Horas, 19 Propinas, 20 Gasolina.";
        }

        private static string NormalizeReasoningEffort(string configuredValue)
        {
            if (string.IsNullOrWhiteSpace(configuredValue))
                return DefaultReasoningEffort;

            var normalized = configuredValue.Trim().ToLowerInvariant();
            switch (normalized)
            {
                // Preserve backward compatibility with legacy env values.
                case "minimal":
                    return DefaultReasoningEffort;
                case "none":
                case "low":
                case "medium":
                case "high":
                case "xhigh":
                    return normalized;
                default:
                    return DefaultReasoningEffort;
            }
        }

        private static string _NormalizeQuickCreateReasoningFallback()
        {
            return string.IsNullOrWhiteSpace(DefaultReasoningEffort)
                ? "low"
                : DefaultReasoningEffort;
        }

        private static string GetDraftProfileText(ExpenseTicketDraftProfile profile)
        {
            return profile == ExpenseTicketDraftProfile.QuickCreate ? "quick-create" : "full-draft";
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
                return string.Join(" ", new[] { type, code, message }.Where(p => !string.IsNullOrWhiteSpace(p)));
            }
            catch
            {
                return string.Empty;
            }
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

        private static string TryExtractIncompleteReason(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return null;

            try
            {
                var root = JObject.Parse(responseBody);
                return root["incomplete_details"]?["reason"]?.ToString();
            }
            catch
            {
                return null;
            }
        }

        private static ExpenseTicketResponseMetrics TryReadResponseMetrics(string responseBody)
        {
            if (string.IsNullOrWhiteSpace(responseBody))
                return new ExpenseTicketResponseMetrics();

            try
            {
                var root = JObject.Parse(responseBody);
                var usage = root["usage"];
                return new ExpenseTicketResponseMetrics
                {
                    ActualServiceTier = root["service_tier"]?.ToString(),
                    InputTokens = ToNullableInt(usage?["input_tokens"]),
                    CachedTokens = ToNullableInt(usage?["input_tokens_details"]?["cached_tokens"]),
                    OutputTokens = ToNullableInt(usage?["output_tokens"]),
                    ReasoningTokens = ToNullableInt(usage?["output_tokens_details"]?["reasoning_tokens"]),
                    TotalTokens = ToNullableInt(usage?["total_tokens"])
                };
            }
            catch
            {
                return new ExpenseTicketResponseMetrics();
            }
        }

        private static int? ToNullableInt(JToken token)
        {
            if (token == null)
                return null;

            if (token.Type == JTokenType.Integer)
                return token.Value<int>();

            if (int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return null;
        }

        private static string ToMetricText(int? value)
        {
            return value.HasValue ? value.Value.ToString(CultureInfo.InvariantCulture) : "na";
        }

        private static string ToMetricText(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "na" : value.Trim();
        }

        private static string NormalizeImageDetail(string configuredValue)
        {
            if (string.IsNullOrWhiteSpace(configuredValue))
                return DefaultImageDetail;

            var normalized = configuredValue.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "auto":
                case "low":
                case "high":
                case "original":
                    return normalized;
                default:
                    return DefaultImageDetail;
            }
        }

        private static string NormalizeServiceTier(string configuredValue)
        {
            if (string.IsNullOrWhiteSpace(configuredValue))
                return DefaultServiceTier;

            var normalized = configuredValue.Trim().ToLowerInvariant();
            switch (normalized)
            {
                case "auto":
                case "default":
                case "flex":
                case "priority":
                    return normalized;
                default:
                    return DefaultServiceTier;
            }
        }

        private static ExpenseSheetDraftResponse TryParseExpenseDraft(string responseBody)
        {
            var json = TryExtractOpenAiPayloadJson(responseBody);
            if (string.IsNullOrWhiteSpace(json))
                return null;

            var root = JObject.Parse(json);
            var rawCurrency = NormalizeText(root["rawCurrency"]?.ToString(), null);
            var currencyCode = CurrencyCodeHelper.ResolveToIso4217(
                root["currencyCode"]?.ToString(),
                rawCurrency);
            var normalizedTicketDate = NormalizeDate(root["ticketDate"]?.ToString());
            var normalizedTransDate = NormalizeDate(root["transDate"]?.ToString()) ?? normalizedTicketDate;

            var request = new ExpenseSheetDraftResponse
            {
                mode = 0,
                userId = string.Empty,
                description = NormalizeText(root["description"]?.ToString(), "Ticket"),
                currencyCode = string.IsNullOrWhiteSpace(currencyCode) ? null : currencyCode,
                gastoType = NormalizeTypeValue(root["gastoType"]),
                transDate = normalizedTransDate,
                ticketDate = normalizedTicketDate ?? normalizedTransDate,
                ticketTime = NormalizeTime(root["ticketTime"]?.ToString()),
                totalAmount = TryParseDecimal(root["totalAmount"]),
                exchRate = TryParseDecimal(root["exchRate"]),
                projId = NormalizeText(root["projId"]?.ToString(), null),
                lines = new List<CreateExpenseSheetLineRequest>(),
                Confidence = NormalizeConfidence(root["confidence"]),
                Warnings = ExtractWarnings(root["warnings"]),
                RawCurrency = rawCurrency,
                Merchant = NormalizeText(root["merchant"]?.ToString(), null)
            };

            var warnings = request.Warnings;
            if (string.IsNullOrWhiteSpace(request.currencyCode))
            {
                warnings = EnsureWarnings(warnings, "No se detecto currencyCode en el ticket. Revisar manualmente.");
            }

            var lines = root["lines"] as JArray;
            if (lines != null)
            {
                foreach (var line in lines)
                {
                    var mapped = TryMapLine(line as JObject, request);
                    if (mapped != null)
                        request.lines.Add(mapped);
                }
            }

            if (request.lines == null || request.lines.Count == 0)
            {
                request.lines.Add(MapFallbackLine(request));
                request.Warnings = EnsureWarnings(warnings, "No se detecto ninguna linea valida. Se genera una linea de respaldo para revision manual.");
                request.Confidence = request.Confidence.HasValue ? request.Confidence : 0m;
            }

            request.gastoType = ResolveDraftGastoType(request.gastoType, request.lines);

            if (request.Warnings == null || request.Warnings.Count == 0)
                request.Warnings = null;

            return request;
        }

        private static void ApplyCurrencyFallbackFromOcr(ExpenseSheetDraftResponse draft, AzureReceiptAnalysisResult receiptAnalysis)
        {
            if (draft == null)
                return;

            if (receiptAnalysis?.HasAuthoritativeVndTotal == true)
            {
                draft.currencyCode = "VND";
                draft.RawCurrency = NormalizeText(receiptAnalysis.RawCurrency, "VND");
                draft.Warnings = RemoveCurrencyMissingWarning(draft.Warnings);
                return;
            }

            var fallbackCurrencyCode = CurrencyCodeHelper.ResolveToIso4217(
                draft.currencyCode,
                draft.RawCurrency,
                receiptAnalysis?.CurrencyCode,
                receiptAnalysis?.RawCurrency,
                receiptAnalysis?.CurrencyHints == null ? null : string.Join(" ", receiptAnalysis.CurrencyHints));

            if (!string.IsNullOrWhiteSpace(fallbackCurrencyCode))
                draft.currencyCode = fallbackCurrencyCode;

            if (string.IsNullOrWhiteSpace(draft.RawCurrency))
            {
                draft.RawCurrency = NormalizeText(
                    receiptAnalysis?.RawCurrency
                    ?? (receiptAnalysis?.CurrencyHints == null ? null : receiptAnalysis.CurrencyHints.FirstOrDefault()),
                    null);
            }

            if (!string.IsNullOrWhiteSpace(draft.currencyCode))
            {
                draft.Warnings = RemoveCurrencyMissingWarning(draft.Warnings);
                return;
            }

            draft.Warnings = EnsureWarnings(draft.Warnings, "No se detecto currencyCode en el ticket. Revisar manualmente.");
        }

        //MMS - Reconciles the draft with the OCR total and requires an exact match for authoritative VND totals - 2026.08.31
        private static void ReconcileDraftTotalFromOcr(ExpenseSheetDraftResponse draft, AzureReceiptAnalysisResult receiptAnalysis)
        {
            if (draft == null)
                return;

            var totalAmount = receiptAnalysis?.TotalAmount;
            draft.totalAmount = totalAmount.HasValue && totalAmount.Value > 0m
                ? totalAmount
                : null;
            if (!draft.totalAmount.HasValue)
                return;

            var fallbackTypeValue = draft.gastoType.HasValue && IsValidAxEnumValue(draft.gastoType.Value)
                ? draft.gastoType.Value
                : DefaultGastoTypeValue;
            var fallbackTransDate = ResolveOcrTotalLineTransDate(draft);
            var sourceLineCount = draft.lines?.Count ?? 0;
            var validLines = (draft.lines ?? new List<CreateExpenseSheetLineRequest>())
                .Where(IsValidDraftLineForTotal)
                .ToList();
            if (validLines.Count == 0)
            {
                draft.lines = new List<CreateExpenseSheetLineRequest>
                {
                    BuildOcrTotalLine(
                        ResolveSingleLineFallbackDescription(draft, fallbackTypeValue),
                        totalAmount.Value,
                        fallbackTypeValue,
                        fallbackTransDate,
                        draft.projId)
                };

                draft.Warnings = EnsureWarnings(
                    draft.Warnings,
                    "No se detectaron lineas validas; se genero una linea unica con el total bruto OCR.");
                return;
            }

            draft.lines = validLines;
            if (validLines.Count != sourceLineCount)
            {
                draft.Warnings = EnsureWarnings(
                    draft.Warnings,
                    "Se descartaron lineas sin importe valido antes de reconciliar el total bruto OCR.");
            }

            var validLinesTotal = validLines.Sum(CalculateValidDraftLineTotal);
            var difference = totalAmount.Value - validLinesTotal;
            var reconciliationTolerance = receiptAnalysis?.HasAuthoritativeVndTotal == true
                ? 0m
                : OcrTotalTolerance;
            if (Math.Abs(difference) <= reconciliationTolerance)
                return;

            draft.lines.Add(BuildOcrTotalLine(
                OcrTotalAdjustmentDescription,
                difference,
                fallbackTypeValue,
                fallbackTransDate,
                draft.projId));

            draft.Warnings = EnsureWarnings(
                draft.Warnings,
                "La suma de lineas no coincidia con el total bruto OCR; se agrego una linea de ajuste.");
        }

        // Keeps only lines whose signed amount can pass the ticket persistence rules.
        private static bool IsValidDraftLineForTotal(CreateExpenseSheetLineRequest line)
        {
            if (line == null || !line.price.HasValue || line.price.Value == 0m)
                return false;

            var qty = line.qty ?? 0m;
            if (qty < 0m)
                return false;

            return qty > 0m || (qty == 0m && line.price.Value < 0m);
        }

        // Calculates the signed amount exactly as the quick-create mapper will persist it.
        private static decimal CalculateValidDraftLineTotal(CreateExpenseSheetLineRequest line)
        {
            var qty = line?.qty ?? 0m;
            var price = line?.price ?? 0m;
            return qty == 0m && price < 0m ? price : qty * price;
        }

        // Builds either a positive gross-total line or a zero-quantity negative adjustment.
        private static CreateExpenseSheetLineRequest BuildOcrTotalLine(
            string description,
            decimal amount,
            int typeValue,
            string transDate,
            string projId)
        {
            return new CreateExpenseSheetLineRequest
            {
                transDate = transDate,
                typeValue = typeValue,
                description = description,
                internacional = false,
                fileId = null,
                qty = amount < 0m ? 0m : 1m,
                price = amount,
                projId = projId
            };
        }

        // Reuses the first detected line date and falls back to the normalized header date.
        private static string ResolveOcrTotalLineTransDate(ExpenseSheetDraftResponse draft)
        {
            var lineTransDate = draft?.lines?
                .FirstOrDefault(line => line != null && !string.IsNullOrWhiteSpace(line.transDate))?
                .transDate;
            return string.IsNullOrWhiteSpace(lineTransDate) ? draft?.transDate : lineTransDate;
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
                return TrimJsonBlock(responseBody);
            }
        }

        private static string TrimJsonBlock(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return null;

            var trimmed = text.Trim();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) && trimmed.EndsWith("```", StringComparison.Ordinal))
                trimmed = trimmed.Substring(3, trimmed.Length - 6).Trim();

            var start = trimmed.IndexOf('{');
            var end = trimmed.LastIndexOf('}');
            if (start < 0 || end <= start)
                return null;

            return trimmed.Substring(start, end - start + 1);
        }

        private static CreateExpenseSheetLineRequest TryMapLine(JObject lineToken, ExpenseSheetDraftResponse request)
        {
            if (lineToken == null)
                return null;

            var warnings = request.Warnings ?? new List<string>();

            var transDate = NormalizeDate(lineToken["transDate"]?.ToString());
            if (string.IsNullOrWhiteSpace(transDate))
                warnings = EnsureWarnings(warnings, "No se pudo inferir fecha de gasto. Se deja transDate null.");

            var qtyParsed = TryParseDecimal(lineToken["qty"]);
            var price = TryParseDecimal(lineToken["price"]);
            var lineTotal = TryParseDecimal(lineToken["lineTotal"]);
            var isZeroQtyDiscount = qtyParsed.HasValue &&
                                    qtyParsed.Value == 0m &&
                                    ((lineTotal.HasValue && lineTotal.Value < 0m) || (price.HasValue && price.Value < 0m));
            var qty = qtyParsed.HasValue && (qtyParsed.Value > 0m || isZeroQtyDiscount) ? qtyParsed.Value : 1m;
            if (!qtyParsed.HasValue || qtyParsed.Value < 0m || (qtyParsed.Value == 0m && !isZeroQtyDiscount))
                warnings = EnsureWarnings(warnings, "No se detecto qty valida. Se uso qty=1 por defecto.");

            if (!price.HasValue && lineTotal.HasValue && qty > 0m)
            {
                price = Math.Round(lineTotal.Value / qty, 4, MidpointRounding.AwayFromZero);
                warnings = EnsureWarnings(warnings, "price se calculo desde lineTotal/qty por falta de precio unitario explicito.");
            }
            else if (!price.HasValue && lineTotal.HasValue && qty == 0m && lineTotal.Value < 0m)
            {
                price = lineTotal.Value;
                warnings = EnsureWarnings(warnings, "price se calculo desde lineTotal para descuento con qty=0.");
            }

            if (price.HasValue && lineTotal.HasValue && qty > 0m)
            {
                var expectedTotal = price.Value * qty;
                if (Math.Abs(expectedTotal - lineTotal.Value) > 0.02m)
                {
                    var normalizedPrice = Math.Round(lineTotal.Value / qty, 4, MidpointRounding.AwayFromZero);
                    if (normalizedPrice != 0m)
                    {
                        price = normalizedPrice;
                        warnings = EnsureWarnings(warnings, "Se ajusto price para mantener coherencia con qty y lineTotal detectado.");
                    }
                }
            }
            else if (price.HasValue && lineTotal.HasValue && qty == 0m && lineTotal.Value < 0m && price.Value != lineTotal.Value)
            {
                price = lineTotal.Value;
                warnings = EnsureWarnings(warnings, "Se ajusto price para mantener descuento con qty=0 y lineTotal negativo.");
            }

            if (!price.HasValue)
                warnings = EnsureWarnings(warnings, "No se detecto el price de la linea. Revisar manualmente.");

            var line = new CreateExpenseSheetLineRequest
            {
                transDate = transDate,
                typeValue = NormalizeTypeValue(lineToken["typeValue"]),
                description = NormalizeText(lineToken["description"]?.ToString(), "Ticket"),
                internacional = TryParseBool(lineToken["internacional"]),
                fileId = NormalizeText(lineToken["fileId"]?.ToString(), null),
                qty = qty,
                price = price,
                projId = NormalizeText(lineToken["projId"]?.ToString(), request?.projId)
            };

            request.Warnings = warnings;
            return line;
        }

        private static CreateExpenseSheetLineRequest MapFallbackLine(ExpenseSheetDraftResponse request)
        {
            return new CreateExpenseSheetLineRequest
            {
                transDate = null,
                typeValue = DefaultGastoTypeValue,
                description = NormalizeText(request?.description, "Ticket"),
                internacional = false,
                fileId = null,
                qty = 1m,
                price = null,
                projId = request?.projId
            };
        }

        private static string ResolveSingleLineFallbackDescription(ExpenseSheetDraftResponse draft, int fallbackTypeValue)
        {
            var firstLineDescription = draft?.lines?.Select(line => NormalizeText(line?.description, null))
                .FirstOrDefault(text => !string.IsNullOrWhiteSpace(text) && !string.Equals(text, "Ticket", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(firstLineDescription))
                return firstLineDescription;

            var headerDescription = NormalizeText(draft?.description, null);
            if (!string.IsNullOrWhiteSpace(headerDescription) && !string.Equals(headerDescription, "Ticket", StringComparison.OrdinalIgnoreCase))
                return headerDescription;

            var merchant = NormalizeText(draft?.Merchant, null);
            if (!string.IsNullOrWhiteSpace(merchant))
                return merchant;

            return "Gasto " + fallbackTypeValue.ToString(CultureInfo.InvariantCulture);
        }

        private static List<string> ExtractWarnings(JToken warningsToken)
        {
            var warnings = new List<string>();

            if (warningsToken == null)
                return warnings;

            if (warningsToken.Type == JTokenType.Array)
            {
                foreach (var warning in (JArray)warningsToken)
                {
                    var text = warning?.ToString();
                    if (!string.IsNullOrWhiteSpace(text))
                        warnings.Add(text.Trim());
                }
            }
            else if (warningsToken.Type == JTokenType.String)
            {
                var text = warningsToken.ToString();
                if (!string.IsNullOrWhiteSpace(text))
                    warnings.Add(text.Trim());
            }

            return warnings;
        }

        private static List<string> EnsureWarnings(List<string> existing, string warning)
        {
            if (string.IsNullOrWhiteSpace(warning))
                return existing;

            if (existing == null)
                existing = new List<string>();

            existing.Add(warning.Trim());
            return existing;
        }

        private static List<string> RemoveCurrencyMissingWarning(List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0)
                return warnings;

            return warnings
                .Where(w => !string.Equals((w ?? string.Empty).Trim(), "No se detecto currencyCode en el ticket. Revisar manualmente.", StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        private static decimal? NormalizeConfidence(JToken token)
        {
            var parsed = TryParseDecimal(token);
            if (!parsed.HasValue)
                return null;

            var value = parsed.Value;
            if (value < 0m)
                return 0m;
            if (value > 1m)
                return 1m;
            return value;
        }

        private static decimal? TryParseDecimal(JToken token)
        {
            if (token == null)
                return null;

            if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                return token.Value<decimal>();

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>() ? 1m : 0m;

            return TryParseDecimalFromText(token.ToString());
        }

        private static decimal? TryParseDecimalFromText(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return null;

            var normalized = NormalizeNumericText(raw);
            if (string.IsNullOrWhiteSpace(normalized))
                return null;

            if (decimal.TryParse(
                normalized,
                NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture,
                out var parsed))
            {
                return parsed;
            }

            if (decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.GetCultureInfo("es-ES"), out parsed))
                return parsed;

            return null;
        }

        private static string NormalizeNumericText(string raw)
        {
            var trimmed = (raw ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
                return string.Empty;

            var filteredChars = trimmed
                .Where(ch => char.IsDigit(ch) || ch == '-' || ch == '+' || ch == '.' || ch == ',')
                .ToArray();
            var candidate = new string(filteredChars);
            if (string.IsNullOrWhiteSpace(candidate))
                return string.Empty;

            if (candidate.Length > 1)
            {
                var hasLeadingSign = candidate[0] == '-' || candidate[0] == '+';
                var sign = hasLeadingSign ? candidate[0].ToString() : string.Empty;
                var unsigned = hasLeadingSign ? candidate.Substring(1) : candidate;
                unsigned = unsigned.Replace("+", string.Empty).Replace("-", string.Empty);
                candidate = sign + unsigned;
            }

            var lastComma = candidate.LastIndexOf(',');
            var lastDot = candidate.LastIndexOf('.');

            if (lastComma >= 0 && lastDot >= 0)
            {
                if (lastComma > lastDot)
                {
                    candidate = candidate.Replace(".", string.Empty);
                    candidate = candidate.Replace(',', '.');
                }
                else
                {
                    candidate = candidate.Replace(",", string.Empty);
                }

                return candidate;
            }

            if (lastComma >= 0)
            {
                var commaCount = candidate.Count(ch => ch == ',');
                if (commaCount > 1)
                {
                    var decimalsLen = candidate.Length - candidate.LastIndexOf(',') - 1;
                    candidate = candidate.Replace(",", string.Empty);
                    if (decimalsLen > 0 && decimalsLen <= 2 && candidate.Length > decimalsLen)
                        candidate = candidate.Insert(candidate.Length - decimalsLen, ".");

                    return candidate;
                }

                var decimals = candidate.Length - lastComma - 1;
                if (decimals > 0 && decimals <= 2)
                    return candidate.Replace(',', '.');

                return candidate.Replace(",", string.Empty);
            }

            if (lastDot >= 0)
            {
                var dotCount = candidate.Count(ch => ch == '.');
                if (dotCount > 1)
                {
                    var decimalsLen = candidate.Length - candidate.LastIndexOf('.') - 1;
                    candidate = candidate.Replace(".", string.Empty);
                    if (decimalsLen > 0 && decimalsLen <= 2 && candidate.Length > decimalsLen)
                        candidate = candidate.Insert(candidate.Length - decimalsLen, ".");

                    return candidate;
                }

                var decimals = candidate.Length - lastDot - 1;
                if (decimals == 3 && lastDot > 1)
                    return candidate.Replace(".", string.Empty);
            }

            return candidate;
        }

        private static int? NormalizeTypeValue(JToken token)
        {
            if (token == null)
                return DefaultGastoTypeValue;

            int parsed;
            if (token.Type == JTokenType.Integer)
                parsed = token.Value<int>();
            else if (!int.TryParse(token.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                return DefaultGastoTypeValue;

            return IsValidAxEnumValue(parsed) ? parsed : DefaultGastoTypeValue;
        }

        private static int ResolveDraftGastoType(int? headerGastoType, List<CreateExpenseSheetLineRequest> lines)
        {
            if (headerGastoType.HasValue && IsValidAxEnumValue(headerGastoType.Value))
                return headerGastoType.Value;

            if (lines != null && lines.Count > 0)
            {
                var firstByType = new Dictionary<int, int>();
                for (int i = 0; i < lines.Count; i++)
                {
                    var typeValue = lines[i]?.typeValue;
                    if (!typeValue.HasValue || !IsValidAxEnumValue(typeValue.Value))
                        continue;

                    if (!firstByType.ContainsKey(typeValue.Value))
                        firstByType[typeValue.Value] = i;
                }

                var dominant = lines
                    .Where(l => l != null && l.typeValue.HasValue && IsValidAxEnumValue(l.typeValue.Value))
                    .GroupBy(l => l.typeValue.Value)
                    .Select(g => new
                    {
                        TypeValue = g.Key,
                        Count = g.Count(),
                        FirstIndex = firstByType.ContainsKey(g.Key) ? firstByType[g.Key] : int.MaxValue
                    })
                    .OrderByDescending(g => g.Count)
                    .ThenBy(g => g.FirstIndex)
                    .FirstOrDefault();

                if (dominant != null)
                    return dominant.TypeValue;
            }

            return DefaultGastoTypeValue;
        }

        private static bool IsValidAxEnumValue(int value)
        {
            return value >= 0 && value <= 20;
        }

        private static bool TryParseBool(JToken token, bool defaultValue = false)
        {
            if (token == null)
                return defaultValue;

            if (token.Type == JTokenType.Boolean)
                return token.Value<bool>();

            if (token.Type == JTokenType.Integer)
                return token.Value<int>() != 0;

            var text = token.ToString();
            if (bool.TryParse(text, out var parsed))
                return parsed;

            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue))
                return intValue != 0;

            return defaultValue;
        }

        private static string NormalizeDate(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            var acceptedFormats = new[] { "ddMMyyyy", "dd.MM.yyyy", "d.M.yyyy", "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy" };
            if (DateTime.TryParseExact(trimmed, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var any))
                return any.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            return null;
        }

        private static string NormalizeTime(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var trimmed = value.Trim();
            var acceptedFormats = new[] { "H:mm", "HH:mm", "H:mm:ss", "HH:mm:ss" };
            if (DateTime.TryParseExact(trimmed, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
                return parsed.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            if (DateTime.TryParse(trimmed, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var any))
                return any.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

            return null;
        }

        private static string BuildNormalizedDraftJson(ExpenseSheetDraftResponse draft, ExpenseTicketDraftProfile profile)
        {
            var root = profile == ExpenseTicketDraftProfile.QuickCreate
                ? BuildQuickCreateNormalizedDraftToken(draft)
                : BuildFullNormalizedDraftToken(draft);

            return JsonConvert.SerializeObject(root, Formatting.None);
        }

        private static JObject BuildFullNormalizedDraftToken(ExpenseSheetDraftResponse draft)
        {
            var lines = new JArray();
            foreach (var line in draft?.lines ?? new List<CreateExpenseSheetLineRequest>())
                lines.Add(BuildFullNormalizedLineToken(line));

            return new JObject
            {
                ["mode"] = draft?.mode ?? 0,
                ["description"] = ToNullableStringToken(draft?.description),
                ["currencyCode"] = ToNullableStringToken(draft?.currencyCode),
                ["gastoType"] = ToNullableIntToken(draft?.gastoType),
                ["transDate"] = ToNullableStringToken(draft?.transDate),
                ["ticketDate"] = ToNullableStringToken(draft?.ticketDate),
                ["ticketTime"] = ToNullableStringToken(draft?.ticketTime),
                ["totalAmount"] = ToNullableDecimalToken(draft?.totalAmount),
                ["exchRate"] = ToNullableDecimalToken(draft?.exchRate),
                ["projId"] = ToNullableStringToken(draft?.projId),
                ["confidence"] = ToNullableDecimalToken(draft?.Confidence),
                ["warnings"] = BuildWarningsToken(draft?.Warnings),
                ["rawCurrency"] = ToNullableStringToken(draft?.RawCurrency),
                ["merchant"] = ToNullableStringToken(draft?.Merchant),
                ["lines"] = lines
            };
        }

        private static JObject BuildQuickCreateNormalizedDraftToken(ExpenseSheetDraftResponse draft)
        {
            var lines = new JArray();
            foreach (var line in draft?.lines ?? new List<CreateExpenseSheetLineRequest>())
                lines.Add(BuildQuickCreateNormalizedLineToken(line));

            return new JObject
            {
                ["description"] = ToNullableStringToken(draft?.description),
                ["currencyCode"] = ToNullableStringToken(draft?.currencyCode),
                ["gastoType"] = new JValue(draft?.gastoType ?? DefaultGastoTypeValue),
                ["transDate"] = ToNullableStringToken(draft?.transDate),
                ["ticketDate"] = ToNullableStringToken(draft?.ticketDate),
                ["ticketTime"] = ToNullableStringToken(draft?.ticketTime),
                ["totalAmount"] = ToNullableDecimalToken(draft?.totalAmount),
                ["rawCurrency"] = ToNullableStringToken(draft?.RawCurrency),
                ["merchant"] = ToNullableStringToken(draft?.Merchant),
                ["lines"] = lines
            };
        }

        private static JObject BuildFullNormalizedLineToken(CreateExpenseSheetLineRequest line)
        {
            var price = line?.price;
            var qty = line?.qty.HasValue == true &&
                      (line.qty.Value > 0m || (line.qty.Value == 0m && price.HasValue && price.Value < 0m))
                ? line.qty.Value
                : 1m;
            var lineTotal = price.HasValue
                ? (qty == 0m && price.Value < 0m ? price.Value : qty * price.Value)
                : (decimal?)null;

            return new JObject
            {
                ["transDate"] = ToNullableStringToken(line?.transDate),
                ["typeValue"] = new JValue(line?.typeValue ?? DefaultGastoTypeValue),
                ["description"] = ToNullableStringToken(line?.description),
                ["internacional"] = line?.internacional.HasValue == true ? new JValue(line.internacional.Value) : JValue.CreateNull(),
                ["fileId"] = ToNullableStringToken(line?.fileId),
                ["qty"] = new JValue(qty),
                ["price"] = ToNullableDecimalToken(price),
                ["lineTotal"] = ToNullableDecimalToken(lineTotal),
                ["projId"] = ToNullableStringToken(line?.projId),
                ["reimbursableExpense"] = ToNullableIntToken(line?.reimbursableExpense),
                ["currencyCode"] = ToNullableStringToken(line?.currencyCode),
                ["amountMST"] = ToNullableDecimalToken(line?.amountMST),
                ["exchRate"] = ToNullableDecimalToken(line?.exchRate)
            };
        }

        private static JObject BuildQuickCreateNormalizedLineToken(CreateExpenseSheetLineRequest line)
        {
            var price = line?.price;
            var qty = line?.qty.HasValue == true &&
                      (line.qty.Value > 0m || (line.qty.Value == 0m && price.HasValue && price.Value < 0m))
                ? line.qty.Value
                : 1m;
            var lineTotal = price.HasValue
                ? (qty == 0m && price.Value < 0m ? price.Value : qty * price.Value)
                : (decimal?)null;

            return new JObject
            {
                ["description"] = ToNullableStringToken(line?.description),
                ["qty"] = new JValue(qty),
                ["price"] = ToNullableDecimalToken(price),
                ["lineTotal"] = ToNullableDecimalToken(lineTotal)
            };
        }

        private static JToken BuildWarningsToken(List<string> warnings)
        {
            if (warnings == null || warnings.Count == 0)
                return JValue.CreateNull();

            return new JArray(warnings
                .Where(w => !string.IsNullOrWhiteSpace(w))
                .Select(w => w.Trim()));
        }

        private static JToken ToNullableStringToken(string value)
        {
            var normalized = NormalizeText(value, null);
            return normalized == null ? JValue.CreateNull() : new JValue(normalized);
        }

        private static JToken ToNullableDecimalToken(decimal? value)
        {
            return value.HasValue ? new JValue(value.Value) : JValue.CreateNull();
        }

        private static JToken ToNullableIntToken(int? value)
        {
            return value.HasValue ? new JValue(value.Value) : JValue.CreateNull();
        }

        private static string NormalizeText(string value, string defaultValue)
        {
            var trimmed = (value ?? string.Empty).Trim();
            return string.IsNullOrWhiteSpace(trimmed) ? defaultValue : trimmed;
        }

        private static JObject BuildResponseSchema(ExpenseTicketDraftProfile profile)
        {
            return profile == ExpenseTicketDraftProfile.QuickCreate
                ? BuildQuickCreateResponseSchema()
                : BuildFullDraftResponseSchema();
        }

        private static JObject BuildFullDraftResponseSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["mode"] = new JObject
                    {
                        ["type"] = "integer",
                        ["enum"] = new JArray(0, 1, 2)
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["currencyCode"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["gastoType"] = new JObject
                    {
                        ["type"] = new JArray("integer", "null"),
                        ["minimum"] = 0
                    },
                    ["totalAmount"] = new JObject
                    {
                        ["type"] = new JArray("number", "null"),
                        ["minimum"] = 0
                    },
                    ["transDate"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["ticketDate"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["ticketTime"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["exchRate"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    },
                    ["projId"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["confidence"] = new JObject
                    {
                        ["type"] = new JArray("number", "null"),
                        ["minimum"] = 0,
                        ["maximum"] = 1
                    },
                    ["warnings"] = new JObject
                    {
                        ["type"] = new JArray("array", "null"),
                        ["items"] = new JObject
                        {
                            ["type"] = "string"
                        }
                    },
                    ["rawCurrency"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["merchant"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["lines"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = BuildFullDraftLineSchema()
                    }
                },
                ["required"] = new JArray(
                    "mode",
                    "description",
                    "currencyCode",
                    "gastoType",
                    "totalAmount",
                    "transDate",
                    "ticketDate",
                    "ticketTime",
                    "exchRate",
                    "projId",
                    "confidence",
                    "warnings",
                    "rawCurrency",
                    "merchant",
                    "lines")
            };
        }

        private static JObject BuildFullDraftLineSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["transDate"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["typeValue"] = new JObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0
                    },
                    ["description"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["internacional"] = new JObject
                    {
                        ["type"] = new JArray("boolean", "null")
                    },
                    ["fileId"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["qty"] = new JObject
                    {
                        ["type"] = "number"
                    },
                    ["price"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    },
                    ["lineTotal"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    },
                    ["projId"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    }
                },
                ["required"] = new JArray(
                    "transDate",
                    "typeValue",
                    "description",
                    "internacional",
                    "fileId",
                    "qty",
                    "price",
                    "lineTotal",
                    "projId")
            };
        }

        private static JObject BuildQuickCreateResponseSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["description"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["currencyCode"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["gastoType"] = new JObject
                    {
                        ["type"] = "integer",
                        ["minimum"] = 0
                    },
                    ["totalAmount"] = new JObject
                    {
                        ["type"] = new JArray("number", "null"),
                        ["minimum"] = 0
                    },
                    ["transDate"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["ticketDate"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["ticketTime"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["rawCurrency"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["merchant"] = new JObject
                    {
                        ["type"] = new JArray("string", "null")
                    },
                    ["lines"] = new JObject
                    {
                        ["type"] = "array",
                        ["minItems"] = 1,
                        ["items"] = BuildQuickCreateLineSchema()
                    }
                },
                ["required"] = new JArray(
                    "description",
                    "currencyCode",
                    "gastoType",
                    "totalAmount",
                    "transDate",
                    "ticketDate",
                    "ticketTime",
                    "rawCurrency",
                    "merchant",
                    "lines")
            };
        }

        private static JObject BuildQuickCreateLineSchema()
        {
            return new JObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JObject
                {
                    ["description"] = new JObject
                    {
                        ["type"] = "string"
                    },
                    ["qty"] = new JObject
                    {
                        ["type"] = "number"
                    },
                    ["price"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    },
                    ["lineTotal"] = new JObject
                    {
                        ["type"] = new JArray("number", "null")
                    }
                },
                ["required"] = new JArray(
                    "description",
                    "qty",
                    "price",
                    "lineTotal")
            };
        }

        // Holds the effective request knobs that define a latency profile.
        private sealed class ExpenseTicketRequestOptions
        {
            public ExpenseTicketDraftProfile DraftProfile { get; set; }
            public string Model { get; set; }
            public string ImageDetail { get; set; }
            public int MaxOutputTokens { get; set; }
            public string ServiceTier { get; set; }
            public string ProfileTag { get; set; }
            public string PromptCacheKey { get; set; }
            public string ReasoningEffort { get; set; }
        }

        // Captures usage and service-tier data returned by OpenAI for A/B timing analysis.
        private sealed class ExpenseTicketResponseMetrics
        {
            public string ActualServiceTier { get; set; }
            public int? InputTokens { get; set; }
            public int? CachedTokens { get; set; }
            public int? OutputTokens { get; set; }
            public int? ReasoningTokens { get; set; }
            public int? TotalTokens { get; set; }
        }
    }
}
