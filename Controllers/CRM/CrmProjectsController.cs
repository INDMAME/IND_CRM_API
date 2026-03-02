using System;
using System.Collections.Generic;
using System.Net;
using System.Runtime.InteropServices;
using System.Web.Http;
using System.Web.Http.Description;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers;
using IND_CRM_API.Helpers;
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
        private const int MaxPageSize = 50;
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
        /// <param name="filter">Filtro de busqueda.</param>
        /// <param name="page">Numero de pagina (>= 1).</param>
        /// <param name="pageSize">Tamano de pagina (>= 1).</param>
        [HttpGet, Route("list")]
        [ResponseType(typeof(IndPagedResponse<ProjectListItemDto>))]
        [SwaggerOperation(Tags = new[] { "Proyectos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Listado de proyectos", typeof(IndPagedResponse<ProjectListItemDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Errores de validacion", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.InternalServerError, "Error interno", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetProjectsList([FromUri] string filter = null, [FromUri] int? page = null, [FromUri] int? pageSize = null)
        {
            var traceId = Guid.NewGuid().ToString("N");
            var validationErrors = new List<IndValidationError>();

            // Validate company header.
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            if (!page.HasValue || page.Value <= 0)
                validationErrors.Add(new IndValidationError { Field = "page", Message = "page debe ser mayor que cero." });
            if (!pageSize.HasValue || pageSize.Value <= 0)
                validationErrors.Add(new IndValidationError { Field = "pageSize", Message = "pageSize debe ser mayor que cero." });
            if (pageSize.HasValue && pageSize.Value > MaxPageSize)
                validationErrors.Add(new IndValidationError { Field = "pageSize", Message = $"pageSize no puede ser mayor que {MaxPageSize}." });

            if (validationErrors.Count > 0)
            {
                var validationResponse = new IndApiResponse<object>
                {
                    Success = false,
                    Message = "Error de validacion.",
                    ErrorCode = IndErrorCodes.ValidationError,
                    Errors = validationErrors,
                    Data = null,
                    TraceId = traceId
                };
                return Content((HttpStatusCode)422, validationResponse);
            }

            // Logs the HTTP status for this action.
            void LogOut(HttpStatusCode statusCode)
            {
                Logger.Log($"[API-OUT] GetProjectsList {(int)statusCode} traceId={traceId}");
            }

            try
            {
                var username = GetAuthenticatedUsername();
                var pageValue = page.Value;
                var pageSizeValue = pageSize.Value;
                Logger.Log($"[API-IN] GetProjectsList filter={filter} page={pageValue} pageSize={pageSizeValue} user={username} traceId={traceId}");

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

                var items = MapProjectList(root, pageValue, pageSizeValue, out var message, out var total);
                var okResponse = new IndPagedResponse<ProjectListItemDto>
                {
                    Success = true,
                    Message = string.IsNullOrWhiteSpace(message) ? "OK" : message,
                    Total = total,
                    Page = pageValue,
                    PageSize = pageSizeValue,
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
        private static List<ProjectListItemDto> MapProjectList(IAxaptaContainer root, int page, int pageSize, out string message, out int total)
        {
            message = string.Empty;
            total = 0;
            var items = new List<ProjectListItemDto>();

            if (root == null || AxContainerReadHelper.SafeLength(root) == 0)
                return items;

            if (AxContainerReadHelper.IsSinDatos(root, out message))
                return items;

            total = AxContainerReadHelper.SafeLength(root);
            if (total <= 0)
                return items;

            var skipLong = ((long)page - 1L) * pageSize;
            if (skipLong < 0L)
                skipLong = 0L;

            if (skipLong >= total)
                return items;

            var start = (int)skipLong + 1;
            var end = Math.Min(total, start + pageSize - 1);
            for (int i = start; i <= end; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(root, i);
                if (row == null || AxContainerReadHelper.SafeLength(row) < 2)
                    continue;

                items.Add(new ProjectListItemDto
                {
                    ProjId = AxContainerReadHelper.SafeString(row, 1),
                    Name = AxContainerReadHelper.SafeString(row, 2)
                });
            }

            return items;
        }
    }
}
