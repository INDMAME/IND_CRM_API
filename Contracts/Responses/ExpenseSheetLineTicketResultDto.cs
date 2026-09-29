namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Result of changing the ticket association of an expense sheet line.
    /// </summary>
    public class ExpenseSheetLineTicketResultDto
    {
        /// <summary>Expense sheet identifier returned by AX.</summary>
        public string HojaGastosId { get; set; }

        /// <summary>Persisted expense sheet line identifier.</summary>
        public long LineRecId { get; set; }

        /// <summary>Ticket file identifier after applying the operation.</summary>
        public string FileId { get; set; }

        /// <summary>Numeric AX ticket status after applying the operation.</summary>
        public int? TicketStatus { get; set; }

        /// <summary>Indicates whether AX changed the association.</summary>
        public bool Changed { get; set; }
    }
}
