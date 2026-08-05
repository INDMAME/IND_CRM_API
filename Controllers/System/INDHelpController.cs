using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Authenticated catalog, topic, and feedback endpoints for CRM help.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/help")]
    public sealed class INDHelpController : HelpControllerBase
    {
        private static readonly HashSet<string> AllowedLocales = new HashSet<string>(
            new[] { "es-ES", "eu-ES", "en", "pt", "it", "zh-Hans" },
            StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> NegativeReasons = new HashSet<string>(
            new[] { "incorrect", "outdated", "unclear", "incomplete", "permissions", "other" },
            StringComparer.OrdinalIgnoreCase);

        private readonly IHelpKnowledgeStore _knowledgeStore;
        private readonly IHelpAnalyticsStore _analyticsStore;
        private readonly IHelpFeedbackTokenService _feedbackTokenService;

        public INDHelpController(
            IHelpKnowledgeStore knowledgeStore,
            IHelpAnalyticsStore analyticsStore,
            IHelpFeedbackTokenService feedbackTokenService,
            IAxLogger logger) : base(logger)
        {
            _knowledgeStore = knowledgeStore ?? throw new ArgumentNullException(nameof(knowledgeStore));
            _analyticsStore = analyticsStore ?? throw new ArgumentNullException(nameof(analyticsStore));
            _feedbackTokenService = feedbackTokenService ?? throw new ArgumentNullException(nameof(feedbackTokenService));
        }

        /// <summary>
        /// Returns the ordered help menu without calling OpenAI.
        /// </summary>
        [HttpGet, Route("catalog")]
        [ResponseType(typeof(IndApiResponse<HelpCatalogDto>))]
        [SwaggerOperation(Tags = new[] { "CRM Help" })]
        public IHttpActionResult GetCatalog([FromUri] string responseLocale = null)
        {
            var traceId = Guid.NewGuid().ToString("N");
            if (!IsValidOptionalLocale(responseLocale))
            {
                return BuildValidation<HelpCatalogDto>(traceId, new List<IndValidationError>
                {
                    new IndValidationError { Field = "responseLocale", Message = "responseLocale no esta soportado." }
                });
            }
            try
            {
                var snapshot = _knowledgeStore.GetSnapshot();
                var data = HelpKnowledgeProjection.ToCatalog(snapshot, responseLocale);
                return OkWithEtag(data, traceId, snapshot.BundleHash);
            }
            catch (HelpFeatureUnavailableException ex)
            {
                return BuildUnavailable(traceId, ex.Message, ex.ErrorCode);
            }
        }

        /// <summary>
        /// Returns canonical Spanish topic content without an AI call.
        /// </summary>
        [HttpGet, Route("topics/{topicId}")]
        [ResponseType(typeof(IndApiResponse<HelpTopicDto>))]
        [SwaggerOperation(Tags = new[] { "CRM Help" })]
        public IHttpActionResult GetTopic(string topicId, [FromUri] string responseLocale = null)
        {
            var traceId = Guid.NewGuid().ToString("N");
            if (!IsValidOptionalLocale(responseLocale))
            {
                return BuildValidation<HelpTopicDto>(traceId, new List<IndValidationError>
                {
                    new IndValidationError { Field = "responseLocale", Message = "responseLocale no esta soportado." }
                });
            }
            try
            {
                var snapshot = _knowledgeStore.GetSnapshot();
                HelpKnowledgeTopic topic;
                if (string.IsNullOrWhiteSpace(topicId) || !snapshot.TopicsById.TryGetValue(topicId.Trim(), out topic))
                {
                    return Content(HttpStatusCode.NotFound, BuildEnvelope<HelpTopicDto>(
                        false,
                        "No se encontro el tema solicitado.",
                        HelpErrorCodes.TopicNotFound,
                        null,
                        traceId));
                }

                var data = HelpKnowledgeProjection.ToTopic(snapshot, topic, responseLocale);
                return OkWithEtag(data, traceId, snapshot.BundleHash + "-" + (topic.contentHash ?? topic.id));
            }
            catch (HelpFeatureUnavailableException ex)
            {
                return BuildUnavailable(traceId, ex.Message, ex.ErrorCode);
            }
        }

        /// <summary>
        /// Records explicit answer feedback using a short-lived signed token.
        /// </summary>
        [HttpPost, Route("feedback")]
        [ResponseType(typeof(IndApiResponse<HelpFeedbackResponse>))]
        [SwaggerOperation(Tags = new[] { "CRM Help" })]
        public IHttpActionResult SubmitFeedback([FromBody] HelpFeedbackRequest body)
        {
            var traceId = Guid.NewGuid().ToString("N");
            if (!_knowledgeStore.IsEnabled)
                return BuildUnavailable(traceId, "El asistente de ayuda no esta habilitado.", HelpErrorCodes.FeatureDisabled);
            if (!_feedbackTokenService.IsConfigured)
                return BuildUnavailable(traceId, "La valoracion no esta configurada.", HelpErrorCodes.FeedbackUnavailable);

            var errors = ValidateFeedback(body);
            if (errors.Count > 0)
                return BuildValidation<HelpFeedbackResponse>(traceId, errors);

            var userKey = ResolveUserKey();
            HelpFeedbackTokenPayload payload;
            if (string.IsNullOrWhiteSpace(userKey) ||
                !_feedbackTokenService.TryConsume(body.feedbackToken, userKey, out payload))
            {
                return Content(HttpStatusCode.Forbidden, BuildEnvelope<HelpFeedbackResponse>(
                    false,
                    "La autorizacion de valoracion no es valida o ha caducado.",
                    HelpErrorCodes.FeedbackTokenInvalid,
                    null,
                    traceId));
            }

            _analyticsStore.RecordFeedback(new HelpFeedbackAnalyticsEvent
            {
                InteractionId = payload.InteractionId,
                UserKey = userKey,
                Helpful = body.helpful.Value,
                Reason = string.IsNullOrWhiteSpace(body.reason) ? null : body.reason.Trim().ToLowerInvariant(),
                RedactedComment = HelpTextRedactor.Redact(body.comment, 1000)
            });

            return Ok(BuildEnvelope(
                true,
                "OK",
                null,
                new HelpFeedbackResponse { Accepted = true },
                traceId));
        }

        private static List<IndValidationError> ValidateFeedback(HelpFeedbackRequest body)
        {
            var errors = new List<IndValidationError>();
            if (body == null)
            {
                errors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
                return errors;
            }
            if (string.IsNullOrWhiteSpace(body.feedbackToken) || body.feedbackToken.Length > 2048)
                errors.Add(new IndValidationError { Field = "feedbackToken", Message = "feedbackToken no es valido." });
            if (!body.helpful.HasValue)
                errors.Add(new IndValidationError { Field = "helpful", Message = "helpful es obligatorio." });
            if (body.helpful == false && (string.IsNullOrWhiteSpace(body.reason) || !NegativeReasons.Contains(body.reason.Trim())))
                errors.Add(new IndValidationError { Field = "reason", Message = "reason es obligatorio para una valoracion negativa." });
            if (body.helpful == true && !string.IsNullOrWhiteSpace(body.reason))
                errors.Add(new IndValidationError { Field = "reason", Message = "reason solo se admite para una valoracion negativa." });
            if (!string.IsNullOrWhiteSpace(body.comment) && body.comment.Length > 1000)
                errors.Add(new IndValidationError { Field = "comment", Message = "comment supera el maximo permitido." });
            return errors;
        }

        private static bool IsValidOptionalLocale(string locale)
        {
            return string.IsNullOrWhiteSpace(locale) || AllowedLocales.Contains(locale.Trim());
        }
    }

    /// <summary>
    /// Authenticated generative endpoint for CRM help.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/ia/service/help")]
    public sealed class INDHelpAiController : HelpControllerBase
    {
        private const int MaxQuestionChars = 1200;
        private const int MaxAnswerInstructionsChars = 2000;
        private const int MaxHistoryMessages = 8;
        private const int MaxHistoryMessageChars = 1600;
        private static readonly HashSet<string> AllowedLocales = new HashSet<string>(
            new[] { "es-ES", "eu-ES", "en", "pt", "it", "zh-Hans" },
            StringComparer.OrdinalIgnoreCase);

        private readonly IHelpKnowledgeStore _knowledgeStore;
        private readonly IHelpTopicRetriever _retriever;
        private readonly IHelpAnswerService _answerService;
        private readonly IHelpAnalyticsStore _analyticsStore;
        private readonly IHelpFeedbackTokenService _feedbackTokenService;

        public INDHelpAiController(
            IHelpKnowledgeStore knowledgeStore,
            IHelpTopicRetriever retriever,
            IHelpAnswerService answerService,
            IHelpAnalyticsStore analyticsStore,
            IHelpFeedbackTokenService feedbackTokenService,
            IAxLogger logger) : base(logger)
        {
            _knowledgeStore = knowledgeStore ?? throw new ArgumentNullException(nameof(knowledgeStore));
            _retriever = retriever ?? throw new ArgumentNullException(nameof(retriever));
            _answerService = answerService ?? throw new ArgumentNullException(nameof(answerService));
            _analyticsStore = analyticsStore ?? throw new ArgumentNullException(nameof(analyticsStore));
            _feedbackTokenService = feedbackTokenService ?? throw new ArgumentNullException(nameof(feedbackTokenService));
        }

        /// <summary>
        /// Answers one question using only locally retrieved CRM documentation.
        /// </summary>
        [HttpPost, Route("ask")]
        [ResponseType(typeof(IndApiResponse<AskHelpResponse>))]
        [SwaggerOperation(Tags = new[] { "CRM Help AI" })]
        [SwaggerResponse((HttpStatusCode)422, "Validation error", typeof(IndApiResponse<AskHelpResponse>))]
        [SwaggerResponse((HttpStatusCode)429, "Rate limit exceeded", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Help or AI provider unavailable", typeof(IndApiResponse<AskHelpResponse>))]
        public async Task<IHttpActionResult> Ask([FromBody] AskHelpRequest body, CancellationToken cancellationToken)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var interactionId = Guid.NewGuid().ToString("D");
            var stopwatch = Stopwatch.StartNew();
            if (!_knowledgeStore.IsEnabled)
                return BuildUnavailable(traceId, "El asistente de ayuda no esta habilitado.", HelpErrorCodes.FeatureDisabled);
            var errors = ValidateAsk(body);
            if (errors.Count > 0)
                return BuildValidation<AskHelpResponse>(traceId, errors);

            HelpKnowledgeSnapshot snapshot = null;
            HelpRetrievalResult retrieval = null;
            var userKey = ResolveUserKey();
            var responseLocale = body.responseLocale.Trim();
            var safeQuestion = HelpTextRedactor.Redact(body.question, MaxQuestionChars);
            var safeAnswerInstructions = HelpTextRedactor.Redact(
                body.answerInstructions,
                MaxAnswerInstructionsChars);
            var safeHistory = RedactHistory(body.history);
            try
            {
                snapshot = _knowledgeStore.GetSnapshot();
                responseLocale = snapshot.ResolveLocale(responseLocale);
                retrieval = _retriever.Retrieve(snapshot, new HelpRetrievalRequest
                {
                    Question = safeQuestion,
                    SelectedTopicId = body.selectedTopicId,
                    SelectedModuleId = body.selectedModuleId,
                    ResponseLocale = responseLocale
                });

                if (retrieval.Resolution == "needsSelection" || retrieval.Resolution == "notDocumented")
                {
                    var unresolved = BuildResponse(
                        interactionId,
                        retrieval.Resolution,
                        null,
                        retrieval.Candidates.Take(4).ToList(),
                        new List<HelpAnswerSourceDto>(),
                        new List<HelpAnswerActionDto>(),
                        snapshot,
                        responseLocale,
                        userKey,
                        false,
                        null);
                    stopwatch.Stop();
                    RecordInteraction(unresolved, retrieval, safeQuestion, userKey, stopwatch.ElapsedMilliseconds, 0, 0, 0);
                    return Ok(BuildEnvelope(true, "OK", null, unresolved, traceId));
                }

                if (retrieval.QuickAnswer != null && string.IsNullOrWhiteSpace(safeAnswerInstructions))
                {
                    var quickSources = BuildQuickAnswerSources(retrieval);
                    var quickActions = BuildActions(retrieval, new[] { retrieval.QuickAnswerTopic.routeKey });
                    var quick = BuildResponse(
                        interactionId,
                        "answered",
                        retrieval.QuickAnswer.answer,
                        new List<HelpTopicCandidateDto>(),
                        quickSources,
                        quickActions,
                        snapshot,
                        responseLocale,
                        userKey,
                        true,
                        null);
                    stopwatch.Stop();
                    RecordInteraction(quick, retrieval, safeQuestion, userKey, stopwatch.ElapsedMilliseconds, 0, 0, 0);
                    return Ok(BuildEnvelope(true, "OK", null, quick, traceId));
                }

                var generated = await _answerService.AnswerAsync(new HelpAnswerRequest
                {
                    Question = safeQuestion,
                    ResponseLocale = responseLocale,
                    AnswerInstructions = safeAnswerInstructions,
                    History = safeHistory,
                    Snapshot = snapshot,
                    Retrieval = retrieval
                }, cancellationToken).ConfigureAwait(false);

                var answer = BuildResponse(
                    interactionId,
                    "answered",
                    generated.Answer,
                    new List<HelpTopicCandidateDto>(),
                    BuildGeneratedSources(retrieval, generated.CitationChunkIds),
                    BuildActions(retrieval, generated.ActionRouteKeys),
                    snapshot,
                    responseLocale,
                    userKey,
                    false,
                    generated.Model);
                stopwatch.Stop();
                RecordInteraction(
                    answer,
                    retrieval,
                    safeQuestion,
                    userKey,
                    stopwatch.ElapsedMilliseconds,
                    generated.InputTokens,
                    generated.OutputTokens,
                    generated.CachedInputTokens);
                return Ok(BuildEnvelope(true, "OK", null, answer, traceId));
            }
            catch (HelpFeatureUnavailableException ex)
            {
                RecordFailure(snapshot, retrieval, interactionId, responseLocale, safeQuestion, userKey, stopwatch);
                return BuildUnavailable(traceId, ex.Message, ex.ErrorCode);
            }
            catch (IND_OpenAiRateLimitException ex)
            {
                RecordFailure(snapshot, retrieval, interactionId, responseLocale, safeQuestion, userKey, stopwatch);
                return BuildRateLimit(traceId, ex.RetryAfterSeconds);
            }
            catch (IND_ExternalServiceException ex)
            {
                RecordFailure(snapshot, retrieval, interactionId, responseLocale, safeQuestion, userKey, stopwatch);
                Logger.Log(
                    "[HELP-API] Provider unavailable summary=" + (ex.ProviderSummary ?? "unavailable") + " traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Warning);
                return Content(ex.StatusCode, BuildEnvelope<AskHelpResponse>(
                    false,
                    ex.UserMessage,
                    ex.ErrorCode,
                    null,
                    traceId));
            }
            catch (Exception ex)
            {
                RecordFailure(snapshot, retrieval, interactionId, responseLocale, safeQuestion, userKey, stopwatch);
                Logger.Log(
                    "[HELP-API] Unexpected failure type=" + ex.GetType().Name + " traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Error);
                return Content(HttpStatusCode.InternalServerError, BuildEnvelope<AskHelpResponse>(
                    false,
                    "Error interno del servidor.",
                    IndErrorCodes.InternalError,
                    null,
                    traceId));
            }
        }

        private static List<IndValidationError> ValidateAsk(AskHelpRequest body)
        {
            var errors = new List<IndValidationError>();
            if (body == null)
            {
                errors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
                return errors;
            }
            if (string.IsNullOrWhiteSpace(body.question))
                errors.Add(new IndValidationError { Field = "question", Message = "question es obligatorio." });
            else if (body.question.Trim().Length > MaxQuestionChars)
                errors.Add(new IndValidationError { Field = "question", Message = "question supera el maximo permitido." });
            if (string.IsNullOrWhiteSpace(body.responseLocale) || !AllowedLocales.Contains(body.responseLocale.Trim()))
                errors.Add(new IndValidationError { Field = "responseLocale", Message = "responseLocale no esta soportado." });
            if (!string.IsNullOrWhiteSpace(body.selectedTopicId) && body.selectedTopicId.Trim().Length > 80)
                errors.Add(new IndValidationError { Field = "selectedTopicId", Message = "selectedTopicId no es valido." });
            if (!string.IsNullOrWhiteSpace(body.selectedModuleId) && body.selectedModuleId.Trim().Length > 80)
                errors.Add(new IndValidationError { Field = "selectedModuleId", Message = "selectedModuleId no es valido." });
            if (!string.IsNullOrWhiteSpace(body.answerInstructions) &&
                body.answerInstructions.Trim().Length > MaxAnswerInstructionsChars)
            {
                errors.Add(new IndValidationError
                {
                    Field = "answerInstructions",
                    Message = "answerInstructions supera el maximo permitido."
                });
            }
            if (!string.IsNullOrWhiteSpace(body.clientInteractionId) && !Guid.TryParse(body.clientInteractionId, out _))
                errors.Add(new IndValidationError { Field = "clientInteractionId", Message = "clientInteractionId debe ser UUID." });

            var history = body.history ?? new List<HelpConversationMessageRequest>();
            if (history.Count > MaxHistoryMessages)
                errors.Add(new IndValidationError { Field = "history", Message = "history supera el maximo de mensajes." });
            for (var index = 0; index < history.Count; index++)
            {
                var message = history[index];
                if (message == null || (message.role != "user" && message.role != "assistant"))
                    errors.Add(new IndValidationError { Field = "history[" + index + "].role", Message = "role no es valido." });
                if (message == null || string.IsNullOrWhiteSpace(message.content) || message.content.Length > MaxHistoryMessageChars)
                    errors.Add(new IndValidationError { Field = "history[" + index + "].content", Message = "content no es valido." });
            }
            return errors;
        }

        private static List<HelpConversationMessageRequest> RedactHistory(IEnumerable<HelpConversationMessageRequest> history)
        {
            return (history ?? Enumerable.Empty<HelpConversationMessageRequest>())
                .Select(message => new HelpConversationMessageRequest
                {
                    role = message.role,
                    content = HelpTextRedactor.Redact(message.content, MaxHistoryMessageChars)
                })
                .ToList();
        }

        private AskHelpResponse BuildResponse(
            string interactionId,
            string resolution,
            string answer,
            List<HelpTopicCandidateDto> candidates,
            List<HelpAnswerSourceDto> sources,
            List<HelpAnswerActionDto> actions,
            HelpKnowledgeSnapshot snapshot,
            string responseLocale,
            string userKey,
            bool quickAnswerUsed,
            string model)
        {
            return new AskHelpResponse
            {
                InteractionId = interactionId,
                Resolution = resolution,
                Answer = answer,
                Candidates = candidates ?? new List<HelpTopicCandidateDto>(),
                Sources = sources ?? new List<HelpAnswerSourceDto>(),
                Actions = actions ?? new List<HelpAnswerActionDto>(),
                KnowledgeVersion = snapshot.Bundle.knowledgeVersion,
                ResponseLocale = responseLocale,
                FeedbackToken = _feedbackTokenService.Create(interactionId, userKey),
                QuickAnswerUsed = quickAnswerUsed,
                Model = model
            };
        }

        private static List<HelpAnswerSourceDto> BuildQuickAnswerSources(HelpRetrievalResult retrieval)
        {
            var topic = retrieval.QuickAnswerTopic;
            return retrieval.QuickAnswer.sourceChunkIds
                .Select(id => topic.chunks.FirstOrDefault(chunk => string.Equals(chunk.id, id, StringComparison.OrdinalIgnoreCase)))
                .Where(chunk => chunk != null)
                .Select(chunk => new HelpAnswerSourceDto
                {
                    TopicId = topic.id,
                    TopicTitle = topic.title,
                    ChunkId = chunk.id,
                    Heading = chunk.heading
                })
                .ToList();
        }

        private static List<HelpAnswerSourceDto> BuildGeneratedSources(
            HelpRetrievalResult retrieval,
            IEnumerable<string> sourceKeys)
        {
            var result = new List<HelpAnswerSourceDto>();
            foreach (var sourceKey in sourceKeys ?? Enumerable.Empty<string>())
            {
                var separator = sourceKey.IndexOf(':');
                if (separator <= 0 || separator >= sourceKey.Length - 1)
                    continue;
                var topicId = sourceKey.Substring(0, separator);
                var chunkId = sourceKey.Substring(separator + 1);
                var topic = retrieval.Topics.Select(item => item.Topic).FirstOrDefault(item =>
                    string.Equals(item.id, topicId, StringComparison.OrdinalIgnoreCase));
                var chunk = topic?.chunks.FirstOrDefault(item =>
                    string.Equals(item.id, chunkId, StringComparison.OrdinalIgnoreCase));
                if (topic == null || chunk == null)
                    continue;
                result.Add(new HelpAnswerSourceDto
                {
                    TopicId = topic.id,
                    TopicTitle = topic.title,
                    ChunkId = chunk.id,
                    Heading = chunk.heading
                });
            }
            return result;
        }

        private static List<HelpAnswerActionDto> BuildActions(
            HelpRetrievalResult retrieval,
            IEnumerable<string> routeKeys)
        {
            var result = new List<HelpAnswerActionDto>();
            foreach (var routeKey in (routeKeys ?? Enumerable.Empty<string>())
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var topic = retrieval.Topics.Select(item => item.Topic).FirstOrDefault(item =>
                    string.Equals(item.routeKey, routeKey, StringComparison.OrdinalIgnoreCase));
                if (topic == null)
                    continue;
                result.Add(new HelpAnswerActionDto
                {
                    Type = "navigate",
                    RouteKey = topic.routeKey,
                    Label = topic.title
                });
            }
            return result;
        }

        private void RecordInteraction(
            AskHelpResponse response,
            HelpRetrievalResult retrieval,
            string redactedQuestion,
            string userKey,
            long latencyMilliseconds,
            int inputTokens,
            int outputTokens,
            int cachedInputTokens)
        {
            _analyticsStore.RecordInteraction(new HelpInteractionAnalyticsEvent
            {
                InteractionId = response.InteractionId,
                UserKey = userKey,
                KnowledgeVersion = response.KnowledgeVersion,
                Resolution = response.Resolution,
                ResponseLocale = response.ResponseLocale,
                RetrievalMode = retrieval.Mode,
                Confidence = retrieval.Confidence,
                TopicIds = retrieval.Topics.Select(item => item.Topic.id).ToList(),
                CandidateTopicIds = retrieval.Candidates.Select(item => item.TopicId).ToList(),
                QuickAnswerUsed = response.QuickAnswerUsed,
                InputTokens = inputTokens,
                OutputTokens = outputTokens,
                CachedInputTokens = cachedInputTokens,
                LatencyMilliseconds = latencyMilliseconds,
                RedactedQuestion = redactedQuestion,
                IsProblematic = response.Resolution != "answered"
            });
        }

        private void RecordFailure(
            HelpKnowledgeSnapshot snapshot,
            HelpRetrievalResult retrieval,
            string interactionId,
            string responseLocale,
            string redactedQuestion,
            string userKey,
            Stopwatch stopwatch)
        {
            if (!_knowledgeStore.IsEnabled)
                return;
            stopwatch.Stop();
            _analyticsStore.RecordInteraction(new HelpInteractionAnalyticsEvent
            {
                InteractionId = interactionId,
                UserKey = userKey,
                KnowledgeVersion = snapshot?.Bundle?.knowledgeVersion,
                Resolution = "error",
                ResponseLocale = responseLocale,
                RetrievalMode = retrieval?.Mode,
                Confidence = retrieval?.Confidence ?? 0m,
                TopicIds = retrieval?.Topics?.Select(item => item.Topic.id).ToList() ?? new List<string>(),
                CandidateTopicIds = retrieval?.Candidates?.Select(item => item.TopicId).ToList() ?? new List<string>(),
                QuickAnswerUsed = false,
                LatencyMilliseconds = stopwatch.ElapsedMilliseconds,
                RedactedQuestion = redactedQuestion,
                IsProblematic = true
            });
        }
    }

    /// <summary>
    /// Common response and identity helpers for CRM help endpoints.
    /// </summary>
    public abstract class HelpControllerBase : ApiController
    {
        protected HelpControllerBase(IAxLogger logger)
        {
            Logger = logger ?? new FileAxLogger();
        }

        protected IAxLogger Logger { get; }

        protected string ResolveUserKey()
        {
            var claims = User as ClaimsPrincipal;
            var stableId = claims?.FindFirst("oid")?.Value ??
                           claims?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
                           claims?.FindFirst("sub")?.Value;
            var value = string.IsNullOrWhiteSpace(stableId) ? User?.Identity?.Name : stableId;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        protected IHttpActionResult BuildUnavailable(string traceId, string message, string errorCode)
        {
            return Content(HttpStatusCode.ServiceUnavailable, BuildEnvelope<object>(
                false,
                message,
                errorCode,
                null,
                traceId));
        }

        protected IHttpActionResult BuildValidation<T>(string traceId, List<IndValidationError> errors)
        {
            var response = BuildEnvelope<T>(
                false,
                "Error de validacion.",
                HelpErrorCodes.InvalidRequest,
                default(T),
                traceId);
            response.Errors = errors;
            return Content((HttpStatusCode)422, response);
        }

        protected IHttpActionResult BuildRateLimit(string traceId, int? retryAfterSeconds)
        {
            var response = Request.CreateResponse((HttpStatusCode)429, BuildEnvelope<object>(
                false,
                "Se excedio el limite de solicitudes de IA. Intente de nuevo en unos segundos.",
                IndErrorCodes.AiRateLimitExceeded,
                null,
                traceId));
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
                response.Headers.Add("Retry-After", retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture));
            return ResponseMessage(response);
        }

        protected IHttpActionResult OkWithEtag<T>(T data, string traceId, string etagSeed)
        {
            var etag = "\"" + BuildShortHash(etagSeed) + "\"";
            if (Request.Headers.IfNoneMatch.Any(value => string.Equals(value.Tag, etag, StringComparison.Ordinal)))
            {
                var notModified = Request.CreateResponse(HttpStatusCode.NotModified);
                notModified.Headers.TryAddWithoutValidation("ETag", etag);
                return ResponseMessage(notModified);
            }

            var response = Request.CreateResponse(HttpStatusCode.OK, BuildEnvelope(true, "OK", null, data, traceId));
            response.Headers.TryAddWithoutValidation("ETag", etag);
            response.Headers.CacheControl = new global::System.Net.Http.Headers.CacheControlHeaderValue
            {
                Private = true,
                MustRevalidate = true,
                MaxAge = TimeSpan.Zero
            };
            return ResponseMessage(response);
        }

        protected static IndApiResponse<T> BuildEnvelope<T>(
            bool success,
            string message,
            string errorCode,
            T data,
            string traceId)
        {
            return new IndApiResponse<T>
            {
                Success = success,
                Message = message,
                ErrorCode = errorCode,
                Data = data,
                Errors = null,
                TraceId = traceId
            };
        }

        private static string BuildShortHash(string value)
        {
            using (var sha = global::System.Security.Cryptography.SHA256.Create())
            {
                var bytes = sha.ComputeHash(global::System.Text.Encoding.UTF8.GetBytes(value ?? string.Empty));
                return BitConverter.ToString(bytes, 0, 16).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
