using System;
using System.Collections.Generic;
using System.Globalization;
using IND_CRM_API.Contracts.Responses;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Reads the CRMWeb* result containers (CRM_INSERTEC docs/RECEPTOR_CRMWEB.md, section 5.5) after
    /// they were copied to plain object arrays (nested containers become nested object[]). Pure code
    /// without COM, so the layout can be tested. A container that does not follow the layout raises
    /// CrmWebStoreResultException.
    /// </summary>
    public static class CrmWebStoreResultReader
    {
        /// <summary>Header positions: [result, recId, code, modifiedDate, modifiedTime, errorCode, errorMessage, company, accountRecId].</summary>
        public const int ResultPosition = 1;
        public const int RecIdPosition = 2;
        public const int CodePosition = 3;
        public const int ModifiedDatePosition = 4;
        public const int ModifiedTimePosition = 5;
        public const int ErrorCodePosition = 6;
        public const int ErrorMessagePosition = 7;
        public const int CompanyPosition = 8;
        public const int AccountRecIdPosition = 9;

        private static readonly string[] KnownResults = { "applied", "duplicate", "rejected" };
        private static readonly string[] AxDateFormats = { "yyyyMMdd", "yyyy-MM-dd", "dd/MM/yyyy", "dd.MM.yyyy" };
        private const string MadridTimeZoneId = "Romance Standard Time";

        /// <summary>
        /// Converts [[header], [[externalId, recId], ...]] into the JSON result of section 5.5.
        /// </summary>
        public static WebStoreResultDto ReadResult(object[] root, string messageId, out string axCompany)
        {
            var header = ChildAt(root, 1);
            if (header == null)
                throw new CrmWebStoreResultException("The AX result has no header container.");

            var result = Text(header, ResultPosition).Trim().ToLowerInvariant();
            if (Array.IndexOf(KnownResults, result) < 0)
                throw new CrmWebStoreResultException("Unknown AX result '" + result + "'.");

            axCompany = NullIfEmpty(Text(header, CompanyPosition));
            var dto = new WebStoreResultDto
            {
                Result = result,
                RecId = RecIdOrNull(At(header, RecIdPosition)),
                Code = NullIfEmpty(Text(header, CodePosition)),
                ModifiedAt = FormatModifiedAt(At(header, ModifiedDatePosition), At(header, ModifiedTimePosition)),
                ErrorCode = NullIfEmpty(Text(header, ErrorCodePosition)),
                ErrorMessage = NullIfEmpty(Text(header, ErrorMessagePosition)),
                AccountRecId = RecIdOrNull(At(header, AccountRecIdPosition)),
                Attendees = ReadAttendees(At(root, 2)),
                MessageId = messageId
            };

            return dto;
        }

        /// <summary>
        /// Reads the ping answer: [company, companyName, axUser], flat or wrapped in one more container.
        /// </summary>
        public static WebStorePingDto ReadPing(object[] root)
        {
            var row = ChildAt(root, 1) ?? root;
            if (row == null || row.Length < 1)
                throw new CrmWebStoreResultException("The AX ping answer is empty.");

            return new WebStorePingDto
            {
                Company = Text(row, 1),
                CompanyName = Text(row, 2),
                AxUser = Text(row, 3)
            };
        }

        /// <summary>
        /// Formats the AX modified date (date or yyyymmdd text) and time (seconds since midnight)
        /// as ISO 8601 with the Europe/Madrid offset of that moment; null when the date is empty.
        /// </summary>
        public static string FormatModifiedAt(object axDate, object axSeconds)
        {
            DateTime date;
            if (axDate is DateTime dateValue)
            {
                date = dateValue.Date;
            }
            else
            {
                var text = Convert.ToString(axDate, CultureInfo.InvariantCulture)?.Trim();
                if (string.IsNullOrEmpty(text) ||
                    !DateTime.TryParseExact(text, AxDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
                    return null;
            }

            // AX uses 1900-01-01 as the empty date.
            if (date.Year <= 1900)
                return null;

            var seconds = IntOrZero(axSeconds);
            if (seconds < 0 || seconds >= 86400)
                seconds = 0;

            var local = DateTime.SpecifyKind(date.AddSeconds(seconds), DateTimeKind.Unspecified);
            var offset = ResolveMadridOffset(local);
            return offset.HasValue
                ? new DateTimeOffset(local, offset.Value).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture)
                : local.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
        }

        // Second element: [[externalId, recId], ...]; absent or empty means no attendees.
        private static List<WebStoreAttendeeResultDto> ReadAttendees(object value)
        {
            var list = new List<WebStoreAttendeeResultDto>();
            if (value == null || (value is string text && text.Length == 0))
                return list;

            var rows = value as object[];
            if (rows == null)
                throw new CrmWebStoreResultException("The attendee list of the AX result is not a container.");

            for (var i = 1; i <= rows.Length; i++)
            {
                var row = ChildAt(rows, i);
                if (row == null)
                    throw new CrmWebStoreResultException("Attendee row " + i + " of the AX result is not a container.");

                list.Add(new WebStoreAttendeeResultDto
                {
                    ExternalId = Text(row, 1),
                    RecId = RecIdOrNull(At(row, 2))
                });
            }

            return list;
        }

        // 1-based read; positions beyond the end are absent (null).
        private static object At(object[] container, int position)
        {
            if (container == null || position < 1 || position > container.Length)
                return null;

            return container[position - 1];
        }

        // 1-based read of a nested container.
        private static object[] ChildAt(object[] container, int position)
        {
            return At(container, position) as object[];
        }

        // Scalar as invariant text; containers are not text.
        private static string Text(object[] container, int position)
        {
            var value = At(container, position);
            if (value == null || value is object[])
                return string.Empty;

            return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        // AX RecId as a 32-bit integer; 0, empty or not numeric means none.
        private static int? RecIdOrNull(object value)
        {
            var number = IntOrZero(value);
            return number == 0 ? (int?)null : number;
        }

        // Integer conversion that accepts COM numeric types and numeric text.
        private static int IntOrZero(object value)
        {
            if (value == null || value is object[])
                return 0;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                return int.TryParse(Convert.ToString(value, CultureInfo.InvariantCulture), NumberStyles.Integer,
                    CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;
            }
        }

        // Offset of Europe/Madrid for that wall-clock time; null when the zone is not installed.
        private static TimeSpan? ResolveMadridOffset(DateTime local)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(MadridTimeZoneId).GetUtcOffset(local);
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Empty text becomes null in the JSON result.
        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>
    /// The AX result container does not follow the documented layout.
    /// </summary>
    public sealed class CrmWebStoreResultException : Exception
    {
        /// <summary>Creates the exception with a technical message (never shown with secrets).</summary>
        public CrmWebStoreResultException(string message) : base(message)
        {
        }
    }
}
