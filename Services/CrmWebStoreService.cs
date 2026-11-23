using System;
using System.Collections.Generic;
using AxaptaCOMConnector;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Services.Interfaces;

namespace IND_CRM_API.Services
{
    /// <summary>
    /// Calls the X++ classes CRMWeb* through the common per-request COM session
    /// (IAxaptaSessionManager / IND_AxSessionGuard) of the web store user. It only moves data:
    /// CrmWebStoreContainerBuilder gives the input positions and CrmWebStoreResultReader reads
    /// the answer. One COM call per request, without automatic retry, because AX writes.
    /// </summary>
    public sealed class CrmWebStoreService : ICrmWebStoreService
    {
        /// <summary>X++ class with the common helpers and ping.</summary>
        public const string CommonClass = "CRMWebStoreCommon";

        /// <summary>X++ class of visits and attendees.</summary>
        public const string VisitClass = "CRMWebVisitStore";

        /// <summary>X++ class of contacts.</summary>
        public const string ContactClass = "CRMWebContactStore";

        /// <summary>X++ class of customer prospects.</summary>
        public const string ProspectClass = "CRMWebProspectStore";

        private const int MaxContainerDepth = 4;

        private readonly IAxaptaSessionManager _sessionManager;
        private readonly IAxLogger _logger;

        /// <summary>Creates the service over the shared session manager and logger.</summary>
        public CrmWebStoreService(IAxaptaSessionManager sessionManager, IAxLogger logger)
        {
            _sessionManager = sessionManager ?? throw new ArgumentNullException(nameof(sessionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>Calls CRMWebStoreCommon::ping([company]).</summary>
        public WebStorePingDto Ping(string username, string company)
        {
            var root = Call(username, CommonClass, "ping", CrmWebStoreContainerBuilder.BuildPing(company));
            return CrmWebStoreResultReader.ReadPing(root);
        }

        /// <summary>Calls CRMWebVisitStore::upsert.</summary>
        public WebStoreResultDto UpsertVisit(string username, string company, WebStoreVisitUpsertRequest message)
        {
            var values = CrmWebStoreContainerBuilder.BuildVisitUpsert(company, message);
            return ReadResult(Call(username, VisitClass, "upsert", values), message.messageId, VisitClass);
        }

        /// <summary>Calls CRMWebVisitStore::remove.</summary>
        public WebStoreResultDto DeleteVisit(string username, string company, WebStoreVisitDeleteRequest message)
        {
            var values = CrmWebStoreContainerBuilder.BuildVisitDelete(company, message);
            return ReadResult(Call(username, VisitClass, "remove", values), message.messageId, VisitClass);
        }

        /// <summary>Calls CRMWebContactStore::upsert.</summary>
        public WebStoreResultDto UpsertContact(string username, string company, WebStoreContactUpsertRequest message)
        {
            var values = CrmWebStoreContainerBuilder.BuildContactUpsert(company, message);
            return ReadResult(Call(username, ContactClass, "upsert", values), message.messageId, ContactClass);
        }

        /// <summary>Calls CRMWebProspectStore::upsert.</summary>
        public WebStoreResultDto UpsertProspect(string username, string company, WebStoreProspectUpsertRequest message)
        {
            var values = CrmWebStoreContainerBuilder.BuildProspectUpsert(company, message);
            return ReadResult(Call(username, ProspectClass, "upsert", values), message.messageId, ProspectClass);
        }

        // Fills one AX container with the positional values, calls the static method and copies the
        // answer to plain arrays while the COM objects are still owned by the request session.
        private object[] Call(string username, string className, string methodName, IList<object> values)
        {
            var session = _sessionManager.GetAxInstanceForUser(username);
            var input = session.CreateContainer();
            foreach (var value in values)
                input.Append(value);

            _logger.Log("[WEBSTORE-AX] call " + className + "." + methodName + " items=" + values.Count);
            var output = session.CallStaticClassMethod(className, methodName, input) as IAxaptaContainer;
            if (output == null)
                throw new CrmWebStoreResultException(className + "." + methodName + " did not return a container.");

            return Snapshot(session, output, 1);
        }

        // Logs the AX company echoed in the result next to the operation (diagnostics only).
        private WebStoreResultDto ReadResult(object[] root, string messageId, string className)
        {
            var result = CrmWebStoreResultReader.ReadResult(root, messageId, out var axCompany);
            _logger.Log("[WEBSTORE-AX] result class=" + className + " result=" + result.Result +
                        " axCompany=" + (axCompany ?? "-") + " errorCode=" + (result.ErrorCode ?? "-"));
            return result;
        }

        // Recursive copy of an AX container into object arrays (nested containers become object[]).
        // Nested COM containers are tracked by the session so they are released at request end.
        private static object[] Snapshot(AxaptaComSession session, IAxaptaContainer container, int depth)
        {
            if (depth > MaxContainerDepth)
                throw new CrmWebStoreResultException("The AX result is nested deeper than expected.");

            try
            {
                var length = container.Length();
                var values = new object[length];
                for (var i = 1; i <= length; i++)
                {
                    var value = container.Peek(i);
                    var nested = value as IAxaptaContainer;
                    if (nested != null)
                        session.TrackAxObject(nested, "webstore-result-nested");

                    values[i - 1] = nested != null ? Snapshot(session, nested, depth + 1) : value;
                }

                return values;
            }
            catch (CrmWebStoreResultException)
            {
                throw;
            }
            catch (Exception ex)
            {
                throw new CrmWebStoreResultException("The AX result container could not be read: " + ex.GetType().Name + ".");
            }
        }
    }
}
