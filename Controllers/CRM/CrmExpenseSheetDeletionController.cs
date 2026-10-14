using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Web.Http;
using System.Web.Http.Description;
using System.Web.Http.Results;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers;
using IND_CRM_API.Controllers.System;
using IND_CRM_API.Helpers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>Coordinates whole-sheet deletion and durable ticket cleanup through existing AX operations.</summary>
    [Authorize]
    [RoutePrefix("api/crm/expensesheets")]
    public sealed class CrmExpenseSheetDeletionController : BaseCrmController
    {
        private readonly ExpenseSheetDeletionService _deletions;
        private readonly ExpenseTicketBlobStorageService _blobs;

        /// <summary>Uses the shared request COM scope and a journal outside the deployment directory.</summary>
        public CrmExpenseSheetDeletionController(IAxaptaSessionManager sessions, IAxLogger logger)
            : base(sessions, logger)
        {
            _deletions = new ExpenseSheetDeletionService();
            _blobs = new ExpenseTicketBlobStorageService(logger);
        }

        /// <summary>Deletes an own draft sheet, then resumes only confirmed ticket-origin cleanup.</summary>
        /// <remarks>Requires bearer and X-IND-Company, X-IND-AxUserId, X-IND-EntraOid, X-IND-Context-Version,
        /// X-IND-Permissions-Revision and X-IND-Context-Token. Retry the same route and identity after a pending response.
        /// Older AX metadata preserves all tickets and files. Import the updated XPO to enable origin cleanup.</remarks>
        [HttpDelete, Route("{hojaGastosId}/with-tickets")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetDeletionProgressDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Borrado completado", typeof(IndApiResponse<ExpenseSheetDeletionProgressDto>))]
        [SwaggerResponse(HttpStatusCode.Conflict, "Operacion pendiente; reintentar la misma ruta", typeof(IndApiResponse<ExpenseSheetDeletionProgressDto>))]
        [SwaggerResponse(HttpStatusCode.Forbidden, "Actor o propietario no autorizado", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteWithTickets(string hojaGastosId)
        {
            var traceId = GetOrCreateTraceId();
            var identity = ResolveIdentity(hojaGastosId, traceId, out var error);
            if (error != null) return error;

            try
            {
                // Reuse the published mappers and error classification without HTTP or parallel COM calls.
                var sheets = ShareContext(new CrmExpenseSheetsController(SessionManager, Logger));
                var tickets = ShareContext(new CrmExpenseSheetTicketsController(SessionManager, Logger));
                ExpenseSheetDetailDto currentSheet = null;
                var progress = _deletions.Execute(identity.Scope, identity.Actor, identity.Owner,
                    () =>
                    {
                        currentSheet = ReadItem<ExpenseSheetDetailDto>(sheets.GetExpenseSheet(hojaGastosId),
                            IndErrorCodes.CrmExpenseSheetNotFound);
                        return currentSheet == null ? null : BuildSnapshot(currentSheet, identity.Owner,
                            id => ReadItem<ExpenseSheetTicketDetailDto>(tickets.GetExpenseSheetTicket(id),
                                IndErrorCodes.CrmExpenseSheetTicketNotFound), _blobs.ValidateTicketFileUrl);
                    },
                    () => RequireSuccess(sheets.DeleteExpenseSheetForCleanup(hojaGastosId, currentSheet), true),
                    ticket =>
                    {
                        var current = ReadItem<ExpenseSheetTicketDetailDto>(tickets.GetExpenseSheetTicket(ticket.FileId),
                            IndErrorCodes.CrmExpenseSheetTicketNotFound);
                        if (current == null) return;
                        ValidateTicketForCleanup(current, ticket, identity.Owner);
                        RequireSuccess(tickets.DeleteExpenseSheetTicketForCleanup(ticket.FileId, ticket.BlobUrl));
                    },
                    _blobs.EnsureTicketFileDeleted);
                return Ok(Response(true, progress, null, "Hoja de gastos eliminada correctamente.", traceId));
            }
            catch (UnauthorizedAccessException)
            {
                return Failure(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN", "Operacion no autorizada.", traceId);
            }
            catch (Exception ex)
            {
                // Never include saved file URLs or storage credentials in errors or logs.
                Logger.Log($"[EXPENSE-DELETE] failure type={ex.GetType().Name} traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                var progress = ReadAfterFailure(identity);
                if (progress?.SheetDeleted == true)
                    return Content(HttpStatusCode.Conflict, Response(false, progress, "CRM_EXPENSESHEET_CLEANUP_PENDING",
                        "La hoja se ha eliminado. Queda limpieza pendiente; reintente la misma operacion.", traceId));
                if (ex is ExistingActionException action) return action.Result;
                if (ex is DeletionRejectedException rejected && rejected.InnerException is ExistingActionException rejectedAction)
                    return rejectedAction.Result;
                if (ex is DeletionConflictException)
                    return Failure(HttpStatusCode.Conflict, "CRM_EXPENSESHEET_DELETE_CONFLICT", "Los datos han cambiado o la hoja no se puede eliminar. Actualice y reintente.", traceId);
                return Failure(ex is IOException ? HttpStatusCode.Conflict : HttpStatusCode.InternalServerError,
                    "CRM_EXPENSESHEET_DELETE_RETRY", "No se ha completado el borrado. Reintente la misma operacion.", traceId);
            }
        }

        /// <summary>Reads progress for the same signed actor, owner, company, tenant and environment.</summary>
        /// <remarks>Requires the same bearer and signed company headers as DELETE with-tickets.
        /// Exists=false means no journal exists; it does not mean the expense sheet is missing.</remarks>
        [HttpGet, Route("{hojaGastosId}/deletion")]
        [ResponseType(typeof(IndApiResponse<ExpenseSheetDeletionProgressDto>))]
        [SwaggerOperation(Tags = new[] { "Hojas de Gastos" })]
        [SwaggerResponse(HttpStatusCode.OK, "Estado de la operacion", typeof(IndApiResponse<ExpenseSheetDeletionProgressDto>))]
        [SwaggerResponse(HttpStatusCode.Forbidden, "Operacion no autorizada", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetDeletion(string hojaGastosId)
        {
            var traceId = GetOrCreateTraceId();
            var identity = ResolveIdentity(hojaGastosId, traceId, out var error);
            if (error != null) return error;
            try
            {
                return Ok(Response(true, _deletions.Read(identity.Scope, identity.Actor, identity.Owner), null, "OK", traceId));
            }
            catch (UnauthorizedAccessException)
            {
                return Failure(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN", "Operacion no autorizada.", traceId);
            }
            catch (Exception ex)
            {
                Logger.Log($"[EXPENSE-DELETE-STATUS] failure type={ex.GetType().Name} traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                return Failure(HttpStatusCode.InternalServerError, "CRM_EXPENSESHEET_DELETE_RETRY", "No se puede consultar el estado del borrado.", traceId);
            }
        }

        // Captures only assets whose every association explicitly identifies ticket origin.
        internal static DeletionSnapshot BuildSnapshot(ExpenseSheetDetailDto sheet, string owner,
            Func<string, ExpenseSheetTicketDetailDto> readTicket, Action<string> validateBlob)
        {
            if (sheet.ExpenseSheetStatus != 0 || !string.IsNullOrWhiteSpace(sheet.Voucher) ||
                (!string.IsNullOrWhiteSpace(sheet.OwnerAxUserId) && !Same(sheet.OwnerAxUserId, owner)))
                throw new DeletionConflictException();
            if (sheet.Lines == null) throw new DeletionConflictException();

            var linked = sheet.Lines.Where(line => !string.IsNullOrWhiteSpace(line.FileId)).ToList();
            var result = new DeletionSnapshot { MetadataAvailable = sheet.Lines.All(line => line.CreatedFromTicket.HasValue) };
            var groups = linked.GroupBy(line => line.FileId.Trim(), StringComparer.OrdinalIgnoreCase).ToList();
            result.PreservedManualTicketCount = groups.Count(group => !group.All(line => line.CreatedFromTicket == true));
            // Older AX versions do not identify origin; preserve all tickets in that case.
            if (!result.MetadataAvailable) return result;
            foreach (var group in groups.Where(group => group.All(line => line.CreatedFromTicket == true)))
            {
                var ticket = readTicket(group.Key);
                if (ticket == null) throw new DeletionConflictException();
                var ticketOwner = string.IsNullOrWhiteSpace(ticket.OwnerAxUserId) ? ticket.CreatedByUserId : ticket.OwnerAxUserId;
                if (!Same(ticket.FileId, group.Key) || !Same(ticketOwner, owner)) throw new DeletionConflictException();
                var blobUrl = (ticket.UrlFile ?? string.Empty).Trim();
                validateBlob(blobUrl);
                result.Tickets.Add(new DeletionTicket { FileId = group.Key, BlobUrl = blobUrl });
            }
            return result;
        }

        // Recheck ownership and assignment before the guarded AX deletion inside its transaction.
        internal static void ValidateTicketForCleanup(ExpenseSheetTicketDetailDto current, DeletionTicket saved, string owner)
        {
            var ticketOwner = string.IsNullOrWhiteSpace(current.OwnerAxUserId) ? current.CreatedByUserId : current.OwnerAxUserId;
            if (!Same(current.FileId, saved.FileId) || !Same(ticketOwner, owner) || current.Status != 0 ||
                !string.Equals((current.UrlFile ?? string.Empty).Trim(), saved.BlobUrl, StringComparison.Ordinal))
                throw new DeletionConflictException();
        }

        // Bind the journal to server-validated identity; a supplied owner cannot elevate permissions.
        private DeletionIdentity ResolveIdentity(string sheetId, string traceId, out IHttpActionResult error)
        {
            var company = RequireCompanyOrReturn422(out error, traceId);
            if (error != null) return null;
            var actor = RequireValidatedSnapshotAxUserIdOrReturn403(out error, traceId);
            if (error != null) return null;
            var owner = RequireAxUserIdOrReturn422(out error, traceId, IndErrorCodes.CrmExpenseSheetMissingFields);
            if (error != null) return null;
            if (!Same(actor, owner))
            {
                error = Failure(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN", "Solo puede eliminar sus propias hojas en borrador.", traceId);
                return null;
            }
            if (string.IsNullOrWhiteSpace(sheetId) || sheetId.Length > 100)
            {
                error = Failure((HttpStatusCode)422, IndErrorCodes.CrmExpenseSheetMissingFields, "HojaGastosId invalido.", traceId);
                return null;
            }
            var oid = Request.Headers.GetValues("X-IND-EntraOid").First().Trim();
            error = RequireCurrentDeletePermission(oid, actor, company, traceId);
            if (error != null) return null;
            var environment = AppSettingsHelper.GetSetting("Deployment:EnvironmentName", "IND_ENV");
            if (string.IsNullOrWhiteSpace(environment))
            {
                error = Failure(HttpStatusCode.InternalServerError, "CRM_EXPENSESHEET_DELETE_CONFIGURATION", "Entorno de borrado no configurado.", traceId);
                return null;
            }
            return new DeletionIdentity
            {
                Scope = JsonConvert.SerializeObject(new[] { ResolveTenantId(), environment, company, sheetId.Trim() }.Select(value => value.ToUpperInvariant())),
                Actor = JsonConvert.SerializeObject(new[] { oid, actor }.Select(value => value.ToUpperInvariant())),
                Owner = owner
            };
        }

        // Reads current AX rights through the deployed context contract without issuing or replacing a token.
        private IHttpActionResult RequireCurrentDeletePermission(string oid, string actor, string company, string traceId)
        {
            try
            {
                var ax = SessionManager.GetAxInstanceForUser(GetAuthenticatedUsername());
                var input = ax.CreateContainer();
                input.Append(oid);
                input.Append("CRM");
                var root = ax.CallStaticClassMethod("INDCRMUtilityService", "loginEntraContext", input) as IAxaptaContainer;
                var header = AuthController.MapEntraHeader(root);
                if (header == null)
                    return Failure(HttpStatusCode.BadGateway, IndErrorCodes.AxComError,
                        "No se pudo verificar el permiso de borrado en AX.", traceId);
                var companies = AuthController.MapEntraCompanies(root);
                if (!HasExpenseSheetDeletePermission(header, companies, actor, company))
                    return Failure(HttpStatusCode.Forbidden, IndErrorCodes.AuthForbidden,
                        "Se requiere permiso completo sobre hojas de gastos para esta operacion.", traceId);
                return null;
            }
            catch (Exception ex)
            {
                Logger.Log($"[EXPENSE-DELETE-PERMISSION] failure type={ex.GetType().Name} traceId={traceId}", AxaptaSessionManager.LogLevel.Warning);
                return Failure(HttpStatusCode.ServiceUnavailable, IndErrorCodes.AxComError,
                    "No se pudo verificar el permiso de borrado en AX. Reintente la operacion.", traceId);
            }
        }

        // Matches the APP FullAccess deletion policy and rejects changed actors or ambiguous company records.
        internal static bool HasExpenseSheetDeletePermission(EntraContextHeaderDto header,
            IEnumerable<EntraCompanyDto> companies, string actor, string company)
        {
            if (header?.Success != true || !header.UserActive || !header.AppActive || !Same(header.AxUserId, actor))
                return false;
            var matches = (companies ?? Enumerable.Empty<EntraCompanyDto>())
                .Where(item => item != null && Same(item.CompanyId, company)).ToList();
            if (matches.Count != 1 || matches[0].Modules == null) return false;
            var module = matches[0].Modules.FirstOrDefault(item => item != null && IsExpenseSheetModule(item.ModuleCode));
            return module?.IsActive == true && module.AccessRightsInt >= 4;
        }

        // Retains the existing APP aliases for the expense-sheet module.
        private static bool IsExpenseSheetModule(string code)
        {
            var normalized = new string((code ?? string.Empty).Normalize(NormalizationForm.FormD)
                .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(character))
                .Select(char.ToUpperInvariant).ToArray());
            return normalized == "GASTOSHOJAGASTO" || normalized == "GASTOHOJAGASTO" ||
                normalized == "GASTOSHOJAS" || normalized == "GASTOSHOJASGASTOS";
        }

        // Keep the original request and identity while using existing controller operations as adapters.
        private T ShareContext<T>(T controller) where T : ApiController
        {
            controller.Request = Request;
            controller.RequestContext = RequestContext;
            controller.Configuration = Configuration;
            return controller;
        }

        // Only the exact authoritative AX not-found result allows recovery after a lost response.
        internal static T ReadItem<T>(IHttpActionResult result, string notFoundCode) where T : class
        {
            var ok = result as OkNegotiatedContentResult<IndPagedResponse<T>>;
            if (ok?.Content?.Success == true && ok.Content.Items?.Count == 1 && ok.Content.Items[0] != null)
                return ok.Content.Items[0];
            var failure = result as NegotiatedContentResult<IndApiResponse<object>>;
            if (failure?.StatusCode == HttpStatusCode.NotFound && failure.Content?.ErrorCode == notFoundCode)
                return null;
            if (failure != null && !failure.Content.Success && (int)failure.StatusCode >= 400)
                throw new ExistingActionException(result);
            throw new InvalidOperationException("Invalid detail response.");
        }

        // Do not treat transport success or a malformed response as a confirmed business commit.
        private static void RequireSuccess(IHttpActionResult result, bool isSheetDelete = false)
        {
            if ((result as OkNegotiatedContentResult<IndApiResponse<object>>)?.Content?.Success == true) return;
            var failure = result as NegotiatedContentResult<IndApiResponse<object>>;
            if (failure != null && failure.Content?.Success == false && (int)failure.StatusCode >= 400)
            {
                var action = new ExistingActionException(result);
                if (isSheetDelete && (int)failure.StatusCode < 500) throw new DeletionRejectedException(action);
                throw action;
            }
            throw new InvalidOperationException("Invalid deletion response.");
        }

        // A missing or invalid journal never grants permission to skip normal record validation.
        private DeletionProgress ReadAfterFailure(DeletionIdentity identity)
        {
            try { return _deletions.Read(identity.Scope, identity.Actor, identity.Owner); }
            catch { return null; }
        }

        // Expose only the state required to retry the same operation from the web.
        private static IndApiResponse<ExpenseSheetDeletionProgressDto> Response(bool success, DeletionProgress progress,
            string code, string message, string traceId)
        {
            return new IndApiResponse<ExpenseSheetDeletionProgressDto>
            {
                Success = success, Message = message, ErrorCode = code, TraceId = traceId,
                Data = new ExpenseSheetDeletionProgressDto
                {
                    Exists = progress != null, DeleteAttempted = progress?.DeleteAttempted == true,
                    SheetDeleted = progress?.SheetDeleted == true,
                    Complete = progress?.Completed == true, MetadataAvailable = progress?.MetadataAvailable == true
                }
            };
        }

        // Return the shared API error envelope for validation and authorization failures.
        private IHttpActionResult Failure(HttpStatusCode status, string code, string message, string traceId)
        {
            return Content(status, Response(false, null, code, message, traceId));
        }

        // AX user and business ids are compared using their existing case-insensitive convention.
        private static bool Same(string left, string right) =>
            !string.IsNullOrWhiteSpace(left) && string.Equals(left.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

        // Holds validated operation boundaries without passing client paths to storage.
        private sealed class DeletionIdentity
        {
            public string Scope { get; set; }
            public string Actor { get; set; }
            public string Owner { get; set; }
        }

        // Carries an existing API error without reclassifying forbidden or unavailable as not found.
        private sealed class ExistingActionException : Exception
        {
            public ExistingActionException(IHttpActionResult result) { Result = result; }
            public IHttpActionResult Result { get; }
        }

        // Signals an inventory, workflow or assignment conflict before the next destructive step.
        private sealed class DeletionConflictException : Exception { }
    }
}
