namespace IND_CRM_API.Contracts.Responses
{
    /// <summary>
    /// Moneda disponible para captura de hojas de gastos.
    /// </summary>
    public class ExpenseSheetCurrencyDto
    {
        public string CurrencyCode { get; set; }
        public string CurrencyCodeISO { get; set; }
    }
}
