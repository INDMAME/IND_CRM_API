namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Minimal data for expense sheet list rows.
    /// </summary>
    public class ExpenseSheetListItemDto
    {
        public string HojaGastosId { get; set; }
        public string Description { get; set; }
        public string ProjId { get; set; }
        public string CurrencyCode { get; set; }
        public string CreatedDate { get; set; }
    }
}
