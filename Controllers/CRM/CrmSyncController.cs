using System;
using System.Collections.Generic;
using System.Net;
using System.Web.Http;
using System.Web.Http.Description;
using IND_CRM_API.Contracts.Requests;
using IND_CRM_API.Contracts.Responses;
using IND_CRM_API.Controllers;
using IND_CRM_API.Models.Responses;
using IND_CRM_API.Services;
using IND_CRM_API.Services.Interfaces;
using Swashbuckle.Swagger.Annotations;

namespace IND_CRM_API.Controllers.CRM
{
    /// <summary>
    /// Receiver of the CRM_INSERTEC outbox (web -> Axapta). Each action validates the message,
    /// calls INDCrmSyncService through the technical COM session and returns the acknowledgement.
    /// Idempotency, base-version checks and AX business rules live in X++, in one AX transaction.
    /// </summary>
    [RoutePrefix("api/crm/sync")]
    public class CrmSyncController : BaseSyncController
    {
        private const int NameLength = 80;
        private const int CodeLength = 15;
        private const int AxUserLength = 10;
        private const int PhoneLength = 20;
        private const int WebCodeLength = 20;
        private const int MaxAttendees = 50;

        private static readonly string[] AccountOrigins = { "Cliente", "ClientePotencial", "Proveedor", "ProveedorPotencial" };
        private static readonly string[] AttendeeTypes = { "Cliente_Proveedor", "Insertec", "Externo" };
        private static readonly string[] ProspectOrigins = { "ClientePotencial", "ProveedorPotencial" };

        public CrmSyncController(IAxaptaSessionManager sessionManager, IAxLogger logger, ICrmSyncService syncService)
            : base(sessionManager, logger, syncService)
        {
        }

        /// <summary>
        /// Proves the worker bearer, the sync-worker role and the AX company (one COM call).
        /// </summary>
        [HttpGet, Route("ping")]
        [ResponseType(typeof(IndApiResponse<SyncPingDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Worker autorizado", typeof(IndApiResponse<SyncPingDto>))]
        [SwaggerResponse(HttpStatusCode.Forbidden, "Bearer sin rol sync-worker", typeof(IndApiResponse<object>))]
        [SwaggerResponse((HttpStatusCode)422, "Falta X-IND-Company", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult Ping()
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[SYNC-IN] operation=ping user={username} company={company} traceId={traceId}");
                var data = SyncService.Ping(username, company);
                return Ok(new IndApiResponse<SyncPingDto>
                {
                    Success = true,
                    Message = "Worker de sincronizacion autorizado.",
                    ErrorCode = null,
                    Errors = null,
                    Data = data,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "ping", traceId);
            }
        }

        /// <summary>
        /// Creates or updates a visit with its complete attendee list (message visit.upsert).
        /// </summary>
        /// <remarks>
        /// Respuestas: 200 applied/duplicate, 409 SYNC_DIVERGENT cuando la version base no coincide,
        /// 422 SYNC_REJECTED con Data.errorCode de Axapta, 503 cuando Axapta no responde (reintento).
        /// </remarks>
        [HttpPost, Route("visits/upsert")]
        [ResponseType(typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Mensaje aplicado o ya aplicado", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse(HttpStatusCode.Conflict, "Divergencia con la version base", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Validacion o rechazo funcional", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpsertVisit([FromBody] SyncVisitUpsertRequest body)
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var errors = new List<IndValidationError>();
            ValidateEnvelope(errors, body, company);
            if (body != null)
            {
                AddText(errors, "webCode", body.webCode, WebCodeLength, false);
                AddRecId(errors, "axRecId", body.axRecId, false);
                ValidateAccountRef(errors, body.account);
                if (body.visit == null)
                {
                    errors.Add(new IndValidationError { Field = "visit", Message = "visit es obligatorio." });
                }
                else
                {
                    AddIsoDate(errors, "visit.transDate", body.visit.transDate, true);
                    AddEnumValue(errors, "visit.visitType", body.visit.visitType, true);
                    AddEnumValue(errors, "visit.contactMethod", body.visit.contactMethod, true);
                    AddEnumValue(errors, "visit.rating", body.visit.rating, true);
                    AddText(errors, "visit.description", body.visit.description, NameLength, true);
                    AddText(errors, "visit.comments", body.visit.comments, 0, false, singleLine: false);
                    AddText(errors, "visit.background", body.visit.background, 0, false, singleLine: false);
                    AddText(errors, "visit.conclusions", body.visit.conclusions, 0, false, singleLine: false);
                    AddText(errors, "visit.ownerAxUserId", body.visit.ownerAxUserId, AxUserLength, true);
                    AddText(errors, "visit.ownerCrmUserId", body.visit.ownerCrmUserId, CodeLength, false);
                }

                ValidateAttendees(errors, body.attendees);
            }

            if (errors.Count > 0)
                return ValidationFailed(errors, traceId);

            try
            {
                var username = GetAuthenticatedUsername();
                LogIncoming("visits/upsert", username, company, body, traceId);
                var result = SyncService.UpsertVisit(username, company, body);
                return FromSyncResult(result, "visits/upsert", traceId);
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "visits/upsert", traceId);
            }
        }

        /// <summary>
        /// Deletes a visit and its attendees in AX (message visit.delete). Already deleted = applied.
        /// </summary>
        [HttpPost, Route("visits/delete")]
        [ResponseType(typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Mensaje aplicado o ya aplicado", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse(HttpStatusCode.Conflict, "Divergencia con la version base", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Validacion o rechazo funcional", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult DeleteVisit([FromBody] SyncVisitDeleteRequest body)
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var errors = new List<IndValidationError>();
            ValidateEnvelope(errors, body, company);
            if (body != null)
            {
                AddText(errors, "webCode", body.webCode, WebCodeLength, false);
                AddRecId(errors, "axRecId", body.axRecId, false);
            }

            if (errors.Count > 0)
                return ValidationFailed(errors, traceId);

            try
            {
                var username = GetAuthenticatedUsername();
                LogIncoming("visits/delete", username, company, body, traceId);
                var result = SyncService.DeleteVisit(username, company, body);
                return FromSyncResult(result, "visits/delete", traceId);
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "visits/delete", traceId);
            }
        }

        /// <summary>
        /// Creates or updates a web prospect, message account.upsert: CRMClientePotencialTable by default or
        /// CRMProveedorPotencialTable when origin is ProveedorPotencial (plus CRMAccountTable in both cases).
        /// </summary>
        [HttpPost, Route("prospects/upsert")]
        [ResponseType(typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Mensaje aplicado o ya aplicado", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Validacion o rechazo funcional", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpsertProspect([FromBody] SyncProspectUpsertRequest body)
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var errors = new List<IndValidationError>();
            ValidateEnvelope(errors, body, company);
            if (body != null)
            {
                AddRecId(errors, "axRecId", body.axRecId, false);
                AddText(errors, "localCode", body.localCode, WebCodeLength, false);
                AddChoice(errors, "origin", body.origin, ProspectOrigins, false);
                var supplier = string.Equals((body.origin ?? string.Empty).Trim(), "ProveedorPotencial", StringComparison.Ordinal);
                if (body.prospect == null)
                {
                    errors.Add(new IndValidationError { Field = "prospect", Message = "prospect es obligatorio." });
                }
                else
                {
                    AddText(errors, "prospect.tradeName", body.prospect.tradeName, NameLength, true);
                    AddText(errors, "prospect.legalName", body.prospect.legalName, NameLength, true);
                    AddText(errors, "prospect.city", body.prospect.city, NameLength, false);
                    AddText(errors, "prospect.countryId", body.prospect.countryId, CodeLength, false);
                    // CRMClientePotencialTable requires the statistics group; the vendor prospect requires its type.
                    AddText(errors, "prospect.statisticsGroup", body.prospect.statisticsGroup, CodeLength, !supplier);
                    if (supplier && body.prospect.supplierType != 1 && body.prospect.supplierType != 2)
                        errors.Add(new IndValidationError { Field = "prospect.supplierType", Message = "supplierType debe ser 1 (Distribuidor) o 2 (Fabricante)." });
                    AddEnumValue(errors, "prospect.contactOrigin", body.prospect.contactOrigin, false);
                    AddText(errors, "prospect.vatNum", body.prospect.vatNum, NameLength, false);
                    AddText(errors, "prospect.responsibleCrmUserId", body.prospect.responsibleCrmUserId, CodeLength, false);
                }
            }

            if (errors.Count > 0)
                return ValidationFailed(errors, traceId);

            try
            {
                var username = GetAuthenticatedUsername();
                LogIncoming("prospects/upsert", username, company, body, traceId);
                var result = SyncService.UpsertProspect(username, company, body);
                return FromSyncResult(result, "prospects/upsert", traceId);
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "prospects/upsert", traceId);
            }
        }

        /// <summary>
        /// Updates the CRM fields (trade name, legal name, city, country) of an account that already
        /// exists in AX, any origin, message account.update. Never financial or tax data.
        /// </summary>
        [HttpPost, Route("accounts/update")]
        [ResponseType(typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Mensaje aplicado o ya aplicado", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Validacion o rechazo funcional", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpdateAccount([FromBody] SyncAccountUpdateRequest body)
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var errors = new List<IndValidationError>();
            ValidateEnvelope(errors, body, company);
            if (body != null)
            {
                AddRecId(errors, "axRecId", body.axRecId, true);
                ValidateAccountRef(errors, body.account);
                if (body.fields == null)
                {
                    errors.Add(new IndValidationError { Field = "fields", Message = "fields es obligatorio." });
                }
                else
                {
                    AddText(errors, "fields.tradeName", body.fields.tradeName, NameLength, false);
                    AddText(errors, "fields.legalName", body.fields.legalName, NameLength, false);
                    AddText(errors, "fields.city", body.fields.city, NameLength, false);
                    AddText(errors, "fields.countryId", body.fields.countryId, CodeLength, false);
                    if (string.IsNullOrWhiteSpace(body.fields.tradeName) && string.IsNullOrWhiteSpace(body.fields.legalName))
                        errors.Add(new IndValidationError { Field = "fields.tradeName", Message = "Se requiere el nombre comercial o la razon social." });
                }
            }

            if (errors.Count > 0)
                return ValidationFailed(errors, traceId);

            try
            {
                var username = GetAuthenticatedUsername();
                LogIncoming("accounts/update", username, company, body, traceId);
                var result = SyncService.UpdateAccount(username, company, body);
                return FromSyncResult(result, "accounts/update", traceId);
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "accounts/update", traceId);
            }
        }

        /// <summary>
        /// Creates or updates a contact of an AX account (CRMContactoTable), message contact.upsert.
        /// </summary>
        [HttpPost, Route("contacts/upsert")]
        [ResponseType(typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Mensaje aplicado o ya aplicado", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse((HttpStatusCode)422, "Validacion o rechazo funcional", typeof(IndApiResponse<SyncResultDto>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult UpsertContact([FromBody] SyncContactUpsertRequest body)
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            var errors = new List<IndValidationError>();
            ValidateEnvelope(errors, body, company);
            if (body != null)
            {
                AddRecId(errors, "axRecId", body.axRecId, false);
                ValidateAccountRef(errors, body.account);
                if (body.contact == null)
                {
                    errors.Add(new IndValidationError { Field = "contact", Message = "contact es obligatorio." });
                }
                else
                {
                    AddText(errors, "contact.name", body.contact.name, NameLength, true);
                    AddText(errors, "contact.position", body.contact.position, NameLength, false);
                    AddText(errors, "contact.email", body.contact.email, NameLength, false);
                    AddText(errors, "contact.phone", body.contact.phone, PhoneLength, false);
                }
            }

            if (errors.Count > 0)
                return ValidationFailed(errors, traceId);

            try
            {
                var username = GetAuthenticatedUsername();
                LogIncoming("contacts/upsert", username, company, body, traceId);
                var result = SyncService.UpsertContact(username, company, body);
                return FromSyncResult(result, "contacts/upsert", traceId);
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "contacts/upsert", traceId);
            }
        }

        /// <summary>
        /// Current base version (modified timestamp and fingerprint) of an AX visit.
        /// </summary>
        [HttpGet, Route("visits/{recId:int}/version")]
        [ResponseType(typeof(IndApiResponse<SyncVersionDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Version actual", typeof(IndApiResponse<SyncVersionDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Visita inexistente en la empresa", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetVisitVersion(int recId)
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            if (recId == 0)
                return ValidationFailed(new List<IndValidationError>
                {
                    new IndValidationError { Field = "recId", Message = "recId debe ser distinto de cero." }
                }, traceId);

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[SYNC-IN] operation=visits/version user={username} company={company} recId={recId} traceId={traceId}");
                var version = SyncService.GetVisitVersion(username, company, recId);
                if (version == null)
                    return NotFoundResult("Visita no encontrada en Axapta.", traceId);

                return Ok(new IndApiResponse<SyncVersionDto>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Errors = null,
                    Data = version,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "visits/version", traceId);
            }
        }

        /// <summary>
        /// AX snapshot of a visit, used by the web to accept the AX version of a divergence.
        /// </summary>
        [HttpGet, Route("visits/{recId:int}")]
        [ResponseType(typeof(IndApiResponse<SyncVisitSnapshotDto>))]
        [SwaggerOperation(Tags = new[] { "Sincronizacion CRM_INSERTEC" })]
        [SwaggerResponse(HttpStatusCode.OK, "Visita en Axapta", typeof(IndApiResponse<SyncVisitSnapshotDto>))]
        [SwaggerResponse(HttpStatusCode.NotFound, "Visita inexistente en la empresa", typeof(IndApiResponse<object>))]
        [SwaggerResponse(HttpStatusCode.ServiceUnavailable, "Axapta no disponible", typeof(IndApiResponse<object>))]
        public IHttpActionResult GetVisit(int recId)
        {
            var traceId = GetTraceId();
            var company = RequireCompanyOrReturn422(out var companyError, traceId);
            if (companyError != null)
                return companyError;

            if (recId == 0)
                return ValidationFailed(new List<IndValidationError>
                {
                    new IndValidationError { Field = "recId", Message = "recId debe ser distinto de cero." }
                }, traceId);

            try
            {
                var username = GetAuthenticatedUsername();
                Logger.Log($"[SYNC-IN] operation=visits/get user={username} company={company} recId={recId} traceId={traceId}");
                var visit = SyncService.GetVisit(username, company, recId);
                if (visit == null)
                    return NotFoundResult("Visita no encontrada en Axapta.", traceId);

                return Ok(new IndApiResponse<SyncVisitSnapshotDto>
                {
                    Success = true,
                    Message = "OK",
                    ErrorCode = null,
                    Errors = null,
                    Data = visit,
                    TraceId = traceId
                });
            }
            catch (Exception ex)
            {
                return AxFailure(ex, "visits/get", traceId);
            }
        }

        // ---------------------------------------------------------
        // Validation
        // ---------------------------------------------------------

        // Envelope: ids as UUID, version >= 1 and the message company equal to the header.
        private static void ValidateEnvelope(List<IndValidationError> errors, SyncMessageRequest body, string company)
        {
            if (body == null)
            {
                errors.Add(new IndValidationError { Field = "body", Message = "Se requiere el cuerpo de la peticion." });
                return;
            }

            AddGuid(errors, "messageId", body.messageId);
            AddGuid(errors, "externalId", body.externalId);
            if (!body.version.HasValue || body.version.Value < 1)
                errors.Add(new IndValidationError { Field = "version", Message = "version debe ser un entero mayor que cero." });

            if (string.IsNullOrWhiteSpace(body.company) ||
                !string.Equals(body.company.Trim(), company, StringComparison.OrdinalIgnoreCase))
                errors.Add(new IndValidationError { Field = "company", Message = "company debe coincidir con X-IND-Company." });
        }

        private static void ValidateAccountRef(List<IndValidationError> errors, SyncAccountRefDto account)
        {
            if (account == null)
            {
                errors.Add(new IndValidationError { Field = "account", Message = "account es obligatorio." });
                return;
            }

            AddChoice(errors, "account.origin", account.origin, AccountOrigins, true);
            AddText(errors, "account.accountNum", account.accountNum, CodeLength, true);
        }

        private static void ValidateAttendees(List<IndValidationError> errors, List<SyncVisitAttendeeDto> attendees)
        {
            if (attendees == null)
            {
                errors.Add(new IndValidationError { Field = "attendees", Message = "attendees es obligatorio (lista vacia cuando no hay asistentes)." });
                return;
            }

            if (attendees.Count > MaxAttendees)
                errors.Add(new IndValidationError { Field = "attendees", Message = $"attendees no puede superar {MaxAttendees} elementos." });

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < attendees.Count; i++)
            {
                var prefix = $"attendees[{i}]";
                var attendee = attendees[i];
                if (attendee == null)
                {
                    errors.Add(new IndValidationError { Field = prefix, Message = "El asistente no puede ser nulo." });
                    continue;
                }

                AddGuid(errors, prefix + ".externalId", attendee.externalId);
                if (!string.IsNullOrWhiteSpace(attendee.externalId) && !seen.Add(attendee.externalId.Trim()))
                    errors.Add(new IndValidationError { Field = prefix + ".externalId", Message = "externalId repetido en la lista de asistentes." });

                AddRecId(errors, prefix + ".contactRecId", attendee.contactRecId, false);
                AddRecId(errors, prefix + ".axRecId", attendee.axRecId, false);
                AddText(errors, prefix + ".name", attendee.name, NameLength, true);
                AddText(errors, prefix + ".position", attendee.position, NameLength, false);
                AddChoice(errors, prefix + ".type", attendee.type, AttendeeTypes, true);
            }
        }

        private void LogIncoming(string operation, string username, string company, SyncMessageRequest body, string traceId)
        {
            Logger.Log($"[SYNC-IN] operation={operation} user={username} company={company} messageId={body.messageId} " +
                       $"externalId={body.externalId} version={body.version} webTrace={body.actor?.traceId ?? "-"} traceId={traceId}");
        }
    }
}
