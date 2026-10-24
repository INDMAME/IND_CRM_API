using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Http;
using IND_CRM_API.App_Start;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Controllers
{
    /// <summary>
    /// Base of the CRM_INSERTEC sync endpoints. The caller is the outbox worker with its own
    /// technical identity (ind:role=sync-worker), so there is no Entra context per person:
    /// only the bearer, the role and X-IND-Company are validated here.
    /// </summary>
    [SyncWorkerAuthorize]
    public abstract class BaseSyncController : ApiController
    {
        private static readonly Encoding Windows1252 = Encoding.GetEncoding(
            1252,
            EncoderFallback.ExceptionFallback,
            DecoderFallback.ExceptionFallback);

        protected readonly IAxaptaSessionManager SessionManager;
        protected readonly IAxLogger Logger;
        protected readonly ICrmSyncService SyncService;

        protected BaseSyncController(IAxaptaSessionManager sessionManager, IAxLogger logger, ICrmSyncService syncService)
        {
            SessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            Logger = logger ?? throw new ArgumentNullException(nameof(logger));
            SyncService = syncService ?? throw new ArgumentNullException(nameof(syncService));
        }

        protected string GetTraceId()
        {
            return IndRequestDiagnosticsHelper.GetOrCreateTraceId(Request);
        }

        protected string GetAuthenticatedUsername()
        {
            var username = User?.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
                throw new InvalidOperationException("User not authenticated or invalid token.");

            return username.Trim();
        }

        /// <summary>
        /// Requires X-IND-Company (422 when missing). No signed context: the worker is not a person.
        /// </summary>
        protected string RequireCompanyOrReturn422(out IHttpActionResult errorResult, string traceId)
        {
            errorResult = null;
            var company = IndRequestDiagnosticsHelper.GetHeaderValue(Request, "X-IND-Company");
            if (!string.IsNullOrWhiteSpace(company))
                return company.Trim();

            errorResult = ValidationFailed(new List<IndValidationError>
            {
                new IndValidationError { Field = "company", Message = "Header X-IND-Company requerido." }
            }, traceId);
            return null;
        }

        protected IHttpActionResult ValidationFailed(List<IndValidationError> errors, string traceId)
        {
            return Content((HttpStatusCode)422, new IndApiResponse<object>
            {
                Success = false,
                Message = "Error de validacion.",
                ErrorCode = IndErrorCodes.ValidationError,
                Errors = errors,
                Data = null,
                TraceId = traceId
            });
        }

        /// <summary>
        /// Maps the receiver result to HTTP: applied/duplicate 200, divergent 409, rejected 422.
        /// </summary>
        protected IHttpActionResult FromSyncResult(SyncResultDto result, string operation, string traceId)
        {
            Logger.Log($"[SYNC-OUT] operation={operation} result={result.Result} recId={result.RecId ?? "-"} " +
                       $"code={result.Code ?? "-"} errorCode={result.ErrorCode ?? "-"} messageId={result.MessageId} traceId={traceId}");

            switch (result.Result)
            {
                case "applied":
                case "duplicate":
                    return Ok(new IndApiResponse<SyncResultDto>
                    {
                        Success = true,
                        Message = result.Result == "applied" ? "Mensaje aplicado en Axapta." : "Mensaje ya aplicado en Axapta.",
                        ErrorCode = null,
                        Errors = null,
                        Data = result,
                        TraceId = traceId
                    });
                case "divergent":
                    return Content(HttpStatusCode.Conflict, new IndApiResponse<SyncResultDto>
                    {
                        Success = false,
                        Message = result.Detail ?? "El registro cambio en Axapta despues de la version base conocida.",
                        ErrorCode = IndErrorCodes.SyncDivergent,
                        Errors = null,
                        Data = result,
                        TraceId = traceId
                    });
                case "rejected":
                    return Content((HttpStatusCode)422, new IndApiResponse<SyncResultDto>
                    {
                        Success = false,
                        Message = result.ErrorMessage ?? "Axapta rechazo el mensaje.",
                        ErrorCode = IndErrorCodes.SyncRejected,
                        Errors = null,
                        Data = result,
                        TraceId = traceId
                    });
                default:
                    return Content(HttpStatusCode.InternalServerError, new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Resultado de Axapta no reconocido.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    });
            }
        }

        /// <summary>
        /// Any COM or session failure is transient for the worker: 503 so the outbox retries later.
        /// </summary>
        protected IHttpActionResult AxFailure(Exception ex, string operation, string traceId)
        {
            var timeout = ex as IND_AxCallTimeoutException;
            Logger.Log($"[SYNC-ERROR] operation={operation} failureType={ex.GetType().Name} message={ex.Message} traceId={traceId}",
                AxaptaSessionManager.LogLevel.Error);

            return Content(HttpStatusCode.ServiceUnavailable, new IndApiResponse<object>
            {
                Success = false,
                Message = timeout != null
                    ? timeout.UserMessage
                    : "Axapta no esta disponible en este momento. El mensaje se reintentara.",
                ErrorCode = timeout != null ? IndErrorCodes.AxTimeout : IndErrorCodes.AxComError,
                Errors = null,
                Data = null,
                TraceId = traceId
            });
        }

        protected IHttpActionResult NotFoundResult(string message, string traceId)
        {
            return Content(HttpStatusCode.NotFound, new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = IndErrorCodes.SyncNotFound,
                Errors = null,
                Data = null,
                TraceId = traceId
            });
        }

        // ---------------------------------------------------------
        // Validation helpers shared by the sync actions
        // ---------------------------------------------------------

        protected static void AddText(List<IndValidationError> errors, string field, string value, int maxLength, bool required, bool singleLine = true)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                if (required)
                    errors.Add(new IndValidationError { Field = field, Message = $"{field} es obligatorio." });
                return;
            }

            if (maxLength > 0 && text.Length > maxLength)
                errors.Add(new IndValidationError { Field = field, Message = $"{field} supera {maxLength} caracteres." });

            if (singleLine && (text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0))
                errors.Add(new IndValidationError { Field = field, Message = $"{field} no admite saltos de linea." });

            if (!IsWindows1252(text))
                errors.Add(new IndValidationError { Field = field, Message = $"{field} contiene caracteres fuera de Windows-1252." });
        }

        protected static void AddGuid(List<IndValidationError> errors, string field, string value)
        {
            if (string.IsNullOrWhiteSpace(value) || !Guid.TryParse(value.Trim(), out _))
                errors.Add(new IndValidationError { Field = field, Message = $"{field} debe ser un UUID." });
        }

        protected static void AddEnumValue(List<IndValidationError> errors, string field, int? value, bool required)
        {
            if (!value.HasValue)
            {
                if (required)
                    errors.Add(new IndValidationError { Field = field, Message = $"{field} es obligatorio." });
                return;
            }

            if (value.Value < 0 || value.Value > 255)
                errors.Add(new IndValidationError { Field = field, Message = $"{field} debe ser un valor numerico de enum AX entre 0 y 255." });
        }

        protected static void AddChoice(List<IndValidationError> errors, string field, string value, IEnumerable<string> allowed, bool required)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                if (required)
                    errors.Add(new IndValidationError { Field = field, Message = $"{field} es obligatorio." });
                return;
            }

            if (!allowed.Contains(text, StringComparer.Ordinal))
                errors.Add(new IndValidationError { Field = field, Message = $"{field} admite: {string.Join(", ", allowed)}." });
        }

        /// <summary>Optional AX RecId (32-bit, non zero when present).</summary>
        protected static void AddRecId(List<IndValidationError> errors, string field, string value, bool required)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                if (required)
                    errors.Add(new IndValidationError { Field = field, Message = $"{field} es obligatorio." });
                return;
            }

            if (!int.TryParse(text, out var recId) || recId == 0)
                errors.Add(new IndValidationError { Field = field, Message = $"{field} debe ser un RecId de Axapta (entero distinto de cero)." });
        }

        protected static void AddIsoDate(List<IndValidationError> errors, string field, string value, bool required)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                if (required)
                    errors.Add(new IndValidationError { Field = field, Message = $"{field} es obligatorio." });
                return;
            }

            if (!AxSyncDateTime.TryIsoDateToAx(value, out _))
                errors.Add(new IndValidationError { Field = field, Message = $"{field} debe tener formato yyyy-MM-dd." });
        }

        protected static bool IsWindows1252(string value)
        {
            if (string.IsNullOrEmpty(value))
                return true;

            try
            {
                Windows1252.GetBytes(value);
                return true;
            }
            catch (EncoderFallbackException)
            {
                return false;
            }
        }
    }
}
