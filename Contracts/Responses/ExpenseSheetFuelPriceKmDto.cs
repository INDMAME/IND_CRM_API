namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Precio por kilometro para gastos de combustible.
    /// </summary>
    public class ExpenseSheetFuelPriceKmDto
    {
        public decimal? PriceKm { get; set; }
        public string Source { get; set; }
        public string TransDate { get; set; }
    }
}
