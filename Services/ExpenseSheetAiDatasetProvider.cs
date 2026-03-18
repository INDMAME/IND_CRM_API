using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Loads expense sheet list rows and converts them to compact AI records.
    /// </summary>
    public sealed class ExpenseSheetAiDatasetProvider : IExpenseSheetAiDatasetProvider
    {
        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IAxLogger _logger;

        public ExpenseSheetAiDatasetProvider(IAxaptaSessionManager sessionManager, IAxLogger logger)
        {
            _sessionManager = sessionManager;
            _logger = logger ?? new FileAxLogger();
        }

        public string SourceKey => "crm.expensesheets.list";

        public AiDatasetLoadResult Load(string username, string company, string axUserId, ExpenseSheetAiQueryOptions options)
        {
            var effectiveOptions = options ?? new ExpenseSheetAiQueryOptions();
            var ax = _sessionManager.GetAxInstanceForUser(username);
            var con = ax.CreateContainer();
            con.Append(company);
            con.Append(axUserId);
            con.Append(effectiveOptions.Filter ?? string.Empty);
            con.Append(effectiveOptions.BilledMode);
            ExpenseSheetListQueryHelper.AppendExpenseSheetListFilters(
                con,
                effectiveOptions.CreatedDateFromYmd,
                effectiveOptions.CreatedDateToYmd,
                effectiveOptions.ProjId,
                effectiveOptions.CurrencyCode,
                effectiveOptions.ExpenseSheetStatus,
                effectiveOptions.IncludeSubordinates);

            _logger.Log(
                "[AI-EXPENSESHEETS] Loading filtered expense sheets sourceKey=" + SourceKey +
                " billedMode=" + effectiveOptions.BilledMode.ToString(CultureInfo.InvariantCulture) +
                " includeSubordinates=" + effectiveOptions.IncludeSubordinates,
                AxaptaSessionManager.LogLevel.Info);

            var resultObj = ax.CallStaticClassMethod(
                "INDCRMExpenseSheetService",
                "getExpenseSheetsList",
                con);

            var items = ExpenseSheetListQueryHelper.MapAllExpenseSheetListItems(resultObj as AxaptaCOMConnector.IAxaptaContainer, out var message, out var total);
            var warnings = new List<string>();
            if (!string.IsNullOrWhiteSpace(message) &&
                !string.Equals(message, "OK", System.StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(message.Trim());
            }

            return new AiDatasetLoadResult
            {
                SourceKey = SourceKey,
                TotalRecords = total,
                Records = items.Select(ToAiRecord).ToList(),
                Warnings = warnings
            };
        }

        private static AiDatasetRecord ToAiRecord(ExpenseSheetListItemDto item)
        {
            var payload = new
            {
                hojaGastosId = item.HojaGastosId,
                description = item.Description,
                expenseSheetStatus = item.ExpenseSheetStatus,
                estadoComentarios = item.EstadoComentarios,
                userId = item.UserId,
                userName = item.UserName,
                voucher = item.Voucher,
                projId = item.ProjId,
                currencyCode = item.CurrencyCode,
                totalAmount = item.TotalAmount,
                exchRate = item.ExchRate,
                exchangeRateMode = item.ExchangeRateMode,
                createdDate = item.CreatedDate
            };

            return new AiDatasetRecord
            {
                RecordId = string.IsNullOrWhiteSpace(item.HojaGastosId) ? string.Empty : item.HojaGastosId.Trim(),
                JsonPayload = JsonConvert.SerializeObject(
                    payload,
                    new JsonSerializerSettings
                    {
                        NullValueHandling = NullValueHandling.Ignore,
                        Formatting = Formatting.None
                    })
            };
        }
    }
}
