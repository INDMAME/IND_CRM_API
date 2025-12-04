using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using System.Web.Http.Description;
using AxaptaCOMConnector;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using IND_CRM_API.Contracts.Responses;

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
    public class CrmTemplateController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;

        public CrmTemplateController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        // Ejemplo: Llamada genérica a un método AX que devuelve contenedor
        [HttpGet, Route("sample")]
        [ResponseType(typeof(INDPagedResponse<object>))]
        public IHttpActionResult Sample()
        {
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
                    return Ok(new INDPagedResponse<object> { Success = false, Message = "Null AX response.", Total = 0, Items = new List<object>() });

                var data = Helpers.AxContainerHelper.ToArray(root);
                return Ok(new INDPagedResponse<object>
                {
                    Success = true,
                    Message = "OK",
                    Total = data?.Length ?? 0,
                    Items = data?.ToList() ?? new List<object>()
                });
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] Sample API: {ex.Message}");
                var response = new INDPagedResponse<object>
                {
                    Success = false,
                    Message = $"Error Sample: {ex.Message}",
                    Total = 0,
                    Items = new List<object>()
                };
                return Content(System.Net.HttpStatusCode.InternalServerError, response);
            }
        }
    }
}
