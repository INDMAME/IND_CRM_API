using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Web.Http;
using System.Web.Http.Description;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// CRM endpoints for projects list.
    /// </summary>
    [Authorize]
    [RoutePrefix("api/crm/projects")]
    public class CrmProjectsController : BaseCrmController
    {
        private readonly IAxaptaSessionManager _sessionManager;

        /// <summary>
        /// Creates the controller with its dependencies.
        /// </summary>
        public CrmProjectsController(IAxaptaSessionManager sessionManager, IAxLogger logger) : base(sessionManager, logger)
        {
            _sessionManager = sessionManager;
        }

        /// <summary>
        /// Lists projects filtered by search text.
        /// </summary>
        [HttpGet, Route("list")]
        [ResponseType(typeof(IndPagedResponse<ProjectListItemDto>))]
        [SwaggerOperation(Tags = new[] { "Proyectos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de proyectos", typeof(IndPagedResponse<ProjectListItemDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetProjectsList([FromUri] string filter = null)
        {
            var traceId = Guid.NewGuid().ToString("N");

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetProjectsList {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[API-IN] GetProjectsList filter={filter} user={username} traceId={traceId}");

                var ax = _sessionManager.GetAxInstanceForUser(username);
                var con = ax.CreateContainer();
                con.Append(company);
                con.Append(filter?.Trim() ?? string.Empty);

                object resultObj = ax.CallStaticClassMethod(
                    "INDCRMExpenseSheetService",
                    "getProjectsList",
                    con
                );

                var root = resultObj as IAxaptaContainer;
                if (root == null && resultObj != null)
                {
                    var errorResponse = new IndApiResponse<object>
                    {
                        Success = false,
                        Message = "Error al procesar la respuesta de AX.",
                        ErrorCode = IndErrorCodes.AxComError,
                        Data = null,
                        TraceId = traceId
                    };
                    LogOut(HttpStatusCode.InternalServerError);
                    return Content(HttpStatusCode.InternalServerError, errorResponse);
                }

                var items = MapProjectList(root, out var message);
                var okResponse = new IndPagedResponse<ProjectListItemDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Total = items.Count,
                    Items = items,
                    TraceId = traceId
                };

                LogOut(HttpStatusCode.OK);
                return Ok(okResponse);
            }
            catch (Exception ex)
            {
                Logger.Log($"[ERROR] GetProjectsList: {ex}");
                var response = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error interno del servidor.",
                    ErrorCode = ex is COMException ? IndErrorCodes.AxComError : IndErrorCodes.AxSessionError,
                    Data = null,
                    TraceId = traceId
                };
                LogOut(HttpStatusCode.InternalServerError);
                return Content(HttpStatusCode.InternalServerError, response);
            }
        }

        // Maps list items for project list endpoint.
        private static List<ProjectListItemDto> MapProjectList(IAxaptaContainer root, out string message)
        {
            message = string.Empty;
            var items = new List<ProjectListItemDto>();

            if (root == null || SafeLength(root) == 0)
                return items;

            if (IsSinDatos(root, out message))
                return items;

            var len = SafeLength(root);
            for (int i = 1; i <= len; i++)
            {
                var row = SafePeekContainer(root, i);
                if (row == null || SafeLength(row) < 2)
                    continue;

                items.Add(new ProjectListItemDto
                {
                    ProjId = SafeString(row, 1),
                    Name = SafeString(row, 2)
                });
            }

            return items;
        }

        // Checks the common AX "Sin datos." marker.
        private static bool IsSinDatos(IAxaptaContainer root, out string message)
        {
            message = string.Empty;
            if (root == null || SafeLength(root) == 0)
                return false;

            if (SafeLength(root) == 1)
            {
                var single = SafeValue(root, 1);
                if (single is string str && str.Equals("Sin datos.", StringComparison.OrdinalIgnoreCase))
                {
                    message = "Sin datos.";
                    return true;
                }

                var row = single as IAxaptaContainer;
                if (row != null && SafeLength(row) == 1)
                {
                    var first = SafeString(row, 1);
                    if (first.Equals("Sin datos.", StringComparison.OrdinalIgnoreCase))
                    {
                        message = "Sin datos.";
                        return true;
                    }
                }
            }

            return false;
        }

        // Safe container peek.
        private static IAxaptaContainer SafePeekContainer(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index) as IAxaptaContainer;
            }
            catch
            {
                return null;
            }
        }

        // Safe container length.
        private static int SafeLength(IAxaptaContainer container)
        {
            try
            {
                return container?.Length() ?? 0;
            }
            catch
            {
                return 0;
            }
        }

        // Safe string conversion from container.
        private static string SafeString(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index)?.ToString() ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        // Safe raw value from container.
        private static object SafeValue(IAxaptaContainer container, int index)
        {
            try
            {
                return container?.Peek(index);
            }
            catch
            {
                return null;
            }
        }
    }
}
