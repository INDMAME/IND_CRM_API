using System;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.System
{
    [Authorize]
    [RoutePrefix("api/mcp")]
    public class McpToolsController : ApiController
    {
        /// <summary>
        /// Returns the MCP tools catalog for clients.
        /// </summary>
        [HttpGet, Route("tools")]
        [ResponseType(typeof(object))]
        [SwaggerOperation(Tags = new[] { "MCP" })]
        [SwaggerResponse(HttpStatusCode.OK, "Catalogo MCP", typeof(object))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetTools()
        {
            var traceId = Guid.NewGuid().ToString("N");

            if (!McpToolsLoader.TryLoad(out var tools, out var error))
            {
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = string.IsNullOrWhiteSpace(error) ? "Error al cargar MCP_TOOLS.json." : error,
                    ErrorCode = IndErrorCodes.InternalError,
                    Errors = null,
                    Data = null,
                    TraceId = traceId
                };
                return Content(HttpStatusCode.InternalServerError, response);
            }

            return Ok(tools);
        }
    }
}
