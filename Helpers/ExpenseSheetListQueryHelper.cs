using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Responses;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Shared helper for expense sheet list filters, dates, and row mapping.
    /// </summary>
    public static class ExpenseSheetListQueryHelper
    {
        /// <summary>
        /// Converts API date formats into AX yyyyMMdd.
        /// </summary>
        public static bool TryNormalizeApiDateToAxYmd(string input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            return TryParseTicketOrSheetDateExact(
                input.Trim(),
                new[]
                {
                    "ddMMyyyy",
                    "dd.MM.yyyy",
                    "d.M.yyyy"
                },
                out normalized);
        }

        /// <summary>
        /// Formats an AX or API date into DD.MM.YYYY.
        /// </summary>
        public static string FormatApiDate(string input)
        {
            if (!TryNormalizeAnyDateToAxYmd(input, out var normalizedYmd))
                normalizedYmd = DateTime.Today.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

            if (!DateTime.TryParseExact(normalizedYmd, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return DateTime.Today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);

            return date.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Parses AX decimal text without treating a decimal comma as a thousands separator.
        /// </summary>
        public static decimal? ParseAxDecimalOrNull(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            var normalized = NormalizeAxDecimalValue(value);
            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
                return parsed;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("es-MX"), out parsed))
                return parsed;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("es-ES"), out parsed))
                return parsed;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.GetCultureInfo("en-US"), out parsed))
                return parsed;

            if (decimal.TryParse(normalized, NumberStyles.Any, CultureInfo.CurrentCulture, out parsed))
                return parsed;

            return null;
        }

        /// <summary>
        /// Returns valid numeric AX enum values; available options are exposed by the enum catalog endpoint.
        /// </summary>
        public static int? NormalizeExpenseSheetStatusOrNull(int? expenseSheetStatus)
        {
            if (!expenseSheetStatus.HasValue || expenseSheetStatus.Value < 0)
                return null;

            return expenseSheetStatus.Value;
        }

        /// <summary>
        /// Returns a valid INDReimbursableExpense header value (No, Yes, or Both).
        /// </summary>
        public static int? NormalizeReimbursableExpenseOrNull(int? reimbursableExpense)
        {
            if (!reimbursableExpense.HasValue || reimbursableExpense.Value < 0 || reimbursableExpense.Value > 2)
                return null;

            return reimbursableExpense.Value;
        }

        /// <summary>
        /// Appends expense sheet list filters in the AX order expected by the service.
        /// </summary>
        public static void AppendExpenseSheetListFilters(
            IAxaptaContainer container,
            string createdDateFromYmd,
            string createdDateToYmd,
            string projId,
            string currencyCode,
            int? expenseSheetStatus,
            int? reimbursableExpense,
            bool includeSubordinates)
        {
            if (container == null)
                return;

            const string noOptionalValueToken = "null";

            container.Append(createdDateFromYmd ?? string.Empty);
            container.Append(createdDateToYmd ?? string.Empty);
            container.Append(projId ?? string.Empty);
            container.Append(currencyCode ?? string.Empty);

            if (expenseSheetStatus.HasValue)
                container.Append(expenseSheetStatus.Value);
            else
                container.Append(noOptionalValueToken);

            if (reimbursableExpense.HasValue)
                container.Append(reimbursableExpense.Value);
            else
                container.Append(noOptionalValueToken);

            container.Append(includeSubordinates ? 1 : 0);
        }

        /// <summary>
        /// Maps all rows returned by AX to expense sheet list DTOs.
        /// </summary>
        public static List<ExpenseSheetListItemDto> MapAllExpenseSheetListItems(IAxaptaContainer root, out string message, out int total)
        {
            message = string.Empty;
            total = 0;
            var items = new List<ExpenseSheetListItemDto>();

            if (root == null || AxContainerReadHelper.SafeLength(root) == 0)
                return items;

            if (AxContainerReadHelper.IsSinDatos(root, out message))
                return items;

            total = AxContainerReadHelper.SafeLength(root);
            if (total <= 0)
                return items;

            for (int i = 1; i <= total; i++)
            {
                var row = AxContainerReadHelper.SafePeekContainer(root, i);
                var rowLen = AxContainerReadHelper.SafeLength(row);
                if (row == null || rowLen < 3)
                    continue;

                var item = MapExpenseSheetListItem(row, rowLen);
                ApplyExpenseSheetTotalCompatibility(item);
                items.Add(item);
            }

            return items.Where(item => item != null).ToList();
        }

        private static ExpenseSheetListItemDto MapExpenseSheetListItem(IAxaptaContainer row, int rowLen)
        {
            if (rowLen >= 14)
            {
                var totalAmountMST = rowLen >= 17
                    ? ToDecimal(AxContainerReadHelper.SafeString(row, 17))
                    : null;

                return new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                    EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                    UserId = AxContainerReadHelper.SafeString(row, 5),
                    UserName = AxContainerReadHelper.SafeString(row, 6),
                    Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 12)),
                    ProjId = AxContainerReadHelper.SafeString(row, 11),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 7),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                    TotalAmountCurrency = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                    ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 9)),
                    ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 10)),
                    CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 13)),
                    ReimbursableExpense = ToInt(AxContainerReadHelper.SafeString(row, 14)),
                    OwnerAxUserId = rowLen >= 15 ? AxContainerReadHelper.SafeString(row, 15) : string.Empty,
                    OwnerName = rowLen >= 16 ? AxContainerReadHelper.SafeString(row, 16) : string.Empty,
                    TotalAmountMST = totalAmountMST,
                    AxCreatedDate = rowLen >= 18 ? FormatApiDate(AxContainerReadHelper.SafeString(row, 18)) : null,
                    TotalGrossAmountMST = rowLen >= 19 ? ToDecimal(AxContainerReadHelper.SafeString(row, 19)) : null,
                    TotalReimbursableAmount = rowLen >= 20
                        ? ToDecimal(AxContainerReadHelper.SafeString(row, 20))
                        : totalAmountMST
                };
            }

            if (rowLen >= 13)
            {
                return new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                    EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                    UserId = AxContainerReadHelper.SafeString(row, 5),
                    UserName = AxContainerReadHelper.SafeString(row, 6),
                    Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 12)),
                    ProjId = AxContainerReadHelper.SafeString(row, 11),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 7),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                    ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 9)),
                    ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 10)),
                    CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 13))
                };
            }

            if (rowLen == 12)
            {
                return new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                    EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                    UserId = AxContainerReadHelper.SafeString(row, 5),
                    UserName = null,
                    Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 11)),
                    ProjId = AxContainerReadHelper.SafeString(row, 10),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 6),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                    ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                    ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 9)),
                    CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 12))
                };
            }

            if (rowLen == 11)
            {
                var column11 = AxContainerReadHelper.SafeString(row, 11);
                if (IsLikelyDateValue(column11))
                {
                    return new ExpenseSheetListItemDto
                    {
                        HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                        Description = AxContainerReadHelper.SafeString(row, 2),
                        ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                        EstadoComentarios = null,
                        UserId = AxContainerReadHelper.SafeString(row, 4),
                        UserName = null,
                        Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 10)),
                        ProjId = AxContainerReadHelper.SafeString(row, 9),
                        CurrencyCode = AxContainerReadHelper.SafeString(row, 5),
                        TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                        ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                        ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 8)),
                        CreatedDate = FormatApiDate(column11)
                    };
                }

                return new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                    EstadoComentarios = AxContainerReadHelper.SafeString(row, 4),
                    UserId = AxContainerReadHelper.SafeString(row, 5),
                    UserName = null,
                    Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 11)),
                    ProjId = AxContainerReadHelper.SafeString(row, 10),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 6),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                    ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 8)),
                    ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 9)),
                    CreatedDate = null
                };
            }

            if (rowLen == 10)
            {
                return new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                    EstadoComentarios = null,
                    UserId = AxContainerReadHelper.SafeString(row, 4),
                    UserName = null,
                    Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 10)),
                    ProjId = AxContainerReadHelper.SafeString(row, 9),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 5),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                    ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 7)),
                    ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 8)),
                    CreatedDate = null
                };
            }

            if (rowLen == 9)
            {
                return new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = ToInt(AxContainerReadHelper.SafeString(row, 3)),
                    EstadoComentarios = null,
                    UserId = null,
                    UserName = null,
                    Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 9)),
                    ProjId = AxContainerReadHelper.SafeString(row, 8),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 4),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 5)),
                    ExchRate = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                    ExchangeRateMode = ToInt(AxContainerReadHelper.SafeString(row, 7)),
                    CreatedDate = null
                };
            }

            if (rowLen >= 7)
            {
                return new ExpenseSheetListItemDto
                {
                    HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                    Description = AxContainerReadHelper.SafeString(row, 2),
                    ExpenseSheetStatus = null,
                    EstadoComentarios = null,
                    UserId = null,
                    UserName = null,
                    Voucher = NormalizeVoucher(AxContainerReadHelper.SafeString(row, 3)),
                    ProjId = AxContainerReadHelper.SafeString(row, 4),
                    CurrencyCode = AxContainerReadHelper.SafeString(row, 5),
                    TotalAmount = ToDecimal(AxContainerReadHelper.SafeString(row, 6)),
                    ExchRate = null,
                    ExchangeRateMode = null,
                    CreatedDate = FormatApiDate(AxContainerReadHelper.SafeString(row, 7))
                };
            }

            var amountAndDate = ResolveAmountAndDate(row, rowLen);
            return new ExpenseSheetListItemDto
            {
                HojaGastosId = AxContainerReadHelper.SafeString(row, 1),
                Description = AxContainerReadHelper.SafeString(row, 2),
                ExpenseSheetStatus = null,
                EstadoComentarios = null,
                UserId = null,
                UserName = null,
                Voucher = string.Empty,
                ProjId = AxContainerReadHelper.SafeString(row, 3),
                CurrencyCode = rowLen >= 4 ? AxContainerReadHelper.SafeString(row, 4) : string.Empty,
                TotalAmount = amountAndDate.TotalAmount,
                ExchRate = null,
                ExchangeRateMode = null,
                CreatedDate = FormatApiDate(amountAndDate.CreatedDate)
            };
        }

        //MMS - Preserves the legacy currency alias while AX versions coexist - 2026.07.29
        private static void ApplyExpenseSheetTotalCompatibility(ExpenseSheetListItemDto item)
        {
            if (item == null)
                return;

            if (!item.TotalAmountCurrency.HasValue)
                item.TotalAmountCurrency = item.TotalAmount;
        }

        private static bool TryNormalizeAnyDateToAxYmd(string input, out string normalized)
        {
            normalized = string.Empty;
            if (string.IsNullOrWhiteSpace(input))
                return false;

            var trimmed = input.Trim();
            if (trimmed.All(char.IsDigit))
            {
                if (trimmed.Length != 8)
                    return false;

                return TryParseTicketOrSheetDateExact(
                    trimmed,
                    new[]
                    {
                        "yyyyMMdd",
                        "ddMMyyyy"
                    },
                    out normalized);
            }

            return TryParseTicketOrSheetDateExact(
                trimmed,
                new[]
                {
                    "dd.MM.yyyy",
                    "d.M.yyyy",
                    "yyyy-MM-dd",
                    "dd/MM/yyyy"
                },
                out normalized);
        }

        private static bool TryParseTicketOrSheetDateExact(string input, string[] acceptedFormats, out string normalized)
        {
            normalized = string.Empty;
            if (!DateTime.TryParseExact(input, acceptedFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return false;

            if (!IsReasonableTicketOrSheetDate(date))
                return false;

            normalized = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return true;
        }

        private static bool IsReasonableTicketOrSheetDate(DateTime date)
        {
            var minDate = new DateTime(1900, 1, 1);
            var maxDate = DateTime.Today.AddYears(1);
            return date >= minDate && date <= maxDate;
        }

        private static bool IsLikelyDateValue(string value)
        {
            return TryNormalizeAnyDateToAxYmd(value, out _);
        }

        private static (decimal? TotalAmount, string CreatedDate) ResolveAmountAndDate(IAxaptaContainer row, int rowLen)
        {
            var value5 = rowLen >= 5 ? AxContainerReadHelper.SafeString(row, 5) : string.Empty;
            var value6 = rowLen >= 6 ? AxContainerReadHelper.SafeString(row, 6) : string.Empty;

            if (rowLen >= 6)
            {
                var value5IsDate = IsLikelyDateValue(value5);
                var value6IsDate = IsLikelyDateValue(value6);

                if (value5IsDate && !value6IsDate)
                    return (ToDecimal(value6), value5);

                if (!value5IsDate && value6IsDate)
                    return (ToDecimal(value5), value6);
            }

            return (ToDecimal(value5), value6);
        }

        private static decimal? ToDecimal(string value)
        {
            return ParseAxDecimalOrNull(value);
        }

        private static string NormalizeAxDecimalValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var raw = value.Trim()
                .Replace("\u00A0", string.Empty)
                .Replace(" ", string.Empty);

            var hasComma = raw.Contains(",");
            var hasDot = raw.Contains(".");

            if (hasComma && hasDot)
            {
                var lastComma = raw.LastIndexOf(',');
                var lastDot = raw.LastIndexOf('.');
                var decimalSeparator = lastComma > lastDot ? ',' : '.';
                var thousandSeparator = decimalSeparator == ',' ? "." : ",";
                var withoutThousands = raw.Replace(thousandSeparator, string.Empty);

                return decimalSeparator == ','
                    ? withoutThousands.Replace(',', '.')
                    : withoutThousands;
            }

            if (hasComma)
            {
                var commaCount = raw.Count(c => c == ',');
                var lastComma = raw.LastIndexOf(',');
                var digitsAfter = lastComma >= 0 ? raw.Length - lastComma - 1 : 0;

                if (digitsAfter > 0 && digitsAfter <= 2)
                {
                    var whole = raw.Substring(0, lastComma).Replace(",", string.Empty);
                    var fraction = raw.Substring(lastComma + 1);
                    return string.Concat(whole, ".", fraction);
                }

                if (commaCount >= 1)
                    return raw.Replace(",", string.Empty);
            }

            if (hasDot)
            {
                var dotCount = raw.Count(c => c == '.');
                if (dotCount > 1)
                {
                    var lastDot = raw.LastIndexOf('.');
                    var whole = raw.Substring(0, lastDot).Replace(".", string.Empty);
                    var fraction = raw.Substring(lastDot + 1);
                    return string.Concat(whole, ".", fraction);
                }
            }

            return raw;
        }

        private static int? ToInt(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return null;

            return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : (int?)null;
        }

        private static string NormalizeVoucher(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? string.Empty : value.Trim();
        }
    }
}
