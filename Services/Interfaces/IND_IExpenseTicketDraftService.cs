using System.Threading;
using System.Threading.Tasks;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services.Interfaces
{
    public interface IND_IExpenseTicketDraftService
    {
        /// <summary>
        /// Extrae un borrador de hoja de gastos desde una imagen de ticket.
        /// </summary>
        Task<ExpenseSheetDraftResponse> ExtractFromTicketImageAsync(
            byte[] imageBytes,
            string fileName,
            string contentType,
            string languageId,
            string currencyHint,
            CancellationToken cancellationToken);
    }
}
