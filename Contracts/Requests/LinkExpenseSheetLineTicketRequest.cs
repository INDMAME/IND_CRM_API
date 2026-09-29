using System.ComponentModel.DataAnnotations;

namespace IND_CRM_API.Contracts.Requests
{
    /// <summary>
    /// Request data to associate a ticket with an existing expense sheet line.
    /// </summary>
    public class LinkExpenseSheetLineTicketRequest
    {
        /// <summary>Ticket file identifier to associate.</summary>
        [Required]
        public string fileId { get; set; }
    }
}
