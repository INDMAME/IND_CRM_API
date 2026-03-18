using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json.Linq;
using Swashbuckle.Swagger.Annotations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// AI endpoints focused on expense sheet list analysis.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/ia/service/expensesheets")]
    public class INDExpenseSheetsAiController : BaseCrmController
    {
        private const int MaxQuestionChars = 1000;
        private const int MaxAnswerInstructionsChars = 2000;
        private const int MaxInlineSourceJsonRecords = 6000;
        private const long MaxInlineSourceJsonBytes = 4L * 1024 * 1024;

        private readonly IExpenseSheetAiDatasetProvider _datasetProvider;
        private readonly IAiDatasetAnswerService _answerService;

        public INDExpenseSheetsAiController(
            IAxaptaSessionManager sessionManager,
            IExpenseSheetAiDatasetProvider datasetProvider,
            IAiDatasetAnswerService answerService,
            IAxLogger logger) : base(sessionManager, logger)
        {
            _datasetProvider = datasetProvider ?? throw new ArgumentNullException(nameof(datasetProvider));
            _answerService = answerService ?? throw new ArgumentNullException(nameof(answerService));
        }

        /// <summary>
        /// Answers a question using filtered expense sheet list data.
        /// </summary>
        /// <remarks>
        /// Body:
        /// - question: required
        /// - answerInstructions: optional
        /// - listRequest: filters compatible with POST /api/crm/expensesheets/list
        /// - sourceJson: optional JSON captured from a previous list response
        /// Notes:
        /// - page and pageSize are ignored in this AI endpoint.
        /// - createdDateFrom and createdDateTo accept DDMMYYYY or DD.MM.YYYY.
        /// - when sourceJson is sent, the endpoint analyzes that JSON directly and skips the server-side query.
        /// </remarks>
        [HttpPost, Route("ask")]
        [ResponseType(typeof(IndApiResponse<AskExpenseSheetsAiResponse>))]
        [SwaggerOperation(Tags = new[] { "IA Expense Sheets" })]
        [SwaggerResponse(HttpStatusCode.OK, "AI answer generated", typeof(IndApiResponse<AskExpenseSheetsAiResponse>))]
        [SwaggerResponse((HttpStatusCode)422, "Validation errors", typeof(IndApiResponse<AskExpenseSheetsAiResponse>))]
        [SwaggerResponse((HttpStatusCode)429, "AI rate limit exceeded", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Internal error", typeof(IndApiResponse<AskExpenseSheetsAiResponse>))]
        public async Task<IHttpActionResult> Ask([FromBody] AskExpenseSheetsAiRequest body, CancellationToken cancellationToken)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();
            string createdDateFromYmd = string.Empty;
            string createdDateToYmd = string.Empty;
            var requestContentLength = Request?.Content?.Headers?.ContentLength;

            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var axUserId = RequireAxUserIdOrReturn422(out var axUserError, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (axUserError != null)
                return axUserError;

            AddModelStateErrors(validationErrors);

            if (body == null)
            {
                validationErrors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
            }
            else
            {
                ValidateBody(body, validationErrors, requestContentLength, ref createdDateFromYmd, ref createdDateToYmd);
            }

            if (validationErrors.Count > 0)
                return Content((HttpStatusCode)422, BuildValidationResponse(traceId, validationErrors));

            var username = GetAuthenticatedUsername();
            var listRequest = body.listRequest ?? new GetExpenseSheetsListRequest();
            var options = BuildQueryOptions(listRequest, createdDateFromYmd, createdDateToYmd);
            var filtersApplied = BuildAppliedFilters(options);
            var usesInlineSourceJson = HasInlineSourceJson(body?.sourceJson);

            Logger.Log(
                "[API-IN] POST /api/ia/service/expensesheets/ask" +
                " user=" + username +
                " axUserId=" + axUserId +
                " company=" + company +
                " sourceKey=" + _datasetProvider.SourceKey +
                " sourceMode=" + (usesInlineSourceJson ? "inline-json" : "server-query") +
                " billedMode=" + options.BilledMode.ToString(CultureInfo.InvariantCulture) +
                " includeSubordinates=" + options.IncludeSubordinates +
                " traceId=" + traceId,
                AxaptaSessionManager.LogLevel.Info);

            try
            {
                var dataset = usesInlineSourceJson
                    ? BuildInlineDataset(body.sourceJson)
                    : _datasetProvider.Load(username, company, axUserId, options);

                var answer = await _answerService.AnswerAsync(
                    new AiDatasetAnswerRequest
                    {
                        SourceKey = dataset.SourceKey,
                        Question = body.question?.Trim(),
                        AnswerInstructions = string.IsNullOrWhiteSpace(body.answerInstructions) ? null : body.answerInstructions.Trim(),
                        Records = dataset.Records ?? new List<AiDatasetRecord>()
                    },
                    cancellationToken).ConfigureAwait(false);

                var response = new AskExpenseSheetsAiResponse
                {
                    Answer = answer.Answer,
                    Model = answer.Model,
                    SourceKey = dataset.SourceKey,
                    FiltersApplied = filtersApplied,
                    TotalSourceRecords = dataset.TotalRecords,
                    RecordsSentToModel = answer.RecordsSentToModel,
                    RetrievalMode = answer.RetrievalMode,
                    Truncated = answer.Truncated,
                    Warnings = MergeWarnings(dataset.Warnings, answer.Warnings)
                };

                Logger.Log(
                    "[API-OUT] AskExpenseSheetsAi 200" +
                    " totalRecords=" + dataset.TotalRecords.ToString(CultureInfo.InvariantCulture) +
                    " retrievalMode=" + (answer.RetrievalMode ?? "na") +
                    " traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Info);

                return Ok(new IndApiResponse<AskExpenseSheetsAiResponse>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Errors = null,
                    Data = response,
                    TraceId = traceId
                });
            }
            catch (IND_OpenAiRateLimitException ex)
            {
                Logger.Log(
                    "[AI-EXPENSESHEETS] OpenAI rate limit retryAfter=" +
                    (ex.RetryAfterSeconds.HasValue ? ex.RetryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture) : "na") +
                    " summary=" + (ex.ProviderSummary ?? string.Empty) +
                    " traceId=" + traceId,
                    AxaptaSessionManager.LogLevel.Warning);

                return BuildTooManyRequests(traceId, ex.RetryAfterSeconds);
            }
            catch (Exception ex) when (ex is COMException)
            {
                Logger.Log("[ERROR] AskExpenseSheetsAi COM: " + ex, AxaptaSessionManager.LogLevel.Error);
                return Content(HttpStatusCode.InternalServerError, BuildErrorResponse(traceId, "Error interno del servidor.", IndErrorCodes.AxComError));
            }
            catch (Exception ex)
            {
                Logger.Log("[ERROR] AskExpenseSheetsAi: " + ex, AxaptaSessionManager.LogLevel.Error);
                return Content(HttpStatusCode.InternalServerError, BuildErrorResponse(traceId, "Error interno del servidor.", IndErrorCodes.InternalError));
            }
        }

        private static void ValidateBody(
            AskExpenseSheetsAiRequest body,
            List<IndValidationError> validationErrors,
            long? requestContentLength,
            ref string createdDateFromYmd,
            ref string createdDateToYmd)
        {
            if (string.IsNullOrWhiteSpace(body.question))
                validationErrors.Add(new IndValidationError { Field = "question", Message = "question es obligatorio." });
            else if (body.question.Trim().Length > MaxQuestionChars)
                validationErrors.Add(new IndValidationError { Field = "question", Message = "question supera el maximo permitido." });

            if (!string.IsNullOrWhiteSpace(body.answerInstructions) &&
                body.answerInstructions.Trim().Length > MaxAnswerInstructionsChars)
            {
                validationErrors.Add(new IndValidationError { Field = "answerInstructions", Message = "answerInstructions supera el maximo permitido." });
            }

            if (HasInlineSourceJson(body.sourceJson))
            {
                if (!TryExtractInlineRecords(body.sourceJson, out var inlineRecords, out _))
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "sourceJson",
                        Message = "sourceJson debe ser un array de registros o un objeto con Items/records/Data.Items."
                    });
                }
                else
                {
                    if (requestContentLength.HasValue && requestContentLength.Value > MaxInlineSourceJsonBytes)
                    {
                        validationErrors.Add(new IndValidationError
                        {
                            Field = "sourceJson",
                            Message = "sourceJson excede el tamano maximo permitido de 4 MB."
                        });
                    }

                    if (inlineRecords.Count > MaxInlineSourceJsonRecords)
                    {
                        validationErrors.Add(new IndValidationError
                        {
                            Field = "sourceJson",
                            Message = "sourceJson excede el maximo permitido de 6000 registros."
                        });
                    }
                }
            }

            var listRequest = body.listRequest;
            if (listRequest == null)
                return;

            if (!string.IsNullOrWhiteSpace(listRequest.createdDateFrom) &&
                !ExpenseSheetListQueryHelper.TryNormalizeApiDateToAxYmd(listRequest.createdDateFrom, out createdDateFromYmd))
            {
                validationErrors.Add(new IndValidationError { Field = "listRequest.createdDateFrom", Message = "createdDateFrom debe ser DDMMYYYY o DD.MM.YYYY." });
            }

            if (!string.IsNullOrWhiteSpace(listRequest.createdDateTo) &&
                !ExpenseSheetListQueryHelper.TryNormalizeApiDateToAxYmd(listRequest.createdDateTo, out createdDateToYmd))
            {
                validationErrors.Add(new IndValidationError { Field = "listRequest.createdDateTo", Message = "createdDateTo debe ser DDMMYYYY o DD.MM.YYYY." });
            }

            if (!string.IsNullOrWhiteSpace(createdDateFromYmd) && !string.IsNullOrWhiteSpace(createdDateToYmd))
            {
                var fromOk = DateTime.TryParseExact(createdDateFromYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var fromDate);
                var toOk = DateTime.TryParseExact(createdDateToYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var toDate);
                if (fromOk && toOk && fromDate > toDate)
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "listRequest.createdDateFrom",
                        Message = "createdDateFrom no puede ser mayor que createdDateTo."
                    });
                }
            }
        }

        private static ExpenseSheetAiQueryOptions BuildQueryOptions(
            GetExpenseSheetsListRequest listRequest,
            string createdDateFromYmd,
            string createdDateToYmd)
        {
            var billedMode = listRequest?.billedMode ?? 0;
            if (billedMode != 0 && billedMode != 1 && billedMode != 2)
                billedMode = 0;

            return new ExpenseSheetAiQueryOptions
            {
                Filter = listRequest?.filter?.Trim() ?? string.Empty,
                BilledMode = billedMode,
                CreatedDateFromYmd = createdDateFromYmd,
                CreatedDateToYmd = createdDateToYmd,
                ProjId = listRequest?.projId?.Trim() ?? string.Empty,
                CurrencyCode = listRequest?.currencyCode?.Trim() ?? string.Empty,
                ExpenseSheetStatus = ExpenseSheetListQueryHelper.NormalizeExpenseSheetStatusOrNull(listRequest?.expenseSheetStatus),
                IncludeSubordinates = listRequest?.includeSubordinates ?? false
            };
        }

        private static ExpenseSheetsAiAppliedFiltersDto BuildAppliedFilters(ExpenseSheetAiQueryOptions options)
        {
            return new ExpenseSheetsAiAppliedFiltersDto
            {
                Filter = options.Filter,
                BilledMode = options.BilledMode,
                CreatedDateFrom = string.IsNullOrWhiteSpace(options.CreatedDateFromYmd) ? null : ExpenseSheetListQueryHelper.FormatApiDate(options.CreatedDateFromYmd),
                CreatedDateTo = string.IsNullOrWhiteSpace(options.CreatedDateToYmd) ? null : ExpenseSheetListQueryHelper.FormatApiDate(options.CreatedDateToYmd),
                ProjId = options.ProjId,
                CurrencyCode = options.CurrencyCode,
                ExpenseSheetStatus = options.ExpenseSheetStatus,
                IncludeSubordinates = options.IncludeSubordinates
            };
        }

        private static IndApiResponse<AskExpenseSheetsAiResponse> BuildValidationResponse(string traceId, List<IndValidationError> errors)
        {
            return new IndApiResponse<AskExpenseSheetsAiResponse>
            {
                Success = false,
                Message = "Error de validacion.",
                ErrorCode = IndErrorCodes.CrmExpenseSheetMissingFields,
                Errors = errors,
                Data = null,
                TraceId = traceId
            };
        }

        private static IndApiResponse<AskExpenseSheetsAiResponse> BuildErrorResponse(string traceId, string message, string errorCode)
        {
            return new IndApiResponse<AskExpenseSheetsAiResponse>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Errors = null,
                Data = null,
                TraceId = traceId
            };
        }

        private IHttpActionResult BuildTooManyRequests(string traceId, int? retryAfterSeconds)
        {
            var payload = new IndApiResponse<object>
            {
                Success = false,
                Message = "Se excedio el limite de solicitudes de IA. Intente de nuevo en unos segundos.",
                ErrorCode = IndErrorCodes.AiRateLimitExceeded,
                Errors = null,
                Data = null,
                TraceId = traceId
            };

            var response = Request.CreateResponse((HttpStatusCode)429, payload);
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
            {
                response.Headers.Add("Retry-After", retryAfterSeconds.Value.ToString(CultureInfo.InvariantCulture));
            }

            return ResponseMessage(response);
        }

        private void AddModelStateErrors(List<IndValidationError> validationErrors)
        {
            if (validationErrors == null || ModelState == null || ModelState.IsValid)
                return;

            foreach (var entry in ModelState)
            {
                var field = string.IsNullOrWhiteSpace(entry.Key) ? "body" : entry.Key;
                var state = entry.Value;
                if (state == null || state.Errors == null || state.Errors.Count == 0)
                    continue;

                foreach (var modelError in state.Errors)
                {
                    var message = modelError?.ErrorMessage;
                    if (string.IsNullOrWhiteSpace(message))
                        message = modelError?.Exception?.Message;
                    if (string.IsNullOrWhiteSpace(message))
                        message = "Valor invalido.";

                    validationErrors.Add(new IndValidationError
                    {
                        Field = field,
                        Message = message
                    });
                }
            }
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

        private static bool HasInlineSourceJson(JToken sourceJson)
        {
            return sourceJson != null &&
                   sourceJson.Type != JTokenType.Null &&
                   sourceJson.Type != JTokenType.Undefined;
        }

        private AiDatasetLoadResult BuildInlineDataset(JToken sourceJson)
        {
            if (!TryExtractInlineRecords(sourceJson, out var recordsToken, out var total))
            {
                throw new ArgumentException("sourceJson no contiene registros analizables.", nameof(sourceJson));
            }

            if (recordsToken.Count > MaxInlineSourceJsonRecords)
            {
                throw new ArgumentException("sourceJson excede el maximo permitido de registros.", nameof(sourceJson));
            }

            var records = new List<AiDatasetRecord>();
            foreach (var recordToken in recordsToken)
            {
                if (recordToken == null || recordToken.Type == JTokenType.Null || recordToken.Type == JTokenType.Undefined)
                    continue;

                records.Add(new AiDatasetRecord
                {
                    RecordId = ExtractRecordId(recordToken),
                    JsonPayload = recordToken.ToString(Newtonsoft.Json.Formatting.None)
                });
            }

            return new AiDatasetLoadResult
            {
                SourceKey = _datasetProvider.SourceKey,
                TotalRecords = total >= 0 ? total : records.Count,
                Records = records,
                Warnings = new List<string>
                {
                    "Inline sourceJson was used; server-side expense sheet retrieval was skipped."
                }
            };
        }

        private static bool TryExtractInlineRecords(JToken sourceJson, out JArray recordsToken, out int total)
        {
            recordsToken = null;
            total = 0;

            if (!HasInlineSourceJson(sourceJson))
                return false;

            if (sourceJson is JArray rootArray)
            {
                recordsToken = rootArray;
                total = rootArray.Count;
                return true;
            }

            if (!(sourceJson is JObject rootObject))
                return false;

            var directItems = rootObject["Items"] as JArray;
            if (directItems != null)
            {
                recordsToken = directItems;
                total = ReadTotal(rootObject["Total"], directItems.Count);
                return true;
            }

            var directRecords = rootObject["records"] as JArray;
            if (directRecords != null)
            {
                recordsToken = directRecords;
                total = ReadTotal(rootObject["total"], directRecords.Count);
                return true;
            }

            var dataItems = rootObject["Data"]?["Items"] as JArray;
            if (dataItems != null)
            {
                recordsToken = dataItems;
                total = ReadTotal(rootObject["Data"]?["Total"], dataItems.Count);
                return true;
            }

            return false;
        }

        private static int ReadTotal(JToken totalToken, int fallbackValue)
        {
            if (totalToken == null)
                return fallbackValue;

            if (totalToken.Type == JTokenType.Integer)
                return totalToken.Value<int>();

            if (int.TryParse(totalToken.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            return fallbackValue;
        }

        private static string ExtractRecordId(JToken recordToken)
        {
            if (!(recordToken is JObject recordObject))
                return string.Empty;

            var candidates = new[] { "hojaGastosId", "HojaGastosId", "id", "Id" };
            foreach (var candidate in candidates)
            {
                var value = recordObject[candidate]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return string.Empty;
        }
    }
}
