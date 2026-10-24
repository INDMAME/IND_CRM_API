using System;
using System.Globalization;

namespace IND_CRM_API.Helpers
{
    /// <summary>
    /// Converts between the AX 3.0 representation of a timestamp (date as yyyyMMdd plus
    /// seconds since midnight, server wall clock in Europe/Madrid) and ISO 8601 UTC strings.
    /// The CRM_INSERTEC masters agent applies the same wall-clock rule, so base versions match.
    /// </summary>
    public static class AxSyncDateTime
    {
        private const string WindowsZoneId = "Romance Standard Time";
        private static readonly TimeZoneInfo AxZone = ResolveZone();

        private static TimeZoneInfo ResolveZone()
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(WindowsZoneId);
            }
            catch
            {
                return TimeZoneInfo.Local;
            }
        }

        /// <summary>Parses an AX date string (yyyyMMdd). Empty, "0" or invalid values return false.</summary>
        public static bool TryParseAxDate(string axDate, out DateTime date)
        {
            date = DateTime.MinValue;
            var normalized = (axDate ?? string.Empty).Trim();
            if (normalized.Length != 8 || normalized == "00000000")
                return false;

            return DateTime.TryParseExact(normalized, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date);
        }

        /// <summary>Builds the ISO 8601 UTC string of an AX date plus seconds; null when the date is empty.</summary>
        public static string ToIso(string axDate, int seconds)
        {
            if (!TryParseAxDate(axDate, out var date))
                return null;

            var clamped = seconds < 0 || seconds >= 86400 ? 0 : seconds;
            var wallClock = DateTime.SpecifyKind(date.AddSeconds(clamped), DateTimeKind.Unspecified);
            if (AxZone.IsInvalidTime(wallClock))
                wallClock = wallClock.AddHours(1);

            var utc = TimeZoneInfo.ConvertTimeToUtc(wallClock, AxZone);
            return utc.ToString("yyyy-MM-dd'T'HH:mm:ss.fff'Z'", CultureInfo.InvariantCulture);
        }

        /// <summary>Splits an ISO 8601 instant into the AX date (yyyyMMdd) and seconds since midnight.</summary>
        public static bool TryFromIso(string iso, out string axDate, out int seconds)
        {
            axDate = string.Empty;
            seconds = 0;
            if (string.IsNullOrWhiteSpace(iso))
                return false;

            if (!DateTimeOffset.TryParse(iso.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var instant))
                return false;

            var wallClock = TimeZoneInfo.ConvertTimeFromUtc(instant.UtcDateTime, AxZone);
            axDate = wallClock.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            seconds = (int)Math.Floor(wallClock.TimeOfDay.TotalSeconds);
            return true;
        }

        /// <summary>Converts a web date (yyyy-MM-dd) to the AX date string (yyyyMMdd).</summary>
        public static bool TryIsoDateToAx(string isoDate, out string axDate)
        {
            axDate = string.Empty;
            if (string.IsNullOrWhiteSpace(isoDate))
                return false;

            if (!DateTime.TryParseExact(isoDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return false;

            axDate = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>Converts an AX date string (yyyyMMdd) to yyyy-MM-dd; null when empty.</summary>
        public static string AxDateToIsoDate(string axDate)
        {
            return TryParseAxDate(axDate, out var date)
                ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : null;
        }
    }
}
