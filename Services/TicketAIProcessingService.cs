using IND_CRM_API.Helpers;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Services.Interfaces;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Runs the active ticket AI pipeline: blob URL -> Azure OCR -> OpenAI normalization.
    /// </summary>
    public sealed class TicketAIProcessingService : IND_IExpenseTicketDraftService, ITicketAIProcessingService
    {
        private const string BlobReadSasMinutesSettingKey = "AzureDocsIA:BlobReadSasMinutes";
        private const int DefaultBlobReadSasMinutes = 15;

        private readonly IExpenseTicketBlobStorageService _blobStorage;
        private readonly IAzureReceiptAnalyzerService _receiptAnalyzer;
        private readonly IOpenAITicketNormalizationService _normalizer;
        private readonly IAxLogger _logger;
        private readonly int _blobReadSasMinutes;

        public TicketAIProcessingService(
            IExpenseTicketBlobStorageService blobStorage,
            IAzureReceiptAnalyzerService receiptAnalyzer,
            IOpenAITicketNormalizationService normalizer,
            IAxLogger logger)
        {
            _blobStorage = blobStorage ?? throw new ArgumentNullException(nameof(blobStorage));
            _receiptAnalyzer = receiptAnalyzer ?? throw new ArgumentNullException(nameof(receiptAnalyzer));
            _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
            _logger = logger ?? new FileAxLogger();
            _blobReadSasMinutes = ReadBlobReadSasMinutes();
        }

        public async Task<ExpenseSheetDraftResponse> ExtractFromTicketImageAsync(
            byte[] imageBytes,
            string fileName,
            string contentType,
            CancellationToken cancellationToken,
            ExpenseTicketDraftProfile profile = ExpenseTicketDraftProfile.FullDraft)
        {
            var result = await ProcessFromImageAsync(
                imageBytes,
                fileName,
                contentType,
                profile,
                cancellationToken).ConfigureAwait(false);

            return result?.Draft;
        }

        public async Task<TicketAIProcessingResult> ProcessFromStoredBlobAsync(
            string blobUrl,
            string fileName,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(blobUrl))
                throw new ArgumentException("blobUrl es obligatorio.", nameof(blobUrl));

            var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "ticket" : fileName.Trim();
            var sasValidFor = TimeSpan.FromMinutes(_blobReadSasMinutes);
            var readOnlyUrl = _blobStorage.CreateReadOnlyBlobUrl(blobUrl.Trim(), sasValidFor);
            var totalSw = System.Diagnostics.Stopwatch.StartNew();

            _logger.Log(
                $"[TICKET-AI] Pipeline start source=stored-blob profile={profile} blobUrlLength={blobUrl.Trim().Length} fileName={safeFileName} sasMinutes={_blobReadSasMinutes}",
                AxaptaSessionManager.LogLevel.Info);

            var analysisSw = System.Diagnostics.Stopwatch.StartNew();
            var analysis = await _receiptAnalyzer.AnalyzeReceiptFromBlobUrlAsync(readOnlyUrl, cancellationToken).ConfigureAwait(false);
            analysisSw.Stop();

            var normalizationSw = System.Diagnostics.Stopwatch.StartNew();
            var normalization = await _normalizer.NormalizeReceiptAsync(analysis, safeFileName, profile, cancellationToken).ConfigureAwait(false);
            normalizationSw.Stop();

            totalSw.Stop();
            _logger.Log(
                $"[TICKET-AI] Pipeline completed source=stored-blob profile={profile} totalMs={totalSw.ElapsedMilliseconds} ocrMs={analysisSw.ElapsedMilliseconds} normalizeMs={normalizationSw.ElapsedMilliseconds} itemCount={analysis?.ItemCount ?? 0} taxLineCount={CountTaxPercentLines(normalization?.Draft)} ocrCurrency={ToLogValue(analysis?.CurrencyCode)} normalizedCurrency={ToLogValue(normalization?.Draft?.currencyCode)} ocrJsonChars={ToLogLength(analysis?.RawJson)} normalizedJsonChars={ToLogLength(normalization?.NormalizedJson)} attempts={(normalization?.Attempts ?? 0)}",
                AxaptaSessionManager.LogLevel.Info);

            return new TicketAIProcessingResult
            {
                Draft = normalization?.Draft,
                OcrJson = analysis?.RawJson,
                NormalizedJson = normalization?.NormalizedJson
            };
        }

        public async Task<TicketAIProcessingResult> ProcessFromImageAsync(
            byte[] imageBytes,
            string fileName,
            string contentType,
            ExpenseTicketDraftProfile profile,
            CancellationToken cancellationToken)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                throw new ArgumentException("ticketImage no puede estar vacio.", nameof(imageBytes));

            var safeFileName = string.IsNullOrWhiteSpace(fileName) ? "ticket.jpg" : fileName.Trim();
            TicketBlobUploadResult tempBlob = null;

            try
            {
                using (var content = new MemoryStream(imageBytes, writable: false))
                {
                    tempBlob = _blobStorage.UploadTemporaryTicketFile(safeFileName, content, contentType);
                }

                _logger.Log(
                    $"[TICKET-AI] Temporary blob created source=image profile={profile} blobUrlLength={tempBlob?.BlobUrl?.Length ?? 0} fileName={safeFileName}",
                    AxaptaSessionManager.LogLevel.Info);

                return await ProcessFromStoredBlobAsync(
                    tempBlob.BlobUrl,
                    safeFileName,
                    profile,
                    cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                if (tempBlob != null && !string.IsNullOrWhiteSpace(tempBlob.BlobUrl))
                {
                    var deleted = _blobStorage.DeleteTicketFileByUrl(tempBlob.BlobUrl);
                    _logger.Log(
                        $"[TICKET-AI] Temporary blob cleanup deleted={deleted} fileName={safeFileName}",
                        deleted ? AxaptaSessionManager.LogLevel.Info : AxaptaSessionManager.LogLevel.Warning);
                }
            }
        }

        private static int ReadBlobReadSasMinutes()
        {
            var rawValue = AppSettingsHelper.GetSetting(BlobReadSasMinutesSettingKey);
            return int.TryParse(rawValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) && parsed > 0
                ? parsed
                : DefaultBlobReadSasMinutes;
        }

        private static string ToLogLength(string value)
        {
            return value == null ? "null" : value.Length.ToString(CultureInfo.InvariantCulture);
        }

        private static string ToLogValue(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? "null" : value.Trim();
        }

        private static int CountTaxPercentLines(ExpenseSheetDraftResponse draft)
        {
            return draft?.lines?.Count(line => line?.taxPercent.HasValue == true) ?? 0;
        }
    }
}
