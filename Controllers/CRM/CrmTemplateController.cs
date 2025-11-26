using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using AxaptaCOMConnector;
using IND_CRM_API.Controllers;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;

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

        public CrmTemplateController(IAxaptaSessionManager sessionManager) : base(sessionManager)
        {
            _sessionManager = sessionManager;
        }

        // Ejemplo: Llamada genérica a un método AX que devuelve contenedor
        [HttpGet, Route("sample")]
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
                    return Ok(new { success = false, message = "Respuesta nula de AX." });

                var data = Helpers.AxContainerHelper.ToArray(root);
                return Ok(new { success = true, data });
            }
            catch (Exception ex)
            {
                AxaptaSessionManager.LogStatic($"[ERROR] Sample API: {ex.Message}");
                return InternalServerError(new Exception($"Error Sample: {ex.Message}", ex));
            }
        }
    }
}
