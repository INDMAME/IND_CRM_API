using System;
using System.Collections.Generic;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Models.Responses;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Structural checks of the web store messages. They only prevent a technical failure while
    /// building the AX container, a broken idempotency key or a silent truncation in AX: envelope
    /// present, identifiers and short texts within the AX field size and on one line, version >= 1,
    /// RecIds in the 32-bit range, dates in YYYY-MM-DD and a coherent attendee list. No business
    /// rule is checked here; the web sends final values.
    /// </summary>
    public static class CrmWebStoreMessageValidator
    {
        /// <summary>Size of the AX identifier fields (ExternalId and LastMessageId of CRMWebSyncMap).</summary>
        public const int IdentifierLength = 36;

        /// <summary>AX user id fields (INDCreatedByUserId), str 5 (CRM_INSERTEC docs/RECEPTOR_CRMWEB.md, section 5).</summary>
        public const int AxUserIdLength = 5;

        /// <summary>Account, country, CRM user and group codes, str 15.</summary>
        public const int AxCode15Length = 15;

        /// <summary>CustAccount and phone fields, str 20.</summary>
        public const int AxCode20Length = 20;

        /// <summary>Names, descriptions, positions, e-mail and VAT number, str 80.</summary>
        public const int AxText80Length = 80;

        /// <summary>Checks the envelope shared by every message (positions 1 to 5).</summary>
        public static List<IndValidationError> ValidateEnvelope(string headerCompany, WebStoreMessageRequest message)
        {
            var errors = new List<IndValidationError>();
            if (string.IsNullOrWhiteSpace(headerCompany))
                Add(errors, "company", "Falta la cabecera X-IND-Company.");

            if (message == null)
            {
                Add(errors, "body", "Falta el cuerpo del mensaje.");
                return errors;
            }

            if (!string.IsNullOrWhiteSpace(message.company) && !string.IsNullOrWhiteSpace(headerCompany) &&
                !string.Equals(message.company.Trim(), headerCompany.Trim(), StringComparison.OrdinalIgnoreCase))
                Add(errors, "company", "company no coincide con X-IND-Company.");

            Identifier(errors, "messageId", message.messageId);
            Identifier(errors, "externalId", message.externalId);

            if (!message.version.HasValue || message.version.Value < 1)
                Add(errors, "version", "version debe ser un entero mayor o igual que 1.");

            RecId(errors, "axRecId", message.axRecId);
            return errors;
        }

        /// <summary>Checks visits/upsert: envelope, activity present, dates, RecIds and attendees.</summary>
        public static List<IndValidationError> ValidateVisitUpsert(string headerCompany, WebStoreVisitUpsertRequest message)
        {
            var errors = ValidateEnvelope(headerCompany, message);
            if (message == null)
                return errors;

            if (message.activity == null)
            {
                Add(errors, "activity", "Falta activity.");
            }
            else
            {
                var a = message.activity;
                RecId(errors, "activity.refRecId", a.refRecId);
                Date(errors, "activity.transDate", a.transDate);
                Date(errors, "activity.realDate", a.realDate);
                ShortText(errors, "activity.accountNum", a.accountNum, AxCode15Length);
                ShortText(errors, "activity.custAccount", a.custAccount, AxCode20Length);
                ShortText(errors, "activity.name", a.name, AxText80Length);
                ShortText(errors, "activity.country", a.country, AxCode15Length);
                ShortText(errors, "activity.userId", a.userId, AxCode15Length);
                ShortText(errors, "activity.createdByUserId", a.createdByUserId, AxUserIdLength);
                ShortText(errors, "activity.description", a.description, AxText80Length);
            }

            ValidateAttendees(errors, message.attendees);
            return errors;
        }

        /// <summary>Checks visits/delete: only the envelope and axRecId.</summary>
        public static List<IndValidationError> ValidateVisitDelete(string headerCompany, WebStoreVisitDeleteRequest message)
        {
            return ValidateEnvelope(headerCompany, message);
        }

        /// <summary>Checks contacts/upsert: envelope and contact present.</summary>
        public static List<IndValidationError> ValidateContactUpsert(string headerCompany, WebStoreContactUpsertRequest message)
        {
            var errors = ValidateEnvelope(headerCompany, message);
            if (message == null)
                return errors;

            var c = message.contact;
            if (c == null)
            {
                Add(errors, "contact", "Falta contact.");
                return errors;
            }

            ShortText(errors, "contact.accountNum", c.accountNum, AxCode15Length);
            ShortText(errors, "contact.name", c.name, AxText80Length);
            ShortText(errors, "contact.position", c.position, AxText80Length);
            ShortText(errors, "contact.email", c.email, AxText80Length);
            ShortText(errors, "contact.phone", c.phone, AxCode20Length);
            ShortText(errors, "contact.company", c.company, AxText80Length);
            ShortText(errors, "contact.companyPhone", c.companyPhone, AxCode20Length);
            return errors;
        }

        /// <summary>Checks prospects/upsert: envelope and prospect present.</summary>
        public static List<IndValidationError> ValidateProspectUpsert(string headerCompany, WebStoreProspectUpsertRequest message)
        {
            var errors = ValidateEnvelope(headerCompany, message);
            if (message == null)
                return errors;

            var p = message.prospect;
            if (p == null)
            {
                Add(errors, "prospect", "Falta prospect.");
                return errors;
            }

            ShortText(errors, "prospect.name", p.name, AxText80Length);
            ShortText(errors, "prospect.legalName", p.legalName, AxText80Length);
            ShortText(errors, "prospect.city", p.city, AxText80Length);
            ShortText(errors, "prospect.countryId", p.countryId, AxCode15Length);
            ShortText(errors, "prospect.statisticsGroup", p.statisticsGroup, AxCode15Length);
            ShortText(errors, "prospect.vatNum", p.vatNum, AxText80Length);
            ShortText(errors, "prospect.userId", p.userId, AxCode15Length);
            return errors;
        }

        // The list must exist (an absent list would delete every attendee in AX), without null rows,
        // with unique external ids and unique non-zero AX RecIds.
        private static void ValidateAttendees(List<IndValidationError> errors, List<WebStoreAttendeeDto> attendees)
        {
            if (attendees == null)
            {
                Add(errors, "attendees", "Falta attendees (lista vacia si la visita no tiene asistentes).");
                return;
            }

            var externalIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var recIds = new HashSet<long>();
            for (var i = 0; i < attendees.Count; i++)
            {
                var field = "attendees[" + i + "]";
                var attendee = attendees[i];
                if (attendee == null)
                {
                    Add(errors, field, "Asistente vacio.");
                    continue;
                }

                if (Identifier(errors, field + ".externalId", attendee.externalId) &&
                    !externalIds.Add(attendee.externalId.Trim()))
                    Add(errors, field + ".externalId", "externalId repetido en la lista de asistentes.");

                if (RecId(errors, field + ".axRecId", attendee.axRecId) &&
                    attendee.axRecId.HasValue && attendee.axRecId.Value != 0 && !recIds.Add(attendee.axRecId.Value))
                    Add(errors, field + ".axRecId", "axRecId repetido en la lista de asistentes.");

                RecId(errors, field + ".contactRecId", attendee.contactRecId);
                ShortText(errors, field + ".name", attendee.name, AxText80Length);
                ShortText(errors, field + ".position", attendee.position, AxText80Length);
                ShortText(errors, field + ".createdByUserId", attendee.createdByUserId, AxUserIdLength);
            }
        }

        // Optional short text: at most the AX field size and without line breaks (AX would truncate it).
        private static void ShortText(List<IndValidationError> errors, string field, string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value))
                return;

            if (value.Length > maxLength || value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0)
                Add(errors, field, field + " admite como maximo " + maxLength + " caracteres en una linea.");
        }

        // Required identifier within the AX field size and on one line; true when valid.
        private static bool Identifier(List<IndValidationError> errors, string field, string value)
        {
            var text = (value ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                Add(errors, field, field + " es obligatorio.");
                return false;
            }

            if (text.Length > IdentifierLength || text.IndexOf('\n') >= 0 || text.IndexOf('\r') >= 0)
            {
                Add(errors, field, field + " admite como maximo " + IdentifierLength + " caracteres en una linea.");
                return false;
            }

            return true;
        }

        // Optional RecId in the AX 3.0 32-bit range; true when valid.
        private static bool RecId(List<IndValidationError> errors, string field, long? value)
        {
            if (CrmWebStoreContainerBuilder.IsRecIdInRange(value))
                return true;

            Add(errors, field, field + " no cabe en un RecId de Axapta (entero de 32 bits).");
            return false;
        }

        // Optional date that must be YYYY-MM-DD when present.
        private static void Date(List<IndValidationError> errors, string field, string value)
        {
            if (!CrmWebStoreContainerBuilder.TryAxDate(value, out _))
                Add(errors, field, field + " debe tener formato YYYY-MM-DD.");
        }

        private static void Add(List<IndValidationError> errors, string field, string message)
        {
            errors.Add(new IndValidationError { Field = field, Message = message });
        }
    }
}
