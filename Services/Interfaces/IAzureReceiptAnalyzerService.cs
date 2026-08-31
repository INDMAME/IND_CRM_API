using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services.Interfaces
{
    /// <summary>
    /// Runs Azure Document Intelligence receipt OCR from a blob URL.
    /// </summary>
    public interface IAzureReceiptAnalyzerService
    {
        Task<AzureReceiptAnalysisResult> AnalyzeReceiptFromBlobUrlAsync(string blobReadUrl, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Structured OCR result used by the ticket normalization pipeline.
    /// </summary>
    public sealed class AzureReceiptAnalysisResult
    {
        public string RawJson { get; set; }
        public string PromptJson { get; set; }
        public string MerchantName { get; set; }
        public string TransactionDate { get; set; }
        public string CurrencyCode { get; set; }
        public string RawCurrency { get; set; }
        public decimal? TotalAmount { get; set; }
        internal bool HasAuthoritativeVndTotal { get; set; }
        public int ItemCount { get; set; }
        public List<string> Warnings { get; set; }
        public List<string> CurrencyHints { get; set; }
    }
}
