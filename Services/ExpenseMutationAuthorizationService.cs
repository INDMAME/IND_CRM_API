using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers.CRM;
using IND_CRM_API.Controllers.System;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Services
{
    // Carries only server-validated actors and the permitted header payload to the AX write.
    internal sealed class ExpenseMutationAuthorization
    {
        internal string OwnerAxUserId { get; set; }
        internal string NotificationActorAxUserId { get; set; }
        internal UpdateExpenseSheetHeaderRequest Header { get; set; }
    }

    // Separates a denied business operation from an unavailable authorization read.
    internal sealed class ExpenseAuthorizationException : Exception
    {
        internal HttpStatusCode Status { get; }
        internal string Code { get; }
        internal ExpenseAuthorizationException(HttpStatusCode status, string code, string message) : base(message)
        { Status = status; Code = code; }
    }

    // Rechecks existing APP permissions against AX; its only reuse lasts for one HTTP request.
    internal sealed class ExpenseMutationAuthorizationService
    {
        private readonly Func<string, string, object[], IAxaptaContainer> _read;
        private readonly string _actor;
        private readonly string _companyId;
        private readonly string _oid;
        private EntraCompanyDto _company;
        private List<ExpenseSheetSubordinateDto> _subordinates;

        // Uses the shared COM session and leaves container ownership to its request scope.
        internal ExpenseMutationAuthorizationService(IAxaptaSessionManager sessions, string technicalUser,
            string actor, string company, string oid)
            : this((className, method, values) =>
            {
                var ax = sessions.GetAxInstanceForUser(technicalUser);
                var input = ax.CreateContainer();
                foreach (var value in values) input.Append(value);
                return ax.CallStaticClassMethod(className, method, input) as IAxaptaContainer;
            }, actor, company, oid) { }

        // Supports isolated tests without constructing a COM session or reading live configuration.
        internal ExpenseMutationAuthorizationService(Func<string, string, object[], IAxaptaContainer> read,
            string actor, string company, string oid)
        { _read = read ?? throw new ArgumentNullException(nameof(read)); _actor = actor; _companyId = company; _oid = oid; }

        // Refuses owner substitution before AX writes and applies the existing operation-specific matrix.
        internal ExpenseMutationAuthorization Authorize(ExpenseMutationOperation operation, string requestedOwner,
            string method, string path, string sheetId = null, string ticketId = null,
            UpdateExpenseSheetHeaderRequest header = null)
        {
            var company = CurrentCompany();
            if (!ExpenseMutationPolicy.HasModuleAccess(company, operation, method, path))
                throw Denied("Se requiere permiso sobre el modulo para esta operacion.");
            var ownRequest = IsOwn(requestedOwner);
            var result = new ExpenseMutationAuthorization { OwnerAxUserId = _actor, Header = header };
            if (operation.ToString().StartsWith("Ticket", StringComparison.Ordinal))
            {
                if (!ownRequest) throw Denied("No se permite modificar tickets de otro usuario.");
                if (!string.IsNullOrWhiteSpace(ticketId)) ReadOwnTicket(ticketId);
                if (!string.IsNullOrWhiteSpace(sheetId)) RequireOwnEditableSheet(sheetId);
                return result;
            }
            if (operation == ExpenseMutationOperation.SheetCreate)
            {
                if (!ownRequest) throw Denied("No se permite crear hojas para otro usuario.");
                return result;
            }
            if (string.IsNullOrWhiteSpace(sheetId)) throw Unavailable();
            var sheet = ReadSheet(sheetId);
            var owner = string.IsNullOrWhiteSpace(sheet.OwnerAxUserId) ? sheet.UserId : sheet.OwnerAxUserId;
            if (string.IsNullOrWhiteSpace(owner)) throw Unavailable();
            var managed = !IsOwn(owner);
            if (managed)
            {
                if (operation != ExpenseMutationOperation.SheetHeaderUpdate || ownRequest)
                    throw Denied("Solo se permiten las acciones de estado sobre hojas del ambito de jefatura.");
                var subordinate = Subordinates().FirstOrDefault(item => Matches(item, owner) && Matches(item, requestedOwner));
                if (subordinate == null) throw Denied("El propietario no pertenece al ambito de jefatura del usuario.");
                result.OwnerAxUserId = string.IsNullOrWhiteSpace(subordinate.AxUserId) ? subordinate.UserId : subordinate.AxUserId;
            }
            else if (!ownRequest) throw Denied("El propietario solicitado no coincide con la hoja.");

            var paid = sheet.ExpenseSheetStatus == 4 || HasVoucher(sheet.Voucher);
            var policy = ExpenseMutationPolicy.Resolve(sheet.ExpenseSheetStatus, paid, managed, company.AllowSelfManagement);
            if (paid) throw ReadOnly(true);
            if (operation != ExpenseMutationOperation.SheetHeaderUpdate)
            {
                if (!policy.FullEdit) throw ReadOnly(false);
                if (operation == ExpenseMutationOperation.SheetReimbursementPropagate &&
                    !ExpenseMutationPolicy.IsConcreteReimbursement(sheet.ReimbursableExpense))
                    throw new ExpenseAuthorizationException(HttpStatusCode.BadRequest,
                        "INVALID_REQUEST", "El indicador de reembolso no permite esta propagacion.");
                if (operation == ExpenseMutationOperation.SheetTicketAssociation && !string.IsNullOrWhiteSpace(ticketId))
                    ReadOwnTicket(ticketId);
                return result;
            }
            if (header == null) throw Unavailable();
            if (!ExpenseMutationPolicy.CanUpdateReimbursement(sheet.ReimbursableExpense, header.reimbursableExpense))
                throw new ExpenseAuthorizationException(HttpStatusCode.Conflict,
                    "CRM_EXPENSESHEET_REIMBURSABLE_READ_ONLY", "El indicador de reembolso es de solo lectura.");
            var next = header.expenseSheetStatus ?? sheet.ExpenseSheetStatus;
            var changed = next != sheet.ExpenseSheetStatus;
            if ((!policy.FullEdit && !policy.StatusOnly) || (policy.StatusOnly && !changed)) throw ReadOnly(false);
            if (changed && (!next.HasValue || !policy.NextStatuses.Contains(next.Value)))
                throw new ExpenseAuthorizationException(HttpStatusCode.Conflict,
                    "CRM_EXPENSESHEET_STATUS_TRANSITION_NOT_ALLOWED", "La transicion de estado no esta permitida.");
            if (policy.StatusOnly) result.Header = ExpenseMutationPolicy.StatusPayload(sheet, header);
            if (managed && changed) result.NotificationActorAxUserId = _actor;
            return result;
        }

        // Resolves the exact current company, never a fallback company or a cached authorization grant.
        private EntraCompanyDto CurrentCompany()
        {
            if (_company != null) return _company;
            if (string.IsNullOrWhiteSpace(_actor) || string.IsNullOrWhiteSpace(_oid) || string.IsNullOrWhiteSpace(_companyId))
                throw Denied("Contexto de usuario requerido.");
            var root = _read("INDCRMUtilityService", "loginEntraContext", new object[] { _oid, "CRM" });
            var header = AuthController.MapEntraHeader(root);
            if (header == null) throw Unavailable();
            if (!header.Success && !AuthController.HasExplicitContextDenial(root)) throw Unavailable();
            if (!header.Success || !header.UserActive || !header.AppActive || !Same(header.AxUserId, _actor))
                throw Denied("El contexto actual no permite esta operacion.");
            var companies = AuthController.MapEntraCompanies(root);
            var matches = companies.Where(item => item != null && Same(item.CompanyId, _companyId)).ToList();
            if (matches.Count != 1) throw Denied("Compania no permitida para el usuario.");
            _company = matches[0];
            return _company;
        }

        // Queries the same deployed hierarchy used by the APP, using the signed actor as its root.
        private List<ExpenseSheetSubordinateDto> Subordinates()
        {
            if (_subordinates != null) return _subordinates;
            var root = _read("INDCRMExpenseSheetService", "getSubordinatesByUser", new object[] { _companyId, _actor });
            if (root == null) throw Unavailable();
            var items = new List<ExpenseSheetSubordinateDto>();
            var count = root.Length();
            for (var i = 1; i <= count; i++)
            {
                var value = root.Peek(i);
                if (count == 1 && value is string && Same(Text(value), "Sin datos.")) break;
                var row = value as IAxaptaContainer;
                if (row == null) throw Unavailable();
                var size = row.Length();
                if (count == 1 && size == 1 && Same(Text(row.Peek(1)), "Sin datos.")) break;
                if (size < 2) throw Unavailable();
                var first = Text(row.Peek(1));
                var second = Text(row.Peek(2));
                items.Add(new ExpenseSheetSubordinateDto { UserId = first,
                    AxUserId = size >= 3 ? second : first, Name = size >= 3 ? Text(row.Peek(3)) : second });
            }
            _subordinates = items;
            return items;
        }

        // Reads only the header needed for authorization through the signed-viewer detail contract.
        private ExpenseSheetDetailDto ReadSheet(string sheetId)
        {
            var root = _read("INDCRMExpenseSheetService", "getExpenseSheet", new object[] { _companyId, _actor, sheetId.Trim(), 1 });
            var extras = ReadHeader(root, "CRM_EXPENSESHEET_NOT_FOUND");
            var detail = CrmExpenseSheetsController.MapExpenseSheetDetail(extras, null);
            if (detail == null || !Same(detail.HojaGastosId, sheetId) || !detail.ExpenseSheetStatus.HasValue) throw Unavailable();
            return detail;
        }

        // Existing-ticket writes require an actual ticket owned by the signed-in user.
        private void ReadOwnTicket(string ticketId)
        {
            var root = _read("INDCRMExpenseSheetService", "getExpenseSheetTicket", new object[] { _companyId, _actor, ticketId.Trim() });
            var detail = CrmExpenseSheetTicketsController.MapExpenseSheetTicketDetail(ReadHeader(root, "CRM_EXPENSESHEET_TICKET_NOT_FOUND"), null);
            if (detail == null || !Same(detail.FileId, ticketId)) throw Unavailable();
            var owner = string.IsNullOrWhiteSpace(detail.OwnerAxUserId) ? detail.CreatedByUserId : detail.OwnerAxUserId;
            if (string.IsNullOrWhiteSpace(owner)) throw Unavailable();
            if (!IsOwn(owner)) throw Denied("El ticket no pertenece al usuario.");
        }

        // Linking tickets to an existing sheet preserves the APP owner and editable-draft restriction.
        private void RequireOwnEditableSheet(string sheetId)
        {
            var sheet = ReadSheet(sheetId);
            var owner = string.IsNullOrWhiteSpace(sheet.OwnerAxUserId) ? sheet.UserId : sheet.OwnerAxUserId;
            if (!IsOwn(owner)) throw Denied("La hoja no pertenece al usuario.");
            var paid = sheet.ExpenseSheetStatus == 4 || HasVoucher(sheet.Voucher);
            if (!ExpenseMutationPolicy.Resolve(sheet.ExpenseSheetStatus, paid, false, CurrentCompany().AllowSelfManagement).FullEdit)
                throw ReadOnly(paid);
        }

        // Reads authorization fields without the permissive COM fallback used for display data.
        private static List<string> ReadHeader(IAxaptaContainer root, string notFoundCode)
        {
            if (root == null) throw Unavailable();
            var size = root.Length();
            var header = size >= 2 ? root.Peek(1) as IAxaptaContainer : root;
            if (header == null || header.Length() < 1) throw Unavailable();
            var row = header.Peek(1) as IAxaptaContainer ?? header;
            if (row.Length() < 2) throw Unavailable();
            var success = Text(row.Peek(1));
            var message = Text(row.Peek(2));
            if (success != "1" && !Same(success, "true"))
            {
                if ((success == "0" || Same(success, "false")) &&
                    (message.IndexOf("no encontrad", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     message.IndexOf("no existe", StringComparison.OrdinalIgnoreCase) >= 0))
                    throw new ExpenseAuthorizationException(HttpStatusCode.NotFound, notFoundCode, "Registro no encontrado.");
                throw Unavailable();
            }
            var extras = new List<string>();
            for (var i = 3; i <= row.Length(); i++) extras.Add(Text(row.Peek(i)));
            return extras;
        }

        // Accepts only the current actor and its company-specific CRM alias.
        private bool IsOwn(string user) { return Same(user, _actor) || Same(user, CurrentCompany().CrmUserId); }
        private static bool Matches(ExpenseSheetSubordinateDto item, string user)
        { return item != null && (Same(item.AxUserId, user) || Same(item.UserId, user)); }
        internal static bool Same(string left, string right)
        { return !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase); }
        private static string Text(object value) { return Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty; }
        private static bool HasVoucher(string value)
        { return !string.IsNullOrWhiteSpace(value) && !Same(value, "-") && !Same(value, ".") && !Same(value, "0"); }
        private static ExpenseAuthorizationException Denied(string message)
        { return new ExpenseAuthorizationException(HttpStatusCode.Forbidden, "AUTH_FORBIDDEN", message); }
        private static ExpenseAuthorizationException ReadOnly(bool paid)
        { return new ExpenseAuthorizationException(HttpStatusCode.Conflict, paid ? "CRM_EXPENSESHEET_PAID_READ_ONLY" : "CRM_EXPENSESHEET_STATUS_READ_ONLY", "La hoja de gastos es de solo lectura."); }
        private static ExpenseAuthorizationException Unavailable()
        { return new ExpenseAuthorizationException(HttpStatusCode.ServiceUnavailable, "AX_COM_ERROR", "No se pudo verificar el permiso actual. Reintente la operacion."); }
    }
}
