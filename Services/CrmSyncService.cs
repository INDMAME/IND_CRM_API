using System;
using System.Collections.Generic;
using System.Globalization;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Helpers;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Calls the X++ receiver INDCrmSyncService through the per-request COM session and
    /// translates its positional containers into the sync DTOs. Container layouts are the
    /// contract documented in .codex/ENDPOINTS.md (Sincronizacion CRM_INSERTEC). Only
    /// primitive values travel in the input container; nested rows are read from the output.
    /// </summary>
    public sealed class CrmSyncService : ICrmSyncService
    {
        private const string AxClassName = "INDCrmSyncService";
        private const int AttendeeFieldCount = 6;

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IAxLogger _logger;

        public CrmSyncService(IAxaptaSessionManager sessionManager, IAxLogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public SyncPingDto Ping(string username, string company)
        {
            var root = Call(username, "ping", con => con.Append(company));
            var row = RowAt(root, 1);
            return new SyncPingDto
            {
                User = username,
                Company = Str(row, 1),
                CompanyName = Str(row, 2),
                AxUser = Str(row, 3)
            };
        }

        public SyncResultDto UpsertVisit(string username, string company, SyncVisitUpsertRequest message)
        {
            var root = Call(username, "upsertVisit", con =>
            {
                AppendEnvelope(con, company, message);              // 1..4
                con.Append(RecIdOrZero(message.axRecId));           // 5
                AppendBaseVersion(con, message.baseVersion);        // 6..8
                con.Append(Text(message.account?.origin));          // 9
                con.Append(Text(message.account?.accountNum));      // 10
                con.Append(AxDate(message.visit?.transDate));       // 11
                con.Append(message.visit?.visitType ?? 0);          // 12
                con.Append(message.visit?.contactMethod ?? 0);      // 13
                con.Append(message.visit?.rating ?? 0);             // 14
                con.Append(Text(message.visit?.description));       // 15
                con.Append(Memo(message.visit?.comments));          // 16
                con.Append(Memo(message.visit?.background));        // 17
                con.Append(Memo(message.visit?.conclusions));       // 18
                con.Append(Text(message.visit?.ownerAxUserId));     // 19
                con.Append(Text(message.visit?.ownerCrmUserId));    // 20
                con.Append(Text(message.webCode));                  // 21
                AppendAttendees(con, message.attendees);            // 22 count, then 6 values per attendee
            });
            return ReadResult(root, message.messageId);
        }

        public SyncResultDto DeleteVisit(string username, string company, SyncVisitDeleteRequest message)
        {
            var root = Call(username, "deleteVisit", con =>
            {
                AppendEnvelope(con, company, message);              // 1..4
                con.Append(RecIdOrZero(message.axRecId));           // 5
                AppendBaseVersion(con, message.baseVersion);        // 6..8
                con.Append(Text(message.webCode));                  // 9
            });
            return ReadResult(root, message.messageId);
        }

        public SyncResultDto UpsertProspect(string username, string company, SyncProspectUpsertRequest message)
        {
            var root = Call(username, "upsertProspect", con =>
            {
                AppendEnvelope(con, company, message);                      // 1..4
                con.Append(RecIdOrZero(message.axRecId));                   // 5
                con.Append(Text(message.localCode));                        // 6
                con.Append(Text(message.prospect?.tradeName));              // 7
                con.Append(Text(message.prospect?.legalName));              // 8
                con.Append(Text(message.prospect?.city));                   // 9
                con.Append(Text(message.prospect?.countryId));              // 10
                con.Append(Text(message.prospect?.statisticsGroup));        // 11
                con.Append(message.prospect?.contactOrigin ?? 0);           // 12
                con.Append(Text(message.prospect?.vatNum));                 // 13
                con.Append(Text(message.prospect?.responsibleCrmUserId));   // 14
            });
            return ReadResult(root, message.messageId);
        }

        public SyncResultDto UpsertContact(string username, string company, SyncContactUpsertRequest message)
        {
            var root = Call(username, "upsertContact", con =>
            {
                AppendEnvelope(con, company, message);              // 1..4
                con.Append(RecIdOrZero(message.axRecId));           // 5
                con.Append(Text(message.account?.origin));          // 6
                con.Append(Text(message.account?.accountNum));      // 7
                con.Append(Text(message.contact?.name));            // 8
                con.Append(Text(message.contact?.position));        // 9
                con.Append(Text(message.contact?.email));           // 10
                con.Append(Text(message.contact?.phone));           // 11
            });
            return ReadResult(root, message.messageId);
        }

        public SyncVersionDto GetVisitVersion(string username, string company, int recId)
        {
            var root = Call(username, "getVisitVersion", con =>
            {
                con.Append(company);
                con.Append(recId);
            });
            var row = RowAt(root, 1);
            if (!Flag(row, 1))
                return null;

            return new SyncVersionDto
            {
                ModifiedAt = AxSyncDateTime.ToIso(Str(row, 2), Int(row, 3)),
                Hash = NullIfEmpty(Str(row, 4))
            };
        }

        public SyncVisitSnapshotDto GetVisit(string username, string company, int recId)
        {
            var root = Call(username, "getVisit", con =>
            {
                con.Append(company);
                con.Append(recId);
            });
            var row = RowAt(root, 1);
            if (!Flag(row, 1))
                return null;

            return new SyncVisitSnapshotDto
            {
                RecId = Str(row, 2),
                Code = NullIfEmpty(Str(row, 3)),
                ModifiedAt = AxSyncDateTime.ToIso(Str(row, 4), Int(row, 5)),
                Hash = NullIfEmpty(Str(row, 6)),
                TransDate = AxSyncDateTime.AxDateToIsoDate(Str(row, 7)) ?? string.Empty,
                VisitType = Int(row, 8),
                ContactMethod = Int(row, 9),
                Rating = Int(row, 10),
                Description = Str(row, 11),
                Comments = Str(row, 12),
                Background = NullIfEmpty(Str(row, 13)),
                Conclusions = NullIfEmpty(Str(row, 14)),
                AccountNum = Str(row, 15),
                OwnerAxUserId = Str(row, 16)
            };
        }

        // ---------------------------------------------------------
        // Container building
        // ---------------------------------------------------------

        private IAxaptaContainer Call(string username, string methodName, Action<IAxaptaContainer> fill)
        {
            var session = _sessionManager.GetAxInstanceForUser(username);
            var con = session.CreateContainer();
            fill(con);

            _logger.Log($"[SYNC-AX] call {AxClassName}.{methodName} items={AxContainerReadHelper.SafeLength(con)}");
            var result = session.CallStaticClassMethod(AxClassName, methodName, con) as IAxaptaContainer;
            if (result == null || AxContainerReadHelper.SafeLength(result) == 0)
                throw new InvalidOperationException($"{AxClassName}.{methodName} no devolvio un contenedor valido.");

            return result;
        }

        private static void AppendEnvelope(IAxaptaContainer con, string company, SyncMessageRequest message)
        {
            con.Append(company);
            con.Append(Text(message.messageId));
            con.Append(Text(message.externalId));
            con.Append(message.version ?? 0);
        }

        // Base version travels as AX date (yyyyMMdd), seconds and the product hash; empty when unknown.
        private static void AppendBaseVersion(IAxaptaContainer con, SyncBaseVersionDto baseVersion)
        {
            var axDate = string.Empty;
            var seconds = 0;
            if (baseVersion != null && !AxSyncDateTime.TryFromIso(baseVersion.modifiedAt, out axDate, out seconds))
            {
                axDate = string.Empty;
                seconds = 0;
            }

            con.Append(axDate);
            con.Append(seconds);
            con.Append(Text(baseVersion?.hash));
        }

        private static void AppendAttendees(IAxaptaContainer con, List<SyncVisitAttendeeDto> attendees)
        {
            var list = attendees ?? new List<SyncVisitAttendeeDto>();
            con.Append(list.Count);
            foreach (var attendee in list)
            {
                con.Append(Text(attendee.externalId));
                con.Append(RecIdOrZero(attendee.contactRecId));
                con.Append(Text(attendee.name));
                con.Append(Text(attendee.position));
                con.Append(Text(attendee.type));
                con.Append(RecIdOrZero(attendee.axRecId));
            }
        }

        private static string Text(string value)
        {
            return (value ?? string.Empty).Trim();
        }

        // AX memo fields keep CRLF line breaks; the web sends LF.
        private static string Memo(string value)
        {
            var text = (value ?? string.Empty).Replace("\r\n", "\n").Trim();
            return text.Replace("\n", "\r\n");
        }

        private static string AxDate(string isoDate)
        {
            return AxSyncDateTime.TryIsoDateToAx(isoDate, out var axDate) ? axDate : string.Empty;
        }

        // AX 3.0 RecIds are 32-bit integers; controllers validate the value before this point.
        private static int RecIdOrZero(string value)
        {
            return int.TryParse((value ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var recId)
                ? recId
                : 0;
        }

        // ---------------------------------------------------------
        // Container reading
        // ---------------------------------------------------------

        // Output layout: [[result, recId, code, modifiedDate, modifiedTime, hash, errorCode, errorMessage,
        // axCompany, detail, currentModifiedDate, currentModifiedTime, currentHash], [[externalId, recId, hash], ...]]
        private static SyncResultDto ReadResult(IAxaptaContainer root, string messageId)
        {
            var row = RowAt(root, 1);
            var result = (Str(row, 1) ?? string.Empty).Trim().ToLowerInvariant();
            if (result != "applied" && result != "duplicate" && result != "divergent" && result != "rejected")
                throw new InvalidOperationException($"Resultado AX desconocido: '{result}'.");

            var dto = new SyncResultDto
            {
                Result = result,
                RecId = NullIfEmpty(Str(row, 2)),
                Code = NullIfEmpty(Str(row, 3)),
                ModifiedAt = AxSyncDateTime.ToIso(Str(row, 4), Int(row, 5)),
                Hash = NullIfEmpty(Str(row, 6)),
                ErrorCode = NullIfEmpty(Str(row, 7)),
                ErrorMessage = NullIfEmpty(Str(row, 8)),
                AxCompany = NullIfEmpty(Str(row, 9))?.ToLowerInvariant(),
                Detail = NullIfEmpty(Str(row, 10)),
                Attendees = new List<SyncAttendeeResultDto>(),
                MessageId = messageId
            };

            if (result == "divergent")
            {
                dto.Current = new SyncVersionDto
                {
                    ModifiedAt = AxSyncDateTime.ToIso(Str(row, 11), Int(row, 12)),
                    Hash = NullIfEmpty(Str(row, 13))
                };
            }

            var attendees = AxContainerReadHelper.SafePeekContainer(root, 2);
            var count = AxContainerReadHelper.SafeLength(attendees);
            for (var i = 1; i <= count; i++)
            {
                var item = AxContainerReadHelper.SafePeekContainer(attendees, i);
                if (item == null)
                    continue;

                dto.Attendees.Add(new SyncAttendeeResultDto
                {
                    ExternalId = Str(item, 1),
                    RecId = Str(item, 2),
                    Hash = NullIfEmpty(Str(item, 3))
                });
            }

            return dto;
        }

        private static IAxaptaContainer RowAt(IAxaptaContainer root, int index)
        {
            var row = AxContainerReadHelper.SafePeekContainer(root, index);
            if (row == null)
                throw new InvalidOperationException("La respuesta de AX no tiene la fila esperada.");

            return row;
        }

        private static string Str(IAxaptaContainer row, int index)
        {
            return AxContainerReadHelper.SafeString(row, index);
        }

        private static int Int(IAxaptaContainer row, int index)
        {
            var value = AxContainerReadHelper.SafeValue(row, index);
            if (value == null)
                return 0;

            try
            {
                return Convert.ToInt32(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return int.TryParse(value.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : 0;
            }
        }

        private static bool Flag(IAxaptaContainer row, int index)
        {
            var text = Str(row, index);
            return text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase);
        }

        private static string NullIfEmpty(string value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }
}
