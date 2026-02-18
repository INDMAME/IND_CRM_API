using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Http;
using System.Web.Http.Description;

namespace IND_CRM_API.Controllers.System
{
    /// <summary>
    /// Endpoints del modulo System.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/system")]
    public class SystemController : ApiController
    {
        private static readonly Regex IsoCurrencyRegex = new Regex("^[A-Za-z]{3}$", RegexOptions.Compiled);

        private readonly IExchangeRateProvider _exchangeRateProvider;
        private readonly IAxLogger _logger;

        public SystemController(IExchangeRateProvider exchangeRateProvider, IAxLogger logger)
        {
            _exchangeRateProvider = exchangeRateProvider ?? throw new ArgumentNullException(nameof(exchangeRateProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Consulta tipo de cambio oficial del ECB para baseCurrency y targetCurrency.
        /// </summary>
        /// <remarks>
        /// Si una moneda no es EUR, la conversion se resuelve via EUR.
        /// ErrorCode posibles: VALIDATION_ERROR, EXCHANGE_RATE_NOT_FOUND, INTERNAL_ERROR.
        /// </remarks>
        /// <param name="baseCurrency">Moneda base ISO 4217 (3 letras).</param>
        /// <param name="targetCurrency">Moneda destino ISO 4217 (3 letras).</param>
        /// <param name="date">Fecha opcional yyyy-MM-dd; si no se envia se usa latest.</param>
        [HttpGet, Route("exchange-rate")]
        [SwaggerOperation(Tags = new[] { "Sistema" })]
        [ResponseType(typeof(IndApiResponse<ExchangeRateDto>))]
        [SwaggerResponse(HttpStatusCode.OK, "Tipo de cambio obtenido", typeof(IndApiResponse<ExchangeRateDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Error de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Tipo de cambio no disponible", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public async Task<IHttpActionResult> GetExchangeRate(
            [FromUri] string baseCurrency,
            [FromUri] string targetCurrency,
            [FromUri] string date = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            var traceId = Guid.NewGuid().ToString("N");
            var routePath = "/api/system/exchange-rate";

            _logger.Log($"[API-IN] GET {routePath} baseCurrency={baseCurrency} targetCurrency={targetCurrency} date={date} traceId={traceId}");

            void LogOut(HttpStatusCode statusCode)
            {
                _logger.Log($"[API-OUT] GET {routePath} status={(int)statusCode} traceId={traceId}");
            }

            var validationErrors = new List<IndValidationError>();
            if (string.IsNullOrWhiteSpace(baseCurrency))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "baseCurrency",
                    Message = "baseCurrency is required."
                });
            }
            else if (!IsoCurrencyRegex.IsMatch(baseCurrency.Trim()))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "baseCurrency",
                    Message = "baseCurrency must be a valid ISO 4217 code (3 letters)."
                });
            }

            if (string.IsNullOrWhiteSpace(targetCurrency))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "targetCurrency",
                    Message = "targetCurrency is required."
                });
            }
            else if (!IsoCurrencyRegex.IsMatch(targetCurrency.Trim()))
            {
                validationErrors.Add(new IndValidationError
                {
                    Field = "targetCurrency",
                    Message = "targetCurrency must be a valid ISO 4217 code (3 letters)."
                });
            }

            DateTime? requestedDate = null;
            if (!string.IsNullOrWhiteSpace(date))
            {
                if (DateTime.TryParseExact(
                    date.Trim(),
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var parsedDate))
                {
                    requestedDate = parsedDate.Date;
                }
                else
                {
                    validationErrors.Add(new IndValidationError
                    {
                        Field = "date",
                        Message = "date must use yyyy-MM-dd format."
                    });
                }
            }

            if (validationErrors.Count > 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Validation error.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };

                LogOut((HttpStatusCode)422);
                return Content((HttpStatusCode)422, validationResponse);
            }

            try
            {
                var normalizedBase = baseCurrency.Trim().ToUpperInvariant();
                var normalizedTarget = targetCurrency.Trim().ToUpperInvariant();

                var providerResult = await _exchangeRateProvider
                    .GetExchangeRateAsync(normalizedBase, normalizedTarget, requestedDate, cancellationToken)
                    .ConfigureAwait(false);

                if (providerResult == null || !providerResult.Found)
                {
                    var notFoundResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Exchange rate not available",
                        ErrorCode = IndErrorCodes.ExchangeRateNotFound,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };

                    LogOut(HttpStatusCode.NotFound);
                    return Content(HttpStatusCode.NotFound, notFoundResponse);
                }

                var okResponse = new IndApiResponse<ExchangeRateDto>
                {
                    Success = true,
                    Message = null,
                    ErrorCode = null,
                    Errors = null,
                    Data = new ExchangeRateDto
                    {
                        BaseCurrency = providerResult.BaseCurrency,
                        TargetCurrency = providerResult.TargetCurrency,
                        Rate = providerResult.Rate,
                        Date = providerResult.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Source = providerResult.Source
                    },
                    TraceId = traceId
                };

                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                _logger.Log($"[EXCHANGE-RATE-ERROR] {routePath} traceId={traceId} error={ex.Message}", AxaptaSessionManager.LogLevel.Error);

                var errorResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Internal server error.",
                    ErrorCode = IndErrorCodes.InternalError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };

                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, errorResponse);
            }
        }

        /// <summary>
        /// Endpoint publico de diagnostico para probar el tipo de cambio sin token.
        /// </summary>
        /// <remarks>
        /// Reutiliza la misma logica y contrato de /api/system/exchange-rate para comparar resultados.
        /// </remarks>
        /// <param name="baseCurrency">Moneda base ISO 4217 (3 letras).</param>
        /// <param name="targetCurrency">Moneda destino ISO 4217 (3 letras).</param>
        /// <param name="date">Fecha opcional yyyy-MM-dd; si no se envia se usa latest.</param>
        [AllowAnonymous]
        [HttpGet, Route("exchange-rate/public-direct")]
        [SwaggerOperation(Tags = new[] { "Sistema" })]
        [ResponseType(typeof(IndApiResponse<ExchangeRateDto>))]
        [SwaggerResponse(HttpStatusCode.OK, "Tipo de cambio obtenido", typeof(IndApiResponse<ExchangeRateDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Error de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Tipo de cambio no disponible", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public Task<IHttpActionResult> GetExchangeRatePublicDirect(
            [FromUri] string baseCurrency,
            [FromUri] string targetCurrency,
            [FromUri] string date = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return GetExchangeRate(baseCurrency, targetCurrency, date, cancellationToken);
        }
    }
}
