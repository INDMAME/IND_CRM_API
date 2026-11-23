using System;
using System.Collections.Generic;
using System.Globalization;
using IND_CRM_API.Contracts.Requests;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Builds the positional values of the CRMWeb* input containers (CRM_INSERTEC
    /// docs/RECEPTOR_CRMWEB.md, section 5). Pure code without COM: the service copies the values,
    /// in this order, into an AX container. Only strings and 32-bit integers are produced.
    /// Each Add line is one container position; keep the position comments in sync.
    /// </summary>
    public static class CrmWebStoreContainerBuilder
    {
        /// <summary>Last fixed position of visits/upsert (attendee count).</summary>
        public const int VisitAttendeeCountPosition = 28;

        /// <summary>First position of the attendee block of visits/upsert.</summary>
        public const int VisitFirstAttendeePosition = 29;

        /// <summary>Values per attendee in visits/upsert.</summary>
        public const int AttendeeValueCount = 7;

        /// <summary>Input of CRMWebStoreCommon::ping: [company].</summary>
        public static List<object> BuildPing(string company)
        {
            return new List<object> { Id(company) };
        }

        /// <summary>Input of CRMWebVisitStore::upsert: positions 1 to 28 and seven values per attendee.</summary>
        public static List<object> BuildVisitUpsert(string company, WebStoreVisitUpsertRequest m)
        {
            if (m?.activity == null || m.attendees == null)
                throw new ArgumentException("The visit message needs activity and attendees.", nameof(m));

            var a = m.activity;
            var values = Envelope(company, m);                // 1-4
            values.Add(RecId(m.axRecId));                       // 5 axRecId
            values.Add(Text(a.accountNum));                     // 6 activity.accountNum
            values.Add(Int(a.origin));                          // 7 activity.origin
            values.Add(Text(a.custAccount));                    // 8 activity.custAccount
            values.Add(Int(a.originCustAccount));               // 9 activity.originCustAccount
            values.Add(RecId(a.refRecId));                      // 10 activity.refRecId
            values.Add(Text(a.name));                           // 11 activity.name
            values.Add(Text(a.country));                        // 12 activity.country
            values.Add(Text(a.userId));                         // 13 activity.userId
            values.Add(Text(a.createdByUserId));                // 14 activity.createdByUserId
            values.Add(Int(a.division));                        // 15 activity.division
            values.Add(AxDate(a.transDate));                    // 16 activity.transDate
            values.Add(AxDate(a.realDate));                     // 17 activity.realDate
            values.Add(Int(a.activityType));                    // 18 activity.activityType
            values.Add(Int(a.status));                          // 19 activity.status
            values.Add(Int(a.priority));                        // 20 activity.priority
            values.Add(Int(a.visitType));                       // 21 activity.visitType
            values.Add(Int(a.contactMethod));                   // 22 activity.contactMethod
            values.Add(Int(a.rating));                          // 23 activity.rating
            values.Add(Text(a.description));                    // 24 activity.description
            values.Add(Memo(a.comments));                       // 25 activity.comments
            values.Add(Memo(a.background));                     // 26 activity.background
            values.Add(Memo(a.conclusions));                    // 27 activity.conclusions
            values.Add(m.attendees.Count);                      // 28 attendees.length

            foreach (var t in m.attendees)                      // 29... seven values per attendee
            {
                values.Add(Id(t.externalId));                   // +0 attendee.externalId
                values.Add(RecId(t.axRecId));                   // +1 attendee.axRecId
                values.Add(Int(t.type));                        // +2 attendee.type
                values.Add(Text(t.name));                       // +3 attendee.name
                values.Add(Text(t.position));                   // +4 attendee.position
                values.Add(RecId(t.contactRecId));              // +5 attendee.contactRecId
                values.Add(Text(t.createdByUserId));            // +6 attendee.createdByUserId
            }

            return values;
        }

        /// <summary>Input of CRMWebVisitStore::remove: positions 1 to 5.</summary>
        public static List<object> BuildVisitDelete(string company, WebStoreVisitDeleteRequest m)
        {
            if (m == null)
                throw new ArgumentNullException(nameof(m));

            var values = Envelope(company, m);                // 1-4
            values.Add(RecId(m.axRecId));                       // 5 axRecId
            return values;
        }

        /// <summary>Input of CRMWebContactStore::upsert: positions 1 to 13.</summary>
        public static List<object> BuildContactUpsert(string company, WebStoreContactUpsertRequest m)
        {
            if (m?.contact == null)
                throw new ArgumentException("The contact message needs contact.", nameof(m));

            var c = m.contact;
            var values = Envelope(company, m);                // 1-4
            values.Add(RecId(m.axRecId));                       // 5 axRecId
            values.Add(Text(c.accountNum));                     // 6 contact.accountNum
            values.Add(Int(c.origin));                          // 7 contact.origin
            values.Add(Text(c.name));                           // 8 contact.name
            values.Add(Text(c.position));                       // 9 contact.position
            values.Add(Text(c.email));                          // 10 contact.email
            values.Add(Text(c.phone));                          // 11 contact.phone
            values.Add(Text(c.company));                        // 12 contact.company
            values.Add(Text(c.companyPhone));                   // 13 contact.companyPhone
            return values;
        }

        /// <summary>Input of CRMWebProspectStore::upsert: positions 1 to 13.</summary>
        public static List<object> BuildProspectUpsert(string company, WebStoreProspectUpsertRequest m)
        {
            if (m?.prospect == null)
                throw new ArgumentException("The prospect message needs prospect.", nameof(m));

            var p = m.prospect;
            var values = Envelope(company, m);                // 1-4
            values.Add(RecId(m.axRecId));                       // 5 axRecId
            values.Add(Text(p.name));                           // 6 prospect.name
            values.Add(Text(p.legalName));                      // 7 prospect.legalName
            values.Add(Text(p.city));                           // 8 prospect.city
            values.Add(Text(p.countryId));                      // 9 prospect.countryId
            values.Add(Text(p.statisticsGroup));                // 10 prospect.statisticsGroup
            values.Add(Int(p.contactOrigin));                   // 11 prospect.contactOrigin
            values.Add(Text(p.vatNum));                         // 12 prospect.vatNum
            values.Add(Text(p.userId));                         // 13 prospect.userId
            return values;
        }

        /// <summary>
        /// Converts YYYY-MM-DD into the AX container format yyyymmdd; empty stays empty.
        /// Returns false for any other format.
        /// </summary>
        public static bool TryAxDate(string isoDate, out string axDate)
        {
            axDate = string.Empty;
            if (string.IsNullOrWhiteSpace(isoDate))
                return true;

            if (!DateTime.TryParseExact(isoDate.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                return false;

            axDate = date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
            return true;
        }

        /// <summary>True when the value fits an AX 3.0 RecId (32-bit signed integer); null is allowed.</summary>
        public static bool IsRecIdInRange(long? value)
        {
            return !value.HasValue || (value.Value >= int.MinValue && value.Value <= int.MaxValue);
        }

        // Envelope positions 1 to 4: company, messageId, externalId, version.
        private static List<object> Envelope(string company, WebStoreMessageRequest m)
        {
            return new List<object>
            {
                Id(company),                                    // 1 company
                Id(m.messageId),                                // 2 messageId
                Id(m.externalId),                               // 3 externalId
                Int(m.version)                                  // 4 version
            };
        }

        // Identifiers travel trimmed; they are keys of the AX map.
        private static string Id(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        // Short texts travel as sent; null becomes empty.
        private static string Text(string value)
        {
            return value ?? string.Empty;
        }

        // Memo fields keep their content with CRLF line breaks, as AX stores them.
        private static string Memo(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return value.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
        }

        // Enum and integer values: null becomes 0.
        private static int Int(int? value)
        {
            return value ?? 0;
        }

        // RecId: null becomes 0; out-of-range values are rejected earlier by the validator.
        private static int RecId(long? value)
        {
            if (!IsRecIdInRange(value))
                throw new ArgumentOutOfRangeException(nameof(value), "RecId out of the 32-bit range.");

            return value.HasValue ? (int)value.Value : 0;
        }

        // Dates: YYYY-MM-DD to yyyymmdd; the validator rejects other formats earlier.
        private static string AxDate(string isoDate)
        {
            if (!TryAxDate(isoDate, out var axDate))
                throw new ArgumentException("Date is not YYYY-MM-DD.", nameof(isoDate));

            return axDate;
        }
    }
}
