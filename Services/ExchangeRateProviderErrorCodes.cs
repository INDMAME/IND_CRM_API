namespace IND_CRM_API.Services
{
    /// <summary>
    /// Codigos internos para decisiones de fallback de proveedores de cambio.
    /// </summary>
    public static class ExchangeRateProviderErrorCodes
    {
        public const string CurrencyNotFound = "CURRENCY_NOT_FOUND";
        public const string ProviderError = "PROVIDER_ERROR";
        public const string RateUnavailable = "RATE_UNAVAILABLE";
    }
}
