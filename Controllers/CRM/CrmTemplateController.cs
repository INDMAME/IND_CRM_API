using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;
using AxaptaCOMConnector;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Models.Responses;

/*
    Prompt:
        Quiero un endpoint nuevo de "X Cosa" que haga X y llame al metodo Y de AX
*/

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// Template de controlador CRM.
    /// Usar esta clase como base para crear nuevos endpoints de negocio.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/crm/template")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class CrmTemplateController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;

        public CrmTemplateController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// Ejemplo base para crear nuevos endpoints CRM contra Axapta.
        /// </summary>
        /// <remarks>
        /// Copia este metodo, ajusta el nombre de ruta y el metodo X++ a invocar.
        /// Rellena el contenedor con los parametros que requiera Axapta y mapea la respuesta a tu DTO.
        /// Usa IndApiResponse para comandos o IndPagedResponse para listados, con codigos de IndErrorCodes.
        /// Este controlador se oculta del OpenAPI para servir solo como plantilla interna.
        /// </remarks>
        /// <returns>Plantilla de respuesta con datos de ejemplo.</returns>
        [HttpGet, Route("sample")]
        [ResponseType(typeof(IndPagedResponse<object>))]
        public IHttpActionResult Sample()
        {
            var traceId = Guid.NewGuid().ToString("N");
            try
            {
                var username = GetAuthenticatedUsername();
                var ax = _sessionManager.GetAxInstanceForUser(username);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMApiClass",
                    "sampleMethod"
                );

                var root = resultObj as IAxaptaContainer;

                if (root == null)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Respuesta nula de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Errors = null,
                        Data = null,
                        TraceId = traceId
                    };
                    return Content(global::System.Net.HttpStatusCode.InternalServerError, errorResponse);
                }

                var data = Helpers.AxContainerHelper.ToArray(root);
                return Ok(new IndPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = data?.Length ?? 0,
                    Page = 1,
                    PageSize = data?.Length ?? 0,
                    Items = data?.ToList() ?? new List<object>(),
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] Sample API: {ex.Message}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = $"Error Sample: {ex.Message}",
                    ErrorCode = IndErrorCodes.AxComError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(global::System.Net.HttpStatusCode.InternalServerError, response);
            }
        }
    }
}

