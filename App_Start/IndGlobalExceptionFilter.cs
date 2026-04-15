using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Services; // <-- Agregue esta línea si 'IAxLogger' está en este espacio de nombres
using System;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Filters;

namespace IND_CRM_API.App_Start
{
    /// <summary>
    /// Filtro global para capturar excepciones no controladas en Web API.
    /// </summary>
    public sealed class IndGlobalExceptionFilter : ExceptionFilterAttribute
    {
        /// <summary>
        /// Construye una respuesta estandar cuando ocurre una excepcion no controlada.
        /// </summary>
        public override void OnException(HttpActionExecutedContext context)
        {
            if (context == null)
                return;

            var traceId = Guid.NewGuid().ToString("N");
            TryLogException(context, traceId);

            var statusCode = HttpStatusCode.InternalServerError;
            var message = "Error interno del servidor";
            var errorCode = IndErrorCodes.InternalError;
            var retryAfterSeconds = default(int?);
            if (IND_KnownExceptionMapper.TryMap(context.Exception, out var mappedStatus, out var mappedMessage, out var mappedErrorCode, out var mappedRetryAfter))
            {
                statusCode = mappedStatus;
                message = mappedMessage;
                errorCode = mappedErrorCode;
                retryAfterSeconds = mappedRetryAfter;
            }

            var response = new IndApiResponse<object>
            {
                Success = false,
                Message = message,
                ErrorCode = errorCode,
                Errors = null,
                Data = null,
                TraceId = traceId
            };

            context.Response = context.Request.CreateResponse(statusCode, response);
            if (retryAfterSeconds.HasValue && retryAfterSeconds.Value > 0)
                context.Response.Headers.Add("Retry-After", retryAfterSeconds.Value.ToString());
        }

        private static void TryLogException(HttpActionExecutedContext context, string traceId)
        {
            try
            {
                var resolver = context.ActionContext?.RequestContext?.Configuration?.DependencyResolver
                               ?? GlobalConfiguration.Configuration.DependencyResolver;
                var logger = resolver?.GetService(typeof(IAxLogger)) as IAxLogger;
                var ex = context.Exception;
                var message = ex == null ? "Excepcion no especificada" : ex.GetType().FullName + " " + ex.Message;

                if (logger != null)
                {
                    logger.Log("[ERROR-GLOBAL] " + message + " traceId=" + traceId);
                    return;
                }

                Trace.TraceError("[ERROR-GLOBAL] " + message + " traceId=" + traceId);
            }
            catch
            {
                // No interrumpir el pipeline si falla el log.
            }
        }
    }
}
