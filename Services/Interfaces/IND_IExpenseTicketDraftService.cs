using System.Threading;
using System.Threading.Tasks;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Perfiles de extraccion IA para tickets segun el flujo que consume el draft.
    /// </summary>
    public enum ExpenseTicketDraftProfile
    {
        FullDraft = 0,
        QuickCreate = 1
    }

    public interface IND_IExpenseTicketDraftService
    {
        /// <summary>
        /// Extrae un borrador de hoja de gastos desde una imagen de ticket.
        /// </summary>
        Task<ExpenseSheetDraftResponse> ExtractFromTicketImageAsync(
            byte[] imageBytes,
            string fileName,
            string contentType,
            CancellationToken cancellationToken,
            ExpenseTicketDraftProfile profile = ExpenseTicketDraftProfile.FullDraft);
    }
}
