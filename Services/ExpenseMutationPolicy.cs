using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services
{
    // Names the existing APP write families without granting new delegated operations.
    internal enum ExpenseMutationOperation
    {
        SheetCreate, SheetAddLines, SheetHeaderUpdate, SheetLineUpdate, SheetLineDelete,
        SheetDelete, SheetPropagate, SheetReimbursementPropagate, SheetCurrencyPropagate, SheetTicketAssociation,
        TicketCreate, TicketUpdate, TicketDelete, TicketLineCreate, TicketLineUpdate,
        TicketLineDelete, TicketFileUpload, TicketFileDelete, TicketTotalAdjustment,
        TicketAiUpdate, TicketBulkLink, TicketCleanup
    }

    // Mirrors the APP status matrix; HTTP and AX access remain outside this policy.
    internal sealed class ExpenseMutationPolicy
    {
        internal bool FullEdit { get; private set; }
        internal bool StatusOnly { get; private set; }
        internal bool CanDelete { get; private set; }
        internal HashSet<int> NextStatuses { get; } = new HashSet<int>();

        // Keeps paid sheets locked and preserves owner, self-management and manager transitions.
        internal static ExpenseMutationPolicy Resolve(int? status, bool paid, bool managed, bool selfManagement)
        {
            var policy = new ExpenseMutationPolicy();
            if (paid) return policy;
            if (managed)
            {
                if (status == 1) policy.NextStatuses.UnionWith(new[] { 2, 3 });
                else if (status == 2 || status == 3) policy.NextStatuses.Add(1);
            }
            else if (selfManagement)
            {
                if (status == 0) { policy.FullEdit = true; policy.NextStatuses.Add(2); }
                else if (status == 2) policy.NextStatuses.Add(0);
            }
            else
            {
                if (status == 0) { policy.FullEdit = true; policy.NextStatuses.Add(1); }
                else if (status == 1 || status == 3) policy.NextStatuses.Add(0);
            }
            policy.CanDelete = policy.FullEdit;
            policy.StatusOnly = !policy.FullEdit && policy.NextStatuses.Count > 0;
            return policy;
        }

        // Preserves the APP route exceptions instead of treating every POST as a new record.
        internal static int RequiredAccess(ExpenseMutationOperation operation)
        {
            if (operation == ExpenseMutationOperation.SheetPropagate ||
                operation == ExpenseMutationOperation.SheetReimbursementPropagate ||
                operation == ExpenseMutationOperation.SheetTicketAssociation) return 2;
            if (operation == ExpenseMutationOperation.SheetDelete || operation == ExpenseMutationOperation.SheetLineDelete ||
                operation == ExpenseMutationOperation.TicketDelete || operation == ExpenseMutationOperation.TicketLineDelete ||
                operation == ExpenseMutationOperation.TicketFileDelete || operation == ExpenseMutationOperation.TicketCleanup) return 4;
            if (operation == ExpenseMutationOperation.SheetHeaderUpdate || operation == ExpenseMutationOperation.SheetLineUpdate ||
                operation == ExpenseMutationOperation.TicketUpdate || operation == ExpenseMutationOperation.TicketLineUpdate) return 2;
            return 3;
        }

        // Matches INDModuleAuthorizeFilter, including its existing self-management override.
        internal static bool HasModuleAccess(EntraCompanyDto company, ExpenseMutationOperation operation,
            string method, string path)
        {
            var ticket = operation != ExpenseMutationOperation.TicketCleanup && operation.ToString().StartsWith("Ticket", StringComparison.Ordinal);
            var module = company?.Modules?.FirstOrDefault(item => item != null && IsModule(item.ModuleCode, ticket));
            var required = RequiredAccess(operation);
            if (module != null && module.AccessRightsInt >= required) return true;
            return company?.AllowSelfManagement == true && required >= 2 &&
                (string.Equals(method, "PUT", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(method, "PATCH", StringComparison.OrdinalIgnoreCase)) &&
                (path ?? string.Empty).StartsWith("/api/crm/expensesheets/", StringComparison.OrdinalIgnoreCase) &&
                (path ?? string.Empty).IndexOf("/lines/", StringComparison.OrdinalIgnoreCase) < 0;
        }

        // Omitted reimbursement preserves AX state; only concrete values can be edited or propagated.
        internal static bool CanUpdateReimbursement(int? stored, int? requested)
        {
            return !requested.HasValue ||
                ((stored == 0 || stored == 1 || stored == 2) && IsConcreteReimbursement(requested));
        }

        // The mixed header value is derived from lines and cannot be propagated to them.
        internal static bool IsConcreteReimbursement(int? value)
        {
            return value == 0 || value == 1;
        }

        // Retains the canonical and legacy module aliases accepted by the APP.
        private static bool IsModule(string code, bool ticket)
        {
            var normalized = new string((code ?? string.Empty).Normalize(System.Text.NormalizationForm.FormD)
                .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark && char.IsLetterOrDigit(c))
                .Select(char.ToUpperInvariant).ToArray());
            return ticket ? normalized == "GASTOSTICKETS" || normalized == "GASTOTICKETS" :
                normalized == "GASTOSHOJAGASTO" || normalized == "GASTOHOJAGASTO" ||
                normalized == "GASTOSHOJAS" || normalized == "GASTOSHOJASGASTOS";
        }

        // Status actions retain stored business fields exactly as the APP does before forwarding.
        internal static UpdateExpenseSheetHeaderRequest StatusPayload(ExpenseSheetDetailDto sheet,
            UpdateExpenseSheetHeaderRequest request)
        {
            return new UpdateExpenseSheetHeaderRequest
            {
                description = string.IsNullOrWhiteSpace(sheet.Description) ? "Hoja de gastos " + sheet.HojaGastosId : sheet.Description,
                currencyCode = sheet.CurrencyCode,
                exchRate = string.Equals(sheet.CurrencyCode?.Trim(), "EUR", StringComparison.OrdinalIgnoreCase)
                    ? 100m : sheet.ExchRate ?? 0m,
                projId = null,
                projIdProvided = false,
                expenseSheetStatus = request.expenseSheetStatus,
                exchangeRateMode = sheet.ExchangeRateMode,
                estadoComentarios = request.estadoComentarios,
                reimbursableExpense = sheet.ReimbursableExpense == 0 || sheet.ReimbursableExpense == 1
                    ? sheet.ReimbursableExpense : null
            };
        }
    }
}
